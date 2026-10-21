using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SqlExpert.App.Commands;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;

namespace SqlExpert.App.ViewModels;

/// <summary>Column of the data grid. <see cref="Field"/> is the internal DataTable column name (c0, c1, …).</summary>
public sealed record GridColumnSpec(string Field, string Header, Type DataType, bool IsReadOnly, bool IsNullable, DataColumnInfo Info)
{
    /// <summary>Foreign-key lookup of this column (dropdown "Data of [table]"); null when the column is not in a FK.</summary>
    public ForeignKeyLookupTarget? Lookup { get; init; }

    /// <summary>True when editing this cell can open the FK lookup popup.</summary>
    public bool HasEditableLookup => Lookup is { IsEnabled: true };
}

/// <summary>
/// classic-style "Data" tab: SELECT TOP (n) into an editable grid, navigator, Post/Cancel of pending edits.
/// All database I/O goes through <see cref="ITableDataService"/> off the UI thread.
/// </summary>
public sealed class TableDataViewModel : INotifyPropertyChanged
{
    private readonly ITableDataService _service;
    private readonly Action<string> _setStatus;

    private string? _profileId;
    private string? _database;
    private string? _schemaName;
    private string? _tableName;
    private TableDataSchema? _schema;
    private DataTable? _table;
    private DataView? _view;
    private IReadOnlyList<GridColumnSpec> _columns = Array.Empty<GridColumnSpec>();
    private int _topRows = 1000;
    private bool _isLoaded;
    private bool _isBusy;
    private bool _isReadOnly;
    private bool _isViewActive;
    private int _currentIndex = -1;
    private string _info = "";
    private int _loadVersion;
    private int _lookupFetchRows = UserSettings.DefaultSqlFetchRows;
    /// <summary>FK lookup rows per constraint name, kept while the table editor is open (cleared on reload).</summary>
    private readonly Dictionary<string, LookupData> _lookupCache = new(StringComparer.Ordinal);

