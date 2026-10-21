# SqlExpert

Cliente desktop para **Microsoft SQL Server** (Windows, .NET 8 / WPF), inspirado no fluxo de trabalho do IBExpert: Database Explorer à esquerda, editores de objeto em abas, SQL Editor, menus e barras de ferramentas densos.

> **Projeto independente.** O SqlExpert não é afiliado, patrocinado ou endossado pela HK-Software nem pelo IBExpert. "IBExpert" é marca de seus respectivos donos e é citado aqui apenas como referência de experiência de uso. Todo o código e todos os recursos visuais deste repositório são originais.

Licença: [MIT](./LICENSE).

## Funcionalidades (versão 1.0.1)

- **Registro de bancos**
  - Register Database e **Database Registration Info**, com alias, servidor, protocolo, autenticação Windows ou SQL e parâmetros extras de conexão.
  - **Test Connect**, que mostra a versão do servidor.
  - **Color Mark**, uma cor por banco para separar produção de teste.
  - Scripts *After Connect* / *Before Disconnect* e padrões do SQL Editor por banco (autocommit, linhas buscadas).
  - Clone Registration Info e Unregister, que remove só o registro e nunca apaga o banco.
- **Database Explorer**
  - Árvore com Tables, Views, Procedures, Functions, Triggers, Sequences etc., com as contagens em azul.
  - Menu de contexto do banco: Connect, Reconnect, Disconnect, Refresh, Sort nodes, Hide Disconnected Databases…
  - **Filtro** rápido em todos os níveis, que ignora maiúsculas e acentos.
- **Editor de tabela**
  - Aba **Fields**: adicionar, editar, excluir e reordenar campos. Para reordenar, gera um script de rebuild e não o executa sozinho.
  - Opção **Primary key** no New field / Edit field: cria `PK_<tabela>` ou inclui/tira o campo da chave existente (DROP + ADD na mesma transação, mantendo nome, ordem e CLUSTERED/NONCLUSTERED). Recusa a mudança quando há foreign keys apontando para a chave.
  - Aba **Constraints** com PRIMARY KEY, UNIQUE, FOREIGN KEY e CHECK da tabela: **New constraint** (primary key) e **Drop constraint**.
  - Aba **DDL** com o `CREATE TABLE` completo.
  - Aba **Data** com **Grid View**, **Form View** e **Print Data** (impressão e PDF). Permite editar com Post/Cancel, mostra `<null>` em vermelho e tem lookup de **chave estrangeira** ("Data of [tabela]", F4 ou Alt+↓).
- **SQL Editor**
  - Realce de T-SQL (AvalonEdit), execução de tudo ou da seleção, lotes com `GO` e vários result sets.
  - Aba de mensagens, tempo de execução e linhas afetadas, além de controle de transação (autocommit, commit, rollback).
  - Histórico de consultas e localizar/substituir.
- **Editor de procedure**
  - Duplo clique numa procedure, **Edit Procedure** ou **New Procedure** (Ctrl+N na pasta Procedures).
  - Grade de parâmetros (tipo, tamanho, default, OUTPUT, READONLY, descrição) com o cabeçalho gerado a partir dela, e o corpo no editor; **Edit full text** alterna para o texto completo do `CREATE`.
  - **Compile** (Ctrl+F9) com `CREATE OR ALTER` (ALTER/CREATE antes do SQL Server 2016 SP1); com Autocommit desligado o compile fica numa transação aberta até Commit/Rollback. Erros vão para Messages com a linha do corpo.
  - **Execute** (F9) pede os valores dos parâmetros e mostra os result sets, Messages, os OUTPUT e o RETURN.
  - Abas Description (MS_Description), Dependencies, DDL, Grants e **Version History** local de cada compile.
  - Menu da pasta Procedures: Recompile all stored procedures / Recompile All Triggers (`sp_refreshsqlmodule`, com relatório das falhas) e Recompute selectivity of all indices (`UPDATE STATISTICS`).
- Splash screen e **Help > About** com versão e data do build lidas do próprio executável.
- **Atualização automática** pelas Releases do GitHub: ao abrir (Options > *Check for updates on startup*) e em **Help > Check for updates...**. Mostra a versão nova e as notas, com *Atualizar agora*, *Depois* e *Pular esta versão*; baixa, confere tamanho e SHA-256, instala e abre de novo.

