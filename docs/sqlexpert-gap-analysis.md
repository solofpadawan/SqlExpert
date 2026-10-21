# SqlExpert × IBExpert — Gap analysis

**Data:** 20/10/2026 (America/Sao_Paulo, UTC-3)
**Base analisada:** código do SqlExpert (estado após Fields tab, comandos desabilitados, correção de bindings e janela normal — 147 testes), `CommandCatalog` (249 comandos em 29 categorias), `CommandAvailability` (20 ids + 9 legendas de menu implementados) e `ARCHITECTURE.md`.
**Natureza:** análise somente leitura; nenhum código foi alterado. O mapeamento de funcionalidades vem apenas da interface visível (menus, telas e documentação pública), sem uso de código, binários ou recursos de terceiros.

## Legenda

| Campo | Valores |
|-------|---------|
| Status | ✅ **Feito** · 🟡 **Parcial** · ❌ **Falta** |
| Esforço | **S** (≤ 1 dia) · **M** (2–5 dias) · **L** (> 1 semana) |
| SQL Server | **FB** = específico de Firebird/IBExpert, não se aplica (ou só um análogo distante) · **EQ** = existe, mas precisa do equivalente SQL Server (indicado na nota) · vazio = mapeia direto |

## Visão geral do que já existe

- **Conexões:** Register / Unregister / Registration Info (DPAPI para senha, detecção de duplicado), Connect / Disconnect / Reconnect, banco ativo em verde.
- **Database Explorer:** categorias com contagem (User-Defined Types, Tables, Views, Procedures, Functions, Triggers, Sequences, Roles) com lazy-load; filtro (só nos bancos registrados).
- **Documentos:** uma aba por tabela aberta, Windows bar, menu Windows com a lista, Ctrl+Tab / Ctrl+F4, Close all / Close others.
- **Editor de tabela:** **Fields** completo (New / Edit / Drop / Reorder (script) / Copy list, Field description), **Data** (grade editável com navegador, Insert/Delete/Edit/Post/Cancel, TOP N, font size), **DDL** (CREATE TABLE somente leitura).
- **Infra:** `TableStructureService` (colunas, PK/UQ, CHECK, FKs nos dois sentidos, índices, triggers, MS_Description), builders de DDL puros com testes, `ScriptExecutor.SplitBatches` (GO) testado — **execução ainda stub**.
- **UX:** todos os comandos sem implementação aparecem desabilitados (cinza) de forma centralizada; janela normal com posição/estado persistidos em `settings.json`.

---

## 1. Database Explorer

