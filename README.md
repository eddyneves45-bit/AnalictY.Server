# AnalictY.Server

Backend/API e runtime da plataforma AnalictY.

## Responsabilidades

- Backend/API HTTP e SignalR
- Runtime industrial (MQTT, OPC UA, HTTP/Weintek)
- Serviços Windows
- Banco de dados (SQLite local + MySQL MES)
- Integrações industriais
- Coleta e processamento de dados
- Logs técnicos
- Atualizações
- Diagnóstico local

## Tecnologias

- .NET 8
- ASP.NET Core Minimal APIs
- Entity Framework Core
- SignalR
- SQLite (configuração local)
- MySQL (histórico e eventos MES)
- MQTT com TLS
- OPC UA

## Estrutura

```text
backend/
├── Scada.Api          # API HTTP, SignalR, workers e endpoints
├── Scada.Core         # Regras de domínio e motores centrais
├── Scada.Data         # Persistência SQLite e repositórios
├── Scada.Gateway      # Runtime de TAGs
├── Scada.Drivers      # MQTT, OPC UA e demais drivers
├── Scada.Monitoring   # Monitoramento e métricas
├── Scada.Security     # JWT, autenticação e autorização
└── Scada.MesTests     # Testes automatizados das regras MES
```

## Status da Migração

Este repositório está sendo migrado progressivamente do `scada_mes`.

**Fase atual:** S2 - Server configurado e rodando

- [x] Criar README
- [x] Copiar documentação técnica
- [x] Copiar .gitignore
- [x] Copiar backend
- [x] Criar solução .sln
- [x] Compilar
- [x] Configurar appsettings.json
- [x] Criar .env.example
- [x] Iniciar Server (port 5000)
- [ ] Testar integração com Web

## Próximos Passos

1. Testar integração com Web
2. Validar endpoints principais
3. Implementar telas técnicas no Manager

## Documentação

- [API Reference](docs/API.md)
- [Drivers](docs/DRIVERS.md)
- [Instalação](docs/INSTALACAO.md)

## Plataforma AnalictY

Este é um dos três produtos da plataforma:

- **AnalictY.Server** (este repositório) - Backend/Runtime
- **AnalictY.Web** - Interface operacional web
- **AnalictY.Manager** - Aplicativo Windows para administração local