    public TableDataViewModel(ITableDataService service, Action<string> setStatus)
    {
        _service = service;
        _setStatus = setStatus;

        FirstCommand = new RelayCommand(_ => CurrentIndex = 0, _ => RowCount > 0 && CurrentIndex != 0);
        PriorCommand = new RelayCommand(_ => CurrentIndex = Math.Max(0, CurrentIndex - 1), _ => RowCount > 0 && CurrentIndex > 0);
        NextCommand = new RelayCommand(_ => CurrentIndex = Math.Min(RowCount - 1, CurrentIndex + 1), _ => RowCount > 0 && CurrentIndex < RowCount - 1);
        LastCommand = new RelayCommand(_ => CurrentIndex = RowCount - 1, _ => RowCount > 0 && CurrentIndex != RowCount - 1);
        InsertCommand = new RelayCommand(_ => Insert(), _ => CanModify);
        DeleteCommand = new RelayCommand(_ => RunSafe(DeleteCurrentAsync), _ => CanModify && CurrentRowView is not null);
        EditCommand = new RelayCommand(_ => EditRequested?.Invoke(), _ => CanModify && CurrentRowView is not null);
        PostCommand = new RelayCommand(_ => RunSafe(async () => await PostAsync().ConfigureAwait(true)), _ => _table is not null && !IsBusy && !IsReadOnly);
        CancelCommand = new RelayCommand(_ => CancelChanges(), _ => _table is not null && !IsBusy);
        RefreshCommand = new RelayCommand(_ => RunSafe(RefreshAsync), _ => _tableName is not null && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Columns/rows were replaced: the view rebuilds the grid columns.</summary>
    public event Action? DataReloaded;
    /// <summary>Ask the view to commit the cell/row being edited into the DataTable.</summary>
    public event Action? CommitEditRequested;
    /// <summary>Ask the view to abandon the cell/row being edited.</summary>
    public event Action? CancelEditRequested;
    /// <summary>Ask the view to begin editing the current row (Edit button / after Insert).</summary>
    public event Action? EditRequested;

    public ICommand FirstCommand { get; }
    public ICommand PriorCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand LastCommand { get; }
    public ICommand InsertCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand PostCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshCommand { get; }

    public DataView? View
    {
        get => _view;
        private set { _view = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<GridColumnSpec> Columns => _columns;

    /// <summary>Rows to fetch with SELECT TOP (n).</summary>
    public int TopRows
    {
        get => _topRows;
        set { _topRows = value <= 0 ? 1000 : Math.Min(value, 1_000_000); OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>True when the table has no primary key (or nothing to edit).</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        private set { _isReadOnly = value; OnPropertyChanged(); }
    }

    public bool CanModify => _table is not null && !IsBusy && !IsReadOnly;

    public string Info
    {
        get => _info;
        private set { _info = value; OnPropertyChanged(); }
    }

    public int RowCount => _view?.Count ?? 0;

    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            var v = RowCount == 0 ? -1 : Math.Clamp(value, -1, RowCount - 1);
            if (_currentIndex == v) return;
            _currentIndex = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecordText));
            OnPropertyChanged(nameof(CurrentRowView));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public DataRowView? CurrentRowView =>
        _view is not null && _currentIndex >= 0 && _currentIndex < _view.Count ? _view[_currentIndex] : null;

    public string RecordText
    {
        get
        {
            var pending = HasPendingChanges ? "   (alterações pendentes — Post/Cancel)" : "";
            return RowCount == 0
                ? "Record 0 of 0" + pending
                : $"Record {Math.Max(0, _currentIndex) + 1} of {RowCount}" + pending;
        }
    }

    public bool HasPendingChanges
    {
        get
        {
            if (_table is null) return false;
            foreach (DataRow row in _table.Rows)
            {
                if (row.RowState is DataRowState.Added or DataRowState.Deleted) return true;
                if (row.RowState == DataRowState.Modified && ChangedColumns(row).Count > 0) return true;
            }
            return false;
        }
    }

    /// <summary>Table schema of the loaded data (null until loaded).</summary>
    public TableDataSchema? Schema => _schema;

    /// <summary>Raised after rows change (edit, insert, delete, post, reload): the print preview becomes stale.</summary>
    public event Action? DataChanged;

    public string? TableTitle => _tableName is null ? null : $"[{_schemaName}].[{_tableName}]";

    /// <summary>Set by the view: true while the Data tab is visible. Loads lazily on first activation.</summary>
    public bool IsViewActive
    {
        get => _isViewActive;
        set
        {
            _isViewActive = value;
            if (value && !_isLoaded && _tableName is not null && !IsBusy)
                RunSafe(LoadAsync);
        }
    }

    /// <summary>Called when a table is opened in the editor. Data is only read when the Data tab is shown.</summary>
    public void SetTable(string profileId, string database, string schema, string table)
    {
        _profileId = profileId;
        _database = database;
        _schemaName = schema;
        _tableName = table;
        ResetData();
        OnPropertyChanged(nameof(TableTitle));
        if (_isViewActive)
            RunSafe(LoadAsync);
    }

    public void Clear()
    {
        _profileId = _database = _schemaName = _tableName = null;
        ResetData();
        OnPropertyChanged(nameof(TableTitle));
    }

    private void ResetData()
    {
        _loadVersion++;
        _isLoaded = false;
        _lookupCache.Clear();
        _schema = null;
        DetachTable();
        _table = null;
        _columns = Array.Empty<GridColumnSpec>();
        View = null;
        IsReadOnly = false;
        Info = "";
        _currentIndex = -1;
        RaiseCounters();
        DataReloaded?.Invoke();
    }

    /// <summary>Pushes an in-progress grid cell/row edit into the DataTable (keeps it pending).</summary>
    public void CommitPendingEdit() => CommitEditRequested?.Invoke();

    /// <summary>
    /// Before leaving the table (opening another one, closing, disconnecting): asks what to do with pending changes.
    /// Returns false when the user cancels.
    /// </summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        CommitEditRequested?.Invoke();
        if (!HasPendingChanges) return true;

        var answer = MessageBox.Show(
            $"Existem alterações não gravadas em {TableTitle}.\n\nGravar (Post) agora?\n\nSim = gravar   Não = descartar   Cancelar = continuar editando",
            "SqlExpert - Data",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        switch (answer)
        {
            case MessageBoxResult.Yes:
                return await PostAsync().ConfigureAwait(true);
            case MessageBoxResult.No:
                CancelChanges();
                return true;
            default:
                return false;
        }
    }

    private async Task RefreshAsync()
    {
        CommitEditRequested?.Invoke();
        if (HasPendingChanges)
        {
            var answer = MessageBox.Show("Descartar as alterações não gravadas e recarregar os dados?",
                "SqlExpert - Data", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            CancelEditRequested?.Invoke();
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        if (_profileId is null || _database is null || _schemaName is null || _tableName is null) return;
        var profileId = _profileId; var database = _database; var schemaName = _schemaName; var tableName = _tableName;
        var version = ++_loadVersion;
        var top = TopRows;
        _lookupCache.Clear();

        IsBusy = true;
        _setStatus($"Lendo dados de [{schemaName}].[{tableName}] (TOP {top})...");
        try
        {
            var (schema, page) = await Task.Run(async () =>
            {
                var s = await _service.GetSchemaAsync(profileId, database, schemaName, tableName).ConfigureAwait(false);
                var p = await _service.LoadAsync(profileId, database, s, top).ConfigureAwait(false);
                return (s, p);
            }).ConfigureAwait(true);

            if (version != _loadVersion) return; // another table was opened meanwhile

            ApplyPage(schema, page);
            _isLoaded = true;
            var limited = page.Rows.Count >= top ? $" (limitado a TOP {top})" : "";
            _setStatus(IsReadOnly
                ? $"[{schemaName}].[{tableName}]: {page.Rows.Count} registro(s){limited}. Tabela sem chave primária — dados somente leitura."
                : $"[{schemaName}].[{tableName}]: {page.Rows.Count} registro(s){limited}.");
        }
        catch (Exception ex)
        {
            if (version != _loadVersion) return;
            _setStatus("Erro ao ler dados: " + ex.Message);
            App.ReportError(ex);
        }
        finally
        {
            if (version == _loadVersion) IsBusy = false;
        }
    }

    private void ApplyPage(TableDataSchema schema, TableDataPage page)
    {
        DetachTable();
        _schema = schema;
        var ordered = schema.Columns.OrderBy(c => c.Ordinal).ToList();

        var table = new DataTable(schema.Table) { Locale = System.Globalization.CultureInfo.CurrentCulture };
        var specs = new List<GridColumnSpec>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var info = ordered[i];
            var type = i < page.FieldTypes.Count ? page.FieldTypes[i] : typeof(object);
            var field = "c" + i;
            table.Columns.Add(new DataColumn(field, type) { Caption = info.Name, AllowDBNull = true });
            var readOnlyType = type == typeof(byte[]) || type == typeof(object);
            specs.Add(new GridColumnSpec(field, info.Name, type, !info.IsWritable || readOnlyType, info.IsNullable, info));
        }

        table.BeginLoadData();
        foreach (var values in page.Rows)
            table.LoadDataRow(values, LoadOption.OverwriteChanges);
        table.EndLoadData();
        table.AcceptChanges();

        // FK lookups: a dropdown on every column of a usable FK (multi-column FKs need all their columns editable here).
        var tableReadOnly = !schema.HasPrimaryKey;
        var targets = ForeignKeyLookup.ResolveTargets(schema.ForeignKeys,
            specs.Select(sp => new LookupLocalColumn(sp.Header, !sp.IsReadOnly && !tableReadOnly, sp.IsNullable)).ToList());
        specs = specs.Select(sp => targets.TryGetValue(sp.Header, out var t) ? sp with { Lookup = t } : sp).ToList();

        _table = table;
        _columns = specs;
        IsReadOnly = tableReadOnly;
        Info = IsReadOnly ? "Tabela sem chave primária: somente leitura." : "";

        var view = new DataView(table) { AllowNew = false, AllowDelete = true, AllowEdit = !IsReadOnly };
        view.ListChanged += OnViewListChanged;
        table.RowChanged += OnRowChanged;
        View = view;
        _currentIndex = view.Count > 0 ? 0 : -1;
        RaiseCounters();
        DataReloaded?.Invoke();
    }

    private void DetachTable()
    {
        if (_view is not null) _view.ListChanged -= OnViewListChanged;
        if (_table is not null) _table.RowChanged -= OnRowChanged;
    }

    private void OnViewListChanged(object? sender, ListChangedEventArgs e) => RaiseCounters();
    private void OnRowChanged(object? sender, DataRowChangeEventArgs e) => RaiseCounters();

    private void RaiseCounters()
    {
        DataChanged?.Invoke();
        if (_currentIndex >= RowCount) _currentIndex = RowCount - 1;
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(CurrentRowView));
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(RecordText));
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(CanModify));
        CommandManager.InvalidateRequerySuggested();
    }

    private void Insert()
    {
        if (_table is null || IsReadOnly) return;
        CommitEditRequested?.Invoke();
        var row = _table.NewRow();
        _table.Rows.Add(row);
        var idx = RowCount - 1;
        for (var i = 0; i < RowCount; i++)
            if (ReferenceEquals(_view![i].Row, row)) { idx = i; break; }
        CurrentIndex = idx;
        RaiseCounters();
        EditRequested?.Invoke();
    }

    private async Task DeleteCurrentAsync()
    {
        if (_table is null || _schema is null || IsReadOnly) return;
        CommitEditRequested?.Invoke();
        var rowView = CurrentRowView;
        if (rowView is null) return;
        var row = rowView.Row;

        if (MessageBox.Show("Excluir o registro atual?", "SqlExpert - Data",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        if (row.RowState == DataRowState.Added)
        {
            // Never posted: just drop it.
            _table.Rows.Remove(row);
            RaiseCounters();
            _setStatus("Registro novo descartado.");
            return;
        }

        var change = new RowChange { Kind = RowChangeKind.Delete, Key = KeyValues(row, DataRowVersion.Original) };
        IsBusy = true;
        try
        {
            var (profileId, database, schema) = (_profileId!, _database!, _schema);
            await Task.Run(() => _service.ApplyChangesAsync(profileId, database, schema, new[] { change })).ConfigureAwait(true);
            _table.Rows.Remove(row);
            RaiseCounters();
            _setStatus("Registro excluído.");
        }
        catch (Exception ex)
        {
            _setStatus("Erro ao excluir: " + ex.Message);
            App.ReportError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Persists every pending insert/update in one transaction. Returns false on error (edits are kept).</summary>
    public async Task<bool> PostAsync()
    {
        if (_table is null || _schema is null) return true;
        CommitEditRequested?.Invoke();
        if (IsReadOnly) return true;

        var pending = new List<(DataRow Row, RowChange Change)>();
        var noOp = new List<DataRow>();
        foreach (DataRow row in _table.Rows)
        {
            switch (row.RowState)
            {
                case DataRowState.Added:
                    pending.Add((row, new RowChange { Kind = RowChangeKind.Insert, Values = AllValues(row) }));
                    break;
                case DataRowState.Modified:
                {
                    var changed = ChangedColumns(row);
                    if (changed.Count == 0) { noOp.Add(row); break; }
                    pending.Add((row, new RowChange
                    {
                        Kind = RowChangeKind.Update,
                        Values = changed,
                        Key = KeyValues(row, DataRowVersion.Original)
                    }));
                    break;
                }
            }
        }

        foreach (var r in noOp) r.AcceptChanges();
        if (pending.Count == 0)
        {
            RaiseCounters();
            _setStatus("Nada a gravar.");
            return true;
        }

        IsBusy = true;
        try
        {
            var (profileId, database, schema) = (_profileId!, _database!, _schema);
            var changes = pending.Select(p => p.Change).ToList();
            var results = await Task.Run(() => _service.ApplyChangesAsync(profileId, database, schema, changes)).ConfigureAwait(true);

            var needReload = false;
            for (var i = 0; i < pending.Count; i++)
            {
                var row = pending[i].Row;
                var fresh = i < results.Count ? results[i] : null;
                if (fresh is not null && fresh.Length == _table.Columns.Count)
                {
                    for (var c = 0; c < fresh.Length; c++)
                        row[c] = fresh[c] ?? DBNull.Value;
                }
                else if (pending[i].Change.Kind == RowChangeKind.Insert)
                {
                    needReload = true;
                }
                row.AcceptChanges();
            }

            RaiseCounters();
            _setStatus($"Post: {pending.Count} registro(s) gravado(s).");
            if (needReload)
                await LoadAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            _setStatus("Erro ao gravar (nada foi gravado; alterações mantidas): " + ex.Message);
            App.ReportError(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelChanges()
    {
        CancelEditRequested?.Invoke();
        if (_table is null) return;
        _table.RejectChanges();
        RaiseCounters();
        _setStatus("Alterações canceladas.");
    }

    private IReadOnlyDictionary<string, object?> AllValues(DataRow row)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in _columns)
            d[spec.Info.Name] = row[spec.Field];
        return d;
    }

    private Dictionary<string, object?> ChangedColumns(DataRow row)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!row.HasVersion(DataRowVersion.Original) || !row.HasVersion(DataRowVersion.Current))
            return d;
        foreach (var spec in _columns)
        {
            var o = row[spec.Field, DataRowVersion.Original];
            var n = row[spec.Field, DataRowVersion.Current];
            if (!ValuesEqual(o, n))
                d[spec.Info.Name] = n;
        }
        return d;
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is byte[] x && b is byte[] y) return x.AsSpan().SequenceEqual(y);
        return Equals(a, b);
    }

    private IReadOnlyDictionary<string, object?> KeyValues(DataRow row, DataRowVersion version)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var v = row.HasVersion(version) ? version : DataRowVersion.Current;
        foreach (var spec in _columns.Where(c => c.Info.IsPrimaryKey))
            d[spec.Info.Name] = row[spec.Field, v];
        return d;
    }

    // ===== Foreign-key lookup =====

    /// <summary>Rows fetched by the lookup popup (TOP n): the registration's SQL Editor fetch count, else the global one.</summary>
    public int LookupFetchRows
    {
        get => _lookupFetchRows;
        set => _lookupFetchRows = UserSettings.ClampFetchRows(value);
    }

    /// <summary>Current FK value(s) of <paramref name="row"/> in FK column order (DBNull for NULL).</summary>
    public IReadOnlyList<object?> CurrentLookupKey(DataRowView row, ForeignKeyLookupTarget target)
    {
        var key = new List<object?>();
        foreach (var name in target.LocalColumns)
        {
            var spec = ColumnByName(name);
            object? v = null;
            try { v = spec is null ? null : row[spec.Field]; }
            catch (Exception) { /* row detached */ }
            key.Add(v ?? DBNull.Value);
        }
        return key;
    }

    /// <summary>
    /// Referenced rows for the popup. Cached per FK while the editor is open; re-read when <paramref name="refresh"/>,
    /// when "Fetch all" asks for more than the cache has, or when the current value is not in an incomplete cache.
    /// </summary>
    public async Task<LookupData> GetLookupDataAsync(
        ForeignKeyLookupTarget target, bool fetchAll, bool refresh, IReadOnlyList<object?>? currentKey, CancellationToken ct)
    {
        if (_profileId is null || _database is null)
            throw new InvalidOperationException("Nenhuma tabela aberta.");
        var fk = target.ForeignKey;
        if (!refresh && _lookupCache.TryGetValue(fk.Name, out var cached)
            && (cached.IsComplete || !fetchAll)
            && (cached.IsComplete || currentKey is null || currentKey.Any(DataValueText.IsNull) || ForeignKeyLookup.FindRowIndex(cached.Rows, currentKey) >= 0))
            return cached;

        var (profileId, database, version) = (_profileId, _database, _loadVersion);
        var top = fetchAll ? 0 : LookupFetchRows;
        var data = await Task.Run(() => _service.GetLookupDataAsync(profileId, database, fk, top, currentKey, ct), ct).ConfigureAwait(true);
        if (version == _loadVersion) _lookupCache[fk.Name] = data;
        return data;
    }

    /// <summary>
    /// Puts the picked referenced key (null = "(null)") into the FK column(s) of <paramref name="row"/>.
    /// The row only changes in memory: Post/Cancel decide as for typed values. Returns false on error.
    /// </summary>
    public bool ApplyLookup(DataRowView row, ForeignKeyLookupTarget target, IReadOnlyList<object?>? pickedKey)
    {
        if (IsReadOnly || !target.IsEnabled) return false;
        try
        {
            for (var i = 0; i < target.LocalColumns.Count; i++)
            {
                var spec = ColumnByName(target.LocalColumns[i])
                           ?? throw new InvalidOperationException($"Coluna {target.LocalColumns[i]} não está na grade.");
                var value = pickedKey is null ? DBNull.Value : ForeignKeyLookup.CoerceValue(pickedKey[i], spec.DataType);
                if (!ForeignKeyLookup.KeyEquals(row[spec.Field], value))
                    row[spec.Field] = value;
            }
            RaiseCounters();
            _setStatus(pickedKey is null
                ? $"{string.Join(", ", target.LocalColumns)} = NULL (pendente — Post/Cancel)."
                : $"{string.Join(", ", target.LocalColumns)} = {string.Join(", ", pickedKey.Select(v => DataValueText.FormatCell(v, System.Globalization.CultureInfo.CurrentCulture)))} (pendente — Post/Cancel).");
            return true;
        }
        catch (Exception ex)
        {
            _setStatus("Erro ao aplicar o valor escolhido: " + ex.Message);
            App.ReportError(ex);
            return false;
        }
    }

    /// <summary>
    /// Memo window edit: writes <paramref name="value"/> into one cell of <paramref name="row"/> (in memory; Post/Cancel
    /// decide). Nothing is written when the value is the same. Returns true when the row changed.
    /// </summary>
    public bool SetCellValue(DataRowView row, GridColumnSpec spec, object value)
    {
        if (IsReadOnly || spec.IsReadOnly) return false;
        try
        {
            var current = row[spec.Field];
            if (Equals(current, value) || (DataValueText.IsNull(current) && DataValueText.IsNull(value))) return false;
            row[spec.Field] = value;
            RaiseCounters();
            _setStatus($"{spec.Header} alterado (pendente - Post/Cancel).");
            return true;
        }
        catch (Exception ex)
        {
            _setStatus("Erro ao aplicar o texto: " + ex.Message);
            App.ReportError(ex);
            return false;
        }
    }

    /// <summary>
    /// Explicit "set NULL" (Ctrl+0) of one cell of <paramref name="row"/>. The row only changes in memory (Post/Cancel
    /// decide); an already-NULL cell is not touched. Returns true when the value was set to NULL.
    /// </summary>
    public bool SetNull(DataRowView row, GridColumnSpec spec, bool columnReadOnly)
    {
        try
        {
            var result = CellNullEditing.TrySetNull(row.Row, spec.Field, spec.IsNullable, columnReadOnly || spec.IsReadOnly || IsReadOnly);
            _setStatus(result switch
            {
                CellNullEditing.Result.SetToNull => $"{spec.Header} = NULL (pendente - Post/Cancel).",
                CellNullEditing.Result.AlreadyNull => $"{spec.Header} já é NULL.",
                CellNullEditing.Result.NotNullable => $"{spec.Header} é NOT NULL: não aceita NULL.",
                _ => $"{spec.Header} é somente leitura."
            });
            if (result != CellNullEditing.Result.SetToNull) return false;
            RaiseCounters();
            return true;
        }
        catch (Exception ex)
        {
            _setStatus("Erro ao gravar NULL: " + ex.Message);
            App.ReportError(ex);
            return false;
        }
    }

    private GridColumnSpec? ColumnByName(string name) =>
        _columns.FirstOrDefault(c => string.Equals(c.Header, name, StringComparison.OrdinalIgnoreCase));

    private static async void RunSafe(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { App.ReportError(ex); }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
