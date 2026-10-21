# SqlExpert — Arquitetura v1

**Produto:** SqlExpert  
**Framing:** cliente desktop *IBExpert-like for SQL Server* (UX/layout/menus/fluxos inspirados no IBExpert; **código e assets 100% originais** — sem copiar binários, ícones proprietários ou código do IBExpert).  
**Público:** usuários Windows (pt-BR) que conhecem o fluxo do IBExpert e trabalham com SQL Server.

Projeto independente, sem afiliação com o IBExpert/HK-Software. Nenhuma imagem, binário ou recurso de terceiros faz parte do repositório.

---

## 1. Objetivos

- Replicar a **sensação** do IBExpert: Database Explorer à esquerda, editor de objeto (MDI/abas) no centro, status bar, menu denso e toolbar agrupada.
- Trabalhar nativamente com **SQL Server** (TDS via `Microsoft.Data.SqlClient`), não Firebird/InterBase.
- Entregar um **MVP v1 útil**: registrar conexões, explorar metadados, editar/ver tabelas (Fields/DDL/Data), SQL Editor multi-aba, Script Executive com `GO`, grade de resultados.
- Manter labels no estilo IBExpert onde fizer sentido (ex.: *New Generator*), com **tooltip** explicando o equivalente SQL Server (*SEQUENCE*).

## 2. Não-objetivos (v1)

- Schema/data comparer completo, backup/restore UI polida, scheduler, debugger de procedures, import multi-SGBD, OLAP real, “Database Inside” profundo.
- Qualquer engenharia reversa de licença/proteção do IBExpert.
- Copiar bitmaps/ícones do IBExpert (usar Segoe MDL2 / Fluent / ícones livres).
- Rodar no Linux: o app é **WPF Windows**; o box só faz scaffold/compile com `EnableWindowsTargeting`.

## 3. Stack

| Camada | Escolha |
|--------|---------|
| UI | .NET 8, WPF, MVVM (CommunityToolkit.Mvvm / RelayCommand) |
| Dados | `Microsoft.Data.SqlClient` |
| Testes | xUnit (`SqlExpert.Tests`) |
| Segredos | Windows Credential Manager / DPAPI (nunca senha em git) |

## 4. Layout da solution

```
/workspace/ssms-like-v1/
  SqlExpert.sln
  ARCHITECTURE.md
  README.md
  docs/                       # documentação (gap analysis)
  SqlExpert.App/              # WPF shell (menus, toolbar, explorer, object editor)
  SqlExpert.Core/             # conexão, metadados, script runner (sem UI)
  SqlExpert.Tests/
```

### Responsabilidades

- **SqlExpert.App:** `MainWindow` (menu/toolbar/explorer/editor), ViewModels, comandos stub → status bar.
- **SqlExpert.Core:** `ISqlConnectionFactory`, `IMetadataService`, `IScriptExecutor` + modelos (`ConnectionProfile`, árvore de objetos, batches).
- **SqlExpert.Tests:** unitários do splitter `GO` e builders de connection string.


## 4.1 Connection Manager (implementado)

- `IConnectionProfileStore` → `%AppData%/SqlExpert/connections.json` (sem senha).
- `ICredentialStore` → produção: `DpapiCredentialStore` (DPAPI CurrentUser em `%AppData%/SqlExpert/secrets/`); testes: `InMemoryCredentialStore`.
- `ISqlConnectionFactory` → `Microsoft.Data.SqlClient` (Build / Open / Test).
- `IMetadataService` → `sys.*` com lazy-load no Explorer; Fields + DDL (`DdlScriptBuilder`) para tabelas.

## 5. Fluxo de dados (v1)

```
Register Database → ConnectionProfile (segredo fora do disco/git)
       ↓
Connect → ISqlConnectionFactory.CreateOpenConnectionAsync
       ↓
Database Explorer ← IMetadataService.GetObjectTreeAsync (sys.* / INFORMATION_SCHEMA)
       ↓
Abrir objeto → editor com abas (Fields, Constraints, …, DDL)
       ↓
SQL Editor / Script Executive → IScriptExecutor (split GO → batches) → Result Grid
       ↓
Status bar / change log
```

## 6. Mapeamento Explorer / Object Editor (IBExpert → SQL Server)

