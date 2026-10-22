using System.Windows;
using System.Windows.Input;
using Microsoft.Data.SqlClient;
using SqlExpert.App.Commands;
using SqlExpert.App.Services;
using SqlExpert.App.Views;
using SqlExpert.Core.Commands;
using SqlExpert.Core.Documents;
using SqlExpert.Core.Models;
using SqlExpert.Core.Procedures;
using SqlExpert.Core.Services;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.App.ViewModels;

/// <summary>Stored procedures: editor documents, the "Procedures" folder / procedure node popups and batch maintenance runs.</summary>
public partial class MainViewModel
{
    private readonly ObjectVersionHistoryStore _objectHistory = new();

    /// <summary>True when <paramref name="profileId"/> is the connected registration.</summary>
    public bool IsProfileConnected(string profileId) => IsConnected && _activeProfileId == profileId;

    private static bool IsProceduresFolder(ExplorerNode? n) =>
        n is { NodeKind: ExplorerNodeKind.Category, ObjectKind: ObjectKind.Procedure };

    private static bool IsProcedureNode(ExplorerNode? n) =>
        n is { NodeKind: ExplorerNodeKind.Object, ObjectKind: ObjectKind.Procedure, SchemaName: not null, ObjectName: not null, DatabaseName: not null };

    private SqlEditorSession CreateObjectSession(string profileId, string database)
    {
        var profile = FindProfile(profileId);
        return new SqlEditorSession(ct => _connections.CreateOpenConnectionAsync(profileId, ct), database)
        {
            Autocommit = profile?.SqlEditorAutocommit ?? true
        };
    }

    private string ConnectionLabel(string profileId)
    {
        var profile = FindProfile(profileId);
        if (profile is null) return profileId;
        var server = string.IsNullOrWhiteSpace(profile.Instance) ? profile.Server : $"{profile.Server}\\{profile.Instance}";
        return $"{ProfileLabel(profile)} ({server})";
    }

    private async Task<SqlConnection> OpenDatabaseConnectionAsync(string profileId, string database, CancellationToken ct = default)
    {
        var conn = await _connections.CreateOpenConnectionAsync(profileId, ct).ConfigureAwait(true);
        try
        {
            if (!string.Equals(conn.Database, database, StringComparison.OrdinalIgnoreCase))
                await conn.ChangeDatabaseAsync(database, ct).ConfigureAwait(true);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(true);
            throw;
        }
    }

    // ===== Documents =====

    /// <summary>Opens the procedure editor (or activates it when that procedure is already open).</summary>
    public async Task<ProcedureDocumentViewModel?> OpenProcedureDocumentAsync(string database, string schema, string name)
    {
        if (!IsConnected || _activeProfileId is null) return null;
        var profileId = _activeProfileId;
        var key = DocumentKeys.Procedure(profileId, database, schema, name);
        if (_docs.Find(key) is ProcedureDocumentViewModel open)
        {
            ActiveDocument = open;
            open.Host?.FocusEditor();
            StatusText = $"Ativado: [{schema}].[{name}]";
            return open;
        }
        var doc = new ProcedureDocumentViewModel(this, profileId, database, schema, name, ConnectionLabel(profileId),
            CreateObjectSession(profileId, database), _objectHistory, FindProfile(profileId)?.SqlEditorFetchRows ?? UiSettings.Instance.SqlFetchRows);
        _docs.OpenOrActivate(doc.Key, () => doc);
        StatusText = $"Abrindo [{schema}].[{name}]...";
        await doc.LoadAsync().ConfigureAwait(true);
        StatusText = doc.StatusLine;
        doc.Host?.FocusEditor();
        return doc;
    }

