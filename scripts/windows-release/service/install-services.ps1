param(
    [string]$InstallRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$AdminPassword = ""
)

$ErrorActionPreference = "Stop"

function New-JwtKey {
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }
    return [Convert]::ToBase64String($bytes)
}

function New-SecretKey {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }
    return [Convert]::ToBase64String($bytes)
}

function New-DatabasePassword {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }
    return ([Convert]::ToBase64String($bytes)).TrimEnd("=").Replace("+", "A").Replace("/", "z")
}

function Escape-SqlLiteral {
    param([string]$Value)
    return $Value.Replace("'", "''")
}

function Install-WinSwService($serviceName) {
    $exe = Join-Path $PSScriptRoot "$serviceName.Service.exe"
    if (-not (Test-Path $exe)) {
        throw "WinSW nao encontrado: $exe"
    }

    & $exe stop
    & $exe uninstall
    & $exe install
    & $exe start
}

function Wait-TcpPort {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            try {
                $connectTask = $client.ConnectAsync($HostName, $Port)
                if ($connectTask.Wait(1500) -and $client.Connected) {
                    return $true
                }
            } finally {
                $client.Dispose()
            }
        } catch {
        }

        Start-Sleep -Seconds 2
    }

    return $false
}

function Ensure-FirewallRule($displayName, $port, $profile = "Domain,Private") {
    if (-not (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
        Write-Warning "New-NetFirewallRule nao disponivel. Configure a porta $port manualmente no firewall."
        return
    }

    $existing = Get-NetFirewallRule -DisplayName $displayName -ErrorAction SilentlyContinue
    if ($existing) {
        $existing | Set-NetFirewallRule -Enabled True -Profile $profile -Action Allow
        return
    }

    New-NetFirewallRule `
        -DisplayName $displayName `
        -Direction Inbound `
        -Protocol TCP `
        -LocalPort $port `
        -Action Allow `
        -Profile $profile `
        | Out-Null
}

function Ensure-FirewallUdpRule($displayName, $port, $profile = "Domain,Private") {
    if (-not (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
        Write-Warning "New-NetFirewallRule nao disponivel. Configure a porta UDP $port manualmente no firewall."
        return
    }

    $existing = Get-NetFirewallRule -DisplayName $displayName -ErrorAction SilentlyContinue
    if ($existing) {
        $existing | Set-NetFirewallRule -Enabled True -Profile $profile -Action Allow
        return
    }

    New-NetFirewallRule `
        -DisplayName $displayName `
        -Direction Inbound `
        -Protocol UDP `
        -LocalPort $port `
        -Action Allow `
        -Profile $profile `
        | Out-Null
}

function Ensure-HostsAlias {
    param(
        [string]$HostName
    )

    $hostsPath = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"
    $entry = "127.0.0.1`t$HostName"
    $content = Get-Content -Path $hostsPath -ErrorAction SilentlyContinue
    $hasAlias = $content | Where-Object { $_ -match "^\s*127\.0\.0\.1\s+$([regex]::Escape($HostName))(\s|$)" }

    if (-not $hasAlias) {
        Add-Content -Path $hostsPath -Value $entry -Encoding ASCII
    }
}

function Ensure-HttpsCertificate {
    param(
        [string]$JwtKey
    )

    $certDir = Join-Path $InstallRoot "data\certs"
    New-Item -ItemType Directory -Force -Path $certDir | Out-Null

    $pfxPath = Join-Path $certDir "analicty.pfx"
    $cerPath = Join-Path $certDir "analicty-root.cer"
    $passwordPath = Join-Path $certDir "analicty.pfx.password"

    if (-not (Test-Path $passwordPath)) {
        New-SecretKey | Set-Content -Path $passwordPath -Encoding ASCII
    }
    $certificatePassword = (Get-Content -Path $passwordPath -Raw).Trim()
    $securePassword = ConvertTo-SecureString $certificatePassword -AsPlainText -Force

    if (-not (Test-Path $pfxPath)) {
        $certificate = New-SelfSignedCertificate `
            -DnsName "analicty", "analicty.local", "localhost" `
            -FriendlyName "AnalictY Local HTTPS" `
            -CertStoreLocation "Cert:\LocalMachine\My" `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy Exportable `
            -NotAfter (Get-Date).AddYears(5)

        Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword | Out-Null
        Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null
    }

    if (Test-Path $cerPath) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
    }

    return @{
        Path = $pfxPath
        Password = $certificatePassword
    }
}

function Invoke-AdminBootstrap {
    param(
        [string]$Password,
        [string]$JwtKey
    )

    if ([string]::IsNullOrWhiteSpace($Password) -and -not $script:LocalMySqlConfig) {
        return
    }

    $backendExe = Join-Path $InstallRoot "app\backend\Scada.Api.exe"
    if (-not (Test-Path $backendExe)) {
        throw "Backend nao encontrado para bootstrap: $backendExe"
    }

    $processInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processInfo.FileName = $backendExe
    $processInfo.Arguments = "--bootstrap-admin"
    $processInfo.WorkingDirectory = Split-Path -Parent $backendExe
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    $processInfo.EnvironmentVariables["ANALICTY_DATA"] = (Join-Path $InstallRoot "data")
    $processInfo.EnvironmentVariables["Jwt__Key"] = $JwtKey
    if (-not [string]::IsNullOrWhiteSpace($Password)) {
        $processInfo.EnvironmentVariables["SeedUsers__AdminPassword"] = $Password
    }

    if ($script:LocalMySqlConfig) {
        $processInfo.EnvironmentVariables["BootstrapMySql__Provider"] = "MySQL"
        $processInfo.EnvironmentVariables["BootstrapMySql__Name"] = "MySQL Local AnalictY"
        $processInfo.EnvironmentVariables["BootstrapMySql__Host"] = $script:LocalMySqlConfig.Host
        $processInfo.EnvironmentVariables["BootstrapMySql__Port"] = [string]$script:LocalMySqlConfig.Port
        $processInfo.EnvironmentVariables["BootstrapMySql__Database"] = $script:LocalMySqlConfig.Database
        $processInfo.EnvironmentVariables["BootstrapMySql__User"] = $script:LocalMySqlConfig.User
        $processInfo.EnvironmentVariables["BootstrapMySql__Password"] = $script:LocalMySqlConfig.Password
    }

    $process = [System.Diagnostics.Process]::Start($processInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        throw "Falha ao criar usuario admin inicial. Saida: $stdout $stderr"
    }
}

function Install-LocalMySql {
    $mysqlRoot = Join-Path $InstallRoot "runtime\mysql"
    $mysqldExe = Join-Path $mysqlRoot "bin\mysqld.exe"

    if (-not (Test-Path $mysqldExe)) {
        Write-Warning "Runtime MySQL local nao incluido nesta release. Instalacao seguira sem banco MES local embutido."
        return $null
    }

    $mysqlExe = Join-Path $mysqlRoot "bin\mysql.exe"
    if (-not (Test-Path $mysqlExe)) {
        throw "Cliente MySQL nao encontrado: $mysqlExe"
    }

    $dataRoot = Join-Path $InstallRoot "data"
    $mysqlData = Join-Path $dataRoot "mysql"
    $secretsRoot = Join-Path $dataRoot "secrets"
    $logsRoot = Join-Path $InstallRoot "logs\mysql"
    $configPath = Join-Path $secretsRoot "mysql.my.ini"
    $userPasswordPath = Join-Path $secretsRoot "mysql.user.password"
    $rootPasswordPath = Join-Path $secretsRoot "mysql.root.password"
    $databaseName = "mes_analicty"
    $databaseUser = "user_analicty"
    $databasePort = 3307

    New-Item -ItemType Directory -Force -Path $mysqlData, $secretsRoot, $logsRoot | Out-Null

    if (-not (Test-Path $userPasswordPath)) {
        New-DatabasePassword | Set-Content -Path $userPasswordPath -Encoding ASCII
    }
    if (-not (Test-Path $rootPasswordPath)) {
        New-DatabasePassword | Set-Content -Path $rootPasswordPath -Encoding ASCII
    }

    $databasePassword = (Get-Content -Path $userPasswordPath -Raw).Trim()
    $rootPassword = (Get-Content -Path $rootPasswordPath -Raw).Trim()
    $mysqlRootEscaped = $mysqlRoot.Replace("\", "/")
    $mysqlDataEscaped = $mysqlData.Replace("\", "/")

    function Write-MySqlConfig {
        param([string]$InitFile = "")

        $initFileLine = ""
        if (-not [string]::IsNullOrWhiteSpace($InitFile)) {
            $initFileEscaped = $InitFile.Replace("\", "/")
            $initFileLine = "init-file=$initFileEscaped"
        }

        @"
[mysqld]
basedir=$mysqlRootEscaped
datadir=$mysqlDataEscaped
port=$databasePort
bind-address=127.0.0.1
character-set-server=utf8mb4
collation-server=utf8mb4_unicode_ci
default_storage_engine=InnoDB
skip-name-resolve
$initFileLine

[client]
port=$databasePort
host=127.0.0.1
default-character-set=utf8mb4
"@ | Set-Content -Path $configPath -Encoding ASCII
    }

    Write-MySqlConfig

    $systemDatabase = Join-Path $mysqlData "mysql"
    $firstInstall = -not (Test-Path $systemDatabase)
    if ($firstInstall) {
        $legacyConfigPath = Join-Path $mysqlData "my.ini"
        if (Test-Path $legacyConfigPath) {
            Remove-Item -LiteralPath $legacyConfigPath -Force
        }

        Write-Host "Inicializando banco local AnalictY MySQL..."
        & $mysqldExe --defaults-file="$configPath" --initialize-insecure --console
        if ($LASTEXITCODE -ne 0) {
            throw "Falha ao inicializar MySQL local AnalictY."
        }
    }

    $sqlFile = Join-Path $mysqlData "bootstrap.sql"
    if ($firstInstall) {
        $databasePasswordSql = Escape-SqlLiteral $databasePassword
        $rootPasswordSql = Escape-SqlLiteral $rootPassword
$sql = @"
CREATE DATABASE IF NOT EXISTS $databaseName CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '$databaseUser'@'localhost' IDENTIFIED BY '$databasePasswordSql';
CREATE USER IF NOT EXISTS '$databaseUser'@'127.0.0.1' IDENTIFIED BY '$databasePasswordSql';
ALTER USER '$databaseUser'@'localhost' IDENTIFIED BY '$databasePasswordSql';
ALTER USER '$databaseUser'@'127.0.0.1' IDENTIFIED BY '$databasePasswordSql';
GRANT ALL PRIVILEGES ON $databaseName.* TO '$databaseUser'@'localhost';
GRANT ALL PRIVILEGES ON $databaseName.* TO '$databaseUser'@'127.0.0.1';
ALTER USER 'root'@'localhost' IDENTIFIED BY '$rootPasswordSql';
FLUSH PRIVILEGES;
"@
        $sql | Set-Content -Path $sqlFile -Encoding ASCII
        Write-MySqlConfig -InitFile $sqlFile
    }

    $mysqlTemplatePath = Join-Path $PSScriptRoot "AnalictY.MySQL.xml.template"
    $mysqlServicePath = Join-Path $PSScriptRoot "AnalictY.MySQL.Service.xml"
    if (Test-Path $mysqlTemplatePath) {
        $mysqlTemplate = Get-Content -Path $mysqlTemplatePath -Raw
        $mysqlTemplate.Replace("{{MYSQL_CONFIG_PATH}}", $configPath) |
            Set-Content -Path $mysqlServicePath -Encoding UTF8
    }

    if (Test-Path (Join-Path $PSScriptRoot "AnalictY.MySQL.Service.exe")) {
        Install-WinSwService "AnalictY.MySQL"
    } else {
        Write-Warning "WinSW do MySQL nao encontrado; banco local nao sera registrado como servico."
    }

    if (-not (Wait-TcpPort -HostName "127.0.0.1" -Port $databasePort -TimeoutSeconds 90)) {
        throw "MySQL local AnalictY nao respondeu na porta $databasePort."
    }

    if ($firstInstall) {
        Write-MySqlConfig
        Install-WinSwService "AnalictY.MySQL"
        if (-not (Wait-TcpPort -HostName "127.0.0.1" -Port $databasePort -TimeoutSeconds 90)) {
            throw "MySQL local AnalictY nao respondeu apos bootstrap."
        }

        & $mysqlExe --protocol=tcp --host=127.0.0.1 --port=$databasePort --user=$databaseUser --password=$databasePassword --database=$databaseName --execute="SELECT 1;"
        if ($LASTEXITCODE -ne 0) {
            throw "Falha ao validar usuario MySQL local AnalictY."
        }
        Remove-Item -LiteralPath $sqlFile -Force -ErrorAction SilentlyContinue
    }

    return @{
        Host = "127.0.0.1"
        Port = $databasePort
        Database = $databaseName
        User = $databaseUser
        Password = $databasePassword
    }
}

$jwtFile = Join-Path $InstallRoot "data\jwt.key"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jwtFile), (Join-Path $InstallRoot "logs\backend"), (Join-Path $InstallRoot "logs\frontend") | Out-Null

if (-not (Test-Path $jwtFile)) {
    New-JwtKey | Set-Content -Path $jwtFile -Encoding ASCII
}
$jwtKey = (Get-Content -Path $jwtFile -Raw).Trim()
$httpsCertificate = Ensure-HttpsCertificate -JwtKey $jwtKey
Ensure-HostsAlias "analicty"

$script:LocalMySqlConfig = Install-LocalMySql

Invoke-AdminBootstrap -Password $AdminPassword -JwtKey $jwtKey

$backendTemplate = Get-Content -Path (Join-Path $PSScriptRoot "AnalictY.Backend.xml.template") -Raw
$frontendTemplate = Get-Content -Path (Join-Path $PSScriptRoot "AnalictY.Frontend.xml.template") -Raw

$backendTemplate.
    Replace("{{JWT_KEY}}", $jwtKey).
    Replace("{{HTTPS_CERTIFICATE_PATH}}", $httpsCertificate.Path).
    Replace("{{HTTPS_CERTIFICATE_PASSWORD}}", $httpsCertificate.Password) |
    Set-Content -Path (Join-Path $PSScriptRoot "AnalictY.Backend.Service.xml") -Encoding UTF8
$frontendTemplate | Set-Content -Path (Join-Path $PSScriptRoot "AnalictY.Frontend.Service.xml") -Encoding UTF8

Ensure-FirewallRule "AnalictY Frontend 3000" 3000 "Any"
Ensure-FirewallRule "AnalictY Backend 5000" 5000
Ensure-FirewallRule "AnalictY HTTPS 443" 443
Ensure-FirewallUdpRule "AnalictY mDNS 5353" 5353

Install-WinSwService "AnalictY.Backend"
Install-WinSwService "AnalictY.Frontend"

Write-Host "Servicos AnalictY instalados e iniciados."