| Nó / aba IBExpert | SQL Server |
|-------------------|------------|
| Domains | User-defined types (`CREATE TYPE`) |
| Tables / Views / Procedures / Triggers | `sys.tables`, `sys.views`, `sys.procedures`, `sys.triggers` |
| Generators | `sys.sequences` |
| Exceptions | `sys.messages` / `THROW` / `sp_addmessage` |
| UDFs | Functions (scalar / TVF / CLR) |
| Roles | Database roles |
| Fields | Columns (`sys.columns`) — grid: #, PK, FK, UNQ, name, type, size, scale, null, default, collation, description |
| Constraints / Indices | `sys.key_constraints`, `sys.check_constraints`, `sys.indexes` |
| Dependencies | `sys.sql_expression_dependencies` / `sys.dm_sql_referencing_entities` |
| Data | `SELECT` paginado |
| Description | Extended property `MS_Description` |
| DDL | Script via SMO-like queries / `OBJECT_DEFINITION` + montagem CREATE |
| Grants | `sys.database_permissions` / `sys.database_principals` |

Abas do editor de tabela (v1 stub visual, implementação gradual):  
**Fields, Constraints, Indices, Dependencies, Triggers, Data, Master/Detail View, Description, DDL, Grants, Logging, Comparison, To-do.**

## 7. Especificação de menus (stubs → comandos)

Ordem e separadores espelham o IBExpert. Atalhos iguais quando aplicável. Itens dependentes de conexão usam `StubRequiresConnection` (desabilitados sem connect).

### 7.1 Database

| Item | Atalho | SQL Server / nota |
|------|--------|-------------------|
| Database Registration Info... | | Editar `ConnectionProfile` |
| Register Database | Shift+Alt+R | Novo perfil |
| Unregister Database | Shift+Alt+U | Remove perfil |
| — | | |
| Connect to Database | Shift+Ctrl+C | Open connection |
| Reconnect | | Close+Open |
| Disconnect from Database | | Dispose connection |
| — | | |
| Create Database... | | `CREATE DATABASE` |
| Drop Database | | `DROP DATABASE` |
| Recreate database | | Drop+Create (cuidado) |
| — | | |
| Recompute selectivity of all indices | | `UPDATE STATISTICS` / `sp_updatestats` |
| Recompile all stored procedures | | `sp_recompile` |
| Recompile All Triggers | | `sp_recompile` em tabelas |
| — | | |
| New Domain | | `CREATE TYPE` (UDT) |
| New Table / View / Procedure / Trigger | | DDL correspondente |
| New Generator | | `CREATE SEQUENCE` |
| New Exception | | `sp_addmessage` / padrão THROW |
| New UDF | | `CREATE FUNCTION` |
| New Role | | `CREATE ROLE` |
| — | | |
| Exit | Alt+F4 | Encerrar app |

### 7.2 Edit

Load from File (Ctrl+L, submenu) · Save to File (Ctrl+S, submenu) · Save as... ·  
Cut/Copy/Paste/Select All · Find (Ctrl+F) · Search Again (F3) · Replace (Ctrl+R) · Incremental Search · Go to... (Ctrl+G) ·  
Print preview · Print · Page setup.

### 7.3 Grid

Apply best fit (Ctrl+NUM +) · Save grid data as... (Shift+Ctrl+S) · Copy current record · Copy all to clipboard.

### 7.4 View

[✓] DB Explorer (F11) · [✓] Status bar · [✓] Windows Bar · Toolbars (submenu) · Autohide DB Explorer.

### 7.5 Options

Environment / Editor / Visual Options... · Keyboard Templates... · General templates... · Object Editor Options... · Code formatting options...

### 7.6 Tools

| Item | Atalho | SQL Server / nota |
|------|--------|-------------------|
| SQL Editor | F12 | Editor T-SQL |
| New SQL Editor | Shift+F12 | Nova aba |
| Query builder | | Visual (v2+) |
| Data Analysis / OLAP | | Fora do núcleo v1 |
| Script Executive | Ctrl+F12 | Batches com `GO` (IBEScript-like) |
| Copy database object | | Script+create noutro DB |
| SQL Monitor | Ctrl+M | Extended Events / Profiler-like |
| Dependencies Viewer | | DMVs de dependência |
| SP/Triggers/Views Analyzer | | Análise estática T-SQL |
| Database / Table Data Comparer | | **v2** |
| Log Manager / Import data | | |
| Search in Metadata | Shift+Alt+F | Full-text em definições |
| Extract / Print Metadata · HTML docs | | |
| User Manager · Grant Manager | | Logins/users · GRANT/DENY |
| Secondary Files Manager | | Files / filegroups |
| To-do list | | |
| Localize SQL Server Messages · Localize SqlExpert | | (no IB: IB Messages / IBExpert) |
| Report Manager · Blob Viewer/Editor (disabled) | | |
| Database designer · Test data generator | | |
| ODBC Viewer · Database Inside | | |

