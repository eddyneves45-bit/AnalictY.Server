param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseRoot,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputRoot = "",
    [string]$BaseUrl = "https://analicty-downloads.s3.sa-east-1.amazonaws.com/updates/stable",
    [string]$Channel = "stable",
    [string[]]$Changelog = @()
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..\..")).Path
}

function Copy-DirectoryContents($source, $destination) {
    if (-not (Test-Path $source)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    }
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

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "release\updates\$Channel"
}

$outputRootFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)
$safeVersion = Get-SafeFileName -Value $Version
$packageName = "AnalictY-$safeVersion.zip"
$packagePath = Join-Path $outputRootFull $packageName
$manifestPath = Join-Path $outputRootFull "latest.json"
$stagingRoot = Join-Path $outputRootFull "staging-$safeVersion"

Assert-Exists $releaseRootFull "Release"
Assert-Exists (Join-Path $releaseRootFull "app") "Diretorio app da release"
Assert-Exists (Join-Path $releaseRootFull "app\backend\Scada.Api.exe") "Backend publicado"
Assert-Exists (Join-Path $releaseRootFull "app\frontend\server.js") "Frontend publicado"
Assert-Exists (Join-Path $releaseRootFull "app\agent\AnalictY.Agent.exe") "AnalictY Agent publicado"
Assert-Exists (Join-Path $releaseRootFull "service\install-services.ps1") "Script de instalacao dos servicos"
Assert-Exists (Join-Path $releaseRootFull "service\AnalictY.Backend.Service.exe") "Servico Backend WinSW"
Assert-Exists (Join-Path $releaseRootFull "service\AnalictY.Frontend.Service.exe") "Servico Frontend WinSW"
Assert-Exists (Join-Path $releaseRootFull "service\AnalictY.MySQL.Service.exe") "Servico MySQL WinSW"
Assert-Exists (Join-Path $releaseRootFull "installer\version.json") "Arquivo installer\version.json da release"

Write-Host "AnalictY update package"
Write-Host "Release: $releaseRootFull"
Write-Host "Version: $Version"
Write-Host "Output:  $outputRootFull"

New-Item -ItemType Directory -Force -Path $outputRootFull | Out-Null
Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

Copy-DirectoryContents (Join-Path $releaseRootFull "app") (Join-Path $stagingRoot "app")
Copy-DirectoryContents (Join-Path $releaseRootFull "installer") (Join-Path $stagingRoot "installer")
Copy-DirectoryContents (Join-Path $releaseRootFull "service") (Join-Path $stagingRoot "service")
Copy-DirectoryContents (Join-Path $releaseRootFull "updater") (Join-Path $stagingRoot "updater")

$runtimeRoot = Join-Path $releaseRootFull "runtime"
if (Test-Path (Join-Path $runtimeRoot "node\node.exe")) {
    Copy-DirectoryContents $runtimeRoot (Join-Path $stagingRoot "runtime")
} else {
    Write-Host "Runtime Node.js nao incluido no pacote de update porque nao ha node.exe na release."
}

Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $packagePath -Force
Remove-Item -LiteralPath $stagingRoot -Recurse -Force

$sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$packageUrl = "{0}/{1}" -f $BaseUrl.TrimEnd('/'), $packageName
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
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

Write-Host "Pacote:   $packagePath"
Write-Host "SHA256:   $sha256"
Write-Host "Manifest: $manifestPath"
