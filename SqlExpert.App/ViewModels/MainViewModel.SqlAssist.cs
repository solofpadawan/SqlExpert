using SqlExpert.Core.Models;
using SqlExpert.Core.Services;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.App.ViewModels;

/// <summary>SQL Editor assistance: object catalog for code completion / object links, and opening a linked object.</summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<(string Profile, string Database), Task<CompletionCatalog>> _catalogs = new();

    /// <summary>Raised when catalogs were dropped (Explorer refresh, DDL run in an editor): (profile id, database or null = all).</summary>
    public event Action<string, string?>? CompletionCatalogInvalidated;

    private static (string, string) CatalogKey(string profileId, string database) => (profileId, database.ToUpperInvariant());

    /// <summary>Catalog of a database, read once per connection/database (in the background) and cached.</summary>
    public Task<CompletionCatalog> GetCompletionCatalogAsync(string profileId, string database)
    {
        var key = CatalogKey(profileId, database);
        if (_catalogs.TryGetValue(key, out var task) && !task.IsFaulted && !task.IsCanceled) return task;
        task = Task.Run(async () =>
        {
            await using var conn = await _connections.CreateOpenConnectionAsync(profileId).ConfigureAwait(false);
            if (!string.Equals(conn.Database, database, StringComparison.OrdinalIgnoreCase))
                await conn.ChangeDatabaseAsync(database).ConfigureAwait(false);
            return await CompletionCatalogLoader.LoadAsync(conn).ConfigureAwait(false);
        });
        _catalogs[key] = task;
        return task;
    }

    /// <summary>Drops cached catalogs of <paramref name="profileId"/> (one database, or all when null); editors reload them.</summary>
    public void InvalidateCompletionCatalogs(string? profileId, string? database = null)
    {
        if (profileId is null) return;
        foreach (var key in _catalogs.Keys.Where(k => k.Profile == profileId
                                                     && (database is null || k.Database == database.ToUpperInvariant())).ToList())
            _catalogs.Remove(key);
        CompletionCatalogInvalidated?.Invoke(profileId, database);
    }

    /// <summary>
    /// Double-click on an underlined name in an editor: tables and procedures open their editor; the other kinds
    /// (no editor yet) are selected in the Database Explorer.
    /// </summary>
    public async Task OpenObjectFromEditorAsync(string profileId, string database, CatalogObject obj)
    {
        if (!IsConnected || _activeProfileId != profileId)
        {
            StatusText = "Conecte ao database deste editor para abrir objetos.";
            return;
        }
        switch (obj.Kind)
        {
            case CompletionKind.Table:
                await OpenTableDocumentAsync(database, obj.Schema, obj.Name).ConfigureAwait(true);
                return;
            case CompletionKind.Procedure:
                await OpenProcedureDocumentAsync(database, obj.Schema, obj.Name).ConfigureAwait(true);
                return;
        }
        if (obj.ObjectKind is not { } kind || !MetadataSql.ExplorerKinds.Contains(kind))
        {
            StatusText = $"{obj.Kind} [{obj.Schema}].[{obj.Name}]: este tipo de objeto ainda não aparece no Database Explorer.";
            return;
        }
        if (await SelectInExplorerAsync(profileId, database, kind, obj.Schema, obj.Name).ConfigureAwait(true))
            StatusText = $"{obj.Kind} [{obj.Schema}].[{obj.Name}] selecionado no Database Explorer (editor deste tipo ainda não disponível).";
        else
            StatusText = $"[{obj.Schema}].[{obj.Name}] não encontrado no Database Explorer (Refresh / F5 atualiza a lista).";
    }

    /// <summary>Expands database and folder of the object and selects its node (loading them when needed).</summary>
    private async Task<bool> SelectInExplorerAsync(string profileId, string database, ObjectKind kind, string schema, string name)
    {
        var root = ExplorerRoots.FirstOrDefault(r => r.ProfileId == profileId);
        var db = root?.Children.FirstOrDefault(d => d.NodeKind == ExplorerNodeKind.Database
                                                    && string.Equals(d.DatabaseName, database, StringComparison.OrdinalIgnoreCase));
        if (root is null || db is null) return false;
        await LoadDatabaseChildrenAsync(db).ConfigureAwait(true);
        var folder = db.Children.FirstOrDefault(c => c.NodeKind == ExplorerNodeKind.Category && c.ObjectKind == kind);
        if (folder is null) return false;
        if (folder.HasDummyChild || folder.LoadTask is not null) await LoadCategoryChildrenAsync(folder).ConfigureAwait(true);
        var node = folder.Children.FirstOrDefault(c => c.NodeKind == ExplorerNodeKind.Object
                                                       && string.Equals(c.SchemaName, schema, StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(c.ObjectName, name, StringComparison.OrdinalIgnoreCase));
        if (node is null) return false;
        if (!node.FilterVisible && !string.IsNullOrWhiteSpace(FilterText)) FilterText = "";
        ShowExplorer = true;
        root.IsExpanded = true;
        db.IsExpanded = true;
        folder.IsExpanded = true;
        node.IsSelected = true;
        _selectedNode = node;
        return true;
    }
}
