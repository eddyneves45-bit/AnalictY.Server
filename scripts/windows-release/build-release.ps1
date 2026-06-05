param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ReleaseRoot = "",
    [string]$NodeZipPath = "",
    [string]$MySqlZipPath = "",
    [string]$WinSWExePath = "",
    [string]$InnoSetupCompiler = "",
    [switch]$InstallDependencies,
    [switch]$SkipFrontendBuild,
    [switch]$SkipBackendBuild,
    [switch]$SkipAgentBuild,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..\..")).Path
}

function Ensure-Command($name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Comando obrigatorio nao encontrado no PATH: $name"
    }
}

function Assert-NativeSuccess($description) {
    if ($LASTEXITCODE -ne 0) {
        throw "$description falhou com codigo de saida $LASTEXITCODE."
    }
}

function Copy-Directory($source, $destination) {
    if (-not (Test-Path $source)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    }
}

$repoRoot = Resolve-RepoRoot
if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    $ReleaseRoot = Join-Path $repoRoot "release\AnalictY-$Version"
}

$releaseRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReleaseRoot)
$appRoot = Join-Path $releaseRoot "app"
$backendOut = Join-Path $appRoot "backend"
$frontendOut = Join-Path $appRoot "frontend"
$agentOut = Join-Path $appRoot "agent"
$runtimeOut = Join-Path $releaseRoot "runtime"
$serviceOut = Join-Path $releaseRoot "service"
$updaterOut = Join-Path $releaseRoot "updater"
$installerOut = Join-Path $releaseRoot "installer"
$logsOut = Join-Path $releaseRoot "logs"
$dataOut = Join-Path $releaseRoot "data"

Write-Host "AnalictY Windows release"
Write-Host "Repo:    $repoRoot"
Write-Host "Version: $Version"
Write-Host "Output:  $releaseRoot"

Remove-Item -LiteralPath $releaseRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $backendOut, $frontendOut, $agentOut, $runtimeOut, $serviceOut, $updaterOut, $installerOut, $logsOut, $dataOut | Out-Null

if (-not $SkipBackendBuild) {
    Ensure-Command dotnet
    $project = Join-Path $repoRoot "backend\Scada.Api\Scada.Api.csproj"
    dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $backendOut /p:NuGetAudit=false
    Assert-NativeSuccess "Build do backend"
} else {
    Write-Host "Pulando build do backend."
}

if (-not $SkipFrontendBuild) {
    Ensure-Command npm
    Push-Location (Join-Path $repoRoot "frontend")
    try {
        if ($InstallDependencies) {
            npm ci
        }

        npm run build
        Assert-NativeSuccess "Build do frontend"

        $standalone = Join-Path $repoRoot "frontend\.next\standalone"
        $static = Join-Path $repoRoot "frontend\.next\static"
        $public = Join-Path $repoRoot "frontend\public"

        if (-not (Test-Path $standalone)) {
            throw "Build standalone nao encontrado em $standalone. Confirme output: 'standalone' no next.config.js."
        }

        Copy-Directory $standalone $frontendOut
        Copy-Directory $static (Join-Path $frontendOut ".next\static")
        Copy-Directory (Join-Path $repoRoot "frontend\.next\server\chunks") (Join-Path $frontendOut ".next\server\chunks")
        Copy-Directory (Join-Path $repoRoot "frontend\.next\server\vendor-chunks") (Join-Path $frontendOut ".next\server\vendor-chunks")
        Copy-Directory (Join-Path $repoRoot "frontend\node_modules\next\dist\compiled\next-server") (Join-Path $frontendOut "node_modules\next\dist\compiled\next-server")
        Copy-Directory $public (Join-Path $frontendOut "public")
    } finally {
        Pop-Location
    }
} else {
    Write-Host "Pulando build do frontend."
}

if (-not $SkipAgentBuild) {
    Ensure-Command dotnet
    $agentProject = Join-Path $repoRoot "agent\AnalictY.Agent\AnalictY.Agent.csproj"
    dotnet publish $agentProject -c $Configuration -r $Runtime --self-contained true -o $agentOut /p:NuGetAudit=false /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
    Assert-NativeSuccess "Build do AnalictY Agent"
} else {
    Write-Host "Pulando build do AnalictY Agent."
}

if (-not [string]::IsNullOrWhiteSpace($NodeZipPath)) {
    $nodeRuntime = Join-Path $runtimeOut "node"
    New-Item -ItemType Directory -Force -Path $nodeRuntime | Out-Null
    Expand-Archive -LiteralPath $NodeZipPath -DestinationPath $runtimeOut -Force
    $expandedNode = Get-ChildItem -LiteralPath $runtimeOut -Directory | Where-Object { $_.Name -like "node-*" } | Select-Object -First 1
    if ($expandedNode) {
        if (Test-Path $nodeRuntime) {
            Remove-Item -LiteralPath $nodeRuntime -Recurse -Force
        }
        Move-Item -LiteralPath $expandedNode.FullName -Destination $nodeRuntime
    }
} else {
    @"
Node.js portatil nao foi incluido nesta release.

Para gerar release instalavel, baixe o ZIP Windows x64 do Node.js e rode:

.\scripts\windows-release\build-release.ps1 -Version $Version -NodeZipPath C:\caminho\node-vXX-win-x64.zip -WinSWExePath C:\caminho\WinSW-x64.exe
"@ | Set-Content -Path (Join-Path $runtimeOut "NODE_RUNTIME_AUSENTE.txt") -Encoding UTF8
}

