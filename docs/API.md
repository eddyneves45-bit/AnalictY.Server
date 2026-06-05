# API Reference

Referencia resumida da API HTTP e SignalR usada pelo frontend.

## Base URLs

Ambiente local padrao:

```text
Frontend: http://localhost:3000
Backend:  http://localhost:5000
Hub:      http://localhost:5000/hubs/mes
```

No frontend, a URL da API e definida por `NEXT_PUBLIC_API_URL`. Se nao for informada, usa `http://localhost:5000`.

## Autenticacao

A API usa autenticacao por sessao/cookies seguros, refresh token em cookie e protecao CSRF para mutacoes.

Endpoints principais:

```text
POST /api/auth/login
POST /api/auth/refresh
GET  /api/auth/me
POST /api/auth/logout
```

Rotas administrativas e operacionais exigem usuario autenticado e, quando aplicavel, permissao especifica.

## Endpoints REST principais

### Maquinas

```text
GET    /api/machines
POST   /api/machines
GET    /api/machines/{id}
PUT    /api/machines/{id}
DELETE /api/machines/{id}
GET    /api/machines/{id}/tag-mapping
POST   /api/machines/{id}/tag-mapping
```

### Configuracao industrial

```text
GET/POST/PUT/DELETE /api/config/tags
GET/POST/PUT/DELETE /api/config/opcua
GET/POST/PUT/DELETE /api/config/mqtt
POST                /api/config/mqtt/{id}/test
GET                 /api/config/opcua/browse?node_id={node_id}
GET                 /api/config/system/timezone
PUT                 /api/config/system/timezone
GET                 /api/config/system/time
```

### Runtime

```text
GET /api/runtime/state
```

O runtime retorna o estado atual das TAGs normalizadas em memoria.

### Alertas

```text
GET    /api/alerts?limit=20
POST   /api/alerts
GET    /api/alerts/retention
PUT    /api/alerts/retention
POST   /api/alerts/{id}/acknowledge?acknowledged_by={usuario}
DELETE /api/alerts/{id}
```

`GET /api/alerts` aceita filtros opcionais `machine_id`, `alert_type`, `severity`, `is_acknowledged` e `limit`. A interface usa `limit=20`, pesquisa local nas ocorrencias carregadas e exibe horarios no fuso configurado do sistema. A retencao automatica aceita `retention_days` de 1 a 7.

### Regras de alerta

```text
GET    /api/alert-rules
POST   /api/alert-rules
PUT    /api/alert-rules/{id}
DELETE /api/alert-rules/{id}
```

### Relatorios

```text
GET    /api/reports
POST   /api/reports
PUT    /api/reports/{id}
DELETE /api/reports/{id}
POST   /api/reports/generate
POST   /api/reports/schedule
POST   /api/reports/production/matrix
POST   /api/reports/production/export/csv
POST   /api/reports/export/csv
GET    /api/reports/machine-dashboard
GET    /api/reports/executions
```

Endpoints de relatorio exigem permissao `reports.download`/politica equivalente no backend.

### Rastreio de status e motivos de parada

```text
GET  /api/downtimes?machine_id={id}&limit=30
GET  /api/downtimes/retention
PUT  /api/downtimes/retention
GET  /api/downtime-reasons/catalog
POST /api/downtime-reasons/catalog
POST /api/downtimes/{id}/classify
```

`GET /api/downtimes` retorna a linha do tempo de `eventos_status_maquina`, enriquecida com dados de `eventos_parada` quando o status permite classificacao de parada. Sem `machine_id`, retorna os ultimos eventos gerais. Com `machine_id`, retorna apenas os eventos da maquina selecionada. A retencao aceita `retention_days` de 1 a 7.

### Usuarios e auditoria

```text
GET/POST/PUT/DELETE /api/users
GET                 /api/audit
```

Essas rotas sao administrativas.

### BI e dashboards

```text
GET /api/dashboard/...
GET /api/bi/...
```

A nomenclatura exata pode variar por modulo. Use Swagger em desenvolvimento para lista completa.

### Saude e metricas

```text
GET /api/health/mysql
GET /metrics
```

`/metrics` expoe metricas para Prometheus.

## SignalR

Hub principal:

```text
/hubs/mes
```

Eventos consumidos pelo frontend:

```text
machines:snapshot
machines:update
runtime:snapshot
runtime:update
alerts:snapshot
alerts:created
alerts:updated
alerts:deleted
mes:snapshot
mes:update
```

Exemplo frontend:

```typescript
new HubConnectionBuilder()
  .withUrl(`${HUB_BASE_URL}/hubs/mes`, { withCredentials: true })
  .withAutomaticReconnect()
  .build()
```

## Codigos HTTP comuns

- `200 OK`: sucesso
- `201 Created`: recurso criado
- `204 No Content`: sucesso sem corpo
- `400 Bad Request`: requisicao invalida
- `401 Unauthorized`: nao autenticado
- `403 Forbidden`: sem permissao
- `404 Not Found`: recurso nao encontrado
- `429 Too Many Requests`: limite de requisicoes
- `500 Internal Server Error`: erro interno
- `503 Service Unavailable`: servico indisponivel

## Observacao

Este documento resume a API atual. Para detalhes completos, consultar os arquivos em `backend/Scada.Api/Endpoints` e o Swagger em ambiente de desenvolvimento.