    /// <summary>New Procedure: template NEW_PROCEDURE in the given (or selected / current) database and schema.</summary>
    public async Task NewProcedureAsync(string? database = null, string? schema = null)
    {
        if (!IsConnected || _activeProfileId is null)
        {
            StatusText = "New Procedure: conecte a um database primeiro.";
            return;
        }
        var profileId = _activeProfileId;
        var node = _selectedNode is { } n && n.ProfileId == profileId ? n : null;
        var profile = FindProfile(profileId);
        database ??= node?.DatabaseName ?? _activeDatabase ?? profile?.InitialCatalog ?? "master";
        schema ??= IsProcedureNode(node) ? node!.SchemaName : "dbo";
        var doc = new ProcedureDocumentViewModel(this, profileId, database, schema ?? "dbo", null, ConnectionLabel(profileId),
            CreateObjectSession(profileId, database), _objectHistory, profile?.SqlEditorFetchRows ?? UiSettings.Instance.SqlFetchRows);
        _docs.OpenOrActivate(doc.Key, () => doc);
        await doc.LoadAsync().ConfigureAwait(true);
        StatusText = $"New Procedure em {database}: digite o nome na caixa à direita da barra; Ctrl+F9 compila.";
    }

    /// <summary>After a compile/drop committed on the server: refreshes the Procedures folder (list + count) of that database.</summary>
    public async Task OnProcedureChangedAsync(string profileId, string database, string schema, string name, bool created)
    {
        InvalidateCompletionCatalogs(profileId, database);
        if (!IsProfileConnected(profileId)) return;
        var folder = FindProceduresFolder(profileId, database);
        if (folder is null) return;
        try
        {
            await ReloadProceduresFolderAsync(folder).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = "Explorer não atualizado: " + ex.Message;
        }
    }

    /// <summary>Description saved in the editor: updates the Explorer node text/tooltip.</summary>
    public void UpdateProcedureDescription(string profileId, string database, string schema, string name, string? description)
    {
        var folder = FindProceduresFolder(profileId, database);
        var node = folder?.Children.FirstOrDefault(c => IsProcedureNode(c)
            && string.Equals(c.SchemaName, schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.ObjectName, name, StringComparison.OrdinalIgnoreCase));
        if (node is not null) node.Description = string.IsNullOrWhiteSpace(description) ? null : description;
    }

    private ExplorerNode? FindProceduresFolder(string profileId, string database)
    {
        var root = ExplorerRoots.FirstOrDefault(r => r.ProfileId == profileId);
        var db = root?.Children.FirstOrDefault(d => d.NodeKind == ExplorerNodeKind.Database && string.Equals(d.DatabaseName, database, StringComparison.OrdinalIgnoreCase));
        return db?.Children.FirstOrDefault(IsProceduresFolder);
    }

    /// <summary>Reloads one folder (children + count) without touching the rest of the tree.</summary>
    private async Task ReloadProceduresFolderAsync(ExplorerNode folder)
    {
        folder.LoadTask = null;
        await LoadCategoryChildrenAsync(folder).ConfigureAwait(true);
        folder.Count = folder.Children.Count(c => c.NodeKind == ExplorerNodeKind.Object);
        OnExplorerTreeChanged();
    }

    // ===== Explorer popups =====

    /// <summary>Order of the "Procedures" folder popup (classic layout; separators come from the catalog).</summary>
    internal static readonly string[] ProceduresFolderMenuIds =
    {
        "procfolder.new_procedure",
        "procfolder.apply_block_to_selected_objects",
        "procfolder.autogrant_privileges",
        "procfolder.deactivate_procedures",
        "procfolder.activate_procedures",
        "procfolder.goto_database",
        "procfolder.goto_object",
        "procfolder.refresh",
        "procfolder.connect_to_database",
        "procfolder.reconnect",
        "procfolder.disconnect_from_database",
        "procfolder.register_database",
        "procfolder.unregister_database",
        "procfolder.clone_registration_info",
        "procfolder.database_registration_info",
        "procfolder.recompute_selectivity_of_all_indices",
        "procfolder.recompile_all_stored_procedures",
        "procfolder.recompile_all_triggers",
        "procfolder.show_sql_assistant",
        "procfolder.show_objects_description",
        "procfolder.inspector_page_mode",
        "procfolder.hide_disconnected_databases",
    };