| # | Item | Status | Esforço | SQL Server | Nota |
|---|------|--------|---------|-----------|------|
| 1.1 | Registrar / desregistrar / editar registro de banco | ✅ Feito | S | | DPAPI, duplicados, Test connection |
| 1.2 | Conectar / desconectar / reconectar | ✅ Feito | S | | |
| 1.3 | Nós de categoria com contagem (UDT, Tables, Views, Procedures, Functions, Triggers, Sequences, Roles) | ✅ Feito | S | EQ | Domains → UDT, Generators → Sequences |
| 1.4 | Abrir objeto ao dar duplo clique | 🟡 Parcial | M | | Só tabelas abrem; views/procs/functions/triggers/sequences/types/roles não |
| 1.5 | Nós que faltam: Schemas, Synonyms, Users, Indices, DB triggers, Exceptions, Assemblies, Table types | ❌ Falta | M | EQ | Exceptions → `sys.messages`; Users/Schemas/Synonyms são extras do SQL Server |
| 1.6 | Filtro do Explorer | 🟡 Parcial | S | | Hoje filtra só o nome do banco registrado; IBExpert filtra objetos dentro das categorias |
| 1.7 | Menu de contexto de banco (Register/Unregister/Connect/Disconnect/Reconnect/Registration Info) | ✅ Feito | S | | |
| 1.8 | Menu de contexto de objeto: New / Edit / Drop Object, Show data, Copy object, Goto Database | ❌ Falta | M | | 27 itens no OpenMenu; só os de banco funcionam |
| 1.9 | Activate / Deactivate triggers (todos / selecionados) | ❌ Falta | S | EQ | `ENABLE/DISABLE TRIGGER` |
| 1.10 | Activate / Deactivate stored procedures | ❌ Falta | — | FB | Não existe "procedure inativa" no SQL Server |
| 1.11 | Run Procedure..., Create SIUD procedures, Autogrant privileges | ❌ Falta | M | | SIUD = gerar procs SELECT/INSERT/UPDATE/DELETE |
| 1.12 | Get statistics for selected table(s) | ❌ Falta | S | EQ | `sp_spaceused`, `sys.dm_db_partition_stats`, `UPDATE STATISTICS` |
| 1.13 | Show objects description / Hide disconnected databases | ❌ Falta | S | | MS_Description na árvore |
| 1.14 | Find object in explorer tree / Find in the Database Explorer | ❌ Falta | S | | |
| 1.15 | Arrastar objeto/campo para o editor SQL | ❌ Falta | S | | Depende do SQL Editor |
| 1.16 | Aba **Project** (pastas do usuário, Add Object, User Items, Blocks) | ❌ Falta | M | EQ | Persistir no app (não em tabelas IBE$ do banco) |
| 1.17 | Aba **Windows** (lista de janelas abertas) | ❌ Falta | S | | A Windows bar já existe; falta a aba |
| 1.18 | Aba **Recent** (objetos/arquivos recentes, Clear recent list) | ❌ Falta | S | | |
| 1.19 | Database Objects / Inspector Page Mode / SQL Assistant | ❌ Falta | M | | Painel de propriedades do objeto selecionado |

## 2. Editor de tabela (abas)

| # | Aba / função | Status | Esforço | SQL Server | Nota |
|---|--------------|--------|---------|-----------|------|
| 2.1 | **Fields**: grade, New / Edit / Drop field, Copy list, Field description | ✅ Feito | — | | Transação única, validação, preview do T-SQL |
| 2.2 | Fields: Reorder fields | ✅ Feito | — | EQ | SQL Server não reordena: gera script de rebuild (nunca executa) |
| 2.3 | Fields: sub-aba Field dependencies | ❌ Falta | S | | `sys.sql_expression_dependencies` por coluna |
| 2.4 | Fields: editar coluna computada | ❌ Falta | S | | Hoje é recusado com mensagem |
| 2.5 | **Constraints** (PK, FK, UNIQUE, CHECK, DEFAULT; New / Drop) | ❌ Falta | M | | Os dados já são lidos pelo `TableStructureService` |
| 2.6 | **Indices** (lista, New / Drop, Copy name, rebuild/reorganize) | ❌ Falta | M | EQ | "Recompute selectivity" → `UPDATE STATISTICS`; incluir fragmentação |
| 2.7 | **Dependencies** (usa / usado por; árvore) | ❌ Falta | M | | `sys.sql_expression_dependencies`, `sys.dm_sql_referencing_entities` |
| 2.8 | **Triggers** (lista, editar, enable/disable, ordem) | ❌ Falta | M | EQ | Firebird POSITION/ACTIVE → `sp_settriggerorder`, `ENABLE/DISABLE TRIGGER` |
| 2.9 | **Data**: grade editável, navegador, Post/Cancel, TOP N, font size | ✅ Feito | — | | |
| 2.10 | Data: filtro (Filter panel, Quick add criteria), ordenação por coluna, incremental search | ❌ Falta | M | | Comandos "Data Grids" |
| 2.11 | Data: copiar como INSERT/UPDATE, copiar registros/célula, Save grid data as (CSV/Excel/JSON) | ❌ Falta | M | | |
| 2.12 | Data: Blob viewer/editor (texto, hex, imagem) e Form view | ⚠️ Parcial (20/10: Form View + memo editor + Print Data; falta editor hex/imagem de BLOB) | M | | `varbinary(max)`, `nvarchar(max)`, `xml` |
| 2.13 | Data: paginação além do TOP N / "fetch all" | 🟡 Parcial | S | | Só TOP N configurável |
| 2.14 | **Master/Detail View** (navegar pelas FKs) | ❌ Falta | M | | |
| 2.15 | **Description** (MS_Description da tabela, editável) | ❌ Falta | S | | Leitura já existe no metadata |
| 2.16 | **DDL** (CREATE TABLE) | 🟡 Parcial | S | | Faltam FKs, CHECKs, defaults nomeados, índices, triggers (o builder do Reorder já gera quase tudo) |
| 2.17 | **Grants** (permissões por usuário/role; Grant/Revoke to all) | ❌ Falta | M | EQ | `sys.database_permissions`, também `DENY` |
| 2.18 | **Logging** (histórico de alterações de dados/DDL) | ❌ Falta | M | EQ | IBExpert usa triggers + tabelas IBE$ → no SQL Server: log no próprio app, ou CDC / Change Tracking / tabelas temporais |
| 2.19 | **Comparison** (comparar a tabela com outro banco) | ❌ Falta | L | | Reaproveita o Database Comparer |
| 2.20 | **To-do** (itens por objeto) | ❌ Falta | M | EQ | IBExpert grava em tabela IBE$TODO → guardar no app (JSON) ou extended property |

