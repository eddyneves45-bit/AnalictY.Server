# Windows release

Use preferencialmente o script unico:

```powershell
.\scripts\windows-release\analicty-release.ps1 -Version 0.1.5 -Mode All
```

Ele gera a release, o pacote de atualizacao, o manifest e, quando o Inno Setup estiver disponivel, o instalador `.exe`.

## Pre-requisitos locais

- .NET SDK 8
- Node.js/npm para compilar o frontend
- Inno Setup 6, opcional para gerar o `.exe`
- WinSW x64
- ZIP portatil do Node.js Windows x64
- ZIP portatil do MySQL Community Server Windows x64, opcional para banco MES local embutido

## Comandos principais

Gerar tudo:

```powershell
.\scripts\windows-release\analicty-release.ps1 -Version 0.1.5 -Mode All
```

Gerar apenas instalador completo:

```powershell
.\scripts\windows-release\analicty-release.ps1 -Version 0.1.5 -Mode Installer
```

Gerar apenas update local:

```powershell
.\scripts\windows-release\analicty-release.ps1 -Version 0.1.5 -Mode Update
```

Gerar tudo e publicar no S3:

```powershell
.\scripts\windows-release\analicty-release.ps1 -Version 0.1.5 -Mode All -Upload
```

Saidas principais:

```text
release/AnalictY-0.1.5
release/installer/AnalictY-Setup-0.1.5.exe
release/updates/stable/AnalictY-0.1.5.zip
release/updates/stable/latest.json
```

## Ferramentas locais

Se os arquivos existirem em `tools/windows-release`, o script unico encontra automaticamente:

```text
tools/windows-release/node-v24.15.0-win-x64.zip
tools/windows-release/mysql-winx64.zip
tools/windows-release/WinSW-x64.exe
```

Tambem e possivel informar manualmente:

```powershell
.\scripts\windows-release\analicty-release.ps1 `
  -Version 0.1.5 `
  -Mode All `
  -NodeZipPath C:\tools\node-v24.15.0-win-x64.zip `
  -MySqlZipPath C:\tools\mysql-8.x.x-winx64.zip `
  -WinSWExePath C:\tools\WinSW-x64.exe
```

## Banco local embutido

Quando `-MySqlZipPath` e informado, ou quando `tools/windows-release/mysql-winx64.zip` existe, o instalador inclui um MySQL local dedicado ao AnalictY.

Configuracao criada automaticamente:

```text
Servico Windows: AnalictY MySQL
Host: 127.0.0.1
Porta: 3307
Banco: mes_analicty
Usuario: user_analicty
Senha: gerada automaticamente em data\secrets
```

O usuario define apenas a senha do `admin` do AnalictY no instalador. A senha do banco e tecnica e separada.

## Scripts internos

Os scripts abaixo continuam existindo, mas normalmente nao precisam ser chamados diretamente:

```text
build-release.ps1
create-update-package.ps1
upload-to-s3.ps1
release-and-upload.ps1
```

O `analicty-release.ps1` orquestra essas etapas.

## Aplicador de atualizacao

O release inclui:

```text
updater/apply-update.ps1
```

Esse script aplica um ZIP de atualizacao ja baixado e validado pelo backend:

```powershell
powershell.exe -ExecutionPolicy Bypass `
  -File "C:\Program Files\AnalictY\updater\apply-update.ps1" `
  -InstallRoot "C:\Program Files\AnalictY" `
  -PackagePath "C:\Program Files\AnalictY\data\updates\AnalictY-1.2.0.zip" `
  -TargetVersion "1.2.0" `
  -ExpectedSha256 "HASH_DO_ZIP"
```

O updater preserva `data/`, cria backup em `data/backups/`, registra log em `logs/updater/`, troca somente `app/`, `runtime/` e `installer/` quando esses diretorios existem dentro do ZIP, e valida se backend/frontend voltaram a responder.

## Observacoes

- O script nao baixa Node.js, WinSW ou Inno Setup automaticamente.
- Nao salve credenciais AWS em arquivos do repositorio. Use perfil do AWS CLI, variaveis de ambiente ou parametros no momento do upload.
- O instalador gera uma chave JWT local durante a instalacao.
- O servico Windows define `ANALICTY_DATA`, entao o SQLite e arquivos persistentes ficam em `data/`.
- O pacote de update nunca inclui `data/`, `logs/`, banco SQLite, tokens ou certificados reais.
