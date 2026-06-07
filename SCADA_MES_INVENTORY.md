# Inventário e Classificação - SCADA_MES para AnalictY Platform

**Data:** 2026-06-07  
**Objetivo:** Mapear todos os componentes do SCADA_MES para a nova arquitetura (Server/Web/Manager)

---

## 1. Inventário Completo de Módulos

### 1.1 Backend - Scada.Api

#### Endpoints (32 arquivos)
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `AuthEndpoints.cs` | Login, registro, refresh, MFA, logout | SERVER |
| `UserEndpoints.cs` | CRUD de usuários, permissões | SERVER |
| `AuditEndpoints.cs` | Logs de auditoria | SERVER |
| `MachineEndpoints.cs` | CRUD de máquinas, pastas, setores | SERVER |
| `ConfigEndpoints.cs` | Configurações de sistema, timezone, horário | SERVER |
| `RuntimeEndpoints.cs` | Status do runtime | SERVER |
| `OeeEndpoints.cs` | Cálculos de OEE | SERVER |
| `AlertEndpoints.cs` | Histórico de alertas | SERVER |
| `AlertRuleEndpoints.cs` | Regras de alerta | SERVER |
| `NotificationEndpoints.cs` | Configurações de Telegram | SERVER |
| `DashboardEndpoints.cs` | Configurações de dashboards | SERVER |
| `BiEndpoints.cs` | Business Intelligence, métricas avançadas | SERVER |
| `SimulatorEndpoints.cs` | Simulação de máquinas virtuais | SERVER |
| `ReportEndpoints.cs` | Agendamento e execução de relatórios | SERVER |
| `StateEndpoints.cs` | Estados de máquinas | SERVER |
| `GatewayEndpoints.cs` | Health do gateway, roteamento | SERVER |
| `DriverEndpoints.cs` | Status de drivers (OPC UA, MQTT, Modbus) | SERVER |
| `MonitoringEndpoints.cs` | Métricas de monitoramento | SERVER |
| `IndustrialHealthEndpoints.cs` | Health industrial, heartbeat, MySQL | SERVER |
| `TagHistoryEndpoints.cs` | Histórico de TAGs | SERVER |
| `MetricsEndpoints.cs` | Métricas Prometheus | SERVER |
| `LogEndpoints.cs` | Logs recentes | SERVER |
| `DowntimeEndpoints.cs` | Motivos de parada | SERVER |
| `ProductionDiagnosticEndpoints.cs` | Diagnóstico de produção | SERVER |
| `WeintekEndpoints.cs` | Integração Weintek/FHDX | SERVER |
| `FtpExportEndpoints.cs` | Exportação FTP de relatórios | SERVER |
| `DatabaseBrowserEndpoints.cs` | Navegador de banco de dados | MANAGER |
| `SystemEndpoints.cs` | Versão, health, updates, backup | MANAGER |

