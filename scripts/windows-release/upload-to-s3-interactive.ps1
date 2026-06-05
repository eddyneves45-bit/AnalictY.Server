param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseRoot,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$BucketName = "analicty-downloads",
    [string]$Region = "sa-east-1",
    [string]$Channel = "stable",
    [string[]]$Changelog = @(),
    [switch]$UseConfiguredCredentials
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..\..")).Path
}

function ConvertFrom-SecureStringToPlainText {
    param(
        [Parameter(Mandatory = $true)]
        [securestring]$Value
    )

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

$repoRoot = Resolve-RepoRoot
$uploadScript = Join-Path $repoRoot "scripts\windows-release\upload-to-s3.ps1"

if (-not (Test-Path $uploadScript)) {
    throw "Script de upload nao encontrado: $uploadScript"
}

$oldAccessKey = $env:AWS_ACCESS_KEY_ID
$oldSecretKey = $env:AWS_SECRET_ACCESS_KEY
$oldRegion = $env:AWS_DEFAULT_REGION

try {
    $env:AWS_DEFAULT_REGION = $Region

    if (-not $UseConfiguredCredentials) {
        Write-Host "Credenciais AWS para esta execucao"
        Write-Host "As chaves serao usadas somente neste processo e nao serao gravadas no repositorio."
        Write-Host ""

        $accessKey = Read-Host "AWS Access Key ID"
        $secretKeySecure = Read-Host "AWS Secret Access Key" -AsSecureString
        $secretKey = ConvertFrom-SecureStringToPlainText -Value $secretKeySecure

        if ([string]::IsNullOrWhiteSpace($accessKey) -or [string]::IsNullOrWhiteSpace($secretKey)) {
            throw "Access Key e Secret Key sao obrigatorias."
        }

        $env:AWS_ACCESS_KEY_ID = $accessKey.Trim()
        $env:AWS_SECRET_ACCESS_KEY = $secretKey
    }

    & $uploadScript `
        -ReleaseRoot $ReleaseRoot `
        -Version $Version `
        -BucketName $BucketName `
        -Region $Region `
        -Channel $Channel `
        -Changelog $Changelog

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
} finally {
    $env:AWS_ACCESS_KEY_ID = $oldAccessKey
    $env:AWS_SECRET_ACCESS_KEY = $oldSecretKey
    $env:AWS_DEFAULT_REGION = $oldRegion
}