    /// <summary>Order of the popup on one procedure.</summary>
    internal static readonly string[] ProcedureNodeMenuIds =
    {
        "procnode.edit_procedure",
        "procnode.new_procedure",
        "procnode.drop_procedure",
        "procnode.execute",
        "procnode.copy_name",
        "procnode.script_to_clipboard",
        "procnode.apply_block_to_selected_objects",
        "procnode.autogrant_privileges",
        "procnode.refresh",
    };

    private const int GotoObjectLimit = 400;

    /// <summary>Popup of the Procedures folder or of a procedure node (null for other nodes).</summary>
    private List<CommandMenuItem>? BuildProcedureMenu(ExplorerNode node)
    {
        var folderMenu = IsProceduresFolder(node);
        if (!folderMenu && !IsProcedureNode(node)) return null;
        var profile = node.ProfileId is null ? null : FindProfile(node.ProfileId);
        var ctx = new ExplorerMenuContext(profile is not null, profile is not null && IsProfileConnected(profile.Id), _session.Profiles.Count);
        var disabled = new RelayCommand(_ => { }, _ => false);

        CommandMenuItem Item(string id, bool isChecked = false, IReadOnlyList<CommandMenuItem>? children = null,
            object? parameter = null, string? caption = null, string? tip = null)
        {
            var def = CommandCatalog.Find(id) ?? throw new InvalidOperationException($"Comando '{id}' não está no catálogo.");
            var implemented = CommandAvailability.IsImplemented(id);
            var mapping = def.SqlServerMapping is null ? "" : $"\nSQL Server: {def.SqlServerMapping}";
            return new CommandMenuItem
            {
                Id = id,
                Caption = caption ?? def.Caption,
                Shortcut = def.Shortcut,
                ToolTip = tip ?? (implemented ? def.Caption + mapping : $"{def.Caption} — ainda não implementado{mapping}"),
                IsChecked = isChecked,
                Children = children,
                CommandParameter = parameter,
                // Submenu parents of unimplemented features get a disabled command so the whole entry is greyed.
                Command = children is null
                    ? new RelayCommand(p => ExecuteProcedureMenuItem(id, node, p), _ => CommandAvailability.CanExecuteExplorerItem(id, ctx))
                    : implemented ? null : disabled
            };
        }

        var ids = folderMenu ? ProceduresFolderMenuIds : ProcedureNodeMenuIds;
        var items = new List<CommandMenuItem>();
        foreach (var id in ids)
        {
            var def = CommandCatalog.Find(id)!;
            if (def.IsSeparatorBefore && items.Count > 0)
                items.Add(new CommandMenuItem { Id = id + ".sep", Caption = "-", IsSeparator = true });
            switch (id)
            {
                case "procfolder.deactivate_procedures":
                    items.Add(Item(id, children: new[] { Item("procfolder.deactivate_all"), Item("procfolder.deactivate_selected") }));
                    break;
                case "procfolder.activate_procedures":
                    items.Add(Item(id, children: new[] { Item("procfolder.activate_all"), Item("procfolder.activate_selected") }));
                    break;
                case "procfolder.goto_database":
                {
                    var targets = _session.Ordered(ExplorerSortByAlias)
                        .Select(p => Item(id, isChecked: p.Id == node.ProfileId, parameter: p.Id, caption: ProfileLabel(p).Replace("_", "__"),
                            tip: $"{ServerNameText.Format(p)} : {p.InitialCatalog}"))
                        .ToList();
                    if (targets.Count == 0)
                        targets.Add(new CommandMenuItem { Id = id + ".none", Caption = "(nenhum database registrado)", Command = disabled });
                    items.Add(Item(id, children: targets));
                    break;
                }
                case "procfolder.goto_object":
                {
                    var objects = node.HasDummyChild ? new List<ExplorerNode>() : node.Children.Where(c => IsProcedureNode(c)).ToList();
                    var targets = objects.Take(GotoObjectLimit)
                        .Select(o => Item(id, parameter: o, caption: $"{o.SchemaName}.{o.ObjectName}".Replace("_", "__"), tip: o.Description))
                        .ToList();
                    if (node.HasDummyChild)
                        targets.Add(new CommandMenuItem { Id = id + ".notloaded", Caption = "(expanda a pasta para listar as procedures)", Command = disabled });
                    else if (targets.Count == 0)
                        targets.Add(new CommandMenuItem { Id = id + ".none", Caption = "(nenhuma procedure)", Command = disabled });
                    else if (objects.Count > GotoObjectLimit)
                        targets.Add(new CommandMenuItem { Id = id + ".more", Caption = $"(+{objects.Count - GotoObjectLimit} — use o filtro do Explorer)", Command = disabled });
                    items.Add(Item(id, children: targets));
                    break;
                }
                case "procfolder.show_objects_description":
                    items.Add(Item(id, isChecked: ShowObjectsDescription, tip: "Mostra a descrição (MS_Description) ao lado dos objetos do Explorer"));
                    break;
                case "procfolder.hide_disconnected_databases":
                    items.Add(Item(id, isChecked: HideDisconnectedDatabases));
                    break;
                default:
                    items.Add(Item(id));
                    break;
            }
        }
        return items;
    }