#### Serviços (60+ arquivos)
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `TagValueQueue.cs` | Fila de valores de TAG | SERVER |
| `TagValueProcessorService.cs` | Processamento de TAGs | SERVER |
| `TagRuntimeSnapshotStore.cs` | Snapshot de runtime de TAGs | SERVER |
| `TagHistoryStore.cs` | Histórico de TAGs | SERVER |
| `TagConfigService.cs` | Configuração de TAGs | SERVER |
| `TagHeartbeatMonitorService.cs` | Monitor de heartbeat de TAGs | SERVER |
| `StateEngineManager.cs` | Motor de estado de máquinas | SERVER |
| `StateEngine.cs` | Lógica de transição de estado | SERVER |
| `QualityProcessor.cs` | Processamento de qualidade | SERVER |
| `MachineEngine.cs` | Motor de máquina | SERVER |
| `MesEventRules.cs` | Regras de eventos MES | SERVER |
| `MachineService.cs` | Serviço de máquinas | SERVER |
| `MachineRealtimeService.cs` | Realtime de máquinas | SERVER |
| `MachineGoalService.cs` | Metas de produção | SERVER |
| `VirtualMachineService.cs` | Máquinas virtuais | SERVER |
| `VirtualMachineRuntimeService.cs` | Runtime de máquinas virtuais | SERVER |
| `VirtualMachineSimulationWorker.cs` | Worker de simulação | SERVER |
| `OpcuaSessionFactory.cs` | Factory de sessões OPC UA | SERVER |
| `OpcuaConfigService.cs` | Configuração OPC UA | SERVER |
| `OpcuaTagPollingService.cs` | Polling de TAGs OPC UA | SERVER |
| `MqttConfigService.cs` | Configuração MQTT | SERVER |
| `MqttRuntimeMonitor.cs` | Monitor de runtime MQTT | SERVER |
| `MqttTagSubscriptionService.cs` | Subscrição de TAGs MQTT | SERVER |
| `MySqlConfigService.cs` | Configuração MySQL | SERVER |
| `MySqlPersistenceQueue.cs` | Fila de persistência MySQL | SERVER |
| `MySqlPersistenceWorker.cs` | Worker de persistência MySQL | SERVER |
| `MySqlTagHistoryStore.cs` | Histórico de TAGs MySQL | SERVER |
| `MySqlMesEventStore.cs` | Eventos MES MySQL | SERVER |
| `AlertService.cs` | Serviço de alertas | SERVER |
| `AlertRuleService.cs` | Serviço de regras de alerta | SERVER |
| `AlertRuleEvaluator.cs` | Avaliador de regras de alerta | SERVER |
| `AlertRealtimeService.cs` | Realtime de alertas | SERVER |
| `TelegramNotificationService.cs` | Serviço de notificação Telegram | SERVER |
| `TelegramNotificationQueue.cs` | Fila de notificações Telegram | SERVER |
| `TelegramNotificationWorker.cs` | Worker de notificações Telegram | SERVER |
| `ReportService.cs` | Serviço de relatórios | SERVER |
| `ReportSchedulerService.cs` | Scheduler de relatórios | SERVER |
| `DashboardService.cs` | Serviço de dashboards | SERVER |
| `BiService.cs` | Serviço de BI | SERVER |
| `MesSummaryService.cs` | Resumo MES | SERVER |
| `MesDashboardRealtimeService.cs` | Realtime de dashboard MES | SERVER |
| `OeeApplicationService.cs` | Aplicação de OEE | SERVER |
| `DowntimeService.cs` | Serviço de paradas | SERVER |
| `ShiftService.cs` | Serviço de turnos | SERVER |
| `SystemTimeService.cs` | Serviço de tempo do sistema | SERVER |
| `RuntimeService.cs` | Serviço de runtime | SERVER |
| `RuntimeRealtimeService.cs` | Realtime de runtime | SERVER |
| `StateService.cs` | Serviço de estado | SERVER |
| `GatewayAppService.cs` | Aplicação do gateway | SERVER |
| `DriverStatusService.cs` | Status de drivers | SERVER |
| `MonitoringAppService.cs` | Aplicação de monitoramento | SERVER |
| `IndustrialHeartbeatService.cs` | Heartbeat industrial | SERVER |
| `IndustrialMetricsService.cs` | Métricas industriais | SERVER |
| `ConfigApplicationService.cs` | Aplicação de configuração | SERVER |
| `MdnsResponderService.cs` | Responder mDNS | SERVER |
| `RecentLogStore.cs` | Armazenamento de logs recentes | SERVER |
| `FrontendProxyMiddleware.cs` | Middleware de proxy frontend | SERVER |

#### Realtime
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `MesHub.cs` | Hub SignalR para MES | SERVER |

#### Security
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `PermissionAuthorizationHandler.cs` | Handler de autorização por permissão | SERVER |
| `PermissionAuthorizationRequirement.cs` | Requisito de autorização | SERVER |

### 1.2 Backend - Scada.Core

#### Drivers
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `MqttDriver.cs` | Driver MQTT | SERVER |
| `OpcUaDriver.cs` | Driver OPC UA | SERVER |
| `MySqlDriver.cs` | Driver MySQL | SERVER |

#### StateEngine
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `StateEngine.cs` | Motor de estado | SERVER |
| `StateEngineManager.cs` | Gerenciador de motor de estado | SERVER |
| `MachineState.cs` | Estado de máquina | SERVER |
| `MachineContext.cs` | Contexto de máquina | SERVER |
| `MachineStateContext.cs` | Contexto de estado de máquina | SERVER |
| `StateTransitionEvent.cs` | Evento de transição de estado | SERVER |
| `StateDeriver.cs` | Derivador de estado | SERVER |
| `DelayConfig.cs` | Configuração de delay | SERVER |

#### MachineEngine
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `MachineEngine.cs` | Motor de máquina | SERVER |

#### Mes
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `MesEventRules.cs` | Regras de eventos MES | SERVER |

#### Quality
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `QualityProcessor.cs` | Processador de qualidade | SERVER |

### 1.3 Backend - Scada.Data

