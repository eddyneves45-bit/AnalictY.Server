# Relatório de Build - AnalictY.Server

## Data
05/06/2026

## Status
✅ **Compilação com êxito**

## Comandos Executados
```powershell
dotnet restore AnalictY.Server.sln
dotnet build AnalictY.Server.sln
```

## Projetos na Solução
- Scada.Api
- Scada.Core
- Scada.Data
- Scada.Gateway
- Scada.Drivers
- Scada.Monitoring
- Scada.Security
- Scada.MesTests

## Warnings Encontrados (5)

### 1. Eventos não utilizados em Drivers (3 warnings)
**Arquivo:** `Scada.Core/Drivers/MySqlDriver.cs:22:51`
**Warning:** CS0067 - O evento "MySqlDriver.TagValueChanged" nunca é usado

**Arquivo:** `Scada.Core/Drivers/MqttDriver.cs:20:51`
**Warning:** CS0067 - O evento "MqttDriver.TagValueChanged" nunca é usado

**Arquivo:** `Scada.Core/Drivers/OpcUaDriver.cs:18:51`
**Warning:** CS0067 - O evento "OpcUaDriver.TagValueChanged" nunca é usado

**Impacto:** Baixo - Eventos definidos mas não consumidos atualmente. Não quebra a compilação.

### 2. WebRequest obsoleto (2 warnings)
**Arquivo:** `Scada.Api/Endpoints/FtpExportEndpoints.cs:273:38`
**Warning:** SYSLIB0014 - "WebRequest.Create(Uri)" é obsoleto. Use HttpClient instead.

**Arquivo:** `Scada.Api/Services/ReportService.cs:1376:38`
**Warning:** SYSLIB0014 - "WebRequest.Create(Uri)" é obsoleto. Use HttpClient instead.

**Impacto:** Médio - Código funcional mas usando API obsoleta. Deve ser migrado para HttpClient em fase futura.

## Erros
**0 erros**

## Tempo de Build
00:00:11.47

## Próximos Passos
1. Validar endpoints principais (health, version, auth)
2. Testar runtime MQTT e OPC UA
3. Prosseguir para migração do AnalictY.Web

## Observações
- Namespaces mantidos como `Scada.*` conforme estratégia de migração incremental
- Renomeio para `AnalictY.Server.*` será considerado em fase futura
- Backend copiado completamente do `scada_mes` sem alterações