### 7.7 Services

| Item | SQL Server |
|------|------------|
| Database Monitoring | DMVs / Activity Monitor |
| Backup / Restore Database | `BACKUP` / `RESTORE DATABASE` |
| Server Properties/Log | |
| Trace and audit | XEvents / SQL Audit |
| Database Validation | `DBCC CHECKDB` |
| Database Statistics | `sys.dm_db_index_physical_stats` / `UPDATE STATISTICS` |
| Database Properties | |
| Database Shutdown / Online | `ALTER DATABASE SET OFFLINE/ONLINE` |
| Communication Diagnostics | Teste de rede/TDS |
| Benchmark | |
| **SqlExpert Services Control Center** | (equivalente ao Services Control Center) |

### 7.8 Windows

Windows Manager (Alt+0) · Close All · Cascade · Tile H/V · Minimize All · Arrange · lista dinâmica de janelas de objeto (checkmark na ativa).

### 7.9 Help

What's new? · Online documentation · Learning Center · Additional Help Files (submenu) · Product Home Page · Send bug reports · Support · About · SqlExpert Direct... · **Download SQL Server tools** (no lugar de Download Firebird).

## 8. Toolbar

Referência: fluxo de UX conhecido dos usuários (sem imagens de terceiros no repositório).  
`ToolBarTray` com os mesmos grupos/separadores; cada botão chama o **mesmo** `CommandParameter` do menu; tooltip = label do menu.  
Itens que exigem conexão: `IsEnabled` via `StubRequiresConnection` / `IsConnected`.

| Grupo (E→D) | Botões | Nota |
|-------------|--------|------|
| Registro | Register DB, Unregister DB | |
| Conexão | Connect, Disconnect, Reconnect | |
| Info/Sair | DB Registration Info, Exit | |
| SQL | SQL Editor, New SQL Editor, Query Builder | |
| Script | Script Executive | |
| Meta | SQL Monitor, Extract Metadata, Print Metadata | confirmado via `toolbar.png` |
| Segurança | User Manager, Grant Manager | |
| Extra | Report Manager, Blob Viewer (disabled) | confirmado via `toolbar.png` |
| Arquivo | Load/Save, Save | |
| Clipboard | Cut, Copy, Paste | |
| Busca | Find, Search Again, Replace | |
| New objects | Domain, Table, View, Procedure, Trigger, Generator, Exception, UDF, Role | |

Ícones: placeholders de texto/emoji no skeleton; substituir por Segoe MDL2 / Fluent Icon na próxima iteração visual (**não** copiar PNGs do IBExpert).

## 9. Segurança

- `ConnectionProfile.PasswordSecretId` aponta para cofre local; **proibido** commit de connection strings com senha.
- `.gitignore` cobre `*.user`, `appsettings.*.local.json`, pastas de credencial.
- Preferir autenticação Windows / Azure AD quando possível.
- Encrypt=true por padrão; TrustServerCertificate só explícito (dev).

## 10. Roadmap

### v1 (este skeleton → implementação)

1. Connection Manager (register/connect/disconnect) + cofre de senha.  
2. Metadata real (`sys.*`) no Explorer.  
3. Object editor Fields/DDL/Data para tabelas.  
4. SQL Editor multi-aba + histórico.  
5. Script Executive com `GO` + Results Grid + erros SQL Server.  
6. Properties / description (extended properties).

### v2+

Schema/data compare, backup/restore UI, Services Control Center, Query Builder, SQL Monitor (XEvents), designer ER, scheduler, debugger.

## 11. Princípio legal / ético

SqlExpert é **software novo**, independente e sem afiliação com o IBExpert/HK-Software. Nenhum binário, imagem ou recurso proprietário de terceiros entra no repositório.

## 12. Registry central de comandos

