param(
    [string]$InstallRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

function Uninstall-WinSwService($serviceName) {
    $exe = Join-Path $PSScriptRoot "$serviceName.Service.exe"
    if (-not (Test-Path $exe)) {
        Write-Warning "WinSW nao encontrado: $exe"
        return
    }

    & $exe stop
    & $exe uninstall
}

Uninstall-WinSwService "AnalictY.Frontend"
Uninstall-WinSwService "AnalictY.Backend"
Uninstall-WinSwService "AnalictY.MySQL"

Write-Host "Servicos AnalictY removidos."