#### Models
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `ScadaDbContext.cs` | Contexto EF Core SQLite | SERVER |
| `MachineEntity.cs` | Entidade de máquina | SERVER |
| `UserEntity.cs` | Entidade de usuário | SERVER |
| `TagRuntimeState.cs` | Estado de runtime de TAG | SERVER |

### 1.4 Backend - Scada.Gateway

#### Services
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `GatewayService.cs` | Serviço do gateway | SERVER |
| `HealthCheckService.cs` | Health check do gateway | SERVER |
| `TagRuntimeService.cs` | Serviço de runtime de TAGs | SERVER |

### 1.5 Backend - Scada.Monitoring

#### Services
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `AlertManager.cs` | Gerenciador de alertas | SERVER |
| `MetricsCollector.cs` | Coletor de métricas | SERVER |

### 1.6 Backend - Scada.Drivers

#### Services
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| `DriverManager.cs` | Gerenciador de drivers | SERVER |

### 1.7 Backend - Scada.Security

#### Services
| Arquivo | Responsabilidade | Destino |
|---------|------------------|---------|
| (Vários serviços de autenticação/autorização) | Autenticação JWT, MFA, sessões | SERVER |

### 1.8 Frontend - Next.js

c
### 1.10 Scripts e Ferramentas

| Item | Responsabilidade | Destino |
|------|------------------|---------|
| `bootstrap-system.ps1` | Bootstrap de instalação | SERVER (instalador) |
| `start-system.ps1` | Start do sistema | SERVER (instalador) |
| `scripts/windows-release/` | Scripts de release Windows | SERVER (instalador) |
| `tools/windows-release/` | Ferramentas de release | SERVER (instalador) |

---

## 2. Tabela de Classificação

### 2.1 SERVER (Runtime, API, Banco, Integrações)

