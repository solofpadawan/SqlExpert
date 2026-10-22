using System.Linq;
using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SqlExpert.App.Commands;
using SqlExpert.App.Services;
using SqlExpert.App.Views;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Commands;
using SqlExpert.Core.Documents;
using SqlExpert.Core.Explorer;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.App.ViewModels;

/// <summary>
/// Shell VM: classic-style explorer + object tabs + status.
/// Wires Connection Manager + live SQL Server metadata.
/// </summary>
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    public const string NoActiveConnectionText = "(*** No active connection ***)";

    private readonly IConnectionProfileStore _profileStore;
    private readonly ICredentialStore _credentials;
    private readonly ISqlConnectionFactory _connections;
    private readonly IMetadataService _metadata;
    private readonly ConnectionSession _session;

    private string _statusText = "Pronto — registre e conecte um banco SQL Server.";
    private bool _isConnected;
    private bool _hasSelection;
    private bool _showExplorer = true;
    private bool _showStatusBar = true;
    private bool _showWindowsBar = true;
    private string _filterText = "";
    private readonly ITableDataService _tableDataService;
    private readonly ITableStructureService _tableStructure;
    private readonly DocumentManager<DocumentViewModel> _docs = new(d => d.Key);
    /// <summary>Profile currently connected (null when disconnected).</summary>
    private string? _activeProfileId;
    private string? _activeDatabase;
    /// <summary>Node currently selected in the Database Explorer (right-click also selects).</summary>
    private ExplorerNode? _selectedNode;
    private bool _busy;
    private readonly QueryHistoryStore _queryHistory = new();

    public MainViewModel(
        IConnectionProfileStore profileStore,
        ICredentialStore credentials,
        ISqlConnectionFactory connections,
        IMetadataService metadata,
        ConnectionSession session,
        ITableDataService tableData,
        ITableStructureService tableStructure)
    {
        _tableStructure = tableStructure;
        _profileStore = profileStore;
        _credentials = credentials;
        _connections = connections;
        _metadata = metadata;
        _session = session;
        _tableDataService = tableData;
        _docs.ActiveChanged += OnActiveDocumentChanged;
        _docs.Documents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDocuments));

        Registry = new AppCommandRegistry(
            () => IsConnected,
            () => HasSelection,
            OnCommandExecuted);

        // Main-menu items bound by caption: only captions with a real handler are enabled.
        // SQL Editor actions (Load/Save/Find/Replace) are enabled only while a SQL Editor is the active document.
        Stub = new RelayCommand(
            p => HandleMenuAction(p?.ToString()),
            p => CommandAvailability.IsMenuActionEnabled(p?.ToString(), ActiveDocument is SqlEditorDocumentViewModel));
        StubRequiresConnection = new RelayCommand(
            p => HandleMenuAction(p?.ToString()),
            p => IsConnected && CommandAvailability.IsMenuActionEnabled(p?.ToString(), ActiveDocument is SqlEditorDocumentViewModel));
        // Closing the main window asks about unsaved documents / open transactions first (MainWindow.Closing).
        ExitCommand = new RelayCommand(_ => { if (Application.Current?.MainWindow is { } w) w.Close(); else Application.Current?.Shutdown(); });
        ToggleExplorerCommand = new RelayCommand(_ => ShowExplorer = !ShowExplorer);
        CloseExplorerCommand = new RelayCommand(_ => ShowExplorer = false);
        ToggleStatusBarCommand = new RelayCommand(_ => ShowStatusBar = !ShowStatusBar);
        ToggleWindowsBarCommand = new RelayCommand(_ => ShowWindowsBar = !ShowWindowsBar);
        ActivateDocumentCommand = new RelayCommand(p => { if (p is DocumentViewModel d && _docs.Documents.Contains(d)) ActiveDocument = d; });
        CloseDocumentCommand = new RelayCommand(
            p => { var d = p as DocumentViewModel ?? ActiveDocument; if (d is not null) RunSafe(async () => await CloseDocumentAsync(d).ConfigureAwait(true)); },
            p => (p as DocumentViewModel ?? ActiveDocument) is not null);
        CloseAllDocumentsCommand = new RelayCommand(_ => RunSafe(async () => await CloseDocumentsAsync(_docs.All()).ConfigureAwait(true)), _ => HasDocuments);
        CloseOtherDocumentsCommand = new RelayCommand(
            p => { var d = p as DocumentViewModel ?? ActiveDocument; if (d is not null) RunSafe(async () => { await CloseDocumentsAsync(_docs.Others(d)).ConfigureAwait(true); if (_docs.Documents.Contains(d)) ActiveDocument = d; }); },
            _ => _docs.Count > 1);
        NextDocumentCommand = new RelayCommand(_ => _docs.Cycle(1), _ => _docs.Count > 1);
        PreviousDocumentCommand = new RelayCommand(_ => _docs.Cycle(-1), _ => _docs.Count > 1);
        ClearFilterCommand = new RelayCommand(_ => FilterText = "");

        OpenMenuItems = BuildMenu("OpenMenu");
        {
            var dbIds = new[] { "database.register_database", "database.unregister_database", "database.connect_to_database", "database.disconnect_from_database", "database.reconnect" };
            var defs = Registry.Category("Database").ToDictionary(d => d.Id);
            var pos = 0;
            foreach (var id in dbIds)
            {
                if (!defs.TryGetValue(id, out var def)) continue;
                OpenMenuItems.Insert(pos++, new CommandMenuItem { Id = def.Id + ".ctx", Caption = def.Caption, Shortcut = def.Shortcut, ToolTip = def.Caption, IsSeparator = false, Command = Registry[def.Id] });
            }
            OpenMenuItems.Insert(pos++, new CommandMenuItem { Id = "ctx.db.sep", Caption = "-", IsSeparator = true, Command = Registry[dbIds[0]] });
            OpenMenuItems.Insert(pos++, new CommandMenuItem { Id = "tools.sql_editor.ctx", Caption = "SQL Editor", Shortcut = "F12", ToolTip = "Abre o SQL Editor no database selecionado", Command = Registry["tools.sql_editor"] });
            OpenMenuItems.Insert(pos++, new CommandMenuItem { Id = "tools.new_sql_editor.ctx", Caption = "New SQL Editor", Shortcut = "Shift+F12", ToolTip = "Abre mais um SQL Editor no database selecionado", Command = Registry["tools.new_sql_editor"] });
            OpenMenuItems.Insert(pos, new CommandMenuItem { Id = "ctx.sql.sep", Caption = "-", IsSeparator = true, Command = Registry["tools.sql_editor"] });
        }
        ExplorerMenuItems = new ObservableCollection<CommandMenuItem>();
        FieldsMenuItems = BuildMenu("Fields");
        IndicesMenuItems = BuildMenu("Indices");
        ConstraintsMenuItems = BuildMenu("Constraints");
        DependenciesMenuItems = BuildMenu("Dependencies");
        GrantsMenuItems = BuildMenu("Grants");
        DataGridsMenuItems = BuildMenu("Data Grids");
        GridsMenuItems = BuildMenu("Grids");
        TodoMenuItems = BuildMenu("Todo");
        SchedulerMenuItems = BuildMenu("Sheduler");

        // Startup: empty explorer (registered databases are loaded from disk), no object editor open.
        ExplorerRoots = new ObservableCollection<ExplorerNode>();
        ProfileNames = new ObservableCollection<string>();

        var view = CollectionViewSource.GetDefaultView(ExplorerRoots);
        if (view is not null)
            view.Filter = FilterRoot;

        Initialization = InitializeAsync();
    }

    /// <summary>Parameterless ctor for design-time / XAML fallback.</summary>
    public MainViewModel()
        : this(CreateDesignServices())
    {
    }

    private MainViewModel(
        (IConnectionProfileStore store, ICredentialStore creds, ISqlConnectionFactory conn, IMetadataService meta, ConnectionSession session, ITableDataService data, ITableStructureService structure) s)
        : this(s.store, s.creds, s.conn, s.meta, s.session, s.data, s.structure)
    {
    }

    private static (IConnectionProfileStore, ICredentialStore, ISqlConnectionFactory, IMetadataService, ConnectionSession, ITableDataService, ITableStructureService) CreateDesignServices()
    {
        var session = new ConnectionSession();
        var creds = new InMemoryCredentialStore();
        var store = new ConnectionProfileStore(Path.Combine(Path.GetTempPath(), "SqlExpert-design", "connections.json"));
        var factory = new SqlConnectionFactory(id => session.Find(id), creds);
        var meta = new MetadataService(factory);
        return (store, creds, factory, meta, session, new TableDataService(factory), new TableStructureService(factory));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppCommandRegistry Registry { get; }

    /// <summary>Loading of the registered databases started by the constructor (awaited by the startup splash; never faults).</summary>
    public Task Initialization { get; }

    public ObservableCollection<CommandMenuItem> OpenMenuItems { get; }
    /// <summary>Items of the Database Explorer context menu (rebuilt each time it opens, see <see cref="PrepareExplorerContextMenu"/>).</summary>
    public ObservableCollection<CommandMenuItem> ExplorerMenuItems { get; }
    public ObservableCollection<CommandMenuItem> FieldsMenuItems { get; }
    public ObservableCollection<CommandMenuItem> IndicesMenuItems { get; }
    public ObservableCollection<CommandMenuItem> ConstraintsMenuItems { get; }
    public ObservableCollection<CommandMenuItem> DependenciesMenuItems { get; }
    public ObservableCollection<CommandMenuItem> GrantsMenuItems { get; }
    public ObservableCollection<CommandMenuItem> DataGridsMenuItems { get; }
    public ObservableCollection<CommandMenuItem> GridsMenuItems { get; }
    public ObservableCollection<CommandMenuItem> TodoMenuItems { get; }
    public ObservableCollection<CommandMenuItem> SchedulerMenuItems { get; }

    /// <summary>Root nodes of the Database Explorer: one per registered database.</summary>
    public ObservableCollection<ExplorerNode> ExplorerRoots { get; }
    /// <summary>Names of registered databases (drop-down of the filter box).</summary>
    public ObservableCollection<string> ProfileNames { get; }
    /// <summary>Open documents (one per opened table), shown in the central area and on the Windows bar.</summary>
    public ObservableCollection<DocumentViewModel> Documents => _docs.Documents;

    public DocumentViewModel? ActiveDocument
    {
        get => _docs.Active;
        set => _docs.Activate(value);
    }

    public bool HasDocuments => _docs.Count > 0;

    /// <summary>"SqlExpert - [Table : [dbo].[X]] : profile (server\instance:database)" or "SqlExpert".</summary>
    public string WindowTitle
    {
        get
        {
            var d = ActiveDocument;
            if (d is null) return WindowTitles.AppName;
            var p = FindProfile(d.ProfileId);
            return WindowTitles.Build(d.Caption, p is null ? null : ProfileLabel(p), p?.Server, p?.Instance, d.CurrentDatabase);
        }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionStatusText));
            Registry.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Status-bar panel: connected profile/database or the classic-style "no connection" text.</summary>
    public string ConnectionStatusText
    {
        get
        {
            if (!IsConnected || _activeProfileId is null)
                return NoActiveConnectionText;
            var p = FindProfile(_activeProfileId);
            if (p is null)
                return NoActiveConnectionText;
            var server = string.IsNullOrWhiteSpace(p.Instance) ? p.Server : $"{p.Server}\\{p.Instance}";
            var db = _activeDatabase ?? p.InitialCatalog;
            return $"{ProfileLabel(p)}  —  {server} : {db}";
        }
    }

    public bool HasSelection
    {
        get => _hasSelection;
        set { _hasSelection = value; OnPropertyChanged(); Registry.RaiseCanExecuteChanged(); }
    }

    public bool ShowExplorer
    {
        get => _showExplorer;
        set { _showExplorer = value; OnPropertyChanged(); }
    }

    public bool ShowStatusBar
    {
        get => _showStatusBar;
        set { _showStatusBar = value; OnPropertyChanged(); }
    }

    public bool ShowWindowsBar
    {
        get => _showWindowsBar;
        set { _showWindowsBar = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// "Enter filter string": filters every level of the Database Explorer (registrations, databases,
    /// folders and objects). Debounced; matching runs off the UI thread (see MainViewModel.Filter.cs).
    /// Clearing the text restores the tree immediately.
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            value ??= "";
            if (_filterText == value) return;
            _filterText = value;
            OnPropertyChanged();
            OnFilterTextChanged();
        }
    }

    /// <summary>DB Explorer "Hide Disconnected Databases" (persisted in settings.json).</summary>
    public bool HideDisconnectedDatabases
    {
        get => UiSettings.Instance.HideDisconnectedDatabases;
        set
        {
            if (value == UiSettings.Instance.HideDisconnectedDatabases) return;
            UiSettings.Instance.HideDisconnectedDatabases = value;
            OnPropertyChanged();
            CollectionViewSource.GetDefaultView(ExplorerRoots)?.Refresh();
            StatusText = value ? "Hide Disconnected Databases: só o database conectado aparece no Explorer." : "Todos os databases registrados aparecem no Explorer.";
        }
    }

    /// <summary>DB Explorer "Show objects description": MS_Description next to object names (persisted).</summary>
    public bool ShowObjectsDescription
    {
        get => UiSettings.Instance.ShowObjectsDescription;
        set
        {
            if (value == UiSettings.Instance.ShowObjectsDescription) return;
            UiSettings.Instance.ShowObjectsDescription = value;
            OnPropertyChanged();
            ApplyDescriptionMode(ExplorerRoots, value);
        }
    }

    /// <summary>DB Explorer "Sort nodes": true = By alias, false = As registered (persisted).</summary>
    public bool ExplorerSortByAlias
    {
        get => UiSettings.Instance.ExplorerSortByAlias;
        set
        {
            if (value == UiSettings.Instance.ExplorerSortByAlias) return;
            UiSettings.Instance.ExplorerSortByAlias = value;
            OnPropertyChanged();
            RefreshRegisteredRoots();
        }
    }

    /// <summary>Color Mark of the connected registration (status bar strip).</summary>
    public string? ActiveColorMark => GetActiveProfile()?.ColorMark;

    public ICommand Stub { get; }
    public ICommand StubRequiresConnection { get; }
    public ICommand ExitCommand { get; }
    public ICommand ToggleExplorerCommand { get; }
    public ICommand CloseExplorerCommand { get; }
    public ICommand ToggleStatusBarCommand { get; }
    public ICommand ToggleWindowsBarCommand { get; }
    public ICommand ActivateDocumentCommand { get; }
    public ICommand CloseDocumentCommand { get; }
    public ICommand CloseAllDocumentsCommand { get; }
    public ICommand CloseOtherDocumentsCommand { get; }
    public ICommand NextDocumentCommand { get; }
    public ICommand PreviousDocumentCommand { get; }
    public ICommand ClearFilterCommand { get; }

    public ICommand Cmd(string id) => Registry[id];

    public ConnectionProfile? FindProfile(string id) => _session.Find(id);

    private bool FilterRoot(object item)
    {
        if (UiSettings.Instance.HideDisconnectedDatabases && item is ExplorerNode { NodeKind: ExplorerNodeKind.Profile, IsActive: false })
            return false;
        return FilterChild(item);
    }

    private static string ProfileLabel(ConnectionProfile p) =>
        string.IsNullOrWhiteSpace(p.DisplayName)
            ? $"{p.Server} / {p.InitialCatalog}"
            : p.DisplayName;

    private async Task InitializeAsync()
    {
        try
        {
            var loaded = await _profileStore.LoadAsync().ConfigureAwait(true);
            _session.ReplaceAll(loaded);
            if (_session.Profiles.Count > 0)
                StatusText = $"Perfis carregados: {_session.Profiles.Count} ({_profileStore.StorePath}). Use Register/Connect.";
            RefreshRegisteredRoots();
        }
        catch (Exception ex)
        {
            StatusText = "Falha ao carregar conexões: " + ex.Message;
        }
    }

    private static async void RunSafe(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { App.ReportError(ex); }
    }

    private void HandleMenuAction(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (HandleProcedureMenuAction(name))
            return;

        switch (name)
        {
            case "Register Database":
            case "Register Database...":
                RunSafe(RegisterDatabaseAsync);
                break;
            case "Unregister Database":
                RunSafe(UnregisterDatabaseAsync);
                break;
            case "Connect to Database":
                RunSafe(ConnectAsync);
                break;
            case "Disconnect from Database":
                RunSafe(DisconnectInteractiveAsync);
                break;
            case "Reconnect":
                RunSafe(ReconnectAsync);
                break;
            case "Database Registration Info...":
                RunSafe(EditRegistrationAsync);
                break;
            case "Close All":
                RunSafe(async () => await CloseDocumentsAsync(_docs.All()).ConfigureAwait(true));
                break;
            case "Exit":
                ExitCommand.Execute(null);
                break;
            case "About":
                ShowAbout();
                break;
            case "Check for updates...":
                RunSafe(() => UpdateService.CheckManuallyAsync(Application.Current?.MainWindow, s => StatusText = s));
                break;
            case "SQL Editor":
                RunSafe(() => OpenSqlEditorAsync(forceNew: false));
                break;
            case "New SQL Editor":
                RunSafe(() => OpenSqlEditorAsync(forceNew: true));
                break;
            case "Load from File":
            case "Load from File: SQL Script":
            case "Load from File: Text":
                if (ActiveDocument is SqlEditorDocumentViewModel load && load.OpenCommand.CanExecute(null)) load.OpenCommand.Execute(null);
                break;
            case "Save to File":
            case "Save to File: SQL Script":
            case "Save to File: Text":
                (ActiveDocument as SqlEditorDocumentViewModel)?.Save(saveAs: false);
                break;
            case "Save as...":
                (ActiveDocument as SqlEditorDocumentViewModel)?.Save(saveAs: true);
                break;
            case "Find":
                (ActiveDocument as SqlEditorDocumentViewModel)?.Host?.ShowFind();
                break;
            case "Search Again":
                (ActiveDocument as SqlEditorDocumentViewModel)?.Host?.FindNext(false);
                break;
            case "Replace":
                (ActiveDocument as SqlEditorDocumentViewModel)?.Host?.ShowReplace();
                break;
            default:
                StatusText = $"Comando: {name}";
                break;
        }
    }

    private static void ShowAbout()
    {
        try { SplashWindow.ShowAbout(Application.Current?.MainWindow); }
        catch (Exception ex) { App.ReportError(ex); }
    }

    private void OnCommandExecuted(CommandDefinition def)
    {
        switch (def.Id)
        {
            case "help.about":
                ShowAbout();
                return;
            case "database.register_database":
                RunSafe(RegisterDatabaseAsync);
                return;
            case "database.unregister_database":
                RunSafe(UnregisterDatabaseAsync);
                return;
            case "database.connect_to_database":
                RunSafe(ConnectAsync);
                return;
            case "database.disconnect_from_database":
                RunSafe(DisconnectInteractiveAsync);
                return;
            case "database.reconnect":
                RunSafe(ReconnectAsync);
                return;
            case "database.database_registration_info":
            case "openmenu.database_registration_info":
                RunSafe(EditRegistrationAsync);
                return;
            case "windows.close_all":
            case "windowsmenu.close_all":
                RunSafe(async () => await CloseDocumentsAsync(_docs.All()).ConfigureAwait(true));
                return;
            case "windows.close_all_except_this":
                if (CloseOtherDocumentsCommand.CanExecute(null)) CloseOtherDocumentsCommand.Execute(null);
                return;
            case "fields.new_field":
                RunSafe(NewFieldAsync);
                return;
            case "fields.edit_field":
                RunSafe(() => EditFieldAsync(null));
                return;
            case "fields.drop_field":
                RunSafe(DropFieldAsync);
                return;
            case "constraints.new_constraint":
                RunSafe(NewConstraintAsync);
                return;
            case "constraints.drop_constraint":
                RunSafe(DropConstraintAsync);
                return;
            case "fields.reorder_fields":
                RunSafe(ReorderFieldsAsync);
                return;
            case "fields.copy_fields_list_to_clipboard":
                RunSafe(CopyFieldsListAsync);
                return;
            case "view.db_explorer":
            case "view.database_explorer":
                ShowExplorer = !ShowExplorer;
                break;
            case "view.status_bar":
                ShowStatusBar = !ShowStatusBar;
                break;
            case "view.windows_bar":
                ShowWindowsBar = !ShowWindowsBar;
                break;
            case "database.exit":
                ExitCommand.Execute(null);
                return;
            case "tools.sql_editor":
                RunSafe(() => OpenSqlEditorAsync(forceNew: false));
                return;
            case "tools.new_sql_editor":
                RunSafe(() => OpenSqlEditorAsync(forceNew: true));
                return;
        }

        if (def.Id.StartsWith("dbexplorer.", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteExplorerItem(def.Id, null);
            return;
        }

        var map = def.SqlServerMapping is null ? "" : $" | SQL Server: {def.SqlServerMapping}";
        StatusText = $"[{def.Category}] {def.Caption} ({def.Id}){map}";
    }

    private static void ShowInfo(string message, MessageBoxImage image = MessageBoxImage.Information)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is not null)
            MessageBox.Show(owner, message, "SqlExpert", MessageBoxButton.OK, image);
        else
            MessageBox.Show(message, "SqlExpert", MessageBoxButton.OK, image);
    }

    private ConnectionProfile? FindDuplicate(ConnectionProfile candidate) =>
        _session.Profiles.FirstOrDefault(p => p.Id != candidate.Id && p.HasSameTarget(candidate));

    private async Task RegisterDatabaseAsync()
    {
        if (_busy) return;
        var owner = Application.Current?.MainWindow;
        var dlg = new RegisterDatabaseWindow(_connections) { Owner = owner };
        if (dlg.ShowDialog() != true || dlg.ResultProfile is null)
            return;

        var profile = dlg.ResultProfile;
        var duplicate = FindDuplicate(profile);
        if (duplicate is not null)
        {
            ShowInfo($"Este banco já está registrado como '{ProfileLabel(duplicate)}'.\n\nO registro não foi duplicado.", MessageBoxImage.Warning);
            StatusText = $"Registro ignorado: '{ProfileLabel(duplicate)}' já existe.";
            SelectProfileNode(duplicate.Id);
            return;
        }

        if (profile.AuthMode == AuthMode.SqlLogin)
        {
            profile.PasswordSecretId ??= "pwd-" + profile.Id;
            await _credentials.SetSecretAsync(profile.PasswordSecretId, dlg.ResultPassword ?? "").ConfigureAwait(true);
        }
        else
        {
            profile.PasswordSecretId = null;
        }

        ConnectionProfileMigration.Normalize(profile);
        _session.Upsert(profile);
        await _profileStore.SaveAsync(_session.Profiles).ConfigureAwait(true);
        StatusText = $"Database registrado: {ProfileLabel(profile)}";
        RefreshRegisteredRoots();
        SelectProfileNode(profile.Id);
    }

    /// <summary>Database Registration Info...: the "Database Properties" dialog of the selected/active registration.</summary>
    private async Task EditRegistrationAsync()
    {
        var profile = GetSelectedProfile() ?? GetActiveProfile() ?? _session.Profiles.FirstOrDefault();
        if (profile is null)
        {
            StatusText = "Nenhum database registrado.";
            return;
        }
        await ShowRegistrationDialogAsync(profile, isNew: false, initialPassword: null).ConfigureAwait(true);
    }

    /// <summary>Clone Registration Info: copy of the selected registration as "&lt;alias&gt; (copy)", edited in the dialog.</summary>
    private async Task CloneRegistrationAsync()
    {
        var original = GetSelectedProfile();
        if (original is null)
        {
            ShowInfo("Selecione no Database Explorer o database que deseja clonar.");
            return;
        }

        var clone = original.Clone();
        clone.Id = Guid.NewGuid().ToString("N");
        clone.DisplayName = $"{ProfileLabel(original)} (copy)";
        clone.PasswordSecretId = null;
        string? password = null;
        if (original.AuthMode == AuthMode.SqlLogin && !string.IsNullOrEmpty(original.PasswordSecretId))
            password = await _credentials.GetSecretAsync(original.PasswordSecretId).ConfigureAwait(true) ?? "";
        await ShowRegistrationDialogAsync(clone, isNew: true, initialPassword: password).ConfigureAwait(true);
    }

    /// <summary>
    /// Shows "Database Properties" for <paramref name="profile"/> and saves the result: alias/color update the
    /// Explorer at once; when the connection settings of the connected registration change, offers to reconnect.
    /// <paramref name="isNew"/> = not registered yet (clone): saved only on OK.
    /// </summary>
    private async Task ShowRegistrationDialogAsync(ConnectionProfile profile, bool isNew, string? initialPassword)
    {
        if (_busy) return;
        var owner = Application.Current?.MainWindow;
        var others = _session.Profiles.Where(p => p.Id != profile.Id).ToList();
        var dlg = new DatabasePropertiesWindow(_connections, _credentials, profile, others, initialPassword) { Owner = owner };
        if (dlg.ShowDialog() != true || dlg.ResultProfile is null)
            return;

        var updated = dlg.ResultProfile;
        // Same target as another registration: refused only when this edit moved the target (clones may duplicate on purpose).
        if (!isNew && !profile.HasSameTarget(updated) && FindDuplicate(updated) is { } duplicate)
        {
            ShowInfo($"Já existe outro registro igual: '{ProfileLabel(duplicate)}'.\n\nA alteração não foi salva.", MessageBoxImage.Warning);
            return;
        }

        if (updated.AuthMode == AuthMode.SqlLogin)
        {
            updated.PasswordSecretId ??= "pwd-" + updated.Id;
            if (dlg.ResultPassword is not null)
                await _credentials.SetSecretAsync(updated.PasswordSecretId, dlg.ResultPassword).ConfigureAwait(true);
        }
        else if (!string.IsNullOrEmpty(updated.PasswordSecretId))
        {
            // Windows / Entra: the stored password is no longer used.
            await _credentials.DeleteSecretAsync(updated.PasswordSecretId).ConfigureAwait(true);
            updated.PasswordSecretId = null;
        }

        var connectionChanged = !profile.HasSameConnectionSettings(updated) || dlg.ResultPassword is not null;
        ConnectionProfileMigration.Normalize(updated);
        _session.Upsert(updated);
        await _profileStore.SaveAsync(_session.Profiles).ConfigureAwait(true);
        RefreshRegisteredRoots();
        SelectProfileNode(updated.Id);
        foreach (var d in _docs.Documents) d.RaiseColorMarkChanged();
        OnPropertyChanged(nameof(ConnectionStatusText));
        OnPropertyChanged(nameof(WindowTitle));
        StatusText = isNew
            ? $"Registro clonado: {ProfileLabel(updated)}"
            : $"Registration info atualizado: {ProfileLabel(updated)}";

        if (!isNew && connectionChanged && IsConnected && _activeProfileId == updated.Id)
        {
            var answer = owner is not null
                ? MessageBox.Show(owner, $"Os dados de conexão de '{ProfileLabel(updated)}' mudaram.\n\nReconectar agora para usar os novos dados?\n(As janelas deste database serão fechadas.)",
                    "SqlExpert", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes)
                : MessageBoxResult.No;
            if (answer == MessageBoxResult.Yes)
                await ReconnectAsync().ConfigureAwait(true);
            else
                StatusText += " — os novos dados de conexão valem na próxima conexão.";
        }
    }

    private async Task UnregisterDatabaseAsync()
    {
        var profile = GetSelectedProfile();
        if (profile is null)
        {
            ShowInfo(_session.Profiles.Count == 0
                ? "Nenhum database registrado."
                : "Selecione no Database Explorer o database que deseja remover (Unregister).");
            return;
        }

        if (MessageBox.Show(
                $"Unregister '{ProfileLabel(profile)}'?\n\n{profile.Server}{(string.IsNullOrWhiteSpace(profile.Instance) ? "" : "\\" + profile.Instance)} : {profile.InitialCatalog}\n\n" +
                "Só o registro sai do SqlExpert: o database NÃO é apagado do servidor.",
                "SqlExpert",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (IsConnected && _activeProfileId == profile.Id)
        {
            if (!await CloseDocumentsAsync(DocumentsOf(profile.Id)).ConfigureAwait(true)) return;
            await DisconnectAsync().ConfigureAwait(true);
        }
        DeleteSavedTabs(profile.Id);

        if (!string.IsNullOrEmpty(profile.PasswordSecretId))
            await _credentials.DeleteSecretAsync(profile.PasswordSecretId).ConfigureAwait(true);

        _session.Remove(profile.Id);
        await _profileStore.SaveAsync(_session.Profiles).ConfigureAwait(true);
        _selectedNode = null;
        RefreshRegisteredRoots();
        StatusText = $"Unregister: {ProfileLabel(profile)}";
    }

    private async Task ConnectAsync()
    {
        if (_busy) return;
        var profile = GetSelectedProfile() ?? GetActiveProfile();
        if (profile is null)
        {
            if (_session.Profiles.Count == 0)
            {
                StatusText = "Registre um database primeiro (Database → Register Database).";
                await RegisterDatabaseAsync().ConfigureAwait(true);
                profile = GetSelectedProfile();
                if (profile is null) return;
            }
            else if (_session.Profiles.Count == 1)
            {
                profile = _session.Profiles[0];
            }
            else
            {
                ShowInfo("Selecione no Database Explorer o database que deseja conectar.");
                return;
            }
        }

        if (IsConnected && _activeProfileId == profile.Id)
        {
            StatusText = $"Já conectado: {ProfileLabel(profile)}";
            return;
        }

        if (IsConnected)
        {
            if (!await CloseProfileDocumentsAsync(_activeProfileId).ConfigureAwait(true)) return;
            await DisconnectAsync().ConfigureAwait(true);
        }

        var connected = false;
        _busy = true;
        StatusText = $"Conectando a {ProfileLabel(profile)}...";
        try
        {
            string? version = null;
            await using (var conn = await _connections.OpenAsync(profile).ConfigureAwait(true))
            {
                try { version = await RegistrationScripts.GetServerVersionAsync(conn).ConfigureAwait(true); }
                catch (Exception) { /* version is informative only */ }
            }
            _activeProfileId = profile.Id;
            _activeDatabase = profile.InitialCatalog;
            IsConnected = true;
            if (version is not null && version != profile.ServerVersion)
            {
                profile.ServerVersion = version;
                try { await _profileStore.SaveAsync(_session.Profiles).ConfigureAwait(true); } catch (Exception) { /* not critical */ }
            }
            await RefreshExplorerFromServerAsync().ConfigureAwait(true);
            StatusText = $"Conectado: {ProfileLabel(profile)} ({profile.Server})";
            if (RegistrationScripts.HasScript(profile.AfterConnectScript))
                await RunRegistrationScriptAsync(profile, profile.AfterConnectScript, "After Connect").ConfigureAwait(true);
            connected = true;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _activeProfileId = null;
            _activeDatabase = null;
            StatusText = "Falha na conexão: " + ex.Message;
            RefreshRegisteredRoots();
        }
        finally
        {
            _busy = false;
            OnPropertyChanged(nameof(ConnectionStatusText));
            Registry.RaiseCanExecuteChanged();
        }

        // Open windows of the last session of this registration (tables, procedures, SQL Editors; nothing is executed).
        if (connected && IsProfileConnected(profile.Id))
            await RestoreOpenTabsAsync(profile).ConfigureAwait(true);
    }

    private async Task ReconnectAsync()
    {
        var profile = GetActiveProfile();
        if (profile is null)
        {
            await ConnectAsync().ConfigureAwait(true);
            return;
        }

        if (!await CloseProfileDocumentsAsync(profile.Id).ConfigureAwait(true)) return;
        await DisconnectAsync().ConfigureAwait(true);
        SelectProfileNode(profile.Id);
        await ConnectAsync().ConfigureAwait(true);
    }

    private void Disconnect()
    {
        // Documents of the disconnected profile are closed (callers already confirmed unsaved edits).
        foreach (var d in DocumentsOf(_activeProfileId))
            RemoveDocument(d);
        IsConnected = false;
        HasSelection = false;
        _activeProfileId = null;
        _activeDatabase = null;
        RefreshRegisteredRoots();
        StatusText = "Desconectado.";
        OnPropertyChanged(nameof(ConnectionStatusText));
        OnPropertyChanged(nameof(WindowTitle));
        Registry.RaiseCanExecuteChanged();
    }

    private async Task DisconnectInteractiveAsync()
    {
        if (!IsConnected) return;
        if (!await CloseProfileDocumentsAsync(_activeProfileId).ConfigureAwait(true)) return;
        await DisconnectAsync().ConfigureAwait(true);
    }

    /// <summary>Runs the Before Disconnect script of the connected registration (errors are shown, never thrown), then disconnects.</summary>
    private async Task DisconnectAsync()
    {
        var profile = GetActiveProfile();
        if (profile is not null && RegistrationScripts.HasScript(profile.BeforeDisconnectScript))
            await RunRegistrationScriptAsync(profile, profile.BeforeDisconnectScript, "Before Disconnect").ConfigureAwait(true);
        Disconnect();
    }

    /// <summary>
    /// Runs a registration script (After Connect / Before Disconnect) on its own connection.
    /// Errors (connection or SQL) are shown in a message; nothing is thrown.
    /// </summary>
    private async Task RunRegistrationScriptAsync(ConnectionProfile profile, string? script, string label)
    {
        try
        {
            StatusText = $"Executando o script {label} de {ProfileLabel(profile)}...";
            await using var conn = await _connections.OpenAsync(profile).ConfigureAwait(true);
            var errors = await RegistrationScripts.RunAsync(conn, script).ConfigureAwait(true);
            if (errors.Count == 0)
            {
                StatusText = $"Script {label} de {ProfileLabel(profile)} executado.";
                return;
            }
            StatusText = $"Script {label} de {ProfileLabel(profile)} com erro: {errors[0]}";
            ShowInfo($"O script {label} de '{ProfileLabel(profile)}' terminou com erro:\n\n{string.Join("\n", errors)}\n\n(Database Registration Info > Scripts)", MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusText = $"Script {label} não executado: {ex.Message}";
            ShowInfo($"O script {label} de '{ProfileLabel(profile)}' não pôde ser executado:\n\n{ex.Message}", MessageBoxImage.Warning);
        }
    }

    // ===== Fields tab (New / Edit / Drop / Reorder / Copy) =====

    private TableDocumentViewModel? ActiveTableDocument()
    {
        if (ActiveDocument is TableDocumentViewModel t) return t;
        ShowInfo("Abra uma tabela primeiro (duplo clique na tabela no Database Explorer).");
        return null;
    }

    private bool EnsureConnectedFor(TableDocumentViewModel doc)
    {
        if (IsConnected && _activeProfileId == doc.ProfileId) return true;
        ShowInfo("Conecte ao database desta tabela primeiro.");
        return false;
    }

    private FieldRow? RequireSelectedField(TableDocumentViewModel doc, string action)
    {
        if (doc.SelectedField is { } f) return f;
        ShowInfo($"Selecione um campo na aba Fields para usar \"{action}\".");
        return null;
    }

    private async Task<TableStructure> LoadStructureAsync(TableDocumentViewModel doc)
    {
        var t = await _tableStructure.LoadAsync(doc.ProfileId, doc.Database, doc.Schema, doc.Table).ConfigureAwait(true);
        return t ?? throw new InvalidOperationException($"Tabela [{doc.Schema}].[{doc.Table}] não encontrada.");
    }

    private async Task<IReadOnlyList<string>> LoadCollationsAsync(TableDocumentViewModel doc)
    {
        try { return await _tableStructure.GetCollationsAsync(doc.ProfileId, doc.Database).ConfigureAwait(true); }
        catch (Exception ex)
        {
            StatusText = "Não foi possível listar as collations: " + ex.Message;
            return Array.Empty<string>();
        }
    }

    /// <summary>Runs DDL in one transaction, then reloads the Fields grid, DDL and Data tab.</summary>
    private async Task ApplyDdlAsync(TableDocumentViewModel doc, IReadOnlyList<string> statements, string done, string? selectField)
    {
        StatusText = $"Executando alteração em [{doc.Schema}].[{doc.Table}]...";
        await _tableStructure.ExecuteInTransactionAsync(doc.ProfileId, doc.Database, statements).ConfigureAwait(true);
        await doc.LoadAsync(_metadata, s => StatusText = s, selectField).ConfigureAwait(true);
        StatusText = done;
    }

    private async Task NewFieldAsync()
    {
        var doc = ActiveTableDocument();
        if (doc is null || !EnsureConnectedFor(doc)) return;
        if (!await doc.TableData.ConfirmLeaveAsync().ConfigureAwait(true)) return;
        var structure = await LoadStructureAsync(doc).ConfigureAwait(true);
        var collations = await LoadCollationsAsync(doc).ConfigureAwait(true);

        FieldEditorWindow? dlg = null;
        dlg = new FieldEditorWindow(structure, null, collations,
            stmts => ApplyDdlAsync(doc, stmts, $"Campo [{dlg!.Result?.Name}] adicionado em [{doc.Schema}].[{doc.Table}].", dlg.Result?.Name))
        { Owner = Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true) StatusText = "New field cancelado.";
    }

    /// <summary>Edit field (toolbar or double-click on a row of the Fields grid).</summary>
    public async Task EditFieldAsync(FieldRow? row)
    {
        var doc = ActiveTableDocument();
        if (doc is null || !EnsureConnectedFor(doc)) return;
        row ??= RequireSelectedField(doc, "Edit field");
        if (row is null) return;
        if (!await doc.TableData.ConfirmLeaveAsync().ConfigureAwait(true)) return;

        var structure = await LoadStructureAsync(doc).ConfigureAwait(true);
        var column = structure.FindColumn(row.FieldName)
            ?? throw new InvalidOperationException($"O campo [{row.FieldName}] não existe mais. Atualize a tabela.");
        if (column.IsComputed)
        {
            ShowInfo($"[{column.Name}] é uma coluna computada (AS {column.ComputedDefinition}). A edição de colunas computadas ainda não é suportada.");
            return;
        }
        var collations = await LoadCollationsAsync(doc).ConfigureAwait(true);

        FieldEditorWindow? dlg = null;
        dlg = new FieldEditorWindow(structure, column, collations,
            stmts => ApplyDdlAsync(doc, stmts, $"Campo [{dlg!.Result?.Name}] alterado em [{doc.Schema}].[{doc.Table}].", dlg.Result?.Name))
        { Owner = Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true) StatusText = "Edit field cancelado.";
    }

    public void EditFieldFromGrid(FieldRow row) => RunSafe(() => EditFieldAsync(row));

    private async Task DropFieldAsync()
    {
        var doc = ActiveTableDocument();
        if (doc is null || !EnsureConnectedFor(doc)) return;
        var row = RequireSelectedField(doc, "Drop field");
        if (row is null) return;
        if (!await doc.TableData.ConfirmLeaveAsync().ConfigureAwait(true)) return;

        var structure = await LoadStructureAsync(doc).ConfigureAwait(true);
        if (structure.FindColumn(row.FieldName) is null)
            throw new InvalidOperationException($"O campo [{row.FieldName}] não existe mais. Atualize a tabela.");

        var blockers = FieldDdlBuilder.GetDropBlockers(structure, row.FieldName);
        if (blockers.Count > 0)
        {
            ShowInfo($"O campo [{row.FieldName}] não pode ser excluído porque:\n\n• " + string.Join("\n• ", blockers) +
                     "\n\nRemova essas constraints/índices antes.", MessageBoxImage.Warning);
            StatusText = $"Drop field recusado: [{row.FieldName}].";
            return;
        }

        var stmts = FieldDdlBuilder.BuildDropColumn(structure, row.FieldName);
        var owner = Application.Current?.MainWindow;
        var msg = $"Excluir o campo [{row.FieldName}] de [{doc.Schema}].[{doc.Table}]?\n\nOs dados deste campo serão perdidos.\n\nSQL:\n{string.Join("\n", stmts)}";
        var answer = owner is not null
            ? MessageBox.Show(owner, msg, "SqlExpert - Drop field", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(msg, "SqlExpert - Drop field", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            StatusText = "Drop field cancelado.";
            return;
        }

        await ApplyDdlAsync(doc, stmts, $"Campo [{row.FieldName}] excluído de [{doc.Schema}].[{doc.Table}].", null).ConfigureAwait(true);
    }

    /// <summary>Constraints tab "New constraint": primary key (other kinds are greyed in the dialog).</summary>
    private async Task NewConstraintAsync()
    {
        var doc = ActiveTableDocument();
        if (doc is null || !EnsureConnectedFor(doc)) return;
        if (!await doc.TableData.ConfirmLeaveAsync().ConfigureAwait(true)) return;
        var structure = await LoadStructureAsync(doc).ConfigureAwait(true);
        if (PrimaryKeyDdlBuilder.PrimaryKey(structure) is { } pk)
        {
            ShowInfo($"A tabela [{doc.Schema}].[{doc.Table}] já tem a chave primária [{pk.Name}] ({PrimaryKeyDdlBuilder.ColumnList(pk.Columns)}).\n\n" +
                     "Para incluir ou tirar campos dela, use Edit field (opção Primary key) na aba Fields.\n" +
                     "UNIQUE, FOREIGN KEY e CHECK ainda não podem ser criados por aqui.");
            return;
        }
        PrimaryKeyWindow? dlg = null;
        dlg = new PrimaryKeyWindow(structure,
            stmts => ApplyDdlAsync(doc, stmts, $"Chave primária [{dlg!.ConstraintName}] criada em [{doc.Schema}].[{doc.Table}].", null))
        { Owner = Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true) StatusText = "New constraint cancelado.";
    }

    /// <summary>Constraints tab "Drop constraint": refuses keys that foreign keys depend on.</summary>
    private async Task DropConstraintAsync()
    {
        var doc = ActiveTableDocument();
        if (doc is null || !EnsureConnectedFor(doc)) return;
        if (doc.SelectedConstraint is not { } row)
        {
            ShowInfo("Selecione uma constraint na aba Constraints para usar \"Drop constraint\".");
            return;
        }
        if (!await doc.TableData.ConfirmLeaveAsync().ConfigureAwait(true)) return;
        var structure = await LoadStructureAsync(doc).ConfigureAwait(true);
        var kind = PrimaryKeyDdlBuilder.ConstraintKind(structure, row.Name);
        if (kind is null)
            throw new InvalidOperationException($"A constraint [{row.Name}] não existe mais. Atualize a tabela.");
        var blockers = PrimaryKeyDdlBuilder.GetDropConstraintBlockers(structure, row.Name);
        if (blockers.Count > 0)
        {
            ShowInfo($"A constraint [{row.Name}] não pode ser excluída porque:\n\n• " + string.Join("\n• ", blockers) +
                     "\n\nExclua essas foreign keys antes.", MessageBoxImage.Warning);
            StatusText = $"Drop constraint recusado: [{row.Name}].";
            return;
        }
        var sql = PrimaryKeyDdlBuilder.DropConstraint(doc.Schema, doc.Table, row.Name);
        var owner = Application.Current?.MainWindow;
        var msg = $"Excluir a constraint {kind} [{row.Name}] de [{doc.Schema}].[{doc.Table}]?\n\nSQL:\n{sql}";
        var answer = owner is not null
            ? MessageBox.Show(owner, msg, "SqlExpert - Drop constraint", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(msg, "SqlExpert - Drop constraint", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            StatusText = "Drop constraint cancelado.";
            return;
        }
        await ApplyDdlAsync(doc, new[] { sql }, $"Constraint [{row.Name}] excluída de [{doc.Schema}].[{doc.Table}].", null).ConfigureAwait(true);
    }

    private async Task ReorderFieldsAsync()
    {
        var doc = ActiveTableDocument();
        if (doc is null || !EnsureConnectedFor(doc)) return;
        var structure = await LoadStructureAsync(doc).ConfigureAwait(true);
        var dlg = new ReorderFieldsWindow(structure, doc.SelectedField?.FieldName) { Owner = Application.Current?.MainWindow };
        dlg.ShowDialog();
        StatusText = $"Reorder fields: script de [{doc.Schema}].[{doc.Table}] gerado (não executado).";
    }

    private Task CopyFieldsListAsync()
    {
        var doc = ActiveTableDocument();
        if (doc is null) return Task.CompletedTask;
        var text = FieldListFormatter.ToTabSeparated(doc.SampleColumns.Select(r => r.Column));
        Clipboard.SetText(text);
        StatusText = $"Lista de {doc.SampleColumns.Count} campo(s) de [{doc.Schema}].[{doc.Table}] copiada para a área de transferência.";
        return Task.CompletedTask;
    }

    // ===== SQL Editor (F12 / Shift+F12) =====

    /// <summary>Re-reads the main window title (a SQL Editor changes it after USE / load / save).</summary>
    public void RefreshWindowTitle() => OnPropertyChanged(nameof(WindowTitle));

    /// <summary>
    /// F12: activates the SQL Editor of the selected/active database (opens one when there is none).
    /// Shift+F12 (<paramref name="forceNew"/>): always opens another editor. Connects first when needed.
    /// </summary>
    public async Task OpenSqlEditorAsync(bool forceNew)
    {
        var node = _selectedNode;
        if (!IsConnected || _activeProfileId is null)
        {
            await ConnectAsync().ConfigureAwait(true);
            if (!IsConnected || _activeProfileId is null)
            {
                StatusText = "SQL Editor: conecte a um database primeiro.";
                return;
            }
        }

        var profileId = _activeProfileId;
        var profile = FindProfile(profileId);
        if (profile is null) return;
        var database = (node is not null && node.ProfileId == profileId ? node.DatabaseName : null)
                       ?? (ActiveDocument is { } ad && ad.ProfileId == profileId ? ad.CurrentDatabase : null)
                       ?? _activeDatabase
                       ?? (string.IsNullOrWhiteSpace(profile.InitialCatalog) ? "master" : profile.InitialCatalog);

        var editors = _docs.Documents.OfType<SqlEditorDocumentViewModel>().ToList();
        if (!forceNew)
        {
            var existing = ActiveDocument is SqlEditorDocumentViewModel act && act.ProfileId == profileId
                                                                         && string.Equals(act.CurrentDatabase, database, StringComparison.OrdinalIgnoreCase)
                ? act
                : editors.LastOrDefault(e => e.ProfileId == profileId && string.Equals(e.CurrentDatabase, database, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                ActiveDocument = existing;
                existing.Host?.FocusEditor();
                StatusText = $"SQL Editor ativado: {existing.ShortName.TrimEnd(' ', '*')} ({database}).";
                return;
            }
        }

        var used = editors.Select(e => e.Number).ToHashSet();
        var number = 1;
        while (used.Contains(number)) number++;

        var doc = CreateSqlEditorDocument(profile, database, number);
        _docs.OpenOrActivate(doc.Key, () => doc);
        doc.Host?.FocusEditor();
        StatusText = $"{doc.ShortName} aberto em {database} — F9 / Ctrl+Enter executa.";
    }

    private SqlEditorDocumentViewModel CreateSqlEditorDocument(ConnectionProfile profile, string database, int number)
    {
        var profileId = profile.Id;
        var server = string.IsNullOrWhiteSpace(profile.Instance) ? profile.Server : $"{profile.Server}\\{profile.Instance}";
        var label = $"{ProfileLabel(profile)} ({server})";
        var session = new SqlEditorSession(ct => _connections.CreateOpenConnectionAsync(profileId, ct), database)
        {
            // Registration defaults (Database Properties > Additional > SQL Editor).
            Autocommit = profile.SqlEditorAutocommit
        };
        return new SqlEditorDocumentViewModel(this, profileId, database, number, label, session, _queryHistory, profile.SqlEditorFetchRows, _tableDataService);
    }

    /// <summary>
    /// Closes every document, asking about unsaved edits / open transactions, and saves the open tabs of each
    /// registration for the next connect. False = the user cancelled (nothing is saved then).
    /// </summary>
    public async Task<bool> CloseAllForExitAsync()
    {
        foreach (var profileId in ProfilesWithTabs())
            if (!await CloseProfileDocumentsAsync(profileId).ConfigureAwait(true))
                return false;
        _tabsSavedForExit = true;
        return true;
    }

    public bool AnyDocumentNeedsConfirmation => _docs.Documents.Any(d => d.HasUnsavedChanges);

    // ===== Documents (MDI-like) =====

    private IReadOnlyList<DocumentViewModel> DocumentsOf(string? profileId) =>
        profileId is null ? Array.Empty<DocumentViewModel>() : _docs.Documents.Where(d => d.ProfileId == profileId).ToList();

    private void OnActiveDocumentChanged(DocumentViewModel? previous, DocumentViewModel? current)
    {
        if (previous is not null)
        {
            previous.OnDeactivated();
            previous.IsActive = false;
        }
        if (current is not null)
            current.IsActive = true;
        OnPropertyChanged(nameof(ActiveDocument));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HasDocuments));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Asks about unsaved edits (activating the document first so the user sees it), then closes it.</summary>
    public async Task<bool> CloseDocumentAsync(DocumentViewModel doc)
    {
        if (!_docs.Documents.Contains(doc)) return true;
        if (doc.HasUnsavedChanges)
            ActiveDocument = doc;
        if (!await doc.ConfirmCloseAsync().ConfigureAwait(true)) return false;
        RemoveDocument(doc);
        return true;
    }

    /// <summary>Closes each document in turn; stops (returns false) if the user cancels one.</summary>
    public async Task<bool> CloseDocumentsAsync(IReadOnlyList<DocumentViewModel> docs)
    {
        foreach (var d in docs.ToList())
            if (!await CloseDocumentAsync(d).ConfigureAwait(true))
                return false;
        return true;
    }

    private void RemoveDocument(DocumentViewModel doc)
    {
        doc.OnClosed();
        _docs.Remove(doc);
        OnPropertyChanged(nameof(WindowTitle));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Opens the table editor document, or activates it when that table is already open.</summary>
    private async Task<TableDocumentViewModel?> OpenTableDocumentAsync(string database, string schema, string table)
    {
        if (!IsConnected || _activeProfileId is null) return null;
        var profileId = _activeProfileId;
        var key = DocumentKeys.Table(profileId, database, schema, table);
        var (doc, created) = _docs.OpenOrActivate(key, () => new TableDocumentViewModel(
            this, profileId, database, schema, table, new TableDataViewModel(_tableDataService, s => StatusText = s)
            {
                // FK lookup popup: TOP n = registration's SQL Editor fetch count (Database Properties), else the global one.
                LookupFetchRows = FindProfile(profileId)?.SqlEditorFetchRows ?? UiSettings.Instance.SqlFetchRows
            }));
        if (!created)
        {
            StatusText = $"Ativado: [{schema}].[{table}]";
            return doc as TableDocumentViewModel;
        }

        if (doc is TableDocumentViewModel tableDoc)
        {
            await tableDoc.LoadAsync(_metadata, s => StatusText = s).ConfigureAwait(true);
            if (FindProfile(profileId) is { } p && p.DefaultTableTab != DefaultObjectTab.Fields)
                tableDoc.SelectedTabIndex = TableDocumentViewModel.TabIndexFor(p.DefaultTableTab);
        }
        return doc as TableDocumentViewModel;
    }

    private ConnectionProfile? GetActiveProfile() =>
        IsConnected && _activeProfileId is not null ? FindProfile(_activeProfileId) : null;

    /// <summary>Profile of the selected root node in the explorer (null if none / not a profile node).</summary>
    private ConnectionProfile? GetSelectedProfile()
    {
        var node = _selectedNode;
        if (node is null || node.NodeKind != ExplorerNodeKind.Profile || node.ProfileId is null)
            return null;
        if (!ExplorerRoots.Contains(node))
            return null;
        return FindProfile(node.ProfileId);
    }

    private void SelectProfileNode(string profileId)
    {
        var node = ExplorerRoots.FirstOrDefault(n => n.ProfileId == profileId);
        if (node is null) return;
        node.IsSelected = true;
        _selectedNode = node;
    }

    private void RefreshRegisteredRoots()
    {
        var selectedId = _selectedNode?.NodeKind == ExplorerNodeKind.Profile ? _selectedNode.ProfileId : null;
        var existing = ExplorerRoots.ToList();
        ExplorerRoots.Clear();
        foreach (var p in _session.Ordered(UiSettings.Instance.ExplorerSortByAlias))
        {
            var isActive = IsConnected && _activeProfileId == p.Id;
            ExplorerNode node;
            var keep = isActive ? existing.FirstOrDefault(n => n.ProfileId == p.Id) : null;
            if (keep is not null)
            {
                node = keep;
                node.Name = ProfileLabel(p);
            }
            else
            {
                node = new ExplorerNode(ProfileLabel(p))
                {
                    NodeKind = ExplorerNodeKind.Profile,
                    ProfileId = p.Id
                };
            }
            node.IsActive = isActive;
            node.ColorMark = p.ColorMark;
            node.Description = p.ServerVersion is null
                ? $"{ServerNameText.Format(p)} : {p.InitialCatalog}"
                : $"{ServerNameText.Format(p)} : {p.InitialCatalog}\n{p.ServerVersion}";
            ExplorerRoots.Add(node);
        }

        ProfileNames.Clear();
        foreach (var name in _session.Profiles.Select(ProfileLabel).Distinct(StringComparer.OrdinalIgnoreCase))
            ProfileNames.Add(name);

        if (selectedId is not null)
            SelectProfileNode(selectedId);
        else
            _selectedNode = null;
        OnExplorerTreeChanged();
    }

    private async Task RefreshExplorerFromServerAsync()
    {
        if (_activeProfileId is null) return;
        // Code completion / object links read the catalog again after a refresh (or a new connection).
        InvalidateCompletionCatalogs(_activeProfileId);

        var profile = FindProfile(_activeProfileId);
        if (profile is null) return;

        RefreshRegisteredRoots();
        var profileNode = ExplorerRoots.FirstOrDefault(n => n.ProfileId == _activeProfileId);
        if (profileNode is null) return;
        profileNode.Children.Clear();

        IReadOnlyList<string> databases;
        try
        {
            databases = await _metadata.ListDatabasesAsync(_activeProfileId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = "Falha ao listar databases: " + ex.Message;
            // Fall back to InitialCatalog only
            databases = new[] { profile.InitialCatalog };
        }

        if (!profile.ShowSystemDatabases)
            databases = databases.Where(d => !MetadataSql.IsSystemDatabase(d) || d.Equals(profile.InitialCatalog, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var db in databases)
        {
            var dbNode = new ExplorerNode(db)
            {
                NodeKind = ExplorerNodeKind.Database,
                ProfileId = _activeProfileId,
                DatabaseName = db,
                HasDummyChild = true
            };
            dbNode.Children.Add(ExplorerNode.CreatePlaceholder());
            profileNode.Children.Add(dbNode);
        }

        profileNode.IsExpanded = true;

        // Auto-expand preferred database
        var preferred = databases.FirstOrDefault(d =>
            d.Equals(profile.InitialCatalog, StringComparison.OrdinalIgnoreCase)) ?? databases.FirstOrDefault();
        if (preferred is not null)
        {
            var node = profileNode.Children.FirstOrDefault(c => c.DatabaseName == preferred);
            if (node is not null)
            {
                node.IsExpanded = true;
                await LoadDatabaseChildrenAsync(node).ConfigureAwait(true);
            }
        }
        OnExplorerTreeChanged();
    }

    public async Task OnExplorerExpandedAsync(ExplorerNode node)
    {
        if (!IsConnected || _activeProfileId is null)
            return;
        // Filter auto-expansion / expansion restore: everything shown is already loaded.
        if ((_filterActive || _restoringFilterExpansion) && node.LoadTask is { IsCompletedSuccessfully: true })
            return;

        try
        {
            // Idempotent: auto-expand on connect and the Expanded event may both ask for the same node.
            if (node.NodeKind == ExplorerNodeKind.Database)
                await LoadDatabaseChildrenAsync(node).ConfigureAwait(true);
            else if (node.NodeKind == ExplorerNodeKind.Category && (node.HasDummyChild || node.LoadTask is not null))
                await LoadCategoryChildrenAsync(node).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = "Erro ao expandir: " + ex.Message;
        }
    }

    /// <summary>
    /// Loads a node's children once. Concurrent callers share the same task; on failure the
    /// placeholder stays and a later expand retries. Children are cleared right before filling.
    /// </summary>
    private async Task EnsureChildrenLoadedAsync(ExplorerNode node, Func<Task<List<ExplorerNode>>> fetch)
    {
        if (node.LoadTask is null)
        {
            node.LoadTask = FillAsync();
        }

        try
        {
            await node.LoadTask.ConfigureAwait(true);
        }
        catch
        {
            node.LoadTask = null;
            throw;
        }

        async Task FillAsync()
        {
            var children = await fetch().ConfigureAwait(true);
            node.Children.Clear();
            foreach (var c in children)
                node.Children.Add(c);
            node.HasDummyChild = false;
            // New nodes arrive unfiltered: re-apply an active filter right away.
            OnExplorerTreeChanged();
        }
    }

    private async Task LoadDatabaseChildrenAsync(ExplorerNode dbNode)
    {
        if (_activeProfileId is null || dbNode.DatabaseName is null) return;
        var profileId = _activeProfileId;
        var database = dbNode.DatabaseName;
        // Only the first load makes it the current database: with a virtualized (recycling) tree the
        // Expanded event also fires when an already expanded node scrolls back into view.
        if (dbNode.LoadTask is not { IsCompletedSuccessfully: true })
        {
            _activeDatabase = database;
            OnPropertyChanged(nameof(ConnectionStatusText));
        }

        await EnsureChildrenLoadedAsync(dbNode, async () =>
        {
            var categories = await _metadata.GetObjectTreeAsync(profileId, database).ConfigureAwait(true);
            var list = new List<ExplorerNode>();
            foreach (var cat in categories)
            {
                var catNode = new ExplorerNode(cat.Name)
                {
                    NodeKind = ExplorerNodeKind.Category,
                    ProfileId = profileId,
                    DatabaseName = database,
                    ObjectKind = cat.Kind,
                    Count = cat.Count,
                    HasDummyChild = (cat.Count ?? 0) > 0
                };
                if (catNode.HasDummyChild)
                    catNode.Children.Add(ExplorerNode.CreatePlaceholder());
                list.Add(catNode);
            }
            return list;
        }).ConfigureAwait(true);
    }

    private async Task LoadCategoryChildrenAsync(ExplorerNode catNode)
    {
        if (_activeProfileId is null || catNode.DatabaseName is null || catNode.ObjectKind is null)
            return;
        var profileId = _activeProfileId;
        var database = catNode.DatabaseName;
        var kind = catNode.ObjectKind.Value;

        await EnsureChildrenLoadedAsync(catNode, async () =>
        {
            var objects = await _metadata.ListObjectsAsync(profileId, database, kind).ConfigureAwait(true);
            return objects.Select(obj => new ExplorerNode($"{obj.Schema}.{obj.Name}")
            {
                NodeKind = ExplorerNodeKind.Object,
                ProfileId = profileId,
                DatabaseName = obj.Database,
                ObjectKind = obj.Kind,
                SchemaName = obj.Schema,
                ObjectName = obj.Name,
                Description = obj.Description,
                ShowDescription = UiSettings.Instance.ShowObjectsDescription
            }).ToList();
        }).ConfigureAwait(true);
    }

    public async Task OnExplorerSelectionChangedAsync(ExplorerNode? node)
    {
        _selectedNode = node;
        if (node is null) return;

        // Selecting anything inside a database of the connection makes it the current database.
        if (node.NodeKind != ExplorerNodeKind.Profile && node.DatabaseName is { } selDb
            && IsConnected && node.ProfileId == _activeProfileId
            && !string.Equals(_activeDatabase, selDb, StringComparison.Ordinal))
        {
            _activeDatabase = selDb;
            OnPropertyChanged(nameof(ConnectionStatusText));
        }

        if (node.NodeKind == ExplorerNodeKind.Profile && node.ProfileId is not null)
        {
            HasSelection = false;
            StatusText = $"Selecionado: {node.Caption}";
            return;
        }

        if (node.NodeKind == ExplorerNodeKind.Object
            && node.ObjectKind == ObjectKind.Table
            && node.DatabaseName is not null
            && node.SchemaName is not null
            && node.ObjectName is not null
            && IsConnected
            && _activeProfileId is not null)
        {
            HasSelection = true;
            // Single click only activates an already-open document; double-click / Enter opens it (classic-style).
            var open = _docs.Find(DocumentKeys.Table(_activeProfileId, node.DatabaseName, node.SchemaName, node.ObjectName));
            if (open is not null)
                ActiveDocument = open;
            await Task.CompletedTask.ConfigureAwait(true);
        }
        else if (IsProcedureNode(node) && IsConnected && _activeProfileId is not null)
        {
            HasSelection = true;
            var open = _docs.Find(DocumentKeys.Procedure(_activeProfileId, node.DatabaseName!, node.SchemaName!, node.ObjectName!));
            if (open is not null)
                ActiveDocument = open;
        }
        else
        {
            HasSelection = node.NodeKind == ExplorerNodeKind.Object;
        }
    }

    /// <summary>Double-click: on a registered database connects to it; on a table or procedure opens its editor.</summary>
    public async Task OnExplorerDoubleClickAsync(ExplorerNode? node)
    {
        if (node is null) return;
        _selectedNode = node;
        if (node.NodeKind == ExplorerNodeKind.Profile && node.ProfileId is not null)
        {
            if (!(IsConnected && _activeProfileId == node.ProfileId))
                await ConnectAsync().ConfigureAwait(true);
            return;
        }

        if (node.NodeKind == ExplorerNodeKind.Object
            && node.ObjectKind == ObjectKind.Table
            && node.DatabaseName is not null
            && node.SchemaName is not null
            && node.ObjectName is not null)
        {
            HasSelection = true;
            await OpenTableDocumentAsync(node.DatabaseName, node.SchemaName, node.ObjectName).ConfigureAwait(true);
        }
        else if (IsProcedureNode(node) && IsConnected)
        {
            HasSelection = true;
            await OpenProcedureDocumentAsync(node.DatabaseName!, node.SchemaName!, node.ObjectName!).ConfigureAwait(true);
        }
    }

    // ===== Database Explorer context menu =====

    /// <summary>Order of the popup on a registered database (classic layout; separators come from the catalog).</summary>
    private static readonly string[] DatabaseNodeMenuIds =
    {
        "dbexplorer.new_database_folder",
        "dbexplorer.apply_block_to_selected_objects",
        "dbexplorer.sort_nodes",
        "dbexplorer.autogrant_privileges",
        "dbexplorer.goto_database",
        "dbexplorer.refresh",
        "dbexplorer.connect_to_database",
        "dbexplorer.reconnect",
        "dbexplorer.disconnect_from_database",
        "dbexplorer.register_database",
        "dbexplorer.unregister_database",
        "dbexplorer.clone_registration_info",
        "dbexplorer.database_registration_info",
        "dbexplorer.show_sql_assistant",
        "dbexplorer.show_objects_description",
        "dbexplorer.inspector_page_mode",
        "dbexplorer.hide_disconnected_databases",
    };

    private ExplorerMenuContext CurrentExplorerMenuContext()
    {
        var profile = GetSelectedProfile();
        return new ExplorerMenuContext(
            profile is not null,
            profile is not null && IsConnected && _activeProfileId == profile.Id,
            _session.Profiles.Count);
    }

    /// <summary>Called when the Explorer context menu opens: database-node popup on a registered database (or empty area), the object popup elsewhere.</summary>
    public void PrepareExplorerContextMenu()
    {
        ExplorerMenuItems.Clear();
        var node = _selectedNode;
        var items = node is null || node.NodeKind == ExplorerNodeKind.Profile || !ContainsNode(node)
            ? BuildDatabaseNodeMenu()
            : BuildProcedureMenu(node) ?? OpenMenuItems.ToList();
        foreach (var item in items)
            ExplorerMenuItems.Add(item);
    }

    private bool ContainsNode(ExplorerNode node) =>
        ExplorerRoots.Contains(node) || ExplorerRoots.Any(r => r.ProfileId == node.ProfileId);

    private List<CommandMenuItem> BuildDatabaseNodeMenu()
    {
        var ctx = CurrentExplorerMenuContext();
        var selectedId = GetSelectedProfile()?.Id;

        CommandMenuItem Item(string id, bool isChecked = false, IReadOnlyList<CommandMenuItem>? children = null,
            object? parameter = null, string? caption = null, string? tip = null)
        {
            var def = CommandCatalog.Find(id) ?? throw new InvalidOperationException($"Comando '{id}' não está no catálogo.");
            return new CommandMenuItem
            {
                Id = id,
                Caption = caption ?? def.Caption,
                Shortcut = def.Shortcut,
                ToolTip = tip ?? (CommandAvailability.IsImplemented(id) ? def.Caption : $"{def.Caption} — ainda não implementado"),
                IsChecked = isChecked,
                Children = children,
                CommandParameter = parameter,
                Command = children is null
                    ? new RelayCommand(p => ExecuteExplorerItem(id, p), _ => CommandAvailability.CanExecuteExplorerItem(id, ctx))
                    : null
            };
        }

        var items = new List<CommandMenuItem>();
        foreach (var id in DatabaseNodeMenuIds)
        {
            var def = CommandCatalog.Find(id)!;
            if (def.IsSeparatorBefore && items.Count > 0)
                items.Add(new CommandMenuItem { Id = id + ".sep", Caption = "-", IsSeparator = true });

            switch (id)
            {
                case "dbexplorer.sort_nodes":
                    items.Add(Item(id, children: new[]
                    {
                        Item("dbexplorer.sort_by_alias", isChecked: ExplorerSortByAlias),
                        Item("dbexplorer.sort_as_registered", isChecked: !ExplorerSortByAlias)
                    }));
                    break;
                case "dbexplorer.goto_database":
                    var targets = _session.Ordered(ExplorerSortByAlias)
                        .Select(p => Item(id, isChecked: p.Id == selectedId, parameter: p.Id, caption: ProfileLabel(p).Replace("_", "__"),
                            tip: $"{ServerNameText.Format(p)} : {p.InitialCatalog}"))
                        .ToList();
                    if (targets.Count == 0)
                        targets.Add(new CommandMenuItem { Id = id + ".none", Caption = "(nenhum database registrado)", Command = new RelayCommand(_ => { }, _ => false) });
                    items.Add(Item(id, children: targets));
                    break;
                case "dbexplorer.show_objects_description":
                    items.Add(Item(id, isChecked: ShowObjectsDescription,
                        tip: "Mostra a descrição (MS_Description) ao lado dos objetos do Explorer"));
                    break;
                case "dbexplorer.hide_disconnected_databases":
                    items.Add(Item(id, isChecked: HideDisconnectedDatabases));
                    break;
                default:
                    items.Add(Item(id));
                    break;
            }
        }
        return items;
    }

    /// <summary>F5 in the Database Explorer: Refresh when the selected node belongs to the connected database.</summary>
    public void RefreshExplorerShortcut()
    {
        if (IsConnected && _activeProfileId is not null && (_selectedNode is null || _selectedNode.ProfileId == _activeProfileId))
            RunSafe(RefreshActiveExplorerAsync);
    }

    private void ExecuteExplorerItem(string id, object? parameter)
    {
        if (!CommandAvailability.IsImplemented(id)) return;
        switch (id)
        {
            case "dbexplorer.sort_by_alias":
                ExplorerSortByAlias = true;
                StatusText = "Databases ordenados pelo alias.";
                break;
            case "dbexplorer.sort_as_registered":
                ExplorerSortByAlias = false;
                StatusText = "Databases na ordem em que foram registrados.";
                break;
            case "dbexplorer.goto_database":
                if (parameter is string profileId) GotoDatabase(profileId);
                break;
            case "dbexplorer.refresh":
                RunSafe(RefreshActiveExplorerAsync);
                break;
            case "dbexplorer.connect_to_database":
                RunSafe(ConnectAsync);
                break;
            case "dbexplorer.reconnect":
                RunSafe(ReconnectAsync);
                break;
            case "dbexplorer.disconnect_from_database":
                RunSafe(DisconnectInteractiveAsync);
                break;
            case "dbexplorer.register_database":
                RunSafe(RegisterDatabaseAsync);
                break;
            case "dbexplorer.unregister_database":
                RunSafe(UnregisterDatabaseAsync);
                break;
            case "dbexplorer.clone_registration_info":
                RunSafe(CloneRegistrationAsync);
                break;
            case "dbexplorer.database_registration_info":
                RunSafe(EditRegistrationAsync);
                break;
            case "dbexplorer.show_objects_description":
                ShowObjectsDescription = !ShowObjectsDescription;
                StatusText = ShowObjectsDescription ? "Show objects description: ligado." : "Show objects description: desligado.";
                break;
            case "dbexplorer.hide_disconnected_databases":
                HideDisconnectedDatabases = !HideDisconnectedDatabases;
                break;
        }
    }

    /// <summary>Goto Database: selects the registration's node (clears a text filter that hides it).</summary>
    private void GotoDatabase(string profileId)
    {
        var profile = FindProfile(profileId);
        if (profile is null) return;
        var node = ExplorerRoots.FirstOrDefault(n => n.ProfileId == profileId);
        if (node is null) return;
        if (!FilterRoot(node) && !string.IsNullOrWhiteSpace(_filterText))
            FilterText = "";
        if (!FilterRoot(node))
        {
            StatusText = $"'{ProfileLabel(profile)}' está oculto (Hide Disconnected Databases ligado).";
            return;
        }
        SelectProfileNode(profileId);
        StatusText = $"Selecionado: {ProfileLabel(profile)}";
    }

    /// <summary>Refresh (F5): reloads the databases and objects of the connected registration.</summary>
    private async Task RefreshActiveExplorerAsync()
    {
        if (!IsConnected || _activeProfileId is null) return;
        var profile = FindProfile(_activeProfileId);
        StatusText = $"Atualizando {(profile is null ? "" : ProfileLabel(profile))}...";
        await RefreshExplorerFromServerAsync().ConfigureAwait(true);
        if (profile is not null) SelectProfileNode(profile.Id);
        StatusText = $"Explorer atualizado: {(profile is null ? "" : ProfileLabel(profile))}";
    }

    private static void ApplyDescriptionMode(IEnumerable<ExplorerNode> nodes, bool show)
    {
        foreach (var n in nodes)
        {
            if (n.NodeKind == ExplorerNodeKind.Object) n.ShowDescription = show;
            ApplyDescriptionMode(n.Children, show);
        }
    }

    private ObservableCollection<CommandMenuItem> BuildMenu(string category)
    {
        var items = new ObservableCollection<CommandMenuItem>();
        foreach (var def in Registry.Category(category))
        {
            if (def.IsSeparatorBefore)
            {
                items.Add(new CommandMenuItem
                {
                    Id = def.Id + ".sep",
                    Caption = "-",
                    IsSeparator = true,
                    Command = Registry[def.Id]
                });
            }

            var tip = def.SqlServerMapping is null
                ? def.Caption
                : $"{def.Caption}\nSQL Server: {def.SqlServerMapping}";

            items.Add(new CommandMenuItem
            {
                Id = def.Id,
                Caption = def.Caption.TrimEnd('>', ' ').TrimEnd(),
                Shortcut = def.Shortcut,
                ToolTip = tip,
                IsSeparator = false,
                IconKey = SqlExpert.Core.Commands.CommandIcons.For(def.Id),
                Command = Registry[def.Id]
            });
        }

        return items;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(ConnectionStatusText))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveColorMark)));
    }
}

public enum ExplorerNodeKind
{
    Root,
    Profile,
    Database,
    Category,
    Object,
    Placeholder
}

public sealed class ExplorerNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isActive;
    private string _name;

    public ExplorerNode(string name) => _name = name;

    public static ExplorerNode CreatePlaceholder() =>
        new("(loading...)") { NodeKind = ExplorerNodeKind.Placeholder };

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            _hlStart = -1;
            _hlLength = 0;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));
            RaiseHighlightChanged();
        }
    }

    // ----- Explorer filter state (set in one batch by MainViewModel.ApplyFilterResult) -----

    private int _hlStart = -1;
    private int _hlLength;
    private int? _filteredCount;

    /// <summary>Kept by the active Explorer filter (plain field: read by the CollectionView filter, no notification).</summary>
    internal bool FilterVisible { get; set; } = true;

    /// <summary>Caption before the highlighted filter match (the whole name when there is none).</summary>
    public string NamePrefix => _hlStart < 0 ? _name : _name[.._hlStart];
    /// <summary>Filter match inside the caption (drawn bold), or "".</summary>
    public string NameMatch => _hlStart < 0 ? "" : _name.Substring(_hlStart, _hlLength);
    /// <summary>Caption after the highlighted filter match, or "".</summary>
    public string NameSuffix => _hlStart < 0 ? "" : _name[(_hlStart + _hlLength)..];

    /// <summary>Highlights <paramref name="length"/> chars at <paramref name="start"/> (-1 clears). Notifies only on change.</summary>
    internal void SetHighlight(int start, int length)
    {
        if (start < 0 || length <= 0 || start + length > _name.Length) { start = -1; length = 0; }
        if (start == _hlStart && length == _hlLength) return;
        _hlStart = start;
        _hlLength = length;
        RaiseHighlightChanged();
    }

    private void RaiseHighlightChanged()
    {
        var h = PropertyChanged;
        if (h is null) return;
        h(this, new PropertyChangedEventArgs(nameof(NamePrefix)));
        h(this, new PropertyChangedEventArgs(nameof(NameMatch)));
        h(this, new PropertyChangedEventArgs(nameof(NameSuffix)));
    }

    /// <summary>Matching children of a folder while the filter is active ("Tables (3/505)"); null = plain count.</summary>
    internal int? FilteredCount
    {
        get => _filteredCount;
        set
        {
            if (_filteredCount == value) return;
            _filteredCount = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
        }
    }

    private int? _count;

    /// <summary>Number of objects under a folder node (null = no count shown).</summary>
    public int? Count
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));
        }
    }

    /// <summary>" (505)" drawn in blue after the name (separate TextBlock), or null.</summary>
    public string? CountText => ExplorerNodeText.CountText(_count, _filteredCount);

    /// <summary>Plain caption "Tables (505)" (status bar).</summary>
    public string Caption => ExplorerNodeText.FullText(_name, _count);

    private string? _colorMark;
    private string? _description;
    private bool _showDescription;

    /// <summary>Registration Color Mark ("#RRGGBB" or null): colored marker before the database icon.</summary>
    public string? ColorMark
    {
        get => _colorMark;
        set
        {
            if (_colorMark == value) return;
            _colorMark = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColorMark)));
        }
    }

    /// <summary>MS_Description of an object (or server/database info of a registration): tooltip.</summary>
    public string? Description
    {
        get => _description;
        set
        {
            if (_description == value) return;
            _description = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionSuffix)));
        }
    }

    /// <summary>"Show objects description" on: the description is also written after the name.</summary>
    public bool ShowDescription
    {
        get => _showDescription;
        set
        {
            if (_showDescription == value) return;
            _showDescription = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDescription)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionSuffix)));
        }
    }

    /// <summary>Gray text after the name ("  — description", first line only) or null.</summary>
    public string? DescriptionSuffix =>
        _showDescription && !string.IsNullOrWhiteSpace(_description)
            ? "  — " + _description.Replace("\r\n", "\n").Split('\n')[0].Trim()
            : null;

    public ExplorerNodeKind NodeKind { get; set; } = ExplorerNodeKind.Root;

    /// <summary>
    /// Vector icon key (Themes/Icons.xaml): registration (connected / not), database, or the object type - the same
    /// icon on the folder ("Tables") and on each object under it. Null = no icon (placeholder).
    /// </summary>
    public string? IconKey => NodeKind switch
    {
        ExplorerNodeKind.Profile => ObjectIcons.Registration(IsActive),
        ExplorerNodeKind.Database => ObjectIcons.Database,
        ExplorerNodeKind.Category or ExplorerNodeKind.Object when ObjectKind is { } kind => ObjectIcons.For(kind),
        _ => null
    };

    public string? ProfileId { get; set; }
    public string? DatabaseName { get; set; }
    public ObjectKind? ObjectKind { get; set; }
    public string? SchemaName { get; set; }
    public string? ObjectName { get; set; }
    public bool HasDummyChild { get; set; }
    /// <summary>Running/completed child load (null = not loaded yet). Makes lazy loading idempotent.</summary>
    public Task? LoadTask { get; set; }
    public ObservableCollection<ExplorerNode> Children { get; init; } = new();

    /// <summary>True for the registered database that is currently connected.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconKey)));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One row of the Fields grid (wraps the column metadata it was built from).</summary>
