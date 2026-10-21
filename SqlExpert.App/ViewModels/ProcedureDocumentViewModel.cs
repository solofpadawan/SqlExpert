using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using SqlExpert.App.Commands;
using SqlExpert.App.Services;
using SqlExpert.App.Views;
using SqlExpert.Core.Commands;
using SqlExpert.Core.Documents;
using SqlExpert.Core.Procedures;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.App.ViewModels;

/// <summary>Editor operations the procedure view model needs from its view.</summary>
public interface IProcedureEditorHost
{
    /// <summary>Puts the caret on a line of the visible editor (body or full text) and optionally paints it as the error line.</summary>
    void GoToLine(int line, bool markError);
    void FocusEditor();
    /// <summary>Commits a cell edit in progress in the parameter grid.</summary>
    void CommitParameterEdit();
}

/// <summary>
/// Procedure editor document (classic layout): parameter grid + body editor (header generated from the grid),
/// or the whole CREATE text in "Edit full text" mode. Compile runs CREATE OR ALTER (ALTER/CREATE before
/// SQL Server 2016 SP1) on the document's own session, so with Autocommit off the compile stays inside an
/// open transaction until Commit/Rollback. Execute asks the input values and shows result sets, Messages,
/// OUTPUT values and RETURN. Tabs: Description, Dependencies, DDL, Grants and a local Version History.
/// </summary>
public sealed class ProcedureDocumentViewModel : DocumentViewModel
{
    private static int _newCounter;

    private readonly SqlEditorSession _session;
    private readonly ObjectVersionHistoryStore _history;
    private readonly string _connectionLabel;
    private readonly int _fetchRows;
    private readonly DispatcherTimer _analyzeTimer;
    private readonly System.Diagnostics.Stopwatch _runClock = new();
    private readonly DispatcherTimer _clockTimer;
    private string? _serverSchema;
    private string? _serverName;
    private string _schema;
    private string _objectName;
    private string? _options;
    private bool _forReplication;
    private string? _preamble;
    private bool _isFullTextMode;
    private bool _isModified;
    private bool _isBusy;
    private bool _loading;
    private string _statusLine = "Pronto.";
    private string _caretText = "1 : 1";
    private string _headerText = "";
    private string _editHeader = "Edit";
    private ProcedureParameter? _selectedParameter;
    private int _selectedTabIndex;
    private int _selectedSubTab;
    private int _selectedResultTab;
    private string _description = "";
    private string _savedDescription = "";
    private string _ddl = "";
    private string _declarationsNote = "";
    private string? _serverVersion;
    private bool _explorerRefreshPending;
    private bool _compiledInTransaction;
    private IReadOnlyDictionary<string, string> _savedParamDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private ObjectVersionEntry? _selectedHistory;
    private string _historyText = "";
    private CancellationTokenSource? _cts;

    public ProcedureDocumentViewModel(MainViewModel shell, string profileId, string database, string schema, string? name,
        string connectionLabel, SqlEditorSession session, ObjectVersionHistoryStore history, int fetchRows)
        : base(shell, name is null
            ? DocumentKeys.NewProcedure(profileId, database, Interlocked.Increment(ref _newCounter))
            : DocumentKeys.Procedure(profileId, database, schema, name), profileId, database)
    {
        _session = session;
        _history = history;
        _connectionLabel = connectionLabel;
        _fetchRows = fetchRows;
        _schema = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema;
        _objectName = name ?? ProcedureScriptBuilder.NewProcedureName;
        if (name is not null) { _serverSchema = _schema; _serverName = name; }

        Parameters.CollectionChanged += OnParametersCollectionChanged;
        BodyDocument.TextChanged += (_, _) => OnTextChanged();
        FullTextDocument.TextChanged += (_, _) => OnTextChanged();
        _analyzeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _analyzeTimer.Tick += (_, _) => { _analyzeTimer.Stop(); RefreshDerived(); };
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _clockTimer.Tick += (_, _) => StatusLine = $"Executando... {SqlEditorStatus.FormatElapsed(_runClock.Elapsed)}  (Stop / Ctrl+Break cancela)";

        CompileCommand = Cmd("proceditor.compile", () => Run(async () => await CompileAsync().ConfigureAwait(true)));
        ExecuteCommand = Cmd("proceditor.execute", () => Run(ExecuteAsync));
        CommitCommand = Cmd("proceditor.commit", () => Run(CommitAsync));
        RollbackCommand = Cmd("proceditor.rollback", () => Run(RollbackAsync));
        ToggleFullTextCommand = Cmd("proceditor.edit_full_text", ToggleFullText);
        ReloadCommand = Cmd("proceditor.reload", () => Run(ReloadAsync));
        CopyNameCommand = Cmd("proceditor.copy_name", CopyName);
        ScriptToClipboardCommand = Cmd("proceditor.script_to_clipboard", ScriptToClipboard);
        DropCommand = Cmd("proceditor.drop_procedure", () => Run(DropAsync));
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsBusy && _cts is not null);
        DisabledCommand = new RelayCommand(_ => { }, _ => false);

        AddParameterCommand = new RelayCommand(_ => AddParameter(insert: false), _ => CanEditGrid);
        InsertParameterCommand = new RelayCommand(_ => AddParameter(insert: true), _ => CanEditGrid);
        DeleteParameterCommand = new RelayCommand(_ => DeleteParameter(), _ => CanEditGrid && SelectedParameter is not null);
        MoveParameterUpCommand = new RelayCommand(_ => MoveParameter(-1), _ => CanEditGrid && SelectedParameter is { } p && Parameters.IndexOf(p) > 0);
        MoveParameterDownCommand = new RelayCommand(_ => MoveParameter(1), _ => CanEditGrid && SelectedParameter is { } p && Parameters.IndexOf(p) < Parameters.Count - 1);

        SaveDescriptionCommand = new RelayCommand(_ => Run(SaveDescriptionAsync), _ => !IsBusy && Exists && IsConnected && DescriptionModified);
        RefreshDependenciesCommand = new RelayCommand(_ => Run(ReloadTabsAsync), _ => !IsBusy && Exists && IsConnected);
        RestoreHistoryCommand = new RelayCommand(_ => RestoreHistory(), _ => !IsBusy && SelectedHistory is not null);
        RefreshHistoryCommand = new RelayCommand(_ => LoadHistory(), _ => true);

