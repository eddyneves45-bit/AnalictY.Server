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

**Fase atual:** S1 - Base do Server

- [x] Criar README
- [ ] Copiar documentação técnica
- [ ] Copiar .gitignore
- [ ] Copiar backend
- [ ] Criar solução .sln
- [ ] Compilar

## Próximos Passos

1. Copiar backend do `scada_mes`
2. Criar solução `AnalictY.Server.sln`
3. Compilar e validar
4. Testar endpoints principais

## Documentação

- [API Reference](docs/API.md)
- [Drivers](docs/DRIVERS.md)
- [Instalação](docs/INSTALACAO.md)

## Plataforma AnalictY

Este é um dos três produtos da plataforma:

- **AnalictY.Server** (este repositório) - Backend/Runtime
- **AnalictY.Web** - Interface operacional web
- **AnalictY.Manager** - Aplicativo Windows para administração local