/// <summary>One row of the Constraints tab.</summary>
public sealed class ConstraintRow
{
    public ConstraintRow(ConstraintInfo c)
    {
        Name = c.Name;
        TypeCode = c.Type;
        Type = Display(c.Type);
        Definition = c.Definition;
    }

    public string Name { get; }
    /// <summary>sys type_desc (PRIMARY_KEY_CONSTRAINT, UNIQUE_CONSTRAINT, FOREIGN_KEY_CONSTRAINT, CHECK_CONSTRAINT).</summary>
    public string TypeCode { get; }
    public string Type { get; }
    /// <summary>Key fields, referenced table (FK) or CHECK expression.</summary>
    public string Definition { get; }

    public static string Display(string typeDesc) => typeDesc switch
    {
        "PRIMARY_KEY_CONSTRAINT" => "PRIMARY KEY",
        "UNIQUE_CONSTRAINT" => "UNIQUE",
        "FOREIGN_KEY_CONSTRAINT" => "FOREIGN KEY",
        "CHECK_CONSTRAINT" => "CHECK",
        _ => typeDesc
    };

    public static int Order(string typeDesc) => typeDesc switch
    {
        "PRIMARY_KEY_CONSTRAINT" => 0,
        "UNIQUE_CONSTRAINT" => 1,
        "FOREIGN_KEY_CONSTRAINT" => 2,
        _ => 3
    };
}

public sealed class FieldRow
{
    public FieldRow(ColumnInfo c)
    {
        Column = c;
        Size = FieldListFormatter.SizeText(c);
        Scale = FieldListFormatter.ScaleText(c);
    }

    public ColumnInfo Column { get; }
    public int Ordinal => Column.Ordinal;
    public bool PK => Column.IsPrimaryKey;
    public bool FK => Column.IsForeignKey;
    public bool UNQ => Column.IsUnique;
    public string FieldName => Column.Name;
    public string FieldType => Column.DataType;
    public string Size { get; }
    public string Scale { get; }
    public bool Nullable => Column.IsNullable;
    public string? DefaultValue => Column.DefaultValue;
    public string? Collation => Column.Collation;
    /// <summary>MS_Description of the column.</summary>
    public string? Notes => Column.Description;
}
