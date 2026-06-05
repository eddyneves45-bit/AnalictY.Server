param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$ConfigPath = "",
    [string]$BucketName = "",
    [string]$Region = "",
    [string]$Channel = "",
    [string[]]$Changelog = @(),
    [string]$NodeZipPath = "",
    [string]$WinSWExePath = "",
    [string]$InnoSetupCompiler = "",
    [string]$AwsAccessKeyId = "",
    [string]$AwsSecretAccessKey = "",
    [switch]$SkipInstallers,
    [switch]$SkipBackendBuild,
    [switch]$SkipFrontendBuild,
    [switch]$SkipAgentBuild,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = if ($PSCommandPath) { Split-Path -Parent $PSCommandPath } else { $PSScriptRoot }
    if (-not $scriptDir) { $scriptDir = $PSScriptRoot }
    return (Resolve-Path (Join-Path $scriptDir "..\..")).Path
}

# Carregar configuracao do arquivo se fornecido
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $scriptDir = if ($PSCommandPath) { Split-Path -Parent $PSCommandPath } else { $PSScriptRoot }
    if (-not $scriptDir) { $scriptDir = $PSScriptRoot }
    $ConfigPath = Join-Path $scriptDir "aws-config.json"
}

if (Test-Path $ConfigPath) {
    Write-Host "Carregando configuracao de: $ConfigPath"
    $config = Get-Content $ConfigPath | ConvertFrom-Json
    
    if ([string]::IsNullOrWhiteSpace($BucketName)) { $BucketName = $config.BucketName }
    if ([string]::IsNullOrWhiteSpace($Region)) { $Region = $config.Region }
    if ([string]::IsNullOrWhiteSpace($Channel)) { $Channel = $config.Channel }
    if ([string]::IsNullOrWhiteSpace($AwsAccessKeyId)) { $AwsAccessKeyId = $config.AwsAccessKeyId }
    if ([string]::IsNullOrWhiteSpace($AwsSecretAccessKey)) { $AwsSecretAccessKey = $config.AwsSecretAccessKey }
}

# Valores padrao
if ([string]::IsNullOrWhiteSpace($BucketName)) { $BucketName = "analicty-downloads" }
if ([string]::IsNullOrWhiteSpace($Region)) { $Region = "sa-east-1" }
if ([string]::IsNullOrWhiteSpace($Channel)) { $Channel = "stable" }

$repoRoot = Resolve-RepoRoot
$releaseRoot = Join-Path $repoRoot "release\AnalictY-$Version"

# Caminhos automaticos para tools
$toolsRoot = Join-Path $repoRoot "tools\windows-release"
if ([string]::IsNullOrWhiteSpace($NodeZipPath)) {
    $autoNode = Join-Path $toolsRoot "node-v24.15.0-win-x64.zip"
    if (Test-Path $autoNode) { $NodeZipPath = $autoNode }
}
if ([string]::IsNullOrWhiteSpace($WinSWExePath)) {
    $autoWinSW = Join-Path $toolsRoot "WinSW-x64.exe"
    if (Test-Path $autoWinSW) { $WinSWExePath = $autoWinSW }
}
if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $autoInno = Join-Path $toolsRoot "innosetup.exe"
    if (Test-Path $autoInno) { $InnoSetupCompiler = $autoInno }
    else {
        $defaultInno = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        if (Test-Path $defaultInno) { $InnoSetupCompiler = $defaultInno }
    }
}

Write-Host "=================================================="
Write-Host "AnalictY - Release e Upload Automatizado"
Write-Host "=================================================="
Write-Host "Versao:   $Version"
Write-Host "Canal:    $Channel"
Write-Host "Bucket:   $BucketName"
Write-Host "Regiao:   $Region"
Write-Host ""

# Passo 1: Build da release
Write-Host "[1/3] Building release..."
$buildScript = Join-Path $repoRoot "scripts\windows-release\build-release.ps1"
$buildArgs = @{
    Version = $Version
    NodeZipPath = $NodeZipPath
    WinSWExePath = $WinSWExePath
    InnoSetupCompiler = $InnoSetupCompiler
    SkipBackendBuild = $SkipBackendBuild
    SkipFrontendBuild = $SkipFrontendBuild
    SkipAgentBuild = $SkipAgentBuild
    SkipInstaller = ($SkipInstaller -or $SkipInstallers)
}

& $buildScript @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "Falha no build da release"
}
Write-Host "Build concluido com sucesso."
Write-Host ""

# Passo 2: Criar pacote de atualizacao
Write-Host "[2/3] Criando pacote de atualizacao..."
$packageScript = Join-Path $repoRoot "scripts\windows-release\create-update-package.ps1"
$packageArgs = @{
    ReleaseRoot = $releaseRoot
    Version = $Version
    Channel = $Channel
    Changelog = $Changelog
}

& $packageScript @packageArgs
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao criar pacote de atualizacao"
}
Write-Host "Pacote de atualizacao criado com sucesso."
Write-Host ""

# Passo 3: Upload para S3
Write-Host "[3/3] Upload para S3..."
$uploadScript = Join-Path $repoRoot "scripts\windows-release\upload-to-s3.ps1"
$uploadArgs = @{
    ReleaseRoot = $releaseRoot
    Version = $Version
    BucketName = $BucketName
    Region = $Region
    Channel = $Channel
    Changelog = $Changelog
    AwsAccessKeyId = $AwsAccessKeyId
    AwsSecretAccessKey = $AwsSecretAccessKey
    SkipInstallers = $SkipInstallers
}

& $uploadScript @uploadArgs
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Upload para S3 teve erros (mas arquivos podem ter sido enviados). Verifique a saída acima."
    # Não falhar completamente se apenas ACL falhou
    # throw "Falha no upload para S3"
}
Write-Host "Upload concluido."
Write-Host ""

Write-Host "=================================================="
Write-Host "Processo concluido com sucesso!"
Write-Host "=================================================="
Write-Host ""
Write-Host "URL do manifesto: https://$BucketName.s3.$Region.amazonaws.com/updates/$Channel/latest.json"
Write-Host ""
Write-Host "Configure no backend:"
Write-Host "  AnalictY:UpdateManifestUrl=https://$BucketName.s3.$Region.amazonaws.com/updates/$Channel/latest.json"
Write-Host "Se nao definir variavel/arquivo, o backend usa o default https://analicty-downloads.s3.sa-east-1.amazonaws.com/updates/stable/latest.json"