| Nome | Localização | Responsabilidade |
|------|-------------|------------------|
| **Core Runtime** | | |
| StateEngineManager | Scada.Core/StateEngine | Gerenciamento de motor de estado |
| StateEngine | Scada.Core/StateEngine | Lógica de transição de estado |
| MachineEngine | Scada.Core/MachineEngine | Motor de máquina |
| QualityProcessor | Scada.Core/Quality | Processamento de qualidade |
| MesEventRules | Scada.Core/Mes | Regras de eventos MES |
| **Drivers** | | |
| MqttDriver | Scada.Core/Drivers | Driver MQTT |
| OpcUaDriver | Scada.Core/Drivers | Driver OPC UA |
| MySqlDriver | Scada.Core/Drivers | Driver MySQL |
| DriverManager | Scada.Drivers/Services | Gerenciador de drivers |
| **Tag Processing** | | |
| TagValueQueue | Scada.Api/Services | Fila de valores de TAG |
| TagValueProcessorService | Scada.Api/Services | Processamento de TAGs |
| TagRuntimeSnapshotStore | Scada.Api/Services | Snapshot de runtime |
| TagHistoryStore | Scada.Api/Services | Histórico de TAGs |
| TagConfigService | Scada.Api/Services | Configuração de TAGs |
| TagHeartbeatMonitorService | Scada.Api/Services | Monitor de heartbeat |
| **OPC UA** | | |
| OpcuaSessionFactory | Scada.Api/Services | Factory de sessões |
| OpcuaConfigService | Scada.Api/Services | Configuração OPC UA |
| OpcuaTagPollingService | Scada.Api/Services | Polling de TAGs |
| **MQTT** | | |
| MqttConfigService | Scada.Api/Services | Configuração MQTT |
| MqttRuntimeMonitor | Scada.Api/Services | Monitor de runtime |
| MqttTagSubscriptionService | Scada.Api/Services | Subscrição de TAGs |
| **MySQL/MES** | | |
| MySqlConfigService | Scada.Api/Services | Configuração MySQL |
| MySqlPersistenceQueue | Scada.Api/Services | Fila de persistência |
| MySqlPersistenceWorker | Scada.Api/Services | Worker de persistência |
| MySqlTagHistoryStore | Scada.Api/Services | Histórico de TAGs |
| MySqlMesEventStore | Scada.Api/Services | Eventos MES |
| **Máquinas** | | |
| MachineService | Scada.Api/Services | Serviço de máquinas |
| MachineRealtimeService | Scada.Api/Services | Realtime de máquinas |
| MachineGoalService | Scada.Api/Services | Metas de produção |
| VirtualMachineService | Scada.Api/Services | Máquinas virtuais |
| VirtualMachineRuntimeService | Scada.Api/Services | Runtime de virtuais |
| VirtualMachineSimulationWorker | Scada.Api/Services | Worker de simulação |
| **Alertas** | | |
| AlertService | Scada.Api/Services | Serviço de alertas |
| AlertRuleService | Scada.Api/Services | Serviço de regras |
| AlertRuleEvaluator | Scada.Api/Services | Avaliador de regras |
| AlertRealtimeService | Scada.Api/Services | Realtime de alertas |
| **Notificações** | | |
| TelegramNotificationService | Scada.Api/Services | Serviço Telegram |
| TelegramNotificationQueue | Scada.Api/Services | Fila de notificações |
| TelegramNotificationWorker | Scada.Api/Services | Worker de notificações |
| **Relatórios** | | |
| ReportService | Scada.Api/Services | Serviço de relatórios |
| ReportSchedulerService | Scada.Api/Services | Scheduler de relatórios |
| FtpExportEndpoints | Scada.Api/Endpoints | Exportação FTP |
| **BI e Métricas** | | |
| BiService | Scada.Api/Services | Serviço de BI |
| MesSummaryService | Scada.Api/Services | Resumo MES |
| MesDashboardRealtimeService | Scada.Api/Services | Realtime MES |
| OeeApplicationService | Scada.Api/Services | Aplicação OEE |
| **Paradas e Turnos** | | |
| DowntimeService | Scada.Api/Services | Serviço de paradas |
| ShiftService | Scada.Api/Services | Serviço de turnos |
| **Runtime e Estado** | | |
| RuntimeService | Scada.Api/Services | Serviço de runtime |
| RuntimeRealtimeService | Scada.Api/Services | Realtime de runtime |
| StateService | Scada.Api/Services | Serviço de estado |
| SystemTimeService | Scada.Api/Services | Tempo do sistema |
| **Gateway e Monitoramento** | | |
| GatewayService | Scada.Gateway/Services | Serviço do gateway |
| GatewayAppService | Scada.Api/Services | Aplicação do gateway |
| HealthCheckService | Scada.Gateway/Services | Health check |
| TagRuntimeService | Scada.Gateway/Services | Runtime de TAGs |
| DriverStatusService | Scada.Api/Services | Status de drivers |
| MonitoringAppService | Scada.Api/Services | Aplicação de monitoramento |
| IndustrialHeartbeatService | Scada.Api/Services | Heartbeat industrial |
| IndustrialMetricsService | Scada.Api/Services | Métricas industriais |
| AlertManager | Scada.Monitoring/Services | Gerenciador de alertas |
| MetricsCollector | Scada.Monitoring/Services | Coletor de métricas |
| **Configuração** | | |
| ConfigApplicationService | Scada.Api/Services | Aplicação de configuração |
| DashboardService | Scada.Api/Services | Serviço de dashboards |
| **SignalR** | | |
| MesHub | Scada.Api/Realtime | Hub SignalR MES |
| **API Endpoints (Operacionais)** | | |
| AuthEndpoints | Scada.Api/Endpoints | Autenticação |
| UserEndpoints | Scada.Api/Endpoints | Usuários |
| MachineEndpoints | Scada.Api/Endpoints | Máquinas |
| AlertEndpoints | Scada.Api/Endpoints | Alertas |
| AlertRuleEndpoints | Scada.Api/Endpoints | Regras de alerta |
| NotificationEndpoints | Scada.Api/Endpoints | Notificações |
| DashboardEndpoints | Scada.Api/Endpoints | Dashboards |
| BiEndpoints | Scada.Api/Endpoints | BI |
| ReportEndpoints | Scada.Api/Endpoints | Relatórios |
| StateEndpoints | Scada.Api/Endpoints | Estados |
| OeeEndpoints | Scada.Api/Endpoints | OEE |
| DowntimeEndpoints | Scada.Api/Endpoints | Paradas |
| ProductionDiagnosticEndpoints | Scada.Api/Endpoints | Diagnóstico produção |
| RuntimeEndpoints | Scada.Api/Endpoints | Runtime |
| **API Endpoints (Drivers/Integração)** | | |
| DriverEndpoints | Scada.Api/Endpoints | Status de drivers |
| GatewayEndpoints | Scada.Api/Endpoints | Gateway |
| WeintekEndpoints | Scada.Api/Endpoints | Integração Weintek |
| **API Endpoints (Health/Metrics)** | | |
| IndustrialHealthEndpoints | Scada.Api/Endpoints | Health industrial |
| MetricsEndpoints | Scada.Api/Endpoints | Métricas Prometheus |
| MonitoringEndpoints | Scada.Api/Endpoints | Monitoramento |
| TagHistoryEndpoints | Scada.Api/Endpoints | Histórico de TAGs |
| **API Endpoints (Logs/Audit)** | | |
| LogEndpoints | Scada.Api/Endpoints | Logs recentes |
| AuditEndpoints | Scada.Api/Endpoints | Auditoria |
| **Segurança** | | |
| (Scada.Security completo) | Scada.Security | Autenticação/autorização |
| PermissionAuthorizationHandler | Scada.Api/Security | Autorização por permissão |
| **Banco de Dados** | | |
| ScadaDbContext | Scada.Data/Models | Contexto SQLite |
| (Todas as entidades) | Scada.Data/Models | Modelos de dados |
| **Agent/Tray** | | |
| AnalictY.Agent | agent/AnalictY.Agent | Tray Icon, health, updates |
| **Observabilidade** | | |
| RecentLogStore | Scada.Api/Services | Logs recentes |
| MdnsResponderService | Scada.Api/Services | Responder mDNS |
| FrontendProxyMiddleware | Scada.Api/Services | Proxy frontend |

