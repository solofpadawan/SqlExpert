using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;
using SqlExpert.App.Commands;
using SqlExpert.App.Services;
using SqlExpert.Core.Documents;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.App.ViewModels;

/// <summary>Editor-side operations the view model needs from the view (selection, caret, search UI).</summary>
public interface ISqlEditorHost
{
    /// <summary>Selected text and the editor line it starts on, or the whole text and 1.</summary>
    (string Text, int StartLine) GetExecutionText();
    void GoToLine(int line, bool markError);
    void InsertAtCaret(string text);
    void FocusEditor();
    void ShowFind();
    void FindNext(bool backward);
    void ShowReplace();
}

/// <summary>One result-set tab: a read-only DataView with columns c0..cN and the real column names.</summary>
public sealed class SqlResultTab
{
    public SqlResultTab(int index, SqlResultSet set, string? header = null)
    {
        Index = index;
        Set = set;
        Header = header ?? SqlEditorStatus.ResultHeader(index, set);
        var table = new DataTable($"Result{index}");
        for (var i = 0; i < set.Columns.Count; i++)
        {
            var t = set.Columns[i].ClrType;
            table.Columns.Add("c" + i, IsSimple(t) ? t : typeof(object));
        }
        table.BeginLoadData();
        foreach (var row in set.Rows)
        {
            var values = new object[row.Length];
            for (var i = 0; i < row.Length; i++) values[i] = row[i] ?? DBNull.Value;
            table.Rows.Add(values);
        }
        table.EndLoadData();
        table.AcceptChanges();
        View = table.DefaultView;
    }

    private static bool IsSimple(Type t) =>
        t.IsPrimitive || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
        || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[]);

    public int Index { get; }
    public SqlResultSet Set { get; }
    public string Header { get; }
    public DataView View { get; }
    public IReadOnlyList<SqlResultColumn> Columns => Set.Columns;
}

/// <summary>
/// SQL Editor document (F12): AvalonEdit text, execution on its own session (connection kept open),
/// one tab per result set + Messages, Fetch first N / Fetch all, autocommit or explicit transaction with
/// Commit/Rollback, .sql load/save and per-connection history.
/// </summary>
public sealed class SqlEditorDocumentViewModel : DocumentViewModel
{
    private readonly SqlEditorSession _session;
    private readonly QueryHistoryStore _historyStore;
    private readonly string _connectionLabel;
    private readonly DispatcherTimer _timer;
    private readonly System.Diagnostics.Stopwatch _runClock = new();
    private CancellationTokenSource? _cts;
    private Task? _running;
    private SqlExecutionRequest? _lastRequest;
    private bool _lastHasMore;
    private bool _isExecuting;
    private bool _isModified;
    private string? _filePath;
    private Encoding? _fileEncoding;
    private string _statusLine = "Pronto. F9 ou Ctrl+Enter executa (a seleção, se houver).";
    private string _caretText = "1 : 1";
    private int _selectedResultTab;
    private int _selectedPage;
    private int _fetchRows;
    private QueryHistoryEntry? _selectedHistory;

    public SqlEditorDocumentViewModel(MainViewModel shell, string profileId, string database, int number, string connectionLabel,
        SqlEditorSession session, QueryHistoryStore historyStore, int? fetchRows = null)
        : base(shell, DocumentKeys.SqlEditor(profileId, number), profileId, database)
    {
        Number = number;
        _connectionLabel = connectionLabel;
        _session = session;
        _historyStore = historyStore;
        // Registration default (Database Properties > Additional > SQL Editor), else the global setting.
        _fetchRows = fetchRows is { } rows ? Core.Models.UserSettings.ClampFetchRows(rows) : UiSettings.Instance.SqlFetchRows;
        Document.UndoStack.PropertyChanged += OnUndoStackChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => StatusLine = $"Executando... {SqlEditorStatus.FormatElapsed(_runClock.Elapsed)}  (Stop / Ctrl+Break cancela)";

        ExecuteCommand = new RelayCommand(_ => Run(() => ExecuteAsync(null)), _ => !IsExecuting);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsExecuting);
        CommitCommand = new RelayCommand(_ => Run(CommitAsync), _ => !IsExecuting && InTransaction);
        RollbackCommand = new RelayCommand(_ => Run(RollbackAsync), _ => !IsExecuting && InTransaction);
        FetchAllCommand = new RelayCommand(_ => Run(FetchAllAsync), _ => !IsExecuting && _lastHasMore && _lastRequest is not null);
        OpenCommand = new RelayCommand(_ => Run(OpenFileAsync), _ => !IsExecuting);
        SaveCommand = new RelayCommand(_ => Save(saveAs: false));
        SaveAsCommand = new RelayCommand(_ => Save(saveAs: true));
        InsertHistoryCommand = new RelayCommand(p => UseHistory(p, replace: false), p => (p as QueryHistoryEntry ?? SelectedHistory) is not null);
        ReplaceWithHistoryCommand = new RelayCommand(p => UseHistory(p, replace: true), p => (p as QueryHistoryEntry ?? SelectedHistory) is not null);
        CopyHistoryCommand = new RelayCommand(p => { if ((p as QueryHistoryEntry ?? SelectedHistory) is { } e) Clipboard.SetText(e.Sql); },
            p => (p as QueryHistoryEntry ?? SelectedHistory) is not null);