    private void ExecuteProcedureMenuItem(string id, ExplorerNode node, object? parameter)
    {
        if (!CommandAvailability.IsImplemented(id)) return;
        if (CommandAvailability.SharedExplorerId(id) is { } shared)
        {
            ExecuteExplorerItem(shared, parameter);
            return;
        }
        var database = node.DatabaseName;
        switch (id)
        {
            case "procfolder.new_procedure":
                RunSafe(() => NewProcedureAsync(database));
                break;
            case "procnode.new_procedure":
                RunSafe(() => NewProcedureAsync(database, node.SchemaName));
                break;
            case "procfolder.goto_object":
                if (parameter is ExplorerNode target) GotoExplorerObject(node, target);
                break;
            case "procfolder.refresh":
                RunSafe(() => RefreshProceduresFolderInteractiveAsync(node));
                break;
            case "procnode.refresh":
                if (FindParentFolder(node) is { } parent) RunSafe(() => RefreshProceduresFolderInteractiveAsync(parent));
                break;
            case "procfolder.recompute_selectivity_of_all_indices":
                RunSafe(() => RunMaintenanceAsync(ProcedureCatalog.MaintenanceKind.UpdateStatistics, database));
                break;
            case "procfolder.recompile_all_stored_procedures":
                RunSafe(() => RunMaintenanceAsync(ProcedureCatalog.MaintenanceKind.RecompileProcedures, database));
                break;
            case "procfolder.recompile_all_triggers":
                RunSafe(() => RunMaintenanceAsync(ProcedureCatalog.MaintenanceKind.RecompileTriggers, database));
                break;
            case "procnode.edit_procedure":
                RunSafe(async () => await OpenProcedureDocumentAsync(node.DatabaseName!, node.SchemaName!, node.ObjectName!).ConfigureAwait(true));
                break;
            case "procnode.execute":
                RunSafe(async () =>
                {
                    var doc = await OpenProcedureDocumentAsync(node.DatabaseName!, node.SchemaName!, node.ObjectName!).ConfigureAwait(true);
                    if (doc is not null && doc.ExecuteCommand.CanExecute(null)) doc.ExecuteCommand.Execute(null);
                });
                break;
            case "procnode.drop_procedure":
                RunSafe(() => DropProcedureFromExplorerAsync(node));
                break;
            case "procnode.copy_name":
            {
                var text = ProcedureScriptBuilder.QualifiedName(node.SchemaName!, node.ObjectName!);
                Clipboard.SetText(text);
                StatusText = $"Nome copiado: {text}";
                break;
            }
            case "procnode.script_to_clipboard":
                RunSafe(() => ScriptProcedureToClipboardAsync(node));
                break;
        }
    }

    private ExplorerNode? FindParentFolder(ExplorerNode node) =>
        node.ProfileId is null || node.DatabaseName is null ? null : FindProceduresFolder(node.ProfileId, node.DatabaseName);