### 2.2 WEB (Interface Operacional)

| Nome | Localização | Responsabilidade |
|------|-------------|------------------|
| **Páginas Operacionais** | | |
| Dashboard | frontend/app/dashboard | Dashboard principal |
| Máquinas | frontend/app/machines | Lista de máquinas |
| Detalhes de Máquina | frontend/app/machines/[id] | Detalhes de máquina |
| Alertas | frontend/app/alerts | Alertas operacionais |
| Histórico de Produção | frontend/app/production-history | Histórico de produção |
| Relatórios | frontend/app/report | Relatórios operacionais |
| Status | frontend/app/status | Status operacional |
| Dashboards | frontend/app/dashboards | Dashboards customizados |
| Conexões | frontend/app/connections | Conexões MQTT/OPC UA (visualização) |
| Configurações Operacionais | frontend/app/config | Configurações simples |
| **Componentes Visuais** | | |
| GaugeSemiCircle | frontend/components | Gauge semicircular |
| Componentes de Máquinas | frontend/components/machines | Componentes de máquinas |
| Layouts | frontend/components/layout | Layouts da aplicação |
| Providers | frontend/components/providers | Providers de contexto |

### 2.3 MANAGER (Administração Local)

| Nome | Localização | Responsabilidade |
|------|-------------|------------------|
| **API Endpoints (Administração)** | | |
| DatabaseBrowserEndpoints | Scada.Api/Endpoints | Navegador de banco de dados |
| SystemEndpoints | Scada.Api/Endpoints | Versão, health, updates, backup |
| **Páginas Administrativas** | | |
| Monitor MQTT | frontend/app/mqtt-monitor | Monitoramento MQTT avançado |
| Navegador OPC UA | frontend/app/opc-browser | Navegador OPC UA |
| Console MySQL | frontend/app/mysql-console | Console MySQL |
| Navegador de Banco | frontend/app/database-browser | Navegador de banco de dados |
| Usuários | frontend/app/users | Administração de usuários |
| Auditoria | frontend/app/audit | Auditoria técnica |
| Notificações Telegram | frontend/app/telegram-notifications | Configuração Telegram |
| Simulador | frontend/app/simulator | Simulador (ferramenta de teste) |
| TAGs | frontend/app/tags | Configuração de TAGs |
| Motivos de Parada | frontend/app/downtime-reasons | Motivos de parada |
| Turnos | frontend/app/shifts | Configuração de turnos |
| BI Avançado | frontend/app/bi | BI avançado |
| Diagnóstico de Produção | frontend/app/production-diagnostics | Diagnóstico de produção |
| Navegador Weintek | frontend/app/weintek-browser | Navegador Weintek |

### 2.4 FUTURO / NÃO DEFINIDO

| Nome | Localização | Responsabilidade | Observação |
|------|-------------|------------------|-----------|
| Desktop Experimental | desktop/ | Electron wrapper | Não usado em produção |
| Scada.Tests | backend/Scada.Tests | Testes legados | Aposentado |
| Scada.ApiTest | backend/Scada.ApiTest | Testes de API | Futuro |

---

## 3. Dependências Cruzadas

### 3.1 Server → Web
- **SignalR (MesHub)**: Server push para Web (dashboards, status em tempo real)
- **API Endpoints**: Web consome endpoints do Server
- **FrontendProxyMiddleware**: Server serve o Web estático