if (-not [string]::IsNullOrWhiteSpace($MySqlZipPath)) {
    $mysqlRuntime = Join-Path $runtimeOut "mysql"
    New-Item -ItemType Directory -Force -Path $mysqlRuntime | Out-Null
    Expand-Archive -LiteralPath $MySqlZipPath -DestinationPath $runtimeOut -Force
    $expandedMySql = Get-ChildItem -LiteralPath $runtimeOut -Directory |
        Where-Object { $_.Name -like "mysql-*" -or $_.Name -like "mariadb-*" } |
        Select-Object -First 1
    if ($expandedMySql) {
        if (Test-Path $mysqlRuntime) {
            Remove-Item -LiteralPath $mysqlRuntime -Recurse -Force
        }
        Move-Item -LiteralPath $expandedMySql.FullName -Destination $mysqlRuntime
    }
} else {
    @"
MySQL portatil nao foi incluido nesta release.

Para gerar instalador com banco local embutido, baixe o ZIP Windows x64 do MySQL Community Server e rode:

.\scripts\windows-release\build-release.ps1 -Version $Version -NodeZipPath C:\caminho\node-vXX-win-x64.zip -MySqlZipPath C:\caminho\mysql-X.X.X-winx64.zip -WinSWExePath C:\caminho\WinSW-x64.exe
"@ | Set-Content -Path (Join-Path $runtimeOut "MYSQL_RUNTIME_AUSENTE.txt") -Encoding UTF8
}

$templateRoot = Join-Path $repoRoot "scripts\windows-release\service"
Copy-Item -LiteralPath (Join-Path $templateRoot "install-services.ps1") -Destination $serviceOut -Force
Copy-Item -LiteralPath (Join-Path $templateRoot "uninstall-services.ps1") -Destination $serviceOut -Force
Copy-Item -LiteralPath (Join-Path $templateRoot "AnalictY.Backend.xml.template") -Destination $serviceOut -Force
Copy-Item -LiteralPath (Join-Path $templateRoot "AnalictY.Frontend.xml.template") -Destination $serviceOut -Force
Copy-Item -LiteralPath (Join-Path $templateRoot "AnalictY.MySQL.xml.template") -Destination $serviceOut -Force

$updaterRoot = Join-Path $repoRoot "scripts\windows-release\updater"
Copy-Item -LiteralPath (Join-Path $updaterRoot "apply-update.ps1") -Destination $updaterOut -Force

if (-not [string]::IsNullOrWhiteSpace($WinSWExePath)) {
    Copy-Item -LiteralPath $WinSWExePath -Destination (Join-Path $serviceOut "AnalictY.Backend.Service.exe") -Force
    Copy-Item -LiteralPath $WinSWExePath -Destination (Join-Path $serviceOut "AnalictY.Frontend.Service.exe") -Force
    Copy-Item -LiteralPath $WinSWExePath -Destination (Join-Path $serviceOut "AnalictY.MySQL.Service.exe") -Force
} else {
    @"
WinSW nao foi incluido nesta release.

Baixe WinSW x64 e rode:

.\scripts\windows-release\build-release.ps1 -Version $Version -NodeZipPath C:\caminho\node-vXX-win-x64.zip -WinSWExePath C:\caminho\WinSW-x64.exe
"@ | Set-Content -Path (Join-Path $serviceOut "WINSW_AUSENTE.txt") -Encoding UTF8
}

$versionJson = @{
    product = "AnalictY"
    version = $Version
    channel = "stable"
    built_at = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Depth 3
$versionJson | Set-Content -Path (Join-Path $installerOut "version.json") -Encoding UTF8

$installerAssets = Join-Path $repoRoot "scripts\windows-release\installer\assets"
if (Test-Path $installerAssets) {
    Copy-Item -LiteralPath $installerAssets -Destination (Join-Path $installerOut "assets") -Recurse -Force
}

if (-not $SkipInstaller) {
    if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
        $defaultIscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        if (Test-Path $defaultIscc) {
            $InnoSetupCompiler = $defaultIscc
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompiler) -and (Test-Path $InnoSetupCompiler)) {
        $iss = Join-Path $repoRoot "scripts\windows-release\installer\AnalictY.iss"
        $setupOut = Join-Path $repoRoot "release\installer"
        New-Item -ItemType Directory -Force -Path $setupOut | Out-Null
        & $InnoSetupCompiler "/DReleaseRoot=$releaseRoot" "/DAppVersion=$Version" "/O$setupOut" $iss
        Assert-NativeSuccess "Build do instalador Inno Setup"
    } else {
        Write-Warning "Inno Setup Compiler nao encontrado. Release de arquivos criada, mas setup .exe nao foi gerado."
    }
}

Write-Host "Release pronta em: $releaseRoot"
