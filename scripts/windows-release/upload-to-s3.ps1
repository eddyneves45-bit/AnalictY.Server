param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseRoot,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$BucketName = "analicty-downloads",
    [string]$Region = "sa-east-1",
    [string]$Channel = "stable",
    [string[]]$Changelog = @(),
    [string]$AwsAccessKeyId,
    [string]$AwsSecretAccessKey,
    [string]$AwsCliPath,
    [switch]$UseAwsSdk,
    [switch]$SkipInstallers
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..\..")).Path
}

function Assert-Exists($path, $label) {
    if (-not (Test-Path $path)) {
        throw "$label nao encontrado: $path"
    }
}

function Get-SafeFileName {
    param(
        [string]$Value
    )
    $clean = if ([string]::IsNullOrWhiteSpace($Value)) { "unknown" } else { $Value.Trim() }
    [System.IO.Path]::GetInvalidFileNameChars() | ForEach-Object {
        $clean = $clean.Replace($_, "_")
    }
    return $clean
}

$repoRoot = Resolve-RepoRoot
$releaseRootFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReleaseRoot)
$safeVersion = Get-SafeFileName -Value $Version
$packageName = "AnalictY-$safeVersion.zip"
$packagePath = Join-Path $repoRoot "release\updates\$Channel\$packageName"

Assert-Exists $releaseRootFull "Release"

Write-Host "AnalictY Upload para S3"
Write-Host "Bucket:  $BucketName"
Write-Host "Regiao: $Region"
Write-Host "Versao: $Version"
Write-Host "Canal:   $Channel"

# Primeiro, criar o pacote de atualizacao se ainda nao existir
if (-not (Test-Path $packagePath)) {
    Write-Host "Pacote nao encontrado. Criando..."
    $createScript = Join-Path $repoRoot "scripts\windows-release\create-update-package.ps1"
    Assert-Exists $createScript "Script create-update-package.ps1"
    
    & $createScript -ReleaseRoot $ReleaseRoot -Version $Version -Channel $Channel -Changelog $Changelog
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao criar pacote de atualizacao"
    }
}

Assert-Exists $packagePath "Pacote de atualizacao"

# Calcular SHA256
$sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

# Criar manifesto
$packageUrl = "https://$BucketName.s3.$Region.amazonaws.com/updates/$Channel/$packageName"
$manifest = [ordered]@{
    product = "AnalictY"
    channel = $Channel
    version = $Version
    url = $packageUrl
    sha256 = $sha256
    released_at = (Get-Date).ToUniversalTime().ToString("o")
    changelog = $Changelog
}

$manifestJson = $manifest | ConvertTo-Json -Depth 5
$manifestPath = Join-Path $repoRoot "release\updates\$Channel\latest.json"
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

Write-Host "SHA256:  $sha256"
Write-Host "Manifest: $manifestPath"