### 3.2 Web → Server
- **HTTP Client**: Web chama endpoints do Server
- **SignalR Client**: Web conecta ao hub do Server
- **WebSocket**: Web recebe atualizações em tempo real

### 3.3 Manager → Server
- **HTTP Client**: Manager chama endpoints administrativos
- **Health Checks**: Manager verifica saúde do Server
- **Updates**: Manager aplica atualizações no Server

### 3.4 Server → Manager
- **Nenhuma dependência direta**: Server é independente do Manager

### 3.5 Agent → Server
- **HTTP Client**: Agent verifica health/version/updates do Server
- **Tray Icon**: Agent controla/start/stop do Server

### 3.6 Server → Agent
- **Nenhuma dependência direta**: Server é independente do Agent

---

## 4. Itens que Precisam Ser Desacoplados

### 4.1 Frontend - Páginas Mistas
| Item | Problema | Ação Necessária |
|------|----------|-----------------|
| `app/config/page.tsx` | Mistura config operacional + config técnica | Separar em config operacional (WEB) e config técnica (MANAGER) |
| `app/connections/page.tsx` | Mistura visualização + configuração avançada | Manter visualização em WEB, mover config avançada para MANAGER |
| `app/users/page.tsx` | Administração de usuários | Mover para MANAGER |
| `app/audit/page.tsx` | Auditoria técnica | Mover para MANAGER |

### 4.2 Backend - Endpoints Mistos
| Item | Problema | Ação Necessária |
|------|----------|-----------------|
| `ConfigEndpoints.cs` | Mistura config operacional + config técnica | Separar endpoints operacionais (WEB) de técnicos (MANAGER) |
| `SystemEndpoints.cs` | Mistura health (WEB) + updates/backup (MANAGER) | Separar endpoints |
| `UserEndpoints.cs` | CRUD de usuários | Mover para MANAGER ou criar endpoints separados |

### 4.3 FrontendProxyMiddleware
| Item | Problema | Ação Necessária |
|------|----------|-----------------|
| `FrontendProxyMiddleware.cs` | Server serve o Web estático | Desacoplar: Web deve ser servido separadamente ou via CDN |

### 4.4 Configuração de Banco
| Item | Problema | Ação Necessária |
|------|----------|-----------------|
| `DatabaseBrowserEndpoints.cs` | Navegador de banco exposto via API | Mover para Manager (desktop) ou remover do Server |

---

## 5. Itens Ausentes no AnalictY Server

Comparando o SCADA_MES com o AnalictY.Server atual:

### 5.1 Módulos Core Ausentes
- **StateEngineManager**: Motor de estado de máquinas
- **MachineEngine**: Motor de máquina
- **QualityProcessor**: Processador de qualidade
- **MesEventRules**: Regras de eventos MES

### 5.2 Drivers Ausentes
- **MqttDriver**: Driver MQTT completo
- **OpcUaDriver**: Driver OPC UA completo
- **MySqlDriver**: Driver MySQL completo

### 5.3 Serviços de Runtime Ausentes
- **TagValueQueue**: Fila de valores de TAG
- **TagValueProcessorService**: Processamento de TAGs
- **TagRuntimeSnapshotStore**: Snapshot de runtime
- **TagHistoryStore**: Histórico de TAGs
- **TagHeartbeatMonitorService**: Monitor de heartbeat

### 5.4 Serviços de Integração Ausentes
- **OpcuaSessionFactory**: Factory de sessões OPC UA
- **OpcuaTagPollingService**: Polling de TAGs OPC UA
- **MqttRuntimeMonitor**: Monitor de runtime MQTT
- **MqttTagSubscriptionService**: Subscrição de TAGs MQTT
- **MySqlPersistenceQueue**: Fila de persistência MySQL
- **MySqlPersistenceWorker**: Worker de persistência MySQL
- **MySqlTagHistoryStore**: Histórico de TAGs MySQL
- **MySqlMesEventStore**: Eventos MES MySQL

### 5.5 Serviços de Negócio Ausentes
- **MachineService**: Serviço de máquinas
- **MachineRealtimeService**: Realtime de máquinas
- **MachineGoalService**: Metas de produção
- **VirtualMachineService**: Máquinas virtuais
- **AlertService**: Serviço de alertas
- **AlertRuleService**: Serviço de regras de alerta
- **AlertRuleEvaluator**: Avaliador de regras
- **TelegramNotificationService**: Serviço Telegram
- **ReportService**: Serviço de relatórios
- **ReportSchedulerService**: Scheduler de relatórios
- **BiService**: Serviço de BI
- **MesSummaryService**: Resumo MES
- **OeeApplicationService**: Aplicação OEE
- **DowntimeService**: Serviço de paradas
- **ShiftService**: Serviço de turnos

