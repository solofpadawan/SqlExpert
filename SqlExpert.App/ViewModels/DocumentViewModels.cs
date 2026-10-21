using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Documents;
using SqlExpert.Core.Explorer;
using SqlExpert.Core.Services;

namespace SqlExpert.App.ViewModels;

/// <summary>An MDI-like document shown in the central area and on the Windows bar.</summary>
public abstract class DocumentViewModel : INotifyPropertyChanged
{
    private bool _isActive;

    protected DocumentViewModel(MainViewModel shell, string key, string profileId, string database)
    {
        Shell = shell;
        Key = key;
        ProfileId = profileId;
        Database = database;
    }

    /// <summary>Main window VM (menus/commands shared by every document).</summary>
    public MainViewModel Shell { get; }
    /// <summary>Document identity (a new object editor takes the object's key after its first compile).</summary>
    public string Key { get; protected set; }
    public string ProfileId { get; }
    public string Database { get; }

    /// <summary>Database the document currently works on (a SQL Editor follows USE).</summary>
    public virtual string CurrentDatabase => Database;

    /// <summary>Caption used in the main window title, e.g. "Table : [dbo].[Clientes]".</summary>
    public abstract string Caption { get; }
    /// <summary>Document title bar text, e.g. "Table : [dbo].[Clientes] : SqlExpertTeste".</summary>
    public virtual string Title => $"{Caption} : {Database}";
    /// <summary>Short label of the Windows-bar tab (object name).</summary>
    public abstract string ShortName { get; }
    public virtual string ToolTip => Title;
    /// <summary>Vector icon of the Windows-bar tab and title strip (same key as the object's Explorer node).</summary>
    public abstract string IconKey { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Color Mark of the document's registration ("#RRGGBB" or null): thin strip on top of the window.</summary>
    public string? ColorMark => Shell.FindProfile(ProfileId)?.ColorMark;

    /// <summary>Re-reads <see cref="ColorMark"/> (after Database Registration Info changed it).</summary>
    public void RaiseColorMarkChanged() => OnPropertyChanged(nameof(ColorMark));

    /// <summary>True when closing would lose edits (the close asks first).</summary>
    public virtual bool HasUnsavedChanges => false;

    /// <summary>Called before closing; return false to keep the document open.</summary>
    public virtual Task<bool> ConfirmCloseAsync() => Task.FromResult(true);

    /// <summary>Called when the document is removed (release state).</summary>
    public virtual void OnClosed() { }

    /// <summary>Called when another document becomes active (commit in-progress cell edits etc.).</summary>
    public virtual void OnDeactivated() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Table editor document: Fields/…/Data/DDL with its own state and pending Data edits.</summary>
public sealed class TableDocumentViewModel : DocumentViewModel
{
    private string _ddl = "";
    private int _selectedTabIndex;
    private FieldRow? _selectedField;

    public TableDocumentViewModel(MainViewModel shell, string profileId, string database, string schema, string table, TableDataViewModel data)
        : base(shell, DocumentKeys.Table(profileId, database, schema, table), profileId, database)
    {
        Schema = schema;
        Table = table;
        TableData = data;
    }

    public string Schema { get; }
    public string Table { get; }

    public override string Caption => WindowTitles.TableCaption(Schema, Table);
    public override string ShortName => Table;
    public override string IconKey => ObjectIcons.For(Core.Models.ObjectKind.Table);
    public override string ToolTip => $"[{Schema}].[{Table}] : {Database}";

    public ObservableCollection<FieldRow> SampleColumns { get; } = new();

    /// <summary>Rows of the Constraints tab (PRIMARY KEY, UNIQUE, FOREIGN KEY, CHECK).</summary>
    public ObservableCollection<ConstraintRow> Constraints { get; } = new();

    public ConstraintRow? SelectedConstraint
    {
        get => _selectedConstraint;
        set { _selectedConstraint = value; OnPropertyChanged(); }
    }
    private ConstraintRow? _selectedConstraint;
    public TableDataViewModel TableData { get; }

    /// <summary>Row selected in the Fields grid (drives Edit/Drop and the Field description panel).</summary>
    public FieldRow? SelectedField
    {
        get => _selectedField;
        set { _selectedField = value; OnPropertyChanged(); }
    }

    public string Ddl
    {
        get => _ddl;
        set { _ddl = value; OnPropertyChanged(); }
    }

    /// <summary>Tab indexes of the table editor (order of the TabControl in MainWindow.xaml).</summary>
    public const int FieldsTabIndex = 0;
    public const int DataTabIndex = 5;
    public const int DdlTabIndex = 8;

    /// <summary>Tab selected when the table opens (registration option "Default tab when opening a table").</summary>
    public static int TabIndexFor(Core.Models.DefaultObjectTab tab) => tab switch
    {
        Core.Models.DefaultObjectTab.Data => DataTabIndex,
        Core.Models.DefaultObjectTab.Ddl => DdlTabIndex,
        _ => FieldsTabIndex
    };

    /// <summary>Selected editor tab (Fields, Constraints, …), kept per document.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    /// <summary>Loads columns + DDL (Fields/DDL tabs). Data tab loads lazily on its own.</summary>
    public async Task LoadAsync(IMetadataService metadata, Action<string> setStatus, string? selectField = null)
    {
        selectField ??= SelectedField?.FieldName;
        TableData.SetTable(ProfileId, Database, Schema, Table);
        setStatus($"Carregando {Schema}.{Table}...");
        try
        {
            var meta = await metadata.GetTableAsync(ProfileId, Database, Schema, Table).ConfigureAwait(true);
            SampleColumns.Clear();
            var keepConstraint = SelectedConstraint?.Name;
            Constraints.Clear();
            if (meta is null)
            {
                Ddl = $"-- Tabela não encontrada: [{Schema}].[{Table}]";
                setStatus("Tabela não encontrada.");
                return;
            }

            foreach (var c in meta.Columns.OrderBy(x => x.Ordinal))
            {
                SampleColumns.Add(new FieldRow(c));
            }
            SelectedField = SampleColumns.FirstOrDefault(r => string.Equals(r.FieldName, selectField, StringComparison.OrdinalIgnoreCase))
                            ?? SampleColumns.FirstOrDefault();

            foreach (var k in meta.Constraints.OrderBy(k => ConstraintRow.Order(k.Type)).ThenBy(k => k.Name, StringComparer.OrdinalIgnoreCase))
                Constraints.Add(new ConstraintRow(k));
            SelectedConstraint = Constraints.FirstOrDefault(c => string.Equals(c.Name, keepConstraint, StringComparison.OrdinalIgnoreCase))
                                 ?? Constraints.FirstOrDefault();

            Ddl = DdlScriptBuilder.BuildCreateTable(meta);
            setStatus($"Tabela {Schema}.{Table}: {meta.Columns.Count} colunas.");
        }
        catch (Exception ex)
        {
            setStatus("Erro ao carregar tabela: " + ex.Message);
            Ddl = "-- Erro: " + ex.Message;
        }
    }

    public override bool HasUnsavedChanges
    {
        get
        {
            TableData.CommitPendingEdit();
            return TableData.HasPendingChanges;
        }
    }

    public override Task<bool> ConfirmCloseAsync() => TableData.ConfirmLeaveAsync();

    public override void OnDeactivated() => TableData.CommitPendingEdit();

    public override void OnClosed() => TableData.Clear();
}
