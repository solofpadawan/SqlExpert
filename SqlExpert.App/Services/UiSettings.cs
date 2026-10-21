using System.ComponentModel;
using System.Runtime.CompilerServices;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.App.Services;

/// <summary>
/// Shared, observable UI settings (one instance for the whole app). Every Data grid binds to
/// <see cref="DataGridFontSizePx"/>, so a change applies to all open and future grids; it is saved immediately.
/// </summary>
public sealed class UiSettings : INotifyPropertyChanged
{
    private UserSettingsStore _store = new();
    private int _dataGridFontSize = UserSettings.DefaultDataGridFontSize;
    private WindowPlacement? _mainWindow;
    private int _sqlEditorFontSize = UserSettings.DefaultSqlEditorFontSize;
    private int _sqlFetchRows = UserSettings.DefaultSqlFetchRows;
    private bool _explorerSortByAlias;
    private bool _hideDisconnectedDatabases;
    private bool _showObjectsDescription;
    private bool _checkForUpdatesOnStartup = true;
    private bool? _includePreReleases;
    private string? _skippedUpdateVersion;

    public static UiSettings Instance { get; } = new();

    private UiSettings() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Loads settings.json (defaults if missing/corrupt). Call once at startup.</summary>
    public void Initialize(UserSettingsStore store)
    {
        _store = store;
        var loaded = store.Load();
        _dataGridFontSize = loaded.DataGridFontSize;
        _mainWindow = loaded.MainWindow;
        _sqlEditorFontSize = loaded.SqlEditorFontSize;
        _sqlFetchRows = loaded.SqlFetchRows;
        _explorerSortByAlias = loaded.ExplorerSortByAlias;
        _hideDisconnectedDatabases = loaded.HideDisconnectedDatabases;
        _showObjectsDescription = loaded.ShowObjectsDescription;
        _checkForUpdatesOnStartup = loaded.CheckForUpdatesOnStartup;
        _includePreReleases = loaded.IncludePreReleases;
        _skippedUpdateVersion = loaded.SkippedUpdateVersion;
        OnPropertyChanged(nameof(DataGridFontSize));
        OnPropertyChanged(nameof(DataGridFontSizePx));
        OnPropertyChanged(nameof(SqlEditorFontSize));
        OnPropertyChanged(nameof(SqlEditorFontSizePx));
        OnPropertyChanged(nameof(SqlFetchRows));
        OnPropertyChanged(nameof(ExplorerSortByAlias));
        OnPropertyChanged(nameof(HideDisconnectedDatabases));
        OnPropertyChanged(nameof(ShowObjectsDescription));
        OnPropertyChanged(nameof(CheckForUpdatesOnStartup));
        OnPropertyChanged(nameof(IncludePreReleasesSetting));
        OnPropertyChanged(nameof(IncludePreReleases));
    }

    /// <summary>Options: check GitHub for a newer release at startup.</summary>
    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set { if (value == _checkForUpdatesOnStartup) return; _checkForUpdatesOnStartup = value; OnPropertyChanged(); Persist(); }
    }

    /// <summary>"Receber versões de teste (pre-release)" as saved (null = never chosen; the caller applies the default).</summary>
    public bool? IncludePreReleasesSetting
    {
        get => _includePreReleases;
        set { if (value == _includePreReleases) return; _includePreReleases = value; OnPropertyChanged(); OnPropertyChanged(nameof(IncludePreReleases)); Persist(); }
    }

    /// <summary>Effective pre-release option (saved choice, or the default of the running version); bound by Options.</summary>
    public bool IncludePreReleases
    {
        get => _includePreReleases ?? SqlExpert.Core.Updates.ReleaseSelector.DefaultIncludePreReleases(UpdateService.CurrentVersion);
        set
        {
            if (_includePreReleases == value) return;
            _includePreReleases = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IncludePreReleasesSetting));
            Persist();
        }
    }

    /// <summary>Version skipped with "Pular esta versão" (null = none).</summary>
    public string? SkippedUpdateVersion
    {
        get => _skippedUpdateVersion;
        set { if (value == _skippedUpdateVersion) return; _skippedUpdateVersion = value; OnPropertyChanged(); Persist(); }
    }

    /// <summary>DB Explorer "Sort nodes": true = By alias, false = As registered.</summary>
    public bool ExplorerSortByAlias
    {
        get => _explorerSortByAlias;
        set { if (value == _explorerSortByAlias) return; _explorerSortByAlias = value; OnPropertyChanged(); Persist(); }
    }

    /// <summary>DB Explorer "Hide Disconnected Databases".</summary>
    public bool HideDisconnectedDatabases
    {
        get => _hideDisconnectedDatabases;
        set { if (value == _hideDisconnectedDatabases) return; _hideDisconnectedDatabases = value; OnPropertyChanged(); Persist(); }
    }

    /// <summary>DB Explorer "Show objects description".</summary>
    public bool ShowObjectsDescription
    {
        get => _showObjectsDescription;
        set { if (value == _showObjectsDescription) return; _showObjectsDescription = value; OnPropertyChanged(); Persist(); }
    }

    /// <summary>Font size in points (6–32).</summary>
    public int DataGridFontSize
    {
        get => _dataGridFontSize;
        set
        {
            var v = UserSettings.ClampFontSize(value);
            if (v == _dataGridFontSize) return;
            _dataGridFontSize = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DataGridFontSizePx));
            Persist();
        }
    }

    /// <summary>SQL Editor text font size in points (6–32), shared by every SQL Editor.</summary>
    public int SqlEditorFontSize
    {
        get => _sqlEditorFontSize;
        set
        {
            var v = UserSettings.ClampFontSize(value);
            if (v == _sqlEditorFontSize) return;
            _sqlEditorFontSize = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SqlEditorFontSizePx));
            Persist();
        }
    }

    public double SqlEditorFontSizePx => _sqlEditorFontSize * 96.0 / 72.0;

    /// <summary>Default "first N rows" of new SQL Editors (last value used is remembered).</summary>
    public int SqlFetchRows
    {
        get => _sqlFetchRows;
        set
        {
            var v = UserSettings.ClampFetchRows(value);
            if (v == _sqlFetchRows) return;
            _sqlFetchRows = v;
            OnPropertyChanged();
            Persist();
        }
    }

    private void Persist(WindowPlacement? placement = null)
    {
        try
        {
            _store.Save(new UserSettings
            {
                DataGridFontSize = _dataGridFontSize,
                SqlEditorFontSize = _sqlEditorFontSize,
                SqlFetchRows = _sqlFetchRows,
                ExplorerSortByAlias = _explorerSortByAlias,
                HideDisconnectedDatabases = _hideDisconnectedDatabases,
                ShowObjectsDescription = _showObjectsDescription,
                CheckForUpdatesOnStartup = _checkForUpdatesOnStartup,
                IncludePreReleases = _includePreReleases,
                SkippedUpdateVersion = _skippedUpdateVersion,
                MainWindow = placement
            });
        }
        catch (Exception ex)
        {
            App.ReportError(ex);
        }
    }

    /// <summary>Main window bounds/state saved on the last close (null on first run).</summary>
    public WindowPlacement? MainWindowPlacement => _mainWindow;

    /// <summary>Saves the main window bounds/state (called when the window closes).</summary>
    public void SaveMainWindowPlacement(WindowPlacement placement)
    {
        if (!placement.IsValid) return;
        _mainWindow = placement;
        Persist(placement);
    }

    /// <summary>WPF FontSize (device-independent pixels) for the point size.</summary>
    public double DataGridFontSizePx => _dataGridFontSize * 96.0 / 72.0;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