### 5.6 Endpoints Ausentes
- **MachineEndpoints**: CRUD de máquinas
- **AlertEndpoints**: Histórico de alertas
- **AlertRuleEndpoints**: Regras de alerta
- **NotificationEndpoints**: Notificações
- **DashboardEndpoints**: Dashboards
- **BiEndpoints**: BI
- **ReportEndpoints**: Relatórios
- **StateEndpoints**: Estados
- **OeeEndpoints**: OEE
- **DowntimeEndpoints**: Paradas
- **ProductionDiagnosticEndpoints**: Diagnóstico produção
- **DriverEndpoints**: Status de drivers
- **GatewayEndpoints**: Gateway
- **WeintekEndpoints**: Integração Weintek
- **IndustrialHealthEndpoints**: Health industrial
- **MetricsEndpoints**: Métricas Prometheus
- **MonitoringEndpoints**: Monitoramento
- **TagHistoryEndpoints**: Histórico de TAGs
- **DatabaseBrowserEndpoints**: Navegador de banco
- **SystemEndpoints**: Versão, health, updates, backup

### 5.7 SignalR Hub Ausente
- **MesHub**: Hub SignalR para MES

### 5.8 Agent/Tray Ausente
- **AnalictY.Agent**: Tray Icon, health check, updates

### 5.9 Observabilidade Ausente
- **MdnsResponderService**: Responder mDNS
- **IndustrialHeartbeatService**: Heartbeat industrial
- **IndustrialMetricsService**: Métricas industriais

---

## 6. Riscos da Migração

### 6.1 Riscos Críticos
| Risco | Impacto | Mitigação |
|-------|---------|-----------|
| **Perda de dados durante migração** | Alto | Backup completo antes da migração; script de migração de dados |
| **Incompatibilidade de schema MySQL** | Alto | Validar schema antes; manter versão do schema |
| **Quebra de integrações MQTT/OPC UA** | Alto | Testar drivers em ambiente isolado; manter rollback |
| **Perda de configurações** | Alto | Exportar configurações antes da migração |
| **Downtime prolongado** | Médio | Planejar migração em janela de manutenção |

### 6.2 Riscos Médios
| Risco | Impacto | Mitigação |
|-------|---------|-----------|
| **Incompatibilidade de frontend** | Médio | Testar frontend com nova API; manter versão antiga |
| **Problemas de autenticação/autorização** | Médio | Testar todos os perfis de usuário; manter mecanismo de fallback |
| **Performance de SignalR** | Médio | Testar carga; otimizar hub se necessário |
| **Problemas de serialização JSON** | Médio | Validar camelCase/PascalCase; testar todos os endpoints |

### 6.3 Riscos Baixos
| Risco | Impacto | Mitigação |
|-------|---------|-----------|
| **Mudança de portas** | Baixo | Documentar novas portas; atualizar configurações |
| **Mudança de estrutura de arquivos** | Baixo | Documentar nova estrutura; atualizar scripts |
| **Dependências de terceiros** | Baixo | Testar atualizações; manter versões compatíveis |

---

## 7. Recomendação de Ordem de Extração

### Fase 1: Fundação (Server)
1. **Core Runtime**
   - StateEngineManager
   - StateEngine
   - MachineEngine
   - QualityProcessor
   - MesEventRules

2. **Drivers**
   - MqttDriver
   - OpcUaDriver
   - MySqlDriver
   - DriverManager

3. **Tag Processing**
   - TagValueQueue
   - TagValueProcessorService
   - TagRuntimeSnapshotStore
   - TagHistoryStore
   - TagConfigService
   - TagHeartbeatMonitorService

### Fase 2: Integrações (Server)
4. **OPC UA**
   - OpcuaSessionFactory
   - OpcuaConfigService
   - OpcuaTagPollingService

5. **MQTT**
   - MqttConfigService
   - MqttRuntimeMonitor
   - MqttTagSubscriptionService

6. **MySQL/MES**
   - MySqlConfigService
   - MySqlPersistenceQueue
   - MySqlPersistenceWorker
   - MySqlTagHistoryStore
   - MySqlMesEventStore

### Fase 3: Serviços de Negócio (Server)
7. **Máquinas**
   - MachineService
   - MachineRealtimeService
   - MachineGoalService
   - VirtualMachineService
   - VirtualMachineRuntimeService
   - VirtualMachineSimulationWorker