## 3. Editores dos outros objetos

| # | Objeto | Status | Esforço | SQL Server | Nota |
|---|--------|--------|---------|-----------|------|
| 3.1 | **View** (DDL editável, Fields, Data, Dependencies, Grants) | ❌ Falta | M | | `OBJECT_DEFINITION` já existe no `MetadataService`, mas não tem UI |
| 3.2 | **Procedure** (editor, parâmetros, compilar, executar com parâmetros) | ❌ Falta | L | | `CREATE OR ALTER PROCEDURE`, `sys.parameters` |
| 3.3 | Debugger de procedure | ❌ Falta | L | EQ | IBExpert simula a execução; o SQL Server não tem API pública de debug T-SQL → baixa prioridade |
| 3.4 | **Function** (escalar / TVF; editor, executar) | ❌ Falta | M | EQ | Firebird UDF externa (DLL) → função T-SQL/CLR |
| 3.5 | **Trigger** (tabela, DDL de banco, logon) | ❌ Falta | M | EQ | before/after/position → AFTER / INSTEAD OF, ordem via `sp_settriggerorder` |
| 3.6 | **Sequence** (Generator: valor atual, Set value, Next value) | ❌ Falta | S | EQ | Generator → `SEQUENCE` (`ALTER SEQUENCE … RESTART`); para identity → `DBCC CHECKIDENT` |
| 3.7 | **Domain → UDT** (alias type, table type) | ❌ Falta | M | EQ | Alias type não tem CHECK/DEFAULT embutido como o domain do Firebird |
| 3.8 | **Exception** | ❌ Falta | S | EQ | `sp_addmessage` / `THROW` / `sys.messages` |
| 3.9 | **Role** (membros, permissões) | ❌ Falta | M | | `sys.database_role_members` |
| 3.10 | **User / Login** | ❌ Falta | M | EQ | Firebird users (security db) → login de servidor + usuário do banco |
| 3.11 | Schema / Synonym (extras do SQL Server) | ❌ Falta | S | | Não existem no IBExpert/Firebird |
| 3.12 | Comandos "New X" (Table, View, Procedure, Trigger, Generator, Exception, UDF, Role, Domain) | ❌ Falta | M | EQ | Templates de DDL com o nome IBExpert e tooltip com o equivalente SQL Server |

## 4. SQL Editor, Query Builder, Script Executive

