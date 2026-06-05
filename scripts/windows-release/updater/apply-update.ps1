param(
    [Parameter(Mandatory = $true)]
    [string]$InstallRoot,

    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$TargetVersion = "",
    [string]$ExpectedSha256 = "",
    [string]$BackendHealthUrl = "http://127.0.0.1:5000/api/system/health",
    [string]$FrontendUrl = "http://127.0.0.1:3000",
    [int]$HealthTimeoutSeconds = 90,
    [switch]$SkipHealthCheck,
    [switch]$SkipHashCheck
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath($path) {
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($path)
}

function Assert-ChildPath($parent, $child, $label) {
    $parentFull = [System.IO.Path]::GetFullPath($parent).TrimEnd('\')
    $childFull = [System.IO.Path]::GetFullPath($child).TrimEnd('\')

    if ($childFull -ne $parentFull -and -not $childFull.StartsWith("$parentFull\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$label fora do diretorio permitido: $childFull"
    }
}

function Write-Log($message) {
    $line = "{0} {1}" -f (Get-Date).ToString("s"), $message
    $line | Tee-Object -FilePath $script:LogFile -Append
}

function Invoke-ServiceCommand($serviceName, $command) {
    $exe = Join-Path $script:ServiceRoot "$serviceName.Service.exe"

    if (Test-Path $exe) {
        Write-Log "$command via WinSW: $serviceName"
        & $exe $command | Out-String | ForEach-Object { if (-not [string]::IsNullOrWhiteSpace($_)) { Write-Log $_.Trim() } }
        return
    }

    $windowsService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($windowsService) {
        Write-Log "$command via Windows Service: $serviceName"
        if ($command -eq "stop" -and $windowsService.Status -ne "Stopped") {
            Stop-Service -Name $serviceName -Force -ErrorAction Stop
        }
        if ($command -eq "start" -and $windowsService.Status -ne "Running") {
            Start-Service -Name $serviceName -ErrorAction Stop
        }
        return
    }

    Write-Log "Servico nao encontrado para ${command}: $serviceName"
}

function Invoke-WithRetry($description, [scriptblock]$action, $attempts = 8, $delaySeconds = 2) {
    $lastError = $null

    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        try {
            & $action
            return
        } catch {
            $lastError = $_.Exception.Message
            Write-Log "$description falhou na tentativa $attempt/${attempts}: $lastError"
            Start-Sleep -Seconds $delaySeconds
        }
    }

    throw "$description falhou apos $attempts tentativas. Ultimo erro: $lastError"
}

function Stop-InstallRootProcesses {
    $installRoot = [System.IO.Path]::GetFullPath($script:InstallRootFull).TrimEnd('\')
    Write-Log "Verificando processos remanescentes da instalacao."

    try {
        $processes = Get-CimInstance Win32_Process | Where-Object {
            ($_.ExecutablePath -and [System.IO.Path]::GetFullPath($_.ExecutablePath).StartsWith($installRoot, [System.StringComparison]::OrdinalIgnoreCase)) -or
            ($_.CommandLine -and $_.CommandLine.IndexOf($installRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
        }

        foreach ($process in $processes) {
            if ($process.ProcessId -eq $PID) {
                continue
            }

            Write-Log "Encerrando processo AnalictY remanescente: PID=$($process.ProcessId) $($process.Name)"
            Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        }
    } catch {
        Write-Log "Nao foi possivel enumerar processos remanescentes: $($_.Exception.Message)"
    }
}

function Stop-AnalictyServices {
    Invoke-ServiceCommand "AnalictY.Frontend" "stop"
    Invoke-ServiceCommand "AnalictY.Backend" "stop"
    Invoke-ServiceCommand "AnalictY.MySQL" "stop"
    Start-Sleep -Seconds 5
    Stop-InstallRootProcesses
}

function Start-AnalictyServices {
    Invoke-ServiceCommand "AnalictY.MySQL" "start"
    Invoke-ServiceCommand "AnalictY.Backend" "start"
    Invoke-ServiceCommand "AnalictY.Frontend" "start"
}

function Invoke-InstallServices {
    $installScript = Join-Path $script:ServiceRoot "install-services.ps1"
    if (-not (Test-Path $installScript)) {
        Write-Log "install-services.ps1 nao encontrado; iniciando servicos diretamente."
        Start-AnalictyServices
        return
    }

    Write-Log "Reconfigurando servicos AnalictY apos atualizacao."
    & powershell.exe -ExecutionPolicy Bypass -NoProfile -File $installScript -InstallRoot $script:InstallRootFull
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao reconfigurar servicos AnalictY apos atualizacao."
    }
}

function Wait-HttpOk($url, $timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $lastError = ""

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
            if ([int]$response.StatusCode -ge 200 -and [int]$response.StatusCode -lt 500) {
                Write-Log "Health OK: $url -> $($response.StatusCode)"
                return
            }
        } catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Seconds 3
    }

    throw "Health check falhou para $url. Ultimo erro: $lastError"
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

function Replace-Directory($source, $destination, $backupRoot, $backupName) {
    if (-not (Test-Path $source)) {
        return
    }

    Assert-ChildPath $script:InstallRootFull $destination "Destino da atualizacao"

    if (Test-Path $destination) {
        $backupPath = Join-Path $backupRoot $backupName
        Write-Log "Backup: $destination -> $backupPath"
        Invoke-WithRetry "Backup de $destination" {
            Move-Item -LiteralPath $destination -Destination $backupPath -Force
        }
    }

    Write-Log "Aplicando: $source -> $destination"
    Invoke-WithRetry "Copia de $source" {
        Copy-DirectoryContents $source $destination
    }
}

$script:InstallRootFull = Resolve-FullPath $InstallRoot
$packageFull = Resolve-FullPath $PackagePath
$script:ServiceRoot = Join-Path $script:InstallRootFull "service"
$dataRoot = Join-Path $script:InstallRootFull "data"
$updatesRoot = Join-Path $dataRoot "updates"
$backupRoot = Join-Path $dataRoot "backups"
$logsRoot = Join-Path $script:InstallRootFull "logs\updater"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$safeTargetVersion = if ([string]::IsNullOrWhiteSpace($TargetVersion)) { "unknown" } else { $TargetVersion -replace '[^\w\.\-]', '_' }
$stagingRoot = Join-Path $updatesRoot "staging-$timestamp"
$script:LogFile = Join-Path $logsRoot "update-$timestamp.log"

if (-not (Test-Path $script:InstallRootFull)) {
    throw "InstallRoot nao encontrado: $script:InstallRootFull"
}
if (-not (Test-Path $packageFull)) {
    throw "Pacote de atualizacao nao encontrado: $packageFull"
}

New-Item -ItemType Directory -Force -Path $updatesRoot, $backupRoot, $logsRoot | Out-Null
Assert-ChildPath $script:InstallRootFull $updatesRoot "Pasta de updates"
Assert-ChildPath $script:InstallRootFull $backupRoot "Pasta de backup"
Assert-ChildPath $script:InstallRootFull $logsRoot "Pasta de logs"
Assert-ChildPath $updatesRoot $stagingRoot "Pasta de staging"

Write-Log "Iniciando atualizacao AnalictY"
Write-Log "InstallRoot: $script:InstallRootFull"
Write-Log "PackagePath: $packageFull"
Write-Log "TargetVersion: $safeTargetVersion"

try {
    if (-not $SkipHashCheck -and -not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {
        $actualHash = (Get-FileHash -LiteralPath $packageFull -Algorithm SHA256).Hash.ToLowerInvariant()
        $expectedHash = $ExpectedSha256.Trim().ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "SHA256 invalido. Esperado=$expectedHash Atual=$actualHash"
        }
        Write-Log "SHA256 validado."
    } elseif (-not $SkipHashCheck) {
        Write-Log "SHA256 nao informado; validacao de hash pulada."
    } else {
        Write-Log "Validacao de hash pulada por parametro."
    }

    if (Test-Path $stagingRoot) {
        Assert-ChildPath $updatesRoot $stagingRoot "Limpeza de staging"
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
    Write-Log "Extraindo pacote em staging."
    Expand-Archive -LiteralPath $packageFull -DestinationPath $stagingRoot -Force

    $stagedApp = Join-Path $stagingRoot "app"
    $stagedRuntime = Join-Path $stagingRoot "runtime"
    $stagedInstaller = Join-Path $stagingRoot "installer"
    $stagedService = Join-Path $stagingRoot "service"
    $stagedUpdater = Join-Path $stagingRoot "updater"

    if (-not (Test-Path $stagedApp) -and -not (Test-Path $stagedRuntime)) {
        throw "Pacote invalido: esperado diretorio app e/ou runtime na raiz do ZIP."
    }

    $currentApp = Join-Path $script:InstallRootFull "app"
    $currentRuntime = Join-Path $script:InstallRootFull "runtime"
    $currentInstaller = Join-Path $script:InstallRootFull "installer"
    $currentService = Join-Path $script:InstallRootFull "service"
    $currentUpdater = Join-Path $script:InstallRootFull "updater"
    $updateBackupRoot = Join-Path $backupRoot "update-$safeTargetVersion-$timestamp"
    New-Item -ItemType Directory -Force -Path $updateBackupRoot | Out-Null
    Assert-ChildPath $backupRoot $updateBackupRoot "Backup da atualizacao"

    $dbPath = Join-Path $dataRoot "scada.db"
    if (Test-Path $dbPath) {
        $dbBackup = Join-Path $updateBackupRoot "scada-$timestamp.db"
        Write-Log "Backup SQLite: $dbBackup"
        Copy-Item -LiteralPath $dbPath -Destination $dbBackup -Force
    }

    Stop-AnalictyServices

    try {
        Replace-Directory $stagedApp $currentApp $updateBackupRoot "app"
        Replace-Directory $stagedRuntime $currentRuntime $updateBackupRoot "runtime"
        Replace-Directory $stagedService $currentService $updateBackupRoot "service"
        Replace-Directory $stagedUpdater $currentUpdater $updateBackupRoot "updater"

        if (Test-Path $stagedInstaller) {
            New-Item -ItemType Directory -Force -Path $currentInstaller | Out-Null
            Copy-DirectoryContents $stagedInstaller $currentInstaller
        }

        Invoke-InstallServices

        if (-not $SkipHealthCheck) {
            Write-Log "Aguardando health check do backend."
            Wait-HttpOk $BackendHealthUrl $HealthTimeoutSeconds
            Write-Log "Aguardando resposta do frontend."
            Wait-HttpOk $FrontendUrl $HealthTimeoutSeconds
        } else {
            Write-Log "Health check pulado por parametro."
        }
    } catch {
        Write-Log "Falha aplicando arquivos. Iniciando rollback: $($_.Exception.Message)"

        $backupApp = Join-Path $updateBackupRoot "app"
        $backupRuntime = Join-Path $updateBackupRoot "runtime"
        $backupService = Join-Path $updateBackupRoot "service"
        $backupUpdater = Join-Path $updateBackupRoot "updater"

        if (Test-Path $backupApp) {
            if (Test-Path $currentApp) {
                Assert-ChildPath $script:InstallRootFull $currentApp "Rollback app"
                Invoke-WithRetry "Remocao do app parcial para rollback" {
                    Remove-Item -LiteralPath $currentApp -Recurse -Force
                }
            }
            Invoke-WithRetry "Restauracao do app anterior" {
                Move-Item -LiteralPath $backupApp -Destination $currentApp -Force
            }
        }

        if (Test-Path $backupRuntime) {
            if (Test-Path $currentRuntime) {
                Assert-ChildPath $script:InstallRootFull $currentRuntime "Rollback runtime"
                Invoke-WithRetry "Remocao do runtime parcial para rollback" {
                    Remove-Item -LiteralPath $currentRuntime -Recurse -Force
                }
            }
            Invoke-WithRetry "Restauracao do runtime anterior" {
                Move-Item -LiteralPath $backupRuntime -Destination $currentRuntime -Force
            }
        }

        if (Test-Path $backupService) {
            if (Test-Path $currentService) {
                Assert-ChildPath $script:InstallRootFull $currentService "Rollback service"
                Invoke-WithRetry "Remocao do service parcial para rollback" {
                    Remove-Item -LiteralPath $currentService -Recurse -Force
                }
            }
            Invoke-WithRetry "Restauracao do service anterior" {
                Move-Item -LiteralPath $backupService -Destination $currentService -Force
            }
        }

        if (Test-Path $backupUpdater) {
            if (Test-Path $currentUpdater) {
                Assert-ChildPath $script:InstallRootFull $currentUpdater "Rollback updater"
                Invoke-WithRetry "Remocao do updater parcial para rollback" {
                    Remove-Item -LiteralPath $currentUpdater -Recurse -Force
                }
            }
            Invoke-WithRetry "Restauracao do updater anterior" {
                Move-Item -LiteralPath $backupUpdater -Destination $currentUpdater -Force
            }
        }

        Start-AnalictyServices
        throw
    }

    Write-Log "Atualizacao concluida com sucesso."
    exit 0
} catch {
    Write-Log "Atualizacao falhou: $($_.Exception.Message)"
    exit 1
} finally {
    if (Test-Path $stagingRoot) {
        Assert-ChildPath $updatesRoot $stagingRoot "Limpeza final de staging"
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