8. **Alertas e Notificações**
   - AlertService
   - AlertRuleService
   - AlertRuleEvaluator
   - AlertRealtimeService
   - TelegramNotificationService
   - TelegramNotificationQueue
   - TelegramNotificationWorker

9. **Relatórios e BI**
   - ReportService
   - ReportSchedulerService
   - BiService
   - MesSummaryService
   - MesDashboardRealtimeService
   - OeeApplicationService

10. **Paradas e Turnos**
    - DowntimeService
    - ShiftService

### Fase 4: API Endpoints (Server)
11. **Endpoints Operacionais**
    - MachineEndpoints
    - AlertEndpoints
    - AlertRuleEndpoints
    - NotificationEndpoints
    - DashboardEndpoints
    - BiEndpoints
    - ReportEndpoints
    - StateEndpoints
    - OeeEndpoints
    - DowntimeEndpoints
    - ProductionDiagnosticEndpoints
    - RuntimeEndpoints

12. **Endpoints de Integração**
    - DriverEndpoints
    - GatewayEndpoints
    - WeintekEndpoints

13. **Endpoints de Health/Metrics**
    - IndustrialHealthEndpoints
    - MetricsEndpoints
    - MonitoringEndpoints
    - TagHistoryEndpoints

14. **Endpoints Administrativos**
    - DatabaseBrowserEndpoints
    - SystemEndpoints
    - LogEndpoints
    - AuditEndpoints

### Fase 5: SignalR e Realtime (Server)
15. **SignalR Hub**
    - MesHub
    - MesDashboardRealtimeService
    - MachineRealtimeService
    - RuntimeRealtimeService
    - AlertRealtimeService

### Fase 6: Observabilidade (Server)
16. **Monitoramento e Logs**
    - IndustrialHeartbeatService
    - IndustrialMetricsService
    - RecentLogStore
    - MdnsResponderService

### Fase 7: Agent/Tray (Server)
17. **Agent**
    - AnalictY.Agent
    - TrayApplicationContext

### Fase 8: Frontend Operacional (Web)
18. **Páginas Operacionais**
    - Dashboard
    - Máquinas
    - Detalhes de Máquina
    - Alertas
    - Histórico de Produção
    - Relatórios
    - Status
    - Dashboards
    - Conexões (visualização)
    - Configurações Operacionais

19. **Componentes Visuais**
    - GaugeSemiCircle
    - Componentes de Máquinas
    - Layouts
    - Providers

### Fase 9: Frontend Administrativo (Manager)
20. **Páginas Administrativas**
    - Monitor MQTT
    - Navegador OPC UA
    - Console MySQL
    - Navegador de Banco
    - Usuários
    - Auditoria
    - Notificações Telegram
    - Simulador
    - TAGs
    - Motivos de Parada
    - Turnos
    - BI Avançado
    - Diagnóstico de Produção
    - Navegador Weintek

### Fase 10: Desacoplamento Final
21. **Separação de Configurações**
    - Separar config operacional (WEB) de config técnica (MANAGER)
    - Separar endpoints operacionais de administrativos

22. **Remoção de FrontendProxyMiddleware**
    - Desacoplar serving do Web estático do Server

23. **Validação Final**
    - Testes de integração completa
    - Testes de carga
    - Validação de rollback

---

## 8. Resumo Executivo

### 8.1 Estatísticas
- **Total de módulos identificados**: ~120
- **Destino SERVER**: ~90 módulos
- **Destino WEB**: ~15 módulos
- **Destino MANAGER**: ~15 módulos
- **Itens a desacoplar**: 4 principais
- **Itens ausentes no AnalictY Server**: ~50 módulos

### 8.2 Principais Lacunas no AnalictY Server Atual
1. **Core Runtime**: StateEngine, MachineEngine, QualityProcessor
2. **Drivers**: MQTT, OPC UA, MySQL completos
3. **Tag Processing**: Fila, processor, histórico
4. **Serviços de Negócio**: Máquinas, alertas, relatórios, BI, OEE
5. **SignalR**: Hub MES para realtime
6. **Agent**: Tray Icon para health/updates
7. **Observabilidade**: Heartbeat industrial, métricas

### 8.3 Recomendação Imediata
Priorizar a extração de:
1. **Core Runtime** (fundação)
2. **Drivers** (integrações)
3. **Tag Processing** (coleta de dados)
4. **SignalR Hub** (realtime para Web)

Esses quatro blocos são essenciais para que o Server possa funcionar como fonte de verdade para o Web e Manager.