| # | Item | Status | Esforço | SQL Server | Nota |
|---|------|--------|---------|-----------|------|
| 4.1 | **SQL Editor (F12) / New SQL Editor (Shift+F12)** com abas | ❌ Falta | L | | Peça central do uso diário |
| 4.2 | Realce de sintaxe, numeração de linhas, folding (AvalonEdit) | ❌ Falta | M | | |
| 4.3 | Code completion (objetos, colunas, aliases, palavras-chave) | ❌ Falta | L | | Reusa metadata do Explorer |
| 4.4 | Executar (F9 / Ctrl+Enter), executar seleção, cancelar, múltiplos result sets, aba Messages, tempo e linhas afetadas | ❌ Falta | M | | |
| 4.5 | Plano de execução | ❌ Falta | M | EQ | Firebird PLAN → `SET SHOWPLAN_XML` (estimado) / `STATISTICS XML` (real) |
| 4.6 | Estatísticas de performance | ❌ Falta | S | EQ | Reads/writes do IBExpert → `SET STATISTICS IO, TIME` |
| 4.7 | Histórico de SQL (SQL History) | ❌ Falta | S | | Persistir no app |
| 4.8 | Controle de transação (Commit / Rollback) | ❌ Falta | S | EQ | Firebird sempre em transação → modo autocommit / `IMPLICIT_TRANSACTIONS` e botões |
| 4.9 | Parâmetros (`:param`) com prompt | ❌ Falta | S | EQ | `@variáveis` / `sp_executesql` |
| 4.10 | Load / Save to file, recentes, Quick Load/Save | ❌ Falta | S | | |
| 4.11 | Find / Replace / Incremental search / Go to line / bookmarks | ❌ Falta | M | | Editors actions |
| 4.12 | Comentar/descomentar, conversões de case, UTF8, Copy as RTF | ❌ Falta | S | | |
| 4.13 | Code formatter (Format selected text + opções) | ❌ Falta | M | | Pode usar ScriptDom (`Microsoft.SqlServer.TransactSql.ScriptDom`) |
| 4.14 | **Query Builder** visual | ❌ Falta | L | | |
| 4.15 | **Script Executive** (scripts grandes, GO, parar/continuar em erro, log) | 🟡 Parcial | M | EQ | `SplitBatches` pronto e testado; execução é stub e não tem UI. Comandos IBEScript (SET TERM, CONNECT, CREATE DATABASE) → `GO`, `:r`, `:setvar` estilo sqlcmd |
| 4.16 | SQL Monitor | ❌ Falta | M | EQ | Firebird trace → Extended Events / `sys.dm_exec_requests` |

## 5. Menus principais

