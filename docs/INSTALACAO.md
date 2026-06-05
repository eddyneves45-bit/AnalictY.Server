# Guia de Instalacao

Guia para preparar o ambiente local de desenvolvimento/operacao do SCADA/MES.

## Modelo local padrao

O sistema sobe dois servicos principais no host local:

```text
Backend ASP.NET Core: http://localhost:5000
Frontend Next.js:     http://localhost:3000
SignalR:              http://localhost:5000/hubs/mes
```

O cliente pode decidir posteriormente se acessa por loopback, hostname, IP da maquina, VPN, dominio ou proxy. A aplicacao em si permanece local e configuravel.

## Pre-requisitos

### Backend

- Windows 10/11 ou Windows Server
- .NET 8 SDK
- PowerShell
- MySQL opcional para historico MES

### Frontend

- Node.js 18+ ou 20+
- npm
- Navegador moderno para desenvolvimento

### Desktop experimental

- Node.js/npm
- Electron instalado via `npm install` dentro de `desktop/`

## Instalacao rapida

Na raiz do projeto:

```powershell
.\bootstrap-system.ps1
.\start-system.ps1
```

O bootstrap:

- verifica dependencias;
- restaura pacotes do backend;
- instala dependencias do frontend;
- prepara `.env` local;
- configura MySQL MES quando solicitado;
- deixa o SQLite local ser criado/ajustado no primeiro start.

## Configuracao manual

Copie o exemplo de ambiente:

```powershell
Copy-Item .env.example .env
```

Preencha no `.env`:

```text
Jwt__Key=<chave-longa-aleatoria>
Jwt__Issuer=ScadaApi
Jwt__Audience=ScadaClient
SeedUsers__AdminPassword=<senha-admin-local>
```

Nao versionar `.env`.


## Usuarios iniciais

As credenciais iniciais sao definidas no `.env` antes do primeiro start do SQLite.

Padrao de desenvolvimento/local:

```text
admin / Admin@123
operator / Admin@123
viewer / Admin@123
```

Variaveis correspondentes:

```text
SeedUsers__AdminPassword=Admin@123
# SeedUsers__OperatorPassword=
# SeedUsers__ViewerPassword=
```

Se as senhas de `operator` e `viewer` nao forem informadas, elas herdam a senha do admin.

Importante: os usuarios iniciais so sao criados quando o banco SQLite ainda nao tem usuarios. Depois que usuarios existem, mudar o `.env` nao troca automaticamente as senhas ja gravadas.

Em producao, troque as senhas padrao no primeiro acesso ou configure senhas fortes antes do primeiro start.

## Backend manual

```powershell
dotnet restore backend\Scada.Api\Scada.Api.csproj
dotnet run --project backend\Scada.Api\Scada.Api.csproj
```

API:

```text
http://localhost:5000
```

## Frontend manual

```powershell
Set-Location frontend
npm install
npm run dev
```

Interface:

```text
http://localhost:3000
```

## Desktop experimental

Com backend/frontend rodando:

```powershell
Set-Location desktop
npm install
npm start
```

A janela Electron abre `http://localhost:3000` por padrao.

URL customizada:

```powershell
$env:SCADA_MES_URL='http://localhost:3000'; npm start
```

## Certificados MQTT/TLS

Se usar MQTT com TLS, os certificados podem ficar em:

```text
backend/certs/
```

Exemplo:

```text
backend/certs/
|- AmazonRootCA1.pem
|- device-certificate.pem.crt
|- device-private.pem.key
|- device.pfx
```

Quando os arquivos estiverem em `backend/certs/`, a configuracao pode informar apenas o nome do arquivo. Tambem sao aceitos caminhos absolutos.

## Certificados OPC UA

Os certificados OPC UA da aplicacao sao gerados automaticamente quando necessario em:

```text
backend/Scada.Api/pki/own/
```

## Validacoes uteis

```powershell
dotnet build backend\Scada.Api\Scada.Api.csproj
dotnet run --project backend\Scada.MesTests\Scada.MesTests.csproj
Set-Location frontend
npx tsc --noEmit
```

## Testes manuais por protocolo

```powershell
dotnet run --project backend\MqttSimpleTest\MqttSimpleTest.csproj
dotnet run --project backend\OpcUaSimpleTest\OpcUaSimpleTest.csproj
dotnet run --project backend\ModbusSimpleTest\ModbusSimpleTest.csproj
dotnet run --project backend\MySqlSimpleTest\MySqlSimpleTest.csproj
```

`backend/Scada.Tests` e legado e nao deve ser usado como suite oficial.

## Portas

Portas usadas pelo sistema:

```text
3000 - Frontend Next.js
5000 - Backend API/SignalR
3306 - MySQL, se usado localmente
9090 - Prometheus, se observabilidade for ativada
3001 - Grafana, se observabilidade for ativada
```

Portas industriais dependem do ambiente:

```text
1883/8883 - MQTT/MQTT TLS
4840      - OPC UA comum
502       - Modbus TCP
44818     - Ethernet/IP
```

## Solucao de problemas

### Porta em uso

```powershell
netstat -ano | findstr :3000
netstat -ano | findstr :5000
taskkill /PID <pid> /F
```

### Frontend nao abre

- Verifique se `npm install` foi executado em `frontend/`.
- Confirme se `npm run dev` ou `start-system.ps1` subiu a porta `3000`.

### Backend nao sobe

- Verifique `.env` e `Jwt__Key`.
- Rode `dotnet build backend\Scada.Api\Scada.Api.csproj`.
- Confira logs `backend.log` e `backend-error.log` quando gerados.

### OPC UA desconectado

- Confirme endpoint e NodeId no UaExpert.
- Confirme se o valor e escalar ou array.
- Para arrays, use `::0`, `::1`, etc.
- Consulte `docs/DRIVERS.md`.

## Observabilidade

Com a API rodando:

```powershell
docker compose -f docker-compose.observability.yml up -d
```

Acesse:

```text
Prometheus: http://localhost:9090
Grafana:    http://localhost:3001
```