    private void GotoExplorerObject(ExplorerNode folder, ExplorerNode target)
    {
        if (!target.FilterVisible && !string.IsNullOrWhiteSpace(_filterText)) FilterText = "";
        folder.IsExpanded = true;
        target.IsSelected = true;
        _selectedNode = target;
        StatusText = $"Selecionado: {target.Caption}";
    }

    private async Task RefreshProceduresFolderInteractiveAsync(ExplorerNode folder)
    {
        if (folder.ProfileId is null || !IsProfileConnected(folder.ProfileId)) return;
        StatusText = $"Atualizando Procedures de {folder.DatabaseName}...";
        await ReloadProceduresFolderAsync(folder).ConfigureAwait(true);
        folder.IsExpanded = true;
        StatusText = $"Procedures de {folder.DatabaseName}: {folder.Count ?? 0}.";
    }

    /// <summary>F5 on the Procedures folder or a procedure: refreshes only that folder. False = not a procedure node.</summary>
    public bool RefreshProceduresShortcut()
    {
        var node = _selectedNode;
        var folder = IsProceduresFolder(node) ? node : IsProcedureNode(node) ? FindParentFolder(node!) : null;
        if (folder?.ProfileId is null || !IsProfileConnected(folder.ProfileId)) return false;
        RunSafe(() => RefreshProceduresFolderInteractiveAsync(folder));
        return true;
    }

    /// <summary>Ctrl+N in the Explorer on the Procedures folder or a procedure. False = not a procedure node.</summary>
    public bool NewProcedureShortcut()
    {
        var node = _selectedNode;
        if (!IsProceduresFolder(node) && !IsProcedureNode(node)) return false;
        if (node!.ProfileId is null || !IsProfileConnected(node.ProfileId)) return false;
        RunSafe(() => NewProcedureAsync(node.DatabaseName, IsProcedureNode(node) ? node.SchemaName : null));
        return true;
    }

    private static MessageBoxResult AskUser(string text, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult def) =>
        MessageDialog.Show(null, text, "SqlExpert", buttons, image, def);

    private async Task DropProcedureFromExplorerAsync(ExplorerNode node)
    {
        if (node.ProfileId is null || !IsProfileConnected(node.ProfileId)) return;
        var (db, schema, name) = (node.DatabaseName!, node.SchemaName!, node.ObjectName!);
        var key = DocumentKeys.Procedure(node.ProfileId, db, schema, name);
        if (_docs.Find(key) is ProcedureDocumentViewModel open && open.InTransaction)
        {
            ShowInfo("O editor desta procedure tem uma transação aberta. Faça Commit ou Rollback nele antes do Drop.", MessageBoxImage.Warning);
            ActiveDocument = open;
            return;
        }
        var sql = ProcedureScriptBuilder.BuildDrop(schema, name);
        if (AskUser($"Excluir a procedure do database {db}?\n\nSQL:\n{sql}\n\nEsta operação não pode ser desfeita.",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var conn = await OpenDatabaseConnectionAsync(node.ProfileId, db).ConfigureAwait(true);
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(true);
        }
        catch (SqlException ex)
        {
            ShowInfo("DROP falhou:\n\n" + ex.Message, MessageBoxImage.Error);
            return;
        }
        StatusText = $"Excluída: [{schema}].[{name}] ({db}).";
        if (_docs.Find(key) is ProcedureDocumentViewModel doc) RemoveDocument(doc);
        if (FindParentFolder(node) is { } folder) await ReloadProceduresFolderAsync(folder).ConfigureAwait(true);
    }

    private async Task ScriptProcedureToClipboardAsync(ExplorerNode node)
    {
        if (node.ProfileId is null || !IsProfileConnected(node.ProfileId)) return;
        await using var conn = await OpenDatabaseConnectionAsync(node.ProfileId, node.DatabaseName!).ConfigureAwait(true);
        var info = await ProcedureCatalog.LoadAsync(conn, node.SchemaName!, node.ObjectName!).ConfigureAwait(true);
        if (info?.Definition is not { } definition)
        {
            ShowInfo("Definição indisponível (WITH ENCRYPTION, sem permissão VIEW DEFINITION ou a procedure não existe mais).", MessageBoxImage.Warning);
            return;
        }
        Clipboard.SetText(definition.TrimEnd() + ProcedureScriptBuilder.NewLine + "GO" + ProcedureScriptBuilder.NewLine);
        StatusText = $"Script CREATE de [{node.SchemaName}].[{node.ObjectName}] copiado.";
    }

    // ===== Batch maintenance (Recompile all…, Recompute selectivity…) =====

    private async Task RunMaintenanceAsync(ProcedureCatalog.MaintenanceKind kind, string? database)
    {
        if (!IsConnected || _activeProfileId is null) return;
        var profileId = _activeProfileId;
        database ??= (_selectedNode is { } n && n.ProfileId == profileId ? n.DatabaseName : null) ?? _activeDatabase ?? FindProfile(profileId)?.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database)) return;
        var title = kind switch
        {
            ProcedureCatalog.MaintenanceKind.RecompileProcedures => "Recompile all stored procedures",
            ProcedureCatalog.MaintenanceKind.RecompileTriggers => "Recompile All Triggers",
            _ => "Recompute selectivity of all indices"
        };