Comandos ainda não implementados aparecem desabilitados (cinza) nos menus e barras.

## Requisitos

- Windows 10 ou 11, **x64**.
- Acesso a uma instância do SQL Server, local ou remota (o SQL Server Express serve).
- O `.zip` da release é **self-contained**: **não** é preciso instalar o .NET.

## Baixar e executar

1. Abra a página de [**Releases**](https://github.com/solofpadawan/SqlExpert/releases) e baixe `SqlExpert-<versão>-win-x64.zip`.
2. Extraia o `.zip` numa pasta qualquer, por exemplo `C:\Ferramentas\SqlExpert`.
3. Execute `SqlExpert.exe`.

O executável não tem assinatura digital, então o Windows SmartScreen pode mostrar **"O Windows protegeu o computador"**. Clique em **Mais informações** e depois em **Executar assim mesmo**.

### Atualizar

A partir da versão 1.0.1 o SqlExpert se atualiza sozinho:

1. Ao abrir, ele consulta (sem login, só leitura) a lista de Releases deste repositório. Se houver uma versão maior que a sua, aparece a janela **Atualização disponível** com as notas da versão.
2. **Atualizar agora** baixa o `.zip` para `%LocalAppData%\SqlExpert\updates`, confere o tamanho e o SHA-256 (quando publicado), extrai e fecha o programa (perguntando sobre textos não salvos e transações abertas, como num fechamento normal).
3. Um atualizador (uma cópia do próprio `SqlExpert.exe`, rodando fora da pasta do programa) espera o programa fechar, guarda os arquivos antigos, copia os novos e abre o SqlExpert de novo. Se algo falhar, os arquivos antigos voltam e a versão anterior continua funcionando.

**Depois** pergunta de novo na próxima vez; **Pular esta versão** só volta a avisar quando sair uma versão mais nova. Versões marcadas como *pre-release* só são oferecidas com a opção **Receber versões de teste (pre-release)** (na janela de atualização e em **Options**); durante a série 1.x ela vem ligada. A verificação ao abrir pode ser desligada em **Options > Check for updates on startup**. O resultado de cada verificação fica em `%AppData%\SqlExpert\update.log`.

Se a pasta do programa não permitir gravação (por exemplo, dentro de `C:\Program Files`), o SqlExpert oferece abrir a página da Release para baixar manualmente. Nesse caso, feche o programa e substitua os arquivos da pasta pela versão nova. Suas conexões e preferências ficam guardadas fora da pasta do programa.

## Onde ficam as configurações

Tudo fica em `%AppData%\SqlExpert` (por exemplo `C:\Users\<você>\AppData\Roaming\SqlExpert`):

| O quê | Arquivo |
|-------|---------|
| Bancos registrados (servidor, alias, opções; **sem senha**) | `connections.json` |
| Senhas de login SQL, criptografadas com **Windows DPAPI** (só o seu usuário do Windows consegue ler) | `secrets\*.bin` |
| Preferências da interface (janela, fonte da grade etc.) | `settings.json` |
| Histórico do SQL Editor | `history\` |
| Log das verificações e instalações de atualização | `update.log` |
| Versões compiladas de procedures (Version History) | `history\objects\` |

Para começar do zero, feche o programa e apague essa pasta.

## Compilar a partir do código

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) no Windows. O Visual Studio 2022 é opcional.

```powershell
git clone https://github.com/solofpadawan/SqlExpert.git
cd SqlExpert
dotnet build SqlExpert.sln -c Release
dotnet run --project SqlExpert.App -c Release
```

### Publicar uma Release

O pacote é um único `SqlExpert.exe` self-contained. O atualizador é o próprio `SqlExpert.exe` (modo `--apply-update`), então não há outro arquivo para incluir no `.zip`. O script abaixo lê a versão do `SqlExpert.App.csproj`, roda o `dotnet publish`, gera o `.zip` e o `.sha256` em `publish\` e mostra a linha `SHA256:`:

```powershell
powershell -ExecutionPolicy Bypass -File tools\packaging\publish-release.ps1
```

Equivalente manual:

```powershell
dotnet publish SqlExpert.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\SqlExpert-1.0.1-win-x64
Compress-Archive publish\SqlExpert-1.0.1-win-x64\* publish\SqlExpert-1.0.1-win-x64.zip
(Get-FileHash -Algorithm SHA256 publish\SqlExpert-1.0.1-win-x64.zip).Hash.ToLower()
```

Para que a atualização automática encontre a versão:

| Item | Formato | Exemplo |
|------|---------|---------|
| Versão no projeto | `<Version>` em `SqlExpert.App/SqlExpert.App.csproj` | `1.0.2` |
| Tag da Release | `vX.Y.Z` (ou `vX.Y.Z-sufixo` para testes) | `v1.0.2`, `v1.1.0-beta.1` |
| Arquivo anexado | `SqlExpert-X.Y.Z-win-x64.zip`, com `SqlExpert.exe` na raiz (ou numa única pasta) | `SqlExpert-1.0.2-win-x64.zip` |
| SHA-256 (opcional, recomendado) | linha `SHA256: <hex>` nas notas **e/ou** o arquivo `SqlExpert-X.Y.Z-win-x64.zip.sha256` anexado | `SHA256: 3a7b…4f1b` |

- A tag precisa ser **maior** que a versão instalada (comparação semver: `1.0.10` > `1.0.9`, `1.1.0-beta` < `1.1.0`).
- Releases em rascunho (*draft*) são ignoradas. Releases marcadas como *pre-release* (ou com tag `-sufixo`) só chegam a quem marcou **Receber versões de teste**.
- Se o SHA-256 for publicado e não bater com o download, a atualização é cancelada. Sem SHA-256, só o tamanho do arquivo é conferido.

### Testes

```powershell
dotnet test SqlExpert.sln -c Release
```

Os testes unitários não precisam de servidor. Os **testes de integração** só rodam quando a variável de ambiente `SQLEXPERT_IT_CONNSTR` está definida; sem ela, eles são ignorados. Eles só leem os dados de exemplo; quando precisam criar algo (procedures, tabela de teste de chave primária), usam objetos temporários `sqlexpert_it_…` que apagam no fim. Esperam o banco de exemplo `SqlExpertTeste`:

1. Crie o banco rodando [`testdb/SqlExpertTeste.sql`](./testdb/SqlExpertTeste.sql) na sua instância, pelo SQL Editor do próprio SqlExpert, pelo SSMS ou com `sqlcmd -S localhost\SQLEXPRESS -E -i testdb\SqlExpertTeste.sql`.
2. Defina a connection string e rode os testes:

```powershell
$env:SQLEXPERT_IT_CONNSTR = 'Server=localhost\SQLEXPRESS;Database=SqlExpertTeste;Integrated Security=true;TrustServerCertificate=true'
dotnet test SqlExpert.sln -c Release
```

## Estrutura

| Projeto | Função |
|---------|--------|
| `SqlExpert.App` | Interface WPF: janela principal, Explorer, editores, SQL Editor, diálogos |
| `SqlExpert.Core` | Conexão, metadados (`sys.*`), geração de DDL, dados da tabela, SQL Editor, impressão/PDF, verificação/instalação de atualizações (sem dependência de WPF) |
| `SqlExpert.Tests` | Testes xUnit (unitários e de integração opcionais) |

Mais detalhes em [ARCHITECTURE.md](./ARCHITECTURE.md) e no roteiro em [docs/sqlexpert-gap-analysis.md](./docs/sqlexpert-gap-analysis.md).

## Reportar bugs e sugestões

Use a aba [**Issues**](https://github.com/solofpadawan/SqlExpert/issues). Ajuda bastante incluir:

- a versão, que aparece em **Help > About** ou na splash;
- a versão do Windows e do SQL Server;
- os passos para reproduzir, o que você esperava e o que aconteceu;
- um print da tela ou da mensagem de erro.

**Nunca** cole senhas, connection strings com senha, nomes de servidores internos ou dados reais da empresa nas issues.

## Licença

Distribuído sob a licença [MIT](./LICENSE). Copyright (c) 2026 Saulo Padovan.
