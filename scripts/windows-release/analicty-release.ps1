param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet("All", "Installer", "Update")]
    [string]$Mode = "All",

    [string]$Channel = "stable",
    [string[]]$Changelog = @(),

    [string]$NodeZipPath = "",
    [string]$MySqlZipPath = "",
    [string]$WinSWExePath = "",
    [string]$InnoSetupCompiler = "",

    [switch]$Upload,
    [string]$BucketName = "analicty-downloads",
    [string]$Region = "sa-east-1",
    [string]$AwsAccessKeyId = "",
    [string]$AwsSecretAccessKey = "",
    [string]$AwsCliPath = "",

    [switch]$InstallDependencies,
    [switch]$SkipBackendBuild,
    [switch]$SkipFrontendBuild,
    [switch]$SkipAgentBuild
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = if ($PSCommandPath) { Split-Path -Parent $PSCommandPath } else { $PSScriptRoot }
    return (Resolve-Path (Join-Path $scriptDir "..\..")).Path
}

function Resolve-OptionalTool {
    param(
        [string]$CurrentValue,
        [string[]]$Candidates
    )

    if (-not [string]::IsNullOrWhiteSpace($CurrentValue)) {
        return $CurrentValue
    }

    foreach ($candidate in $Candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return ""
}

function Get-SafeFileName {
    param([string]$Value)

    $clean = if ([string]::IsNullOrWhiteSpace($Value)) { "unknown" } else { $Value.Trim() }
    [System.IO.Path]::GetInvalidFileNameChars() | ForEach-Object {
        $clean = $clean.Replace($_, "_")
    }
    return $clean
}

function Write-Section {
    param([string]$Text)

    Write-Host ""
    Write-Host "=================================================="
    Write-Host $Text
    Write-Host "=================================================="
}

function Assert-Exists {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path $Path)) {
        throw "$Label nao encontrado: $Path"
    }
}

$repoRoot = Resolve-RepoRoot
$scriptsRoot = Join-Path $repoRoot "scripts\windows-release"
$toolsRoot = Join-Path $repoRoot "tools\windows-release"
$safeVersion = Get-SafeFileName -Value $Version
$releaseRoot = Join-Path $repoRoot "release\AnalictY-$safeVersion"
$updateRoot = Join-Path $repoRoot "release\updates\$Channel"
$updatePackage = Join-Path $updateRoot "AnalictY-$safeVersion.zip"
$manifestPath = Join-Path $updateRoot "latest.json"
$installerPath = Join-Path $repoRoot "release\installer\AnalictY-Setup-$safeVersion.exe"

$NodeZipPath = Resolve-OptionalTool -CurrentValue $NodeZipPath -Candidates @(
    (Join-Path $toolsRoot "node-v24.15.0-win-x64.zip"),
    (Join-Path $toolsRoot "node-v22.20.0-win-x64.zip"),
    (Join-Path $toolsRoot "node-win-x64.zip")
)

$WinSWExePath = Resolve-OptionalTool -CurrentValue $WinSWExePath -Candidates @(
    (Join-Path $toolsRoot "WinSW-x64.exe"),
    (Join-Path $toolsRoot "winsw-x64.exe")
)

$MySqlZipPath = Resolve-OptionalTool -CurrentValue $MySqlZipPath -Candidates @(
    (Join-Path $toolsRoot "mysql-winx64.zip"),
    (Join-Path $toolsRoot "mysql-8.4-winx64.zip"),
    (Join-Path $toolsRoot "mysql-8.0-winx64.zip")
)

$InnoSetupCompiler = Resolve-OptionalTool -CurrentValue $InnoSetupCompiler -Candidates @(
    (Join-Path $toolsRoot "ISCC.exe"),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)

Write-Section "AnalictY Release"
Write-Host "Versao:     $Version"
Write-Host "Modo:       $Mode"
Write-Host "Canal:      $Channel"
Write-Host "Repositorio: $repoRoot"
Write-Host "Node ZIP:   $(if ($NodeZipPath) { $NodeZipPath } else { 'nao informado' })"
Write-Host "MySQL ZIP:  $(if ($MySqlZipPath) { $MySqlZipPath } else { 'nao informado' })"
Write-Host "WinSW:      $(if ($WinSWExePath) { $WinSWExePath } else { 'nao informado' })"
Write-Host "Inno:       $(if ($InnoSetupCompiler) { $InnoSetupCompiler } else { 'nao encontrado' })"

