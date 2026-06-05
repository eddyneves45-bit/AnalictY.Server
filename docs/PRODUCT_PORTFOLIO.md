# AnalictY Platform

O AnalictY passa a ser organizado como uma plataforma com tres produtos principais.

```text
AnalictY Platform
├── AnalictY Server
├── AnalictY Web
└── AnalictY Manager
```

## AnalictY Server

Camada instalada e executada em segundo plano no Windows.

Responsabilidades:

- Backend/API.
- Runtime local.
- Servicos Windows.
- Banco de dados.
- Integracoes industriais.
- Coleta e processamento.
- Logs tecnicos.
- Atualizacoes.
- Diagnosticos.

## AnalictY Web

Interface operacional da fabrica, acessada pelo navegador.

Responsabilidades:

- Visao geral.
- Status operacional.
- Historico de producao.
- Historico de paradas.
- Relatorios operacionais.
- Alertas.
- Dashboards.
- Configuracoes operacionais simples.

O Web deve ser limpo, direto e focado no uso diario da operacao.

## AnalictY Manager

Aplicativo desktop Windows para administracao local do ambiente.

Responsabilidades:

- Verificar saude do Server.
- Abrir o Web.
- Consultar logs.
- Diagnosticar o ambiente local.
- Verificar e aplicar atualizacoes.
- Gerenciar servicos quando permitido.
- Apoiar instalacao, manutencao e suporte.

O Manager nao substitui o Web. Ele centraliza o que e tecnico ou administrativo.