| # | Menu | Status | Esforço | SQL Server | Nota |
|---|------|--------|---------|-----------|------|
| 5.1 | **Database**: Register, Unregister, Connect, Disconnect, Reconnect, Registration Info, Exit | ✅ Feito | — | | |
| 5.2 | Database: Create / Drop / Recreate database | ❌ Falta | M | EQ | `CREATE/DROP DATABASE` (Firebird cria arquivo .fdb; SQL Server define arquivos/collation) |
| 5.3 | Database: New Domain/Table/View/Procedure/Trigger/Generator/Exception/UDF/Role | ❌ Falta | M | EQ | Ver 3.12 |
| 5.4 | Database: Recompute selectivity of all indices | ❌ Falta | S | EQ | `sp_updatestats` / `UPDATE STATISTICS` |
| 5.5 | Database: Recompile all stored procedures / Recompile & Recreate all triggers | ❌ Falta | S | EQ | `sp_recompile`; "recreate" quase não faz sentido no SQL Server |
| 5.6 | **Edit** (Load/Save, Find, Replace, Search Again, Go to, Print) | ❌ Falta | S | | Depende do SQL Editor; Cut/Copy/Paste nativos do WPF já funcionam |
| 5.7 | **Grid** (Apply best fit, Save grid data as, Copy current record, Copy all) | ❌ Falta | S | | |
| 5.8 | **View**: DB Explorer, Status bar, Windows bar | ✅ Feito | — | | |
| 5.9 | View: Toolbars (Main/Edit/Objects), Autohide DB Explorer, Database Objects | ❌ Falta | S | | |
| 5.10 | **Options** | ❌ Falta | M | | Ver seção 7 |
| 5.11 | **Tools** | ❌ Falta | L | | 30 itens, nenhum implementado (seção 6) |
| 5.12 | **Services** | ❌ Falta | L | | 13 itens, nenhum implementado (seção 6) |
| 5.13 | **Plugins** (Plugin Manager) | ❌ Falta | L | FB | API de plugins é do IBExpert/Delphi; se quiser: plugins .NET próprios (baixa prioridade) |
| 5.14 | **Windows**: Close All, Close all except this, lista de janelas, Ctrl+Tab, Windows bar | ✅ Feito | — | | |
| 5.15 | Windows: Cascade / Tile / Minimize all / Arrange / Windows Manager / Floating windows | ❌ Falta | M | | WPF não tem MDI; hoje são documentos em abas. Floating windows exige docking (ex. AvalonDock) |
| 5.16 | **Help** (About, What's new, documentação, suporte) | ❌ Falta | S | | Conteúdo original do SqlExpert |
| 5.17 | Toolbar principal | 🟡 Parcial | S | | Botões existem; só os de banco estão ativos |
| 5.18 | Menu **Grid**/**Data Grids**/**Editors actions** como menus de contexto | ❌ Falta | M | | Junto com 2.10–2.12 e 4.11–4.12 |

## 6. Ferramentas e serviços (Tools / Services)

| # | Ferramenta | Status | Esforço | SQL Server | Nota |
|---|-----------|--------|---------|-----------|------|
| 6.1 | **Extract Metadata** (script do banco inteiro / objetos selecionados, com ou sem dados) | ❌ Falta | L | | Builders de DDL atuais são a base |
| 6.2 | **Export data** (CSV, Excel, JSON, XML, INSERTs) | ❌ Falta | M | | |
| 6.3 | **Import data** (CSV/Excel → tabela) | ❌ Falta | L | | `SqlBulkCopy`, mapeamento de colunas |
| 6.4 | **Backup Database** | ❌ Falta | M | EQ | gbak → `BACKUP DATABASE … WITH STATS` (progresso via mensagens) |
| 6.5 | **Restore Database** | ❌ Falta | M | EQ | `RESTORE DATABASE … WITH MOVE`, `RESTORE FILELISTONLY` |
| 6.6 | Backup/Restore Scheduler (hkIBRS) e Scheduler (tarefas) | ❌ Falta | L | EQ | SQL Agent jobs; **Express não tem Agent** → Agendador do Windows |
| 6.7 | **Database Statistics** | ❌ Falta | M | EQ | gstat → `sp_spaceused`, `sys.dm_db_index_physical_stats`, contagem por tabela |
| 6.8 | Database Validation | ❌ Falta | S | EQ | gfix → `DBCC CHECKDB` |
| 6.9 | Database Shutdown / Database Online | ❌ Falta | S | EQ | gfix -shut → `ALTER DATABASE SET SINGLE_USER / OFFLINE / ONLINE` |
| 6.10 | Database Properties | ❌ Falta | M | EQ | page size/sweep/forced writes → recovery model, compat level, collation, arquivos, `ALTER DATABASE SET` |
| 6.11 | Server Properties / Log | ❌ Falta | M | EQ | `SERVERPROPERTY`, `sys.configurations`, `xp_readerrorlog` |
| 6.12 | **Database Monitoring** (sessões, requests, bloqueios, kill) | ❌ Falta | M | EQ | MON$ → `sys.dm_exec_sessions/requests`, `sys.dm_tran_locks`, `KILL` |
| 6.13 | Trace and Audit | ❌ Falta | L | EQ | Firebird trace API → Extended Events / SQL Audit |
| 6.14 | Secondary Files Manager | ❌ Falta | M | EQ | Arquivos secundários do Firebird → files/filegroups (`sys.database_files`) |
| 6.15 | Communication Diagnostics | 🟡 Parcial | S | | Test connection do Register existe; falta diagnóstico (porta, instância, SQL Browser, TLS) |
| 6.16 | Services Control Center | ❌ Falta | M | EQ | Firebird service manager → serviços SQL Server via `ServiceController` / WMI |
| 6.17 | Benchmark | ❌ Falta | M | | Baixa prioridade |
| 6.18 | **User Manager** (logins, usuários, roles) | ❌ Falta | M | EQ | `sys.server_principals`, `sys.database_principals` |
| 6.19 | **Grant Manager** (matriz objeto × principal) | ❌ Falta | L | EQ | `GRANT/REVOKE/DENY`, `sys.database_permissions` |
| 6.20 | **Search in Metadata** | ❌ Falta | M | | `sys.sql_modules`, `sys.objects`, `sys.columns`, extended properties |
| 6.21 | **Table Data Comparer** | ❌ Falta | L | | Comparar dados por PK e gerar script de sincronização |
| 6.22 | **Database Comparer** (schema) | ❌ Falta | L | | Diff de objetos e script de migração |
| 6.23 | Dependencies Viewer | ❌ Falta | M | | Ver 2.7, em janela própria |
| 6.24 | SP/Triggers/Views Analyzer | ❌ Falta | M | EQ | Análise de PLAN do Firebird → plano em cache, `sys.dm_db_missing_index_*`, avisos |
| 6.25 | Log Manager | ❌ Falta | L | EQ | Triggers de log do IBExpert → CDC / Change Tracking / temporal tables |
| 6.26 | Copy database object (entre bancos) | ❌ Falta | M | | |
| 6.27 | To-do list | ❌ Falta | M | EQ | Ver 2.20 |
| 6.28 | Print Metadata / Generate HTML Documentation | ❌ Falta | M | | Pode usar MS_Description |
| 6.29 | Database Designer (diagrama ER) | ❌ Falta | L | | |
| 6.30 | Test data generator | ❌ Falta | M | | |
| 6.31 | Blob Viewer/Editor | ❌ Falta | M | | Ver 2.12 |
| 6.32 | Report Manager | ❌ Falta | L | | IBExpert usa FastReport; baixa prioridade |
| 6.33 | Data Analysis / OLAP | ❌ Falta | L | | Baixa prioridade |
| 6.34 | ODBC Viewer | ❌ Falta | S | | Baixa prioridade |
| 6.35 | Database Inside | ❌ Falta | — | FB | Internals de páginas do Firebird; no SQL Server só `DBCC PAGE` (não documentado) → não fazer |
| 6.36 | IBEBlocks / Blocks / scripting (PAX, JavaScript) | ❌ Falta | — | FB | Linguagem própria do IBExpert; análogo seria scripting em C# (fora do escopo) |
| 6.37 | Localize SqlExpert / Localize SQL Server messages | ❌ Falta | M | | Recursos pt-BR/en |

## 7. Options e Environment Options

| # | Item | Status | Esforço | SQL Server | Nota |
|---|------|--------|---------|-----------|------|
| 7.1 | Persistência de preferências (`settings.json`) | ✅ Feito | — | | Font size do Data e posição/estado da janela |
| 7.2 | Environment Options (confirmações, comportamento ao abrir objeto, clique simples/duplo, idioma, pastas) | ❌ Falta | M | | Hoje as preferências só mudam pela própria UI (font size) |
| 7.3 | Editor Options (fonte, tabs, cores, auto-complete) | ❌ Falta | M | | Depende do SQL Editor |
| 7.4 | Visual Options (tema, fontes da UI, cores da grade) | ❌ Falta | M | | |
| 7.5 | Object Editor Options (aba inicial, TOP N padrão, confirmar DDL) | ❌ Falta | S | | |
| 7.6 | Keyboard Templates (atalhos de texto: "sf" → SELECT * FROM) | ❌ Falta | S | | |
| 7.7 | General Templates (templates de DDL dos "New X") | ❌ Falta | S | | |
| 7.8 | Code formatting options | ❌ Falta | M | | Junto com 4.13 |
| 7.9 | Customize toolbars/commands (diálogo Customize do IBExpert) | ❌ Falta | M | | O `CommandCatalog` já é a base |

---

## 8. Contagem por status

| Área | ✅ Feito | 🟡 Parcial | ❌ Falta | Total |
|------|---------|-----------|---------|-------|
| Database Explorer | 4 | 2 | 13 | 19 |
| Editor de tabela | 3 | 2 | 15 | 20 |
| Outros objetos | 0 | 0 | 12 | 12 |
| SQL Editor / QB / Script | 0 | 1 | 15 | 16 |
| Menus principais | 3 | 1 | 14 | 18 |
| Tools / Services | 0 | 1 | 36 | 37 |
| Options | 1 | 0 | 8 | 9 |
| **Total** | **11** | **7** | **113** | **131** |

Marcações: **4** itens FB (específicos de Firebird/IBExpert, não se aplicam) e **46** itens EQ (precisam do equivalente SQL Server).

No catálogo de comandos: 249 comandos, 20 ids + 9 legendas de menu implementados (≈ 8%); o restante aparece desabilitado.

## 9. Roadmap priorizado (uso diário de DBA/dev)

| Prioridade | Entrega | Itens cobertos | Esforço | Por quê |
|-----------|---------|---------------|---------|---------|
| 1 | **SQL Editor** (F12 / Shift+F12): abas, AvalonEdit com realce, executar tudo/seleção (F9/Ctrl+Enter), cancelar, GO, vários result sets na grade + aba Messages, tempo/linhas, Load/Save, Find/Replace | 4.1, 4.2, 4.4, 4.10, 4.11, 5.6 | L | É a ferramenta mais usada no dia a dia; destrava Edit menu, Script Executive e editores de objetos |
| 2 | **Script Executive** real (reusa `SplitBatches` + executor do SQL Editor), parar/continuar em erro, log por batch | 4.15 | M | Rodar scripts de deploy/migração; a base já está testada |
| 3 | **Editores de View / Procedure / Function / Trigger** (DDL editável com `CREATE OR ALTER`, compilar, Dependencies, executar procedure com parâmetros) + abrir pelo duplo clique | 1.4, 3.1, 3.2, 3.4, 3.5 | L | Manutenção de código no banco é tarefa diária |
| 4 | **Constraints e Indices** (lista, New/Drop, fragmentação, rebuild) + **DDL completo** da tabela | 2.5, 2.6, 2.16 | M | Os dados já são carregados pelo `TableStructureService` (custo baixo, valor alto) |
| 5 | **Menu de contexto do Explorer** (New/Edit/Drop Object, Show data, Copy name, Script as CREATE, Refresh, Enable/Disable triggers) + filtro de objetos | 1.6, 1.8, 1.9, 1.14 | M | Fluxo IBExpert "clique direito → ação" |
| 6 | **Comandos da grade** (copiar como INSERT/UPDATE, copiar registros, Save grid data as CSV/Excel/JSON, filtro, ordenar, blob viewer) para Data e resultados do SQL Editor | 2.10–2.12, 5.7, 6.2, 6.31 | M | Exportar e copiar dados é diário; vale para as duas grades |
| 7 | **Search in Metadata** | 6.20 | M | "Onde essa coluna/tabela é usada?" é pergunta constante |
| 8 | **Plano de execução + STATISTICS IO/TIME + SQL History** no SQL Editor | 4.5, 4.6, 4.7 | M | Tuning e reaproveitar queries |
| 9 | **Database Monitoring** (sessões, requests, bloqueios, kill) + **Database Statistics** | 6.12, 6.7 | M | Diagnóstico de lentidão/bloqueio |
| 10 | **Backup / Restore** com progresso | 6.4, 6.5 | M | Tarefa de DBA frequente; essencial no Express (sem Agent) |

**Logo depois:** Dependencies (2.7 / 6.23), Extract Metadata (6.1), User/Grant Manager (6.18 / 6.19), Description editável (2.15), Explorer Recent/Windows (1.17 / 1.18), Environment/Editor Options (7.2 / 7.3), code completion (4.3).

**Deixar por último ou não fazer:** Database Inside (FB), Blocks/IBEBlock (FB), Plugins (FB), Activate/Deactivate procedures (FB), debugger de procedure, OLAP, Report Manager, Benchmark, ODBC Viewer, Query Builder visual (L, uso mais raro para quem escreve SQL).