        MenuItems = BuildMenu();
        ApplyDefinition(ProcedureScriptBuilder.NewTemplate(_schema), fromServer: false);
        if (name is not null)
        {
            // Existing procedure: the template is only a placeholder until LoadAsync reads the definition.
            _loading = true;
            _objectName = name;
            BodyDocument.Text = "";
            BodyDocument.UndoStack.ClearAll();
            HeaderText = "";
            _loading = false;
            _analyzeTimer.Stop();
        }
    }

    // ===== Identity / titles =====

    public string Schema
    {
        get => _schema;
        private set { _schema = value; OnPropertyChanged(); }
    }

    /// <summary>Object-name box of the toolbar ("NAME" or "schema.NAME"); for New Procedure this is where the name goes.</summary>
    public string ObjectName
    {
        get => _objectName;
        set
        {
            var v = (value ?? "").Trim();
            var dot = SplitSchema(v);
            if (dot is { } sn) { Schema = sn.Schema; v = sn.Name; }
            if (_objectName == v) { OnPropertyChanged(); return; }
            _objectName = v;
            OnPropertyChanged();
            if (!_loading)
            {
                IsModified = true;
                UpdateHeader();
            }
            RaiseTitles();
        }
    }

    public bool IsNew => _serverName is null;
    public bool Exists => _serverName is not null;
    public string? ServerSchema => _serverSchema;
    public string? ServerName => _serverName;

    public override string Caption => WindowTitles.ProcedureCaption(Schema, ObjectName);
    public override string Title => $"{Caption}{(IsModified ? " *" : "")} : {Database}";
    public override string ShortName => ObjectName + (IsModified ? " *" : "");
    public override string ToolTip => $"[{Schema}].[{ObjectName}] : {_connectionLabel} : {Database}";

    // ===== Edit tab =====

    public ObservableCollection<ProcedureParameter> Parameters { get; } = new();
    public ObservableCollection<ProcedureParameter> OutputParameters { get; } = new();
    public ObservableCollection<DeclaredItem> Variables { get; } = new();
    public ObservableCollection<DeclaredItem> Cursors { get; } = new();

    public ProcedureParameter? SelectedParameter
    {
        get => _selectedParameter;
        set { _selectedParameter = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>Body after AS (split mode).</summary>
    public TextDocument BodyDocument { get; } = new();
    /// <summary>Whole CREATE text ("Edit full text" mode, or a definition the grid cannot represent).</summary>
    public TextDocument FullTextDocument { get; } = new();

    public bool IsFullTextMode
    {
        get => _isFullTextMode;
        private set
        {
            if (_isFullTextMode == value) return;
            _isFullTextMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSplitMode));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsSplitMode => !_isFullTextMode;
    private bool CanEditGrid => !IsBusy && IsSplitMode;

    /// <summary>Generated header (read-only preview above the body editor).</summary>
    public string HeaderText
    {
        get => _headerText;
        private set { _headerText = value; OnPropertyChanged(); }
    }

    /// <summary>"Edit :: 956 bytes".</summary>
    public string EditHeader
    {
        get => _editHeader;
        private set { _editHeader = value; OnPropertyChanged(); }
    }

    public string? Options => _options;
    public bool ForReplication => _forReplication;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    /// <summary>Input Parameters (0) / Output Parameters (1) / Variables (2) / Cursors (3).</summary>
    public int SelectedSubTab
    {
        get => _selectedSubTab;
        set { _selectedSubTab = value; OnPropertyChanged(); if (value >= 2) RefreshDeclarations(); }
    }

    public string DeclarationsNote
    {
        get => _declarationsNote;
        private set { _declarationsNote = value; OnPropertyChanged(); }
    }

    // ===== Status =====

    public bool IsModified
    {
        get => _isModified;
        private set
        {
            if (_isModified == value) return;
            _isModified = value;
            OnPropertyChanged();
            RaiseTitles();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsIdle));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsIdle => !_isBusy;
    private bool IsConnected => Shell.IsProfileConnected(ProfileId);

    public string StatusLine
    {
        get => _statusLine;
        set { _statusLine = value; OnPropertyChanged(); }
    }

    public string CaretText
    {
        get => _caretText;
        set { _caretText = value; OnPropertyChanged(); }
    }

    /// <summary>Checkbox: true = autocommit (registration default); false = compile/execute inside a transaction kept open.</summary>
    public bool Autocommit
    {
        get => _session.Autocommit;
        set
        {
            if (value == _session.Autocommit) return;
            Run(() => SetAutocommitAsync(value));
        }
    }

    public bool InTransaction => _session.InTransaction;
    public string TransactionText => SqlEditorStatus.TransactionText(_session.Autocommit, _session.TranCount);

    // ===== Results (Compile messages / Execute results) =====

    public ObservableCollection<SqlResultTab> Results { get; } = new();
    public ObservableCollection<SqlMessage> Messages { get; } = new();

    public int SelectedResultTab
    {
        get => _selectedResultTab;
        set { _selectedResultTab = value; OnPropertyChanged(); }
    }

    /// <summary>Raised after Results/Messages were replaced (the view rebuilds the result tabs).</summary>
    public event Action? ResultsChanged;

    // ===== Other tabs =====

    public string Description
    {
        get => _description;
        set { _description = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(DescriptionModified)); CommandManager.InvalidateRequerySuggested(); }
    }

    public bool DescriptionModified => !string.Equals(_description, _savedDescription, StringComparison.Ordinal);

    public ObservableCollection<ObjectDependency> Dependencies { get; } = new();
    public ObservableCollection<ObjectPermission> Grants { get; } = new();

    public string Ddl
    {
        get => _ddl;
        private set { _ddl = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ObjectVersionEntry> HistoryEntries { get; } = new();

    public ObjectVersionEntry? SelectedHistory
    {
        get => _selectedHistory;
        set
        {
            _selectedHistory = value;
            OnPropertyChanged();
            try { HistoryText = value is null ? "" : ObjectVersionHistoryStore.Read(value); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { HistoryText = "-- não foi possível ler: " + ex.Message; }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string HistoryText
    {
        get => _historyText;
        private set { _historyText = value; OnPropertyChanged(); }
    }

    public string HistoryFolder => _history.DirectoryFor(ProfileId, Database, Schema, ObjectName);

    // ===== Commands =====

    public ICommand CompileCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand CommitCommand { get; }
    public ICommand RollbackCommand { get; }
    public ICommand ToggleFullTextCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand CopyNameCommand { get; }
    public ICommand ScriptToClipboardCommand { get; }
    public ICommand DropCommand { get; }
    public ICommand CancelCommand { get; }
    /// <summary>Always disabled: buttons of reference features that are not implemented (Print, Debug…).</summary>
    public ICommand DisabledCommand { get; }
    public ICommand AddParameterCommand { get; }
    public ICommand InsertParameterCommand { get; }
    public ICommand DeleteParameterCommand { get; }
    public ICommand MoveParameterUpCommand { get; }
    public ICommand MoveParameterDownCommand { get; }
    public ICommand SaveDescriptionCommand { get; }
    public ICommand RefreshDependenciesCommand { get; }
    public ICommand RestoreHistoryCommand { get; }
    public ICommand RefreshHistoryCommand { get; }

    /// <summary>"Procedure ▾" drop-down (catalog category ProcedureEditor; unimplemented items greyed).</summary>
    public IReadOnlyList<CommandMenuItem> MenuItems { get; }

    public IProcedureEditorHost? Host { get; set; }

    private ProcedureEditorContext EditorContext() => new(IsConnected, IsBusy, InTransaction, Exists);

    private RelayCommand Cmd(string id, Action run) =>
        new(_ => run(), _ => CommandAvailability.CanExecuteProcedureEditorItem(id, EditorContext()));

    private IReadOnlyList<CommandMenuItem> BuildMenu()
    {
        var map = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase)
        {
            ["proceditor.compile"] = CompileCommand,
            ["proceditor.execute"] = ExecuteCommand,
            ["proceditor.commit"] = CommitCommand,
            ["proceditor.rollback"] = RollbackCommand,
            ["proceditor.edit_full_text"] = ToggleFullTextCommand,
            ["proceditor.reload"] = ReloadCommand,
            ["proceditor.copy_name"] = CopyNameCommand,
            ["proceditor.script_to_clipboard"] = ScriptToClipboardCommand,
            ["proceditor.drop_procedure"] = DropCommand
        };
        var items = new List<CommandMenuItem>();
        foreach (var def in CommandCatalog.ByCategory("ProcedureEditor"))
        {
            if (def.IsSeparatorBefore && items.Count > 0)
                items.Add(new CommandMenuItem { Id = def.Id + ".sep", Caption = "-", IsSeparator = true });
            var implemented = CommandAvailability.IsImplemented(def.Id) && map.ContainsKey(def.Id);
            items.Add(new CommandMenuItem
            {
                Id = def.Id,
                Caption = def.Caption,
                Shortcut = def.Shortcut,
                ToolTip = implemented ? def.SqlServerMapping ?? def.Caption : $"{def.Caption} — ainda não implementado",
                Command = implemented ? map[def.Id] : DisabledCommand
            });
        }
        return items;
    }

    private static async void Run(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { App.ReportError(ex); }
    }

    private void RaiseTitles()
    {
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ShortName));
        OnPropertyChanged(nameof(ToolTip));
        Shell.RefreshWindowTitle();
    }

    private void RaiseTransaction()
    {
        OnPropertyChanged(nameof(InTransaction));
        OnPropertyChanged(nameof(TransactionText));
        OnPropertyChanged(nameof(Autocommit));
        CommandManager.InvalidateRequerySuggested();
    }

    // ===== Definition <-> editor =====

    private static (string Schema, string Name)? SplitSchema(string v)
    {
        if (v.StartsWith('[')) v = v.Replace("[", "").Replace("]", "");
        var i = v.IndexOf('.');
        if (i <= 0 || i == v.Length - 1) return null;
        return (v[..i].Trim(), v[(i + 1)..].Trim());
    }

    /// <summary>Definition currently in the split editor (grid + body).</summary>
    public ProcedureDefinition CurrentDefinition()
    {
        Host?.CommitParameterEdit();
        return new ProcedureDefinition
        {
            Schema = Schema,
            Name = ObjectName,
            Parameters = Parameters.ToList(),
            Options = _options,
            ForReplication = _forReplication,
            Preamble = _preamble,
            Body = BodyDocument.Text
        };
    }

    /// <summary>Verb shown in the header preview / full-text conversion (Compile decides again with the server state).</summary>
    private ProcedureVerb DisplayVerb => ProcedureScriptBuilder.CompileVerb(_serverVersion, Exists);

    /// <summary>Script that Compile would send right now.</summary>
    public string CurrentScript(ProcedureVerb verb) =>
        IsFullTextMode
            ? ProcedureScriptParser.RewriteVerb(FullTextDocument.Text, verb) ?? FullTextDocument.Text
            : ProcedureScriptBuilder.BuildScript(CurrentDefinition(), verb);

    private void ApplyDefinition(ProcedureDefinition def, bool fromServer)
    {
        _loading = true;
        try
        {
            foreach (var p in Parameters) p.Changed -= OnParameterChanged;
            Parameters.Clear();
            foreach (var p in def.Parameters)
            {
                if (fromServer && _savedParamDescriptions.TryGetValue(p.Name, out var d)) p.Description = d;
                Parameters.Add(p);
            }
            Schema = def.Schema;
            _objectName = def.Name;
            OnPropertyChanged(nameof(ObjectName));
            _options = def.Options;
            _forReplication = def.ForReplication;
            _preamble = def.Preamble;
            BodyDocument.Text = def.Body;
            BodyDocument.UndoStack.ClearAll();
            IsFullTextMode = false;
        }
        finally
        {
            _loading = false;
        }
        SelectedParameter = Parameters.FirstOrDefault();
        UpdateHeader();
        RefreshDerived();
        RaiseTitles();
    }

    private void ApplyFullText(string text)
    {
        _loading = true;
        try
        {
            FullTextDocument.Text = text;
            FullTextDocument.UndoStack.ClearAll();
            IsFullTextMode = true;
        }
        finally
        {
            _loading = false;
        }
        RefreshDerived();
    }

    private void OnParametersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (ProcedureParameter p in e.NewItems) { p.Changed -= OnParameterChanged; p.Changed += OnParameterChanged; }
        if (e.OldItems is not null)
            foreach (ProcedureParameter p in e.OldItems) p.Changed -= OnParameterChanged;
        if (_loading) return;
        IsModified = true;
        UpdateHeader();
    }

    private void OnParameterChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        IsModified = true;
        UpdateHeader();
    }

    private void OnTextChanged()
    {
        if (_loading) return;
        IsModified = true;
        _analyzeTimer.Stop();
        _analyzeTimer.Start();
    }

    private void UpdateHeader()
    {
        if (IsSplitMode) HeaderText = ProcedureScriptBuilder.BuildHeader(CurrentDefinitionNoCommit(), DisplayVerb);
        RebuildOutputParameters();
        _analyzeTimer.Stop();
        _analyzeTimer.Start();
    }

    private ProcedureDefinition CurrentDefinitionNoCommit() => new()
    {
        Schema = Schema, Name = ObjectName, Parameters = Parameters.ToList(), Options = _options,
        ForReplication = _forReplication, Preamble = _preamble, Body = BodyDocument.Text
    };

    private void RebuildOutputParameters()
    {
        OutputParameters.Clear();
        foreach (var p in Parameters.Where(p => p.IsOutput)) OutputParameters.Add(p);
    }

    /// <summary>Byte count of the Edit tab header and the Variables / Cursors lists (debounced while typing).</summary>
    private void RefreshDerived()
    {
        var script = IsFullTextMode ? FullTextDocument.Text : ProcedureScriptBuilder.BuildScript(CurrentDefinitionNoCommit(), DisplayVerb);
        EditHeader = $"Edit :: {Encoding.UTF8.GetByteCount(script):N0} bytes";
        if (SelectedSubTab >= 2) RefreshDeclarations();
    }

    private void RefreshDeclarations()
    {
        var text = IsFullTextMode ? FullTextDocument.Text : BodyDocument.Text;
        var found = ProcedureScriptParser.FindDeclarations(text);
        if (found is null)
        {
            DeclarationsNote = "O texto tem erro de sintaxe: lista não atualizada.";
            return;
        }
        Variables.Clear();
        foreach (var v in found.Value.Variables) Variables.Add(v);
        Cursors.Clear();
        foreach (var c in found.Value.Cursors) Cursors.Add(c);
        DeclarationsNote = $"{Variables.Count} variável(is), {Cursors.Count} cursor(es) declarados com DECLARE (linhas do {(IsFullTextMode ? "texto completo" : "corpo")}). Somente leitura.";
    }

    // ===== Parameter grid buttons =====

    private void AddParameter(bool insert)
    {
        Host?.CommitParameterEdit();
        var used = Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = Parameters.Count + 1;
        while (used.Contains("@PARAM" + n)) n++;
        var p = new ProcedureParameter { Name = "@PARAM" + n, TypeName = "int" };
        var index = insert && SelectedParameter is { } sel ? Parameters.IndexOf(sel) : Parameters.Count;
        Parameters.Insert(Math.Max(0, index), p);
        SelectedParameter = p;
    }

    private void DeleteParameter()
    {
        if (SelectedParameter is not { } p) return;
        Host?.CommitParameterEdit();
        var i = Parameters.IndexOf(p);
        Parameters.Remove(p);
        SelectedParameter = Parameters.Count == 0 ? null : Parameters[Math.Min(i, Parameters.Count - 1)];
    }

    private void MoveParameter(int step)
    {
        if (SelectedParameter is not { } p) return;
        Host?.CommitParameterEdit();
        var i = Parameters.IndexOf(p);
        var j = i + step;
        if (i < 0 || j < 0 || j >= Parameters.Count) return;
        Parameters.Move(i, j);
        SelectedParameter = p;
    }

    // ===== Full text toggle =====

    private void ToggleFullText()
    {
        if (!IsFullTextMode)
        {
            var script = ProcedureScriptBuilder.BuildScript(CurrentDefinition(), DisplayVerb);
            var modified = IsModified;
            ApplyFullText(script);
            IsModified = modified;
            StatusLine = "Modo texto completo: edite todo o CREATE. Clique em \"Edit full text\" de novo para voltar à grade.";
            Host?.FocusEditor();
            return;
        }

        var r = ProcedureScriptParser.Parse(FullTextDocument.Text);
        if (!r.Success)
        {
            Ask("Não foi possível separar o texto em parâmetros e corpo:\n\n" + string.Join("\n", r.Errors) +
                "\n\nContinue no modo texto completo (o Compile funciona normalmente).", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
            return;
        }
        if (r.Definition!.HeaderHasComments &&
            Ask("O cabeçalho (parâmetros / WITH) tem comentários, que serão perdidos porque o cabeçalho é gerado pela grade.\n\nVoltar para a grade mesmo assim?",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        var wasModified = IsModified;
        var descriptions = Parameters.Where(p => p.Description is not null).ToDictionary(p => p.Name, p => p.Description!, StringComparer.OrdinalIgnoreCase);
        foreach (var p in r.Definition.Parameters)
            if (descriptions.TryGetValue(p.Name, out var d)) p.Description = d;
        ApplyDefinition(r.Definition, fromServer: false);
        IsModified = wasModified;
        StatusLine = "Modo grade: parâmetros na grade, corpo no editor.";
        Host?.FocusEditor();
    }

    // ===== Load =====

    /// <summary>Loads the procedure from the server (or prepares the New Procedure template).</summary>
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            _serverVersion ??= await _session.RunOnConnectionAsync((c, ct) => ProcedureCatalog.GetServerVersionAsync(c, ct)).ConfigureAwait(true);
            if (IsNew)
            {
                _loading = true;
                ObjectName = ProcedureScriptBuilder.NewProcedureName;
                _loading = false;
                UpdateHeader();
                IsModified = true;
                StatusLine = "Nova procedure: digite o nome na caixa à direita da barra, os parâmetros na grade e o corpo no editor. Ctrl+F9 compila.";
                LoadHistory();
                return;
            }
            await LoadFromServerAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            StatusLine = "Erro ao carregar: " + ex.Message;
            ShowMessages(new[] { new SqlMessage(SqlMessageKind.Error, ex.Message) });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadFromServerAsync()
    {
        var schema = _serverSchema!;
        var name = _serverName!;
        var info = await _session.RunOnConnectionAsync((c, ct) => ProcedureCatalog.LoadAsync(c, schema, name, ct)).ConfigureAwait(true);
        if (info is null)
        {
            StatusLine = $"Procedure [{schema}].[{name}] não encontrada no servidor.";
            ShowMessages(new[] { new SqlMessage(SqlMessageKind.Error, StatusLine) });
            return;
        }
        _savedParamDescriptions = info.ParameterDescriptions;
        _savedDescription = info.Description ?? "";
        Description = _savedDescription;

        var definition = info.Definition;
        if (definition is null)
        {
            ApplyFullText($"-- A definição de [{schema}].[{name}] não está disponível (WITH ENCRYPTION ou sem permissão VIEW DEFINITION).{ProcedureScriptBuilder.NewLine}");
            StatusLine = "Definição indisponível.";
        }
        else
        {
            var parsed = info.IsClr ? null : ProcedureScriptParser.Parse(definition);
            if (parsed is { Success: true } && !parsed.Definition!.HeaderHasComments)
            {
                ApplyDefinition(parsed.Definition, fromServer: true);
                StatusLine = $"[{schema}].[{name}] carregada: {Parameters.Count} parâmetro(s). Ctrl+F9 compila, F9 executa.";
            }
            else
            {
                ApplyFullText(definition);
                StatusLine = parsed is { Success: true }
                    ? "O cabeçalho tem comentários: aberto no modo texto completo para não perdê-los (\"Edit full text\" volta para a grade)."
                    : "Aberto no modo texto completo: " + (info.IsClr ? "procedure CLR." : string.Join(" ", parsed?.Errors ?? Array.Empty<string>()));
            }
        }
        _loading = true;
        _objectName = name;
        Schema = schema;
        _loading = false;
        OnPropertyChanged(nameof(ObjectName));
        IsModified = false;
        UpdateHeader();
        RaiseTitles();
        await ReloadTabsCoreAsync(info).ConfigureAwait(true);
        LoadHistory();
    }

    private async Task ReloadAsync()
    {
        if (IsModified && Ask("Descartar as alterações e recarregar a procedure do servidor?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        IsBusy = true;
        try { await LoadFromServerAsync().ConfigureAwait(true); }
        finally { IsBusy = false; }
    }

    /// <summary>DDL / Dependencies / Grants / Description baseline, read on the session connection.</summary>
    private async Task ReloadTabsAsync()
    {
        if (!Exists) return;
        IsBusy = true;
        try
        {
            var schema = _serverSchema!;
            var name = _serverName!;
            var info = await _session.RunOnConnectionAsync((c, ct) => ProcedureCatalog.LoadAsync(c, schema, name, ct)).ConfigureAwait(true);
            if (info is not null) await ReloadTabsCoreAsync(info).ConfigureAwait(true);
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadTabsCoreAsync(ProcedureInfo info)
    {
        var schema = info.Schema;
        var name = info.Name;
        var (deps, grants) = await _session.RunOnConnectionAsync(async (c, ct) =>
            (await ProcedureCatalog.GetDependenciesAsync(c, schema, name, ct).ConfigureAwait(false),
             await ProcedureCatalog.GetPermissionsAsync(c, schema, name, ct).ConfigureAwait(false))).ConfigureAwait(true);
        Dependencies.Clear();
        foreach (var d in deps) Dependencies.Add(d);
        Grants.Clear();
        foreach (var g in grants) Grants.Add(g);
        Ddl = ProcedureCatalog.BuildDdl(info, grants);
        _savedParamDescriptions = info.ParameterDescriptions;
        _savedDescription = info.Description ?? "";
        OnPropertyChanged(nameof(DescriptionModified));
        RaiseTransaction();
    }

    // ===== Compile =====

    /// <summary>Compiles the editor text. True when the procedure was created/altered without errors.</summary>
    public async Task<bool> CompileAsync()
    {
        if (IsBusy) return false;
        if (!IsConnected)
        {
            Ask("Conecte ao database desta procedure primeiro.", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);
            return false;
        }
        Host?.CommitParameterEdit();

        string schema, name;
        if (IsFullTextMode)
        {
            if (ProcedureScriptParser.ParseName(FullTextDocument.Text) is { } n) { schema = n.Schema; name = n.Name; }
            else { schema = Schema; name = ObjectName; }
        }
        else
        {
            schema = Schema;
            name = ObjectName;
            var bad = Parameters.FirstOrDefault(p => string.IsNullOrWhiteSpace(p.TypeName) || p.Name.Length < 2);
            if (bad is not null)
            {
                Ask($"O parâmetro \"{bad.Name}\" está sem nome ou sem tipo.", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                return false;
            }
            var dup = Parameters.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dup is not null)
            {
                Ask($"O parâmetro {dup.Key} aparece mais de uma vez.", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                return false;
            }
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            Ask("Digite o nome da procedure na caixa à direita da barra de ferramentas.", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
            return false;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            _serverVersion ??= await _session.RunOnConnectionAsync((c, ct) => ProcedureCatalog.GetServerVersionAsync(c, ct)).ConfigureAwait(true);
            var exists = await _session.RunOnConnectionAsync((c, ct) => ProcedureCatalog.ExistsAsync(c, schema, name, ct)).ConfigureAwait(true);
            var sameObject = Exists && string.Equals(schema, _serverSchema, StringComparison.OrdinalIgnoreCase) && string.Equals(name, _serverName, StringComparison.OrdinalIgnoreCase);
            if (exists && !sameObject)
            {
                if (Ask($"Já existe a procedure [{schema}].[{name}] no database {Database}.\n\nSubstituir a definição dela pelo texto deste editor?",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return false;
            }
            else if (!exists && Exists && !sameObject)
            {
                if (Ask($"O nome mudou de [{_serverSchema}].[{_serverName}] para [{schema}].[{name}].\n\nSerá criada uma NOVA procedure; a antiga continua existindo.\n\nContinuar?",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return false;
            }

            string? previous = null;
            if (exists)
            {
                var prevInfo = await _session.RunOnConnectionAsync((c, ct) => ProcedureCatalog.LoadAsync(c, schema, name, ct)).ConfigureAwait(true);
                previous = prevInfo?.Definition;
            }

            var verb = ProcedureScriptBuilder.CompileVerb(_serverVersion, exists);
            ProcedureDefinition? def = null;
            string script;
            int bodyStart = 1;
            if (IsFullTextMode)
            {
                script = ProcedureScriptParser.RewriteVerb(FullTextDocument.Text, verb) ?? FullTextDocument.Text;
            }
            else
            {
                def = CurrentDefinition();
                script = ProcedureScriptBuilder.BuildScript(def, verb);
                bodyStart = ProcedureScriptBuilder.BodyStartLine(def, verb);
            }

            StatusLine = $"Compilando [{schema}].[{name}] ({ProcedureScriptBuilder.VerbText(verb)} PROCEDURE)...";
            _runClock.Restart();
            var result = await _session.ExecuteAsync(new SqlExecutionRequest { Sql = script, MaxRows = 100, StopOnError = true }, _cts.Token).ConfigureAwait(true);
            _runClock.Stop();
            var messages = IsFullTextMode ? ProcedureErrorLines.MapToFullText(result.Messages) : ProcedureErrorLines.MapToBody(result.Messages, bodyStart);
            if (result.HasErrors || result.Cancelled)
            {
                var list = messages.ToList();
                list.Add(new SqlMessage(SqlMessageKind.Status, result.Cancelled ? "Compile cancelado." : $"Compile com erro ({SqlEditorStatus.FormatElapsed(result.Elapsed)}). Duplo clique na mensagem vai para a linha."));
                ShowMessages(list);
                StatusLine = result.Cancelled ? "Compile cancelado." : "Erro no compile: " + FirstErrorText(list);
                if (list.FirstOrDefault(m => m.Kind == SqlMessageKind.Error && m.EditorLine is not null) is { EditorLine: { } line })
                    Host?.GoToLine(line, markError: true);
                return false;
            }

            var extra = new List<SqlMessage>(messages);
            // Parameter descriptions (MS_Description) follow the grid.
            if (def is not null)
            {
                var sync = ProcedureScriptBuilder.BuildParameterDescriptions(
                    new ProcedureDefinition { Schema = schema, Name = name, Parameters = def.Parameters },
                    // every known description is re-applied (idempotent IF EXISTS update/add/drop): safe even if ALTER reset them
                    _savedParamDescriptions.Keys.ToDictionary(k => k, _ => "\u0001", StringComparer.OrdinalIgnoreCase));
                if (sync.Length > 0)
                {
                    var r2 = await _session.ExecuteAsync(new SqlExecutionRequest { Sql = sync, StopOnError = false }).ConfigureAwait(true);
                    extra.AddRange(r2.Messages.Where(m => m.Kind is SqlMessageKind.Error or SqlMessageKind.Info));
                }
            }

            var wasNew = IsNew;
            _serverSchema = schema;
            _serverName = name;
            Key = DocumentKeys.Procedure(ProfileId, Database, schema, name);
            _loading = true;
            Schema = schema;
            _objectName = name;
            _loading = false;
            OnPropertyChanged(nameof(ObjectName));
            OnPropertyChanged(nameof(IsNew));
            OnPropertyChanged(nameof(Exists));

            var info = await _session.RunOnConnectionAsync((c, ct) => ProcedureCatalog.LoadAsync(c, schema, name, ct)).ConfigureAwait(true);
            if (info?.Definition is { } compiled)
            {
                try
                {
                    _history.SaveCompile(ProfileId, Database, schema, name, previous, compiled);
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                {
                    extra.Add(new SqlMessage(SqlMessageKind.Status, "Version History não gravado: " + ex.Message));
                }
            }
            if (info is not null) await ReloadTabsCoreAsync(info).ConfigureAwait(true);
            LoadHistory();

            IsModified = false;
            UpdateHeader();
            RaiseTitles();
            var inTran = _session.InTransaction;
            if (inTran) _compiledInTransaction = true;
            extra.Add(new SqlMessage(SqlMessageKind.Status,
                $"{ProcedureScriptBuilder.VerbText(verb)} PROCEDURE [{schema}].[{name}] concluído em {SqlEditorStatus.FormatElapsed(result.Elapsed)}." +
                (inTran ? " Transação ABERTA: Commit confirma, Rollback desfaz." : "")));
            ShowMessages(extra);
            StatusLine = inTran ? "Compilado dentro da transação aberta (Commit/Rollback)." : $"Compilado: [{schema}].[{name}].";

            if (inTran) _explorerRefreshPending = true;
            else await Shell.OnProcedureChangedAsync(ProfileId, Database, schema, name, created: wasNew || !sameObject).ConfigureAwait(true);
            return true;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
            RaiseTransaction();
        }
    }

    private static string FirstErrorText(IEnumerable<SqlMessage> messages) =>
        messages.FirstOrDefault(m => m.Kind == SqlMessageKind.Error)?.Text.Replace(Environment.NewLine, " ").Replace("\n", " ") ?? "";

    // ===== Execute =====

    private async Task ExecuteAsync()
    {
        if (IsBusy || !Exists) return;
        if (IsModified)
        {
            var answer = Ask("A procedure foi alterada e ainda não foi compilada.\n\nSim = compilar e executar   Não = executar a versão do servidor   Cancelar = voltar",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.Yes && !await CompileAsync().ConfigureAwait(true)) return;
        }

        var schema = _serverSchema!;
        var name = _serverName!;
        // Parameters of the server version: the grid when it is compiled, otherwise the server definition.
        IReadOnlyList<ProcedureParameter> ps;
        if (!IsModified && IsSplitMode) ps = Parameters.ToList();
        else
        {
            var info = await _session.RunOnConnectionAsync((c, ct) => ProcedureCatalog.LoadAsync(c, schema, name, ct)).ConfigureAwait(true);
            var parsed = info?.Definition is { } d ? ProcedureScriptParser.Parse(d) : null;
            ps = parsed?.Definition?.Parameters ?? (IReadOnlyList<ProcedureParameter>)Parameters.ToList();
        }

        var args = ProcedureExecuteWindow.Ask(Application.Current?.MainWindow, $"[{schema}].[{name}]", ps);
        if (args is null) { StatusLine = "Execute cancelado."; return; }

        var sql = ProcedureScriptBuilder.BuildExecuteBatch(schema, name, args);
        IsBusy = true;
        _cts = new CancellationTokenSource();
        _runClock.Restart();
        _clockTimer.Start();
        SqlExecutionResult result;
        try
        {
            result = await _session.ExecuteAsync(new SqlExecutionRequest { Sql = sql, MaxRows = _fetchRows, StopOnError = false }, _cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            result = new SqlExecutionResult();
            result.Messages.Add(new SqlMessage(SqlMessageKind.Error, ex.Message));
        }
        finally
        {
            _clockTimer.Stop();
            _runClock.Stop();
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
        }

        Results.Clear();
        var outputs = result.ResultSets.Count > 0 && result.ResultSets[^1].Columns.Count > 0 && result.ResultSets[^1].Columns[0].Name == "RETURN_VALUE"
            ? result.ResultSets[^1] : null;
        var i = 1;
        foreach (var set in result.ResultSets)
            Results.Add(ReferenceEquals(set, outputs) ? new SqlResultTab(i++, set, "Output / RETURN") : new SqlResultTab(i++, set));
        Messages.Clear();
        Messages.Add(new SqlMessage(SqlMessageKind.Status, "EXEC executado:" + Environment.NewLine + sql));
        foreach (var m in result.Messages) Messages.Add(m);
        if (outputs is { Rows.Count: > 0 })
        {
            var row = outputs.Rows[0];
            var parts = outputs.Columns.Select((c, k) => $"{c.Name} = {(row[k] is null ? "<null>" : Convert.ToString(row[k], System.Globalization.CultureInfo.CurrentCulture))}");
            Messages.Add(new SqlMessage(SqlMessageKind.Status, string.Join("   ", parts)));
        }
        var summary = SqlEditorStatus.Summarize(result);
        Messages.Add(new SqlMessage(SqlMessageKind.Status, summary));
        SelectedResultTab = result.HasErrors || result.Cancelled || Results.Count == 0 ? Results.Count : 0;
        StatusLine = summary;
        ResultsChanged?.Invoke();
        RaiseTransaction();
    }

    private void Cancel()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void ShowMessages(IEnumerable<SqlMessage> messages)
    {
        Results.Clear();
        Messages.Clear();
        foreach (var m in messages) Messages.Add(m);
        SelectedResultTab = 0;
        ResultsChanged?.Invoke();
    }

    // ===== Transactions =====

    private async Task SetAutocommitAsync(bool value)
    {
        try
        {
            if (value && _session.InTransaction)
            {
                var answer = Ask("Há uma transação aberta neste editor.\n\nSim = COMMIT   Não = ROLLBACK   Cancelar = continuar em modo transação",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                if (answer == MessageBoxResult.Cancel) return;
                if (answer == MessageBoxResult.Yes) await CommitAsync().ConfigureAwait(true);
                else await RollbackAsync().ConfigureAwait(true);
                if (_session.InTransaction) return;
            }
            _session.Autocommit = value;
            StatusLine = value ? "Autocommit ligado." : "Modo transação: o próximo Compile/Execute abre uma transação que fica aberta até Commit/Rollback.";
        }
        finally
        {
            Application.Current?.Dispatcher.BeginInvoke(RaiseTransaction, DispatcherPriority.Background);
        }
    }

    private async Task CommitAsync()
    {
        var r = await _session.CommitAsync().ConfigureAwait(true);
        AppendControl(r);
        if (!r.HasErrors && !_session.InTransaction)
        {
            _compiledInTransaction = false;
            await FlushExplorerRefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task RollbackAsync()
    {
        var r = await _session.RollbackAsync().ConfigureAwait(true);
        AppendControl(r);
        if (_session.InTransaction) return;
        var undoneCompile = _compiledInTransaction;
        _compiledInTransaction = false;
        if (undoneCompile)
        {
            // The server is back to the previous definition: the editor text is not compiled any more.
            var info = Exists
                ? await _session.RunOnConnectionAsync((c, ct) => ProcedureCatalog.LoadAsync(c, _serverSchema!, _serverName!, ct)).ConfigureAwait(true)
                : null;
            if (info is null && Exists)
            {
                // Created inside the rolled back transaction: it does not exist any more.
                _serverSchema = null;
                _serverName = null;
                OnPropertyChanged(nameof(IsNew));
                OnPropertyChanged(nameof(Exists));
            }
            else if (info is not null)
            {
                await ReloadTabsCoreAsync(info).ConfigureAwait(true);
            }
            IsModified = true;
            Messages.Add(new SqlMessage(SqlMessageKind.Status, "O compile foi desfeito no servidor; o texto do editor continua aqui (marcado como alterado)."));
            ResultsChanged?.Invoke();
        }
        _explorerRefreshPending = false;
    }

    private void AppendControl(SqlExecutionResult r)
    {
        foreach (var m in r.Messages) Messages.Add(m);
        StatusLine = r.Messages.LastOrDefault()?.Text.Replace(Environment.NewLine, " ") ?? "";
        ResultsChanged?.Invoke();
        RaiseTransaction();
    }

    private async Task FlushExplorerRefreshAsync()
    {
        if (!_explorerRefreshPending || !Exists) return;
        _explorerRefreshPending = false;
        await Shell.OnProcedureChangedAsync(ProfileId, Database, _serverSchema!, _serverName!, created: true).ConfigureAwait(true);
    }

    // ===== Description =====

    private async Task SaveDescriptionAsync()
    {
        if (!Exists) return;
        var sql = ProcedureScriptBuilder.BuildDescriptionStatement(_serverSchema!, _serverName!, null, Description);
        IsBusy = true;
        try
        {
            var r = await _session.ExecuteAsync(new SqlExecutionRequest { Sql = sql }).ConfigureAwait(true);
            if (r.HasErrors)
            {
                ShowMessages(r.Messages);
                StatusLine = "Descrição não gravada: " + FirstErrorText(r.Messages);
                return;
            }
            _savedDescription = Description;
            OnPropertyChanged(nameof(DescriptionModified));
            StatusLine = _session.InTransaction ? "Descrição gravada dentro da transação aberta (Commit/Rollback)." : "Descrição (MS_Description) gravada.";
        }
        finally
        {
            IsBusy = false;
            RaiseTransaction();
        }
        if (Exists) await ReloadTabsAsync().ConfigureAwait(true);
        if (!_session.InTransaction) Shell.UpdateProcedureDescription(ProfileId, Database, _serverSchema!, _serverName!, Description);
    }

    // ===== Version History =====

    private void LoadHistory()
    {
        HistoryEntries.Clear();
        if (string.IsNullOrWhiteSpace(ObjectName)) return;
        try
        {
            foreach (var e in _history.List(ProfileId, Database, _serverSchema ?? Schema, _serverName ?? ObjectName)) HistoryEntries.Add(e);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            StatusLine = "Version History indisponível: " + ex.Message;
        }
        OnPropertyChanged(nameof(HistoryFolder));
    }

    private void RestoreHistory()
    {
        if (SelectedHistory is not { } entry) return;
        string text;
        try { text = ObjectVersionHistoryStore.Read(entry); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Ask("Não foi possível ler a versão:\n\n" + ex.Message, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);
            return;
        }
        if (IsModified && Ask("Substituir o texto do editor (não compilado) por esta versão?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        var parsed = ProcedureScriptParser.Parse(text);
        if (parsed is { Success: true } && !parsed.Definition!.HeaderHasComments) ApplyDefinition(parsed.Definition, fromServer: false);
        else ApplyFullText(text);
        IsModified = true;
        SelectedTabIndex = 0;
        StatusLine = $"Versão de {entry.TimestampText} ({entry.KindText}) restaurada no editor. Compile (Ctrl+F9) para aplicar no servidor.";
        Host?.FocusEditor();
    }

    // ===== Clipboard / drop =====

    private void CopyName()
    {
        var text = ProcedureScriptBuilder.QualifiedName(Schema, ObjectName);
        Clipboard.SetText(text);
        StatusLine = $"Nome copiado: {text}";
    }

    private void ScriptToClipboard()
    {
        var text = CurrentScript(ProcedureVerb.CreateOrAlter);
        Clipboard.SetText(text);
        StatusLine = "Script (CREATE OR ALTER) do editor copiado para a área de transferência.";
    }

    private async Task DropAsync()
    {
        if (!Exists || IsBusy) return;
        var sql = ProcedureScriptBuilder.BuildDrop(_serverSchema!, _serverName!);
        if (Ask($"Excluir a procedure do database {Database}?\n\nSQL:\n{sql}\n\nEsta operação não pode ser desfeita (a não ser com Rollback, se a transação estiver aberta).",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        IsBusy = true;
        SqlExecutionResult r;
        try { r = await _session.ExecuteAsync(new SqlExecutionRequest { Sql = sql }).ConfigureAwait(true); }
        finally { IsBusy = false; RaiseTransaction(); }
        if (r.HasErrors)
        {
            ShowMessages(r.Messages);
            StatusLine = "DROP falhou: " + FirstErrorText(r.Messages);
            return;
        }
        var (schema, name) = (_serverSchema!, _serverName!);
        _serverSchema = null;
        _serverName = null;
        IsModified = true;
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(Exists));
        StatusLine = $"[{schema}].[{name}] excluída. O texto continua no editor (Compile recria).";
        ShowMessages(new[] { new SqlMessage(SqlMessageKind.Status, sql), new SqlMessage(SqlMessageKind.Status, StatusLine) });
        if (!_session.InTransaction) await Shell.OnProcedureChangedAsync(ProfileId, Database, schema, name, created: true).ConfigureAwait(true);
        else _explorerRefreshPending = true;
    }

    // ===== Document lifecycle =====

    public override bool HasUnsavedChanges => IsBusy || _session.InTransaction || IsModified;

    public override async Task<bool> ConfirmCloseAsync()
    {
        if (IsBusy)
        {
            if (Ask("Há uma operação em andamento neste editor.\n\nCancelar e fechar?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return false;
            Cancel();
            return false;
        }

        if (IsModified)
        {
            var answer = Ask($"A procedure [{Schema}].[{ObjectName}] foi alterada e não foi compilada.\n\nSim = compilar   Não = descartar as alterações   Cancelar = não fechar",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.Yes && !await CompileAsync().ConfigureAwait(true)) return false;
        }

        if (_session.InTransaction)
        {
            var answer = Ask($"'{ShortName.TrimEnd(' ', '*')}' tem uma transação aberta (@@TRANCOUNT = {_session.TranCount}).\n\nSim = COMMIT   Não = ROLLBACK   Cancelar = não fechar",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.Yes) await CommitAsync().ConfigureAwait(true);
            else await RollbackAsync().ConfigureAwait(true);
            if (_session.InTransaction) return false;
        }
        return true;
    }

    public override void OnDeactivated() => Host?.CommitParameterEdit();

    public override void OnClosed()
    {
        _analyzeTimer.Stop();
        _clockTimer.Stop();
        Cancel();
        _session.Dispose();
        Host = null;
    }

    private static MessageBoxResult Ask(string text, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        var owner = Application.Current?.MainWindow;
        return owner is not null
            ? MessageBox.Show(owner, text, "SqlExpert - Procedure", buttons, image, defaultResult)
            : MessageBox.Show(text, "SqlExpert - Procedure", buttons, image, defaultResult);
    }
}
