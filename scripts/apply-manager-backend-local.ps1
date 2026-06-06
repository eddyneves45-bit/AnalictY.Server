param(
    [string]$InstallRoot = "C:\Program Files\AnalictY",
    [string]$PublishedBackend = "C:\Users\admin.automacao\CascadeProjects\AnalictY.Server\release\backend-0.1.7-manager-admin"
)

$ErrorActionPreference = "Stop"

function Assert-Path($Path, $Description) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description nao encontrado: $Path"
    }
}

$target = Join-Path $InstallRoot "app\backend"
$data = Join-Path $InstallRoot "data"
$backupRoot = Join-Path $data "backups"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $backupRoot "manual-backend-before-manager-$stamp"

Assert-Path (Join-Path $PublishedBackend "Scada.Api.exe") "Backend publicado"
Assert-Path (Join-Path $target "Scada.Api.exe") "Backend instalado"
Assert-Path $data "Pasta de dados"

Write-Host "AnalictY - aplicando backend com endpoints do Manager"
Write-Host "Instalacao: $InstallRoot"
Write-Host "Origem:     $PublishedBackend"
Write-Host "Destino:    $target"
Write-Host "Backup:     $backup"
Write-Host ""
Write-Host "A pasta data sera preservada: $data"
Write-Host ""

New-Item -ItemType Directory -Force -Path $backup | Out-Null
Copy-Item -LiteralPath $target -Destination $backup -Recurse -Force

Write-Host "Parando AnalictYBackend..."
Stop-Service AnalictYBackend -Force
Start-Sleep -Seconds 3

Write-Host "Atualizando arquivos do backend..."
Copy-Item -Path (Join-Path $PublishedBackend "*") -Destination $target -Recurse -Force

Write-Host "Iniciando AnalictYBackend..."
Start-Service AnalictYBackend
Start-Sleep -Seconds 8

Write-Host ""
Get-Service AnalictYBackend | Select-Object Status,Name,DisplayName | Format-Table -AutoSize

Write-Host "Testando API..."
Invoke-WebRequest http://127.0.0.1:5000/api/system/health -UseBasicParsing -TimeoutSec 15 |
    Select-Object StatusCode,Content |
    Format-List

Write-Host "Testando endpoint do Manager..."
Invoke-WebRequest http://127.0.0.1:5000/api/admin/server/overview -UseBasicParsing -TimeoutSec 15 |
    Select-Object StatusCode,Content |
    Format-List

Write-Host ""
Write-Host "Concluido. Backup do backend anterior: $backup"