# Upload usando AWS CLI
if (-not $UseAwsSdk) {
    # Determinar caminho do AWS CLI
    if ([string]::IsNullOrWhiteSpace($AwsCliPath)) {
        # Tentar encontrar automaticamente
        $awsCli = Get-Command aws -ErrorAction SilentlyContinue
        if ($awsCli) {
            $AwsCliPath = $awsCli.Source
        } else {
            # Caminhos comuns
            $commonPaths = @(
                "$env:ProgramFiles\Amazon\AWSCLI\bin\aws.exe",
                "$env:ProgramFiles(x86)\Amazon\AWSCLI\bin\aws.exe",
                "$env:ProgramFiles\Amazon\AWSCLIV2\aws.exe",
                "$env:ProgramFiles(x86)\Amazon\AWSCLIV2\aws.exe",
                "$env:LOCALAPPDATA\Programs\Amazon\AWSCLI\aws.exe"
            )
            foreach ($path in $commonPaths) {
                if (Test-Path $path) {
                    $AwsCliPath = $path
                    break
                }
            }
        }
    }

    if (-not (Test-Path $AwsCliPath)) {
        throw "AWS CLI nao encontrado em: $AwsCliPath. Use -AwsCliPath para especificar o caminho ou instale o AWS CLI."
    }

    Write-Host "Usando AWS CLI: $AwsCliPath"

    $env:AWS_DEFAULT_REGION = $Region

    if (-not [string]::IsNullOrWhiteSpace($AwsAccessKeyId)) {
        $env:AWS_ACCESS_KEY_ID = $AwsAccessKeyId
    }
    if (-not [string]::IsNullOrWhiteSpace($AwsSecretAccessKey)) {
        $env:AWS_SECRET_ACCESS_KEY = $AwsSecretAccessKey
    }

    if ([string]::IsNullOrWhiteSpace($AwsAccessKeyId) -or [string]::IsNullOrWhiteSpace($AwsSecretAccessKey)) {
        Write-Host "Credenciais AWS nao fornecidas por parametro. O AWS CLI usara credenciais configuradas no ambiente/perfil, se existirem."
    }

    # Upload do pacote
    $s3PackageKey = "updates/$Channel/$packageName"
    Write-Host "Upload pacote: s3://$BucketName/$s3PackageKey"
    & $AwsCliPath s3 cp $packagePath "s3://$BucketName/$s3PackageKey" --region $Region
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao upload pacote para S3"
    }

    # Upload do manifesto latest.json
    $s3ManifestKey = "updates/$Channel/latest.json"
    Write-Host "Upload manifesto: s3://$BucketName/$s3ManifestKey"
    & $AwsCliPath s3 cp $manifestPath "s3://$BucketName/$s3ManifestKey" --region $Region --content-type "application/json"
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao upload manifesto para S3"
    }

    # Upload do manifesto versionado
    $s3VersionManifestKey = "updates/$Channel/AnalictY-$safeVersion.json"
    Write-Host "Upload manifesto versionado: s3://$BucketName/$s3VersionManifestKey"
    & $AwsCliPath s3 cp $manifestPath "s3://$BucketName/$s3VersionManifestKey" --region $Region --content-type "application/json"
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao upload manifesto versionado para S3"
    }

    # Upload do instalador se existir e nao for skip
    if (-not $SkipInstallers) {
        $installerPath = Join-Path $repoRoot "release\installer\AnalictY-Setup-$safeVersion.exe"
        if (Test-Path $installerPath) {
            $s3InstallerKey = "installers/AnalictY-Setup-$safeVersion.exe"
            Write-Host "Upload instalador: s3://$BucketName/$s3InstallerKey"
            & $AwsCliPath s3 cp $installerPath "s3://$BucketName/$s3InstallerKey" --region $Region
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Falha ao upload instalador para S3 (continuando...)"
            }
        } else {
            Write-Host "Instalador nao encontrado em $installerPath (pulando)"
        }
    }

    # Tornar publicos os arquivos de atualizacao (se bucket for publico)
    Write-Host "Configurando ACL publico para arquivos de atualizacao..."
    try {
        & $AwsCliPath s3api put-object-acl --bucket $BucketName --key $s3PackageKey --acl public-read --region $Region
        & $AwsCliPath s3api put-object-acl --bucket $BucketName --key $s3ManifestKey --acl public-read --region $Region
        & $AwsCliPath s3api put-object-acl --bucket $BucketName --key $s3VersionManifestKey --acl public-read --region $Region
    } catch {
        Write-Warning "Falha ao configurar ACL publica (bucket pode ter BlockPublicAcls ativado): $_"
    }

} else {
    # Upload usando .NET AWS SDK
    Write-Host "Usando .NET AWS SDK..."

    # Verificar se AWSSDK.S3 esta disponivel
    try {
        Add-Type -Path (Join-Path $repoRoot "packages\AWSSDK.S3\lib\netstandard2.0\AWSSDK.S3.dll") -ErrorAction Stop
    } catch {
        throw "AWSSDK.S3 nao encontrado. Instale com: dotnet add package AWSSDK.S3"
    }

    # TODO: Implementar upload usando .NET AWS SDK
    throw "Upload via .NET AWS SDK ainda nao implementado. Use AWS CLI ou instale AWSSDK.S3"
}

Write-Host ""
Write-Host "Upload concluido com sucesso!"
Write-Host "URL do manifesto: https://$BucketName.s3.$Region.amazonaws.com/updates/$Channel/latest.json"
Write-Host ""
Write-Host "Configure no backend:"
Write-Host "  AnalictY:UpdateManifestUrl=https://$BucketName.s3.$Region.amazonaws.com/updates/$Channel/latest.json"