        IReadOnlyList<(string Name, string Sql)> targets;
        await using (var conn = await OpenDatabaseConnectionAsync(profileId, database).ConfigureAwait(true))
            targets = await ProcedureCatalog.ListTargetsAsync(conn, kind).ConfigureAwait(true);
        if (targets.Count == 0)
        {
            ShowInfo($"{title}: nenhum objeto em {database}.");
            return;
        }
        var what = kind switch
        {
            ProcedureCatalog.MaintenanceKind.RecompileProcedures => "stored procedure(s)",
            ProcedureCatalog.MaintenanceKind.RecompileTriggers => "trigger(s)",
            _ => "tabela(s) de usuário"
        };
        var note = kind == ProcedureCatalog.MaintenanceKind.UpdateStatistics
            ? "Atualiza as estatísticas de cada tabela (pode demorar e gerar I/O em bancos grandes)."
            : "Revalida cada objeto contra as tabelas/colunas atuais; os que falharem são listados no fim (nada é alterado neles).";
        if (AskUser($"{title}\n\nDatabase: {database}\n{targets.Count} {what}.\n\n{ProcedureCatalog.Describe(kind)}\n\n{note}\n\nContinuar?",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var owner = Application.Current?.MainWindow;
        var progress = new BatchProgressWindow($"{title} — {database}");
        if (owner is not null) progress.Owner = owner;
        progress.Show();
        BatchOperationResult result;
        try
        {
            await using var conn = await OpenDatabaseConnectionAsync(profileId, database).ConfigureAwait(true);
            result = await ProcedureCatalog.RunAsync(conn, targets, progress.Progress, progress.Token).ConfigureAwait(true);
        }
        finally
        {
            progress.Finish();
        }
        var report = ProcedureCatalog.FormatReport($"{title} — {database}", result);
        StatusText = $"{title}: {result.Done}/{result.Total}, {result.Failures.Count} com erro{(result.Cancelled ? " (cancelado)" : "")}.";
        TextReportWindow.ShowReport(owner, title, report);
    }

    /// <summary>Database menu captions handled here (null = not a procedure action).</summary>
    private bool HandleProcedureMenuAction(string name)
    {
        switch (name)
        {
            case "New Procedure":
                RunSafe(() => NewProcedureAsync());
                return true;
            case "Recompile all stored procedures":
                RunSafe(() => RunMaintenanceAsync(ProcedureCatalog.MaintenanceKind.RecompileProcedures, null));
                return true;
            case "Recompile All Triggers":
                RunSafe(() => RunMaintenanceAsync(ProcedureCatalog.MaintenanceKind.RecompileTriggers, null));
                return true;
            case "Recompute selectivity of all indices":
                RunSafe(() => RunMaintenanceAsync(ProcedureCatalog.MaintenanceKind.UpdateStatistics, null));
                return true;
            default:
                return false;
        }
    }
}