$buildScript = Join-Path $scriptsRoot "build-release.ps1"
$packageScript = Join-Path $scriptsRoot "create-update-package.ps1"
$uploadScript = Join-Path $scriptsRoot "upload-to-s3.ps1"

Assert-Exists $buildScript "Script de build"
Assert-Exists $packageScript "Script de update"

$skipInstaller = $Mode -eq "Update"
$shouldCreateUpdate = $Mode -in @("All", "Update") -or $Upload
$skipInstallerMessage = if ($skipInstaller) { "sim" } else { "nao" }
Write-Host "Pular instalador: $skipInstallerMessage"

Write-Section "1/3 - Build da release"
$buildArgs = @{
    Version = $Version
    ReleaseRoot = $releaseRoot
    SkipInstaller = $skipInstaller
    SkipBackendBuild = $SkipBackendBuild
    SkipFrontendBuild = $SkipFrontendBuild
    SkipAgentBuild = $SkipAgentBuild
}

if ($InstallDependencies) { $buildArgs.InstallDependencies = $true }
if (-not [string]::IsNullOrWhiteSpace($NodeZipPath)) { $buildArgs.NodeZipPath = $NodeZipPath }
if (-not [string]::IsNullOrWhiteSpace($MySqlZipPath)) { $buildArgs.MySqlZipPath = $MySqlZipPath }
if (-not [string]::IsNullOrWhiteSpace($WinSWExePath)) { $buildArgs.WinSWExePath = $WinSWExePath }
if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompiler)) { $buildArgs.InnoSetupCompiler = $InnoSetupCompiler }

& $buildScript @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao gerar release."
}

if ($shouldCreateUpdate) {
    Write-Section "2/3 - Pacote de atualizacao"
    & $packageScript `
        -ReleaseRoot $releaseRoot `
        -Version $Version `
        -OutputRoot $updateRoot `
        -Channel $Channel `
        -Changelog $Changelog

    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao gerar pacote de atualizacao."
    }
} else {
    Write-Section "2/3 - Pacote de atualizacao"
    Write-Host "Pulando pacote de atualizacao porque o modo selecionado foi Installer."
}

if ($Upload) {
    Write-Section "3/3 - Upload"
    Assert-Exists $uploadScript "Script de upload"

    $uploadArgs = @{
        ReleaseRoot = $releaseRoot
        Version = $Version
        BucketName = $BucketName
        Region = $Region
        Channel = $Channel
        Changelog = $Changelog
    }

    if (-not [string]::IsNullOrWhiteSpace($AwsAccessKeyId)) { $uploadArgs.AwsAccessKeyId = $AwsAccessKeyId }
    if (-not [string]::IsNullOrWhiteSpace($AwsSecretAccessKey)) { $uploadArgs.AwsSecretAccessKey = $AwsSecretAccessKey }
    if (-not [string]::IsNullOrWhiteSpace($AwsCliPath)) { $uploadArgs.AwsCliPath = $AwsCliPath }
    if ($Mode -eq "Update") { $uploadArgs.SkipInstallers = $true }

    & $uploadScript @uploadArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Falha no upload."
    }
} else {
    Write-Section "3/3 - Upload"
    Write-Host "Pulando upload. Use -Upload para publicar no S3."
}

Write-Section "Resultado"
Write-Host "Release:"
Write-Host "  $releaseRoot"

if ($shouldCreateUpdate -and (Test-Path $updatePackage)) {
    $updateHash = (Get-FileHash -LiteralPath $updatePackage -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ""
    Write-Host "Update:"
    Write-Host "  $updatePackage"
    Write-Host "  SHA256: $updateHash"
}

if ($shouldCreateUpdate -and (Test-Path $manifestPath)) {
    Write-Host ""
    Write-Host "Manifest:"
    Write-Host "  $manifestPath"
}

if (Test-Path $installerPath) {
    $installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ""
    Write-Host "Instalador:"
    Write-Host "  $installerPath"
    Write-Host "  SHA256: $installerHash"
} elseif ($Mode -ne "Update") {
    Write-Host ""
    Write-Warning "Instalador nao foi gerado. Confirme se o Inno Setup esta instalado ou informe -InnoSetupCompiler."
}

if ($Upload) {
    Write-Host ""
    Write-Host "Manifest remoto:"
    Write-Host "  https://$BucketName.s3.$Region.amazonaws.com/updates/$Channel/latest.json"
}

Write-Host ""
Write-Host "Processo concluido."