        try
        {
            foreach (var e in _historyStore.Load(profileId)) History.Add(e);
        }
        catch (Exception ex)
        {
            StatusLine = "Histórico indisponível: " + ex.Message;
        }
    }

    public int Number { get; }
    public TextDocument Document { get; } = new();
    public ISqlEditorHost? Host { get; set; }

    public string? FilePath => _filePath;
    public string? FileName => _filePath is null ? null : Path.GetFileName(_filePath);

    public override string Caption => WindowTitles.SqlEditorCaption(Number, FileName);
    public override string Title => $"{Caption}{(IsModified ? " *" : "")} : {_connectionLabel} : {CurrentDatabase}";
    public override string ShortName => (Number <= 1 ? "SQL Editor" : $"SQL Editor {Number}") + (IsModified ? " *" : "");
    public override string ToolTip => (_filePath is null ? "" : _filePath + "\n") + $"{_connectionLabel} : {CurrentDatabase}";
    public override string CurrentDatabase => _session.CurrentDatabase ?? Database;

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

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            _isExecuting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsIdle));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsIdle => !_isExecuting;

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

    /// <summary>Rows kept per result set ("first N"). The value is remembered for new editors.</summary>
    public int FetchRows
    {
        get => _fetchRows;
        set
        {
            var v = Core.Models.UserSettings.ClampFetchRows(value);
            _fetchRows = v;
            UiSettings.Instance.SqlFetchRows = v;
            OnPropertyChanged();
        }
    }

    /// <summary>Checkbox: true = autocommit; false = explicit transaction kept open until Commit/Rollback.</summary>
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

    public ObservableCollection<SqlResultTab> Results { get; } = new();
    public ObservableCollection<SqlMessage> Messages { get; } = new();
    public ObservableCollection<QueryHistoryEntry> History { get; } = new();

    /// <summary>Selected bottom tab: 0..Results.Count-1 = result sets, Results.Count = Messages.</summary>
    public int SelectedResultTab
    {
        get => _selectedResultTab;
        set { _selectedResultTab = value; OnPropertyChanged(); }
    }

    /// <summary>Edit (0) / History (1) page.</summary>
    public int SelectedPage
    {
        get => _selectedPage;
        set { _selectedPage = value; OnPropertyChanged(); }
    }

    public QueryHistoryEntry? SelectedHistory
    {
        get => _selectedHistory;
        set { _selectedHistory = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>Raised after Results/Messages were replaced (the view rebuilds the result tabs).</summary>
    public event Action? ResultsChanged;

    public ICommand ExecuteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CommitCommand { get; }
    public ICommand RollbackCommand { get; }
    public ICommand FetchAllCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand InsertHistoryCommand { get; }
    public ICommand ReplaceWithHistoryCommand { get; }
    public ICommand CopyHistoryCommand { get; }

    private static async void Run(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { App.ReportError(ex); }
    }

    private void OnUndoStackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UndoStack.IsOriginalFile))
            IsModified = !Document.UndoStack.IsOriginalFile;
    }

    private void RaiseTitles()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ShortName));
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(CurrentDatabase));
        Shell.RefreshWindowTitle();
    }

    private void RaiseTransaction()
    {
        OnPropertyChanged(nameof(Autocommit));
        OnPropertyChanged(nameof(InTransaction));
        OnPropertyChanged(nameof(TransactionText));
        CommandManager.InvalidateRequerySuggested();
    }

    // ===== Execute / Cancel / Fetch all =====

    public void Execute()
    {
        if (ExecuteCommand.CanExecute(null)) ExecuteCommand.Execute(null);
    }

    public void Cancel()
    {
        if (!IsExecuting) return;
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        StatusLine = "Cancelando...";
    }

    private Task ExecuteAsync(SqlExecutionRequest? rerun)
    {
        var task = ExecuteCoreAsync(rerun);
        _running = task;
        return task;
    }

    private async Task ExecuteCoreAsync(SqlExecutionRequest? rerun)
    {
        if (IsExecuting) return;
        string text;
        int startLine;
        if (rerun is not null)
        {
            text = rerun.Sql;
            startLine = rerun.StartLine;
        }
        else
        {
            (text, startLine) = Host?.GetExecutionText() ?? (Document.Text, 1);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            StatusLine = "Nada para executar.";
            return;
        }

        var request = new SqlExecutionRequest
        {
            Sql = text,
            StartLine = startLine,
            MaxRows = rerun is not null ? 0 : FetchRows
        };
        _lastRequest = rerun is not null ? new SqlExecutionRequest { Sql = text, StartLine = startLine, MaxRows = FetchRows } : request;

        _cts = new CancellationTokenSource();
        IsExecuting = true;
        _runClock.Restart();
        _timer.Start();
        StatusLine = "Executando...";
        SqlExecutionResult result;
        try
        {
            result = await _session.ExecuteAsync(request, _cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            result = new SqlExecutionResult { Elapsed = _runClock.Elapsed };
            result.Messages.Add(new SqlMessage(SqlMessageKind.Error, ex.Message));
        }
        finally
        {
            _timer.Stop();
            _runClock.Stop();
            _cts.Dispose();
            _cts = null;
            IsExecuting = false;
        }

        ShowResult(result);
        AddHistory(text, result);
    }

    private void ShowResult(SqlExecutionResult result)
    {
        Results.Clear();
        var i = 1;
        foreach (var set in result.ResultSets)
            Results.Add(new SqlResultTab(i++, set));

        Messages.Clear();
        foreach (var m in result.Messages)
            Messages.Add(m);
        var summary = SqlEditorStatus.Summarize(result);
        Messages.Add(new SqlMessage(SqlMessageKind.Status, summary));

        _lastHasMore = result.HasMoreRows;
        SelectedResultTab = result.HasErrors || result.Cancelled || Results.Count == 0 ? Results.Count : 0;
        StatusLine = summary;
        ResultsChanged?.Invoke();
        RaiseTransaction();
        RaiseTitles();

        if (result.HasErrors && result.FirstErrorLine is { } line)
            Host?.GoToLine(line, markError: true);
    }

    private void AddHistory(string text, SqlExecutionResult result)
    {
        try
        {
            var entry = new QueryHistoryEntry(text.Trim(), DateTimeOffset.Now, CurrentDatabase,
                (long)result.Elapsed.TotalMilliseconds, !result.HasErrors && !result.Cancelled);
            var list = _historyStore.Add(ProfileId, entry);
            History.Clear();
            foreach (var e in list) History.Add(e);
        }
        catch (Exception ex)
        {
            StatusLine += " · histórico não gravado: " + ex.Message;
        }
    }

    private async Task FetchAllAsync()
    {
        if (_lastRequest is null || IsExecuting) return;
        if (!SqlTextAnalyzer.IsReadOnlyQuery(_lastRequest.Sql))
        {
            var answer = Ask("Fetch all executa de novo o último texto executado, sem limite de registros.\n\n" +
                             "Ele contém comandos que não são apenas SELECT (INSERT/UPDATE/EXEC/SET…), que serão executados outra vez.\n\nContinuar?",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }
        await ExecuteAsync(_lastRequest).ConfigureAwait(true);
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
            StatusLine = value ? "Autocommit ligado." : "Modo transação: a transação abre na próxima execução e fica aberta até Commit/Rollback.";
        }
        finally
        {
            // Resync the checkbox after the binding update finished (cancel keeps the old state).
            Application.Current?.Dispatcher.BeginInvoke(RaiseTransaction, DispatcherPriority.Background);
        }
    }

    private async Task CommitAsync() => ShowControlResult(await _session.CommitAsync().ConfigureAwait(true));

    private async Task RollbackAsync() => ShowControlResult(await _session.RollbackAsync().ConfigureAwait(true));

    private void ShowControlResult(SqlExecutionResult r)
    {
        foreach (var m in r.Messages) Messages.Add(m);
        StatusLine = r.Messages.LastOrDefault()?.Text.Replace(Environment.NewLine, " ") ?? "";
        if (r.HasErrors) SelectedResultTab = Results.Count;
        ResultsChanged?.Invoke();
        RaiseTransaction();
    }

    // ===== Files =====

    private const string FileFilter = "SQL scripts (*.sql)|*.sql|Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*";

    private async Task OpenFileAsync()
    {
        if (!ConfirmDiscardOrSave("Abrir arquivo")) return;
        var dlg = new OpenFileDialog { Filter = FileFilter, Title = "Load from File", CheckFileExists = true };
        if (_filePath is not null) dlg.InitialDirectory = Path.GetDirectoryName(_filePath);
        if (dlg.ShowDialog(Application.Current?.MainWindow) != true) return;
        var (text, encoding) = await Task.Run(() => SqlFileText.Read(dlg.FileName)).ConfigureAwait(true);
        Document.Text = text;
        Document.UndoStack.ClearAll();
        Document.UndoStack.MarkAsOriginalFile();
        _filePath = dlg.FileName;
        _fileEncoding = encoding;
        IsModified = false;
        RaiseTitles();
        StatusLine = $"Carregado: {dlg.FileName} ({encoding.WebName}, {Document.LineCount} linhas).";
        Host?.FocusEditor();
    }

    /// <summary>Saves to the current file (or asks a name). Returns false when cancelled or failed.</summary>
    public bool Save(bool saveAs)
    {
        var path = _filePath;
        if (saveAs || path is null)
        {
            var dlg = new SaveFileDialog { Filter = FileFilter, Title = saveAs ? "Save as" : "Save to File", DefaultExt = ".sql", AddExtension = true, OverwritePrompt = true, FileName = FileName ?? "script.sql" };
            if (_filePath is not null) dlg.InitialDirectory = Path.GetDirectoryName(_filePath);
            if (dlg.ShowDialog(Application.Current?.MainWindow) != true) return false;
            path = dlg.FileName;
        }

        try
        {
            SqlFileText.Write(path, Document.Text, _filePath == path ? _fileEncoding : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ask("Não foi possível gravar o arquivo:\n\n" + ex.Message, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);
            return false;
        }

        if (_filePath != path) _fileEncoding = SqlFileText.DefaultEncoding;
        _filePath = path;
        Document.UndoStack.MarkAsOriginalFile();
        IsModified = false;
        RaiseTitles();
        StatusLine = "Gravado: " + path;
        return true;
    }

    /// <summary>Save/Discard/Cancel prompt for unsaved text. True = go on.</summary>
    private bool ConfirmDiscardOrSave(string action)
    {
        if (!IsModified || Document.TextLength == 0) return true;
        var answer = Ask($"O texto de '{ShortName.TrimEnd(' ', '*')}' foi alterado e não foi salvo.\n\nSalvar antes de continuar ({action})?\n\nSim = salvar   Não = descartar   Cancelar = voltar",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return answer switch
        {
            MessageBoxResult.Yes => Save(saveAs: false),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    // ===== History =====

    private void UseHistory(object? p, bool replace)
    {
        if ((p as QueryHistoryEntry ?? SelectedHistory) is not { } entry) return;
        if (replace)
            Document.Replace(0, Document.TextLength, entry.Sql);
        else
            Host?.InsertAtCaret(entry.Sql);
        SelectedPage = 0;
        Host?.FocusEditor();
        StatusLine = replace ? "Texto do editor substituído pelo histórico." : "Comando do histórico inserido.";
    }

    // ===== Document lifecycle =====

    public override bool HasUnsavedChanges => IsExecuting || _session.InTransaction || (IsModified && Document.TextLength > 0);

    public override async Task<bool> ConfirmCloseAsync()
    {
        if (IsExecuting)
        {
            if (Ask("Há uma execução em andamento neste editor.\n\nCancelar a execução e fechar?", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return false;
            Cancel();
            if (_running is { } running)
            {
                try { await running.ConfigureAwait(true); } catch { /* reported by Run */ }
            }
        }

        if (_session.InTransaction)
        {
            var answer = Ask($"'{ShortName.TrimEnd(' ', '*')}' tem uma transação aberta (@@TRANCOUNT = {_session.TranCount}).\n\nSim = COMMIT   Não = ROLLBACK   Cancelar = não fechar",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.Yes) await CommitAsync().ConfigureAwait(true);
            else await RollbackAsync().ConfigureAwait(true);
            if (_session.InTransaction) return false; // commit failed: keep it open so the user sees the error
        }

        return ConfirmDiscardOrSave("fechar");
    }

    public override void OnClosed()
    {
        _timer.Stop();
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        Document.UndoStack.PropertyChanged -= OnUndoStackChanged;
        _session.Dispose();
        Host = null;
    }

    private static MessageBoxResult Ask(string text, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        var owner = Application.Current?.MainWindow;
        return owner is not null
            ? MessageBox.Show(owner, text, "SqlExpert - SQL Editor", buttons, image, defaultResult)
            : MessageBox.Show(text, "SqlExpert - SQL Editor", buttons, image, defaultResult);
    }
}