Fonte única: `SqlExpert.Core/Commands/CommandCatalog.cs` (catálogo de comandos por categoria).

| Artefato | Papel |
|----------|--------|
| `SqlExpert.Core/Commands/CommandCatalog.cs` | Definições: Id, Category, Caption, Shortcut, EnabledWhen, SqlServerMapping |
| `SqlExpert.App/Services/AppCommandRegistry.cs` | Liga cada Id a um `ICommand` WPF com regra de enable |
| Menus / toolbar / popups | Resolvem por **Id** (não por string solta) |

### Regras de enable (`CommandEnabledWhen`)

- `Always` — ex.: Register Database, SQL Editor, Options, Help  
- `Connected` — maioria dos Tools/Services/New Object  
- `ConnectedWithSelection` — itens cinza do OpenMenu até haver seleção (Activate/Deactivate triggers, etc.)  
- `Never` — stub permanente até existir feature (ex.: Blob Viewer/Editor)

### Popups e toolbars de editor

| Categoria catálogo | Onde aparece no SqlExpert |
|--------------------|---------------------------|
| **OpenMenu** | Context menu do Database Explorer |
| **Fields** | Toolbar da aba Fields |
| **Indices** | Toolbar da aba Indices |
| **Constraints** | Toolbar da aba Constraints |
| **Dependencies** | Toolbar da aba Dependencies |
| **Grants** | Toolbar da aba Grants |
| **Data Grids** / **Grids** | Ações da grade de dados / grade genérica |
| **Editors actions** | Editor SQL / DDL |
| **Todo** / **Sheduler** / **Blocks** | Painéis correspondentes (v1 stub / v2) |

### Mapeamento SQL Server por categoria

| Categoria | Equivalente SQL Server (resumo) |
|-----------|----------------------------------|
| Default | Localizar objeto no Explorer (`sys.objects`) |
| Database | Connection profiles; `CREATE`/`DROP DATABASE`; `CREATE TYPE/TABLE/VIEW/PROC/TRIGGER/SEQUENCE/FUNCTION/ROLE`; `UPDATE STATISTICS`; `sp_recompile`; ENABLE/DISABLE TRIGGER |
| View | Visibilidade de painéis/toolbars |
| Tools | SQL Editor; Script Executive (`GO`); Extended Events; extract metadata; User/Grant Manager; comparers (v2) |
| Options | Preferências locais do app |
| Services | `BACKUP`/`RESTORE`; `DBCC CHECKDB`; DMVs; `ALTER DATABASE SET OFFLINE/ONLINE`; SQL Audit; **SqlExpert Services Control Center** |
| Windows | Gerência de janelas MDI/abas |
| Help | Docs SqlExpert; **Download SQL Server tools** |
| OpenMenu | New/Edit/Drop object; **Create SIUD procedures** → gerar procs SELECT/INSERT/UPDATE/DELETE; Autogrant → `GRANT`; Activate/Deactivate triggers → `ENABLE`/`DISABLE TRIGGER`; Get statistics → `sys.dm_db_partition_stats` / `sys.dm_db_index_usage_stats`; Show data → `SELECT TOP` |
| Editors actions | Edição de texto T-SQL (bookmarks, case, UTF-8, format) |
| Data Grids | Clipboard como INSERT/UPDATE; filtros; ajuste de colunas |
| Fields | `ALTER TABLE` add/alter/drop column; reorder |
| Indices | `CREATE`/`DROP INDEX` |
| Constraints | `ALTER TABLE` ADD/DROP CONSTRAINT |
| Dependencies | Árvore via `sys.sql_expression_dependencies` |
| Grants | `GRANT` / `REVOKE` / `DENY` (+ WITH GRANT OPTION) |
| Grids | Best fit / export / copy |
| Blocks | Scripts/blocos reutilizáveis (inspiração IBEBlock; implementação própria) |
| Sheduler | **SQL Agent jobs** (`msdb`) |
| Todo | Lista local de tarefas do projeto |
| ProjectPopup / RecentMenu / WindowsMenu / Menus | Navegação / projetos / recentes |
| Plugins | Extensibilidade (v2+) |
| SQLParamsFrame / SP Editor / Status / Designer | Vazias ou mínimas no catálogo IBExpert — reservadas |

Comandos ainda não implementados executam stub (atualizam a status bar com Id + mapping).
