# SqlExpert

Cliente desktop para **Microsoft SQL Server** (Windows, .NET 8 / WPF), inspirado no fluxo de trabalho do IBExpert: Database Explorer à esquerda, editores de objeto em abas, SQL Editor, menus e barras de ferramentas densos.

> **Projeto independente.** O SqlExpert não é afiliado, patrocinado ou endossado pela HK-Software nem pelo IBExpert. "IBExpert" é marca de seus respectivos donos e é citado aqui apenas como referência de experiência de uso. Todo o código e todos os recursos visuais deste repositório são originais.

Licença: [MIT](./LICENSE).

## Funcionalidades (versão 1.0.0)

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
  - Aba **DDL** com o `CREATE TABLE` completo.
  - Aba **Data** com **Grid View**, **Form View** e **Print Data** (impressão e PDF). Permite editar com Post/Cancel, mostra `<null>` em vermelho e tem lookup de **chave estrangeira** ("Data of [tabela]", F4 ou Alt+↓).
- **SQL Editor**
  - Realce de T-SQL (AvalonEdit), execução de tudo ou da seleção, lotes com `GO` e vários result sets.
  - Aba de mensagens, tempo de execução e linhas afetadas, além de controle de transação (autocommit, commit, rollback).
  - Histórico de consultas e localizar/substituir.
- Splash screen e **Help > About** com versão e data do build lidas do próprio executável.

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

Para atualizar, feche o programa e substitua os arquivos da pasta pela versão nova. Suas conexões e preferências ficam guardadas fora da pasta do programa.

## Onde ficam as configurações

Tudo fica em `%AppData%\SqlExpert` (por exemplo `C:\Users\<você>\AppData\Roaming\SqlExpert`):

| O quê | Arquivo |
|-------|---------|
| Bancos registrados (servidor, alias, opções; **sem senha**) | `connections.json` |
| Senhas de login SQL, criptografadas com **Windows DPAPI** (só o seu usuário do Windows consegue ler) | `secrets\*.bin` |
| Preferências da interface (janela, fonte da grade etc.) | `settings.json` |
| Histórico do SQL Editor | `history\` |

Para começar do zero, feche o programa e apague essa pasta.

## Compilar a partir do código

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) no Windows. O Visual Studio 2022 é opcional.

```powershell
git clone https://github.com/solofpadawan/SqlExpert.git
cd SqlExpert
dotnet build SqlExpert.sln -c Release
dotnet run --project SqlExpert.App -c Release
```

Para gerar o pacote de release (um único `.exe`, self-contained):

```powershell
dotnet publish SqlExpert.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\SqlExpert-win-x64
```

### Testes

```powershell
dotnet test SqlExpert.sln -c Release
```

Os testes unitários não precisam de servidor. Os **testes de integração** só rodam quando a variável de ambiente `SQLEXPERT_IT_CONNSTR` está definida; sem ela, eles são ignorados. Eles são somente leitura e esperam o banco de exemplo `SqlExpertTeste`:

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
| `SqlExpert.Core` | Conexão, metadados (`sys.*`), geração de DDL, dados da tabela, SQL Editor, impressão/PDF (sem dependência de WPF) |
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
