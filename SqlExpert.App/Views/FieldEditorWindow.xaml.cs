using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.App.Views;

/// <summary>
/// Field editor (New field / Edit field). Builds the T-SQL with <see cref="FieldDdlBuilder"/>, shows it,
/// and on OK runs it through the supplied executor; on error the dialog stays open with the input intact.
/// </summary>
public partial class FieldEditorWindow : Window
{
    private readonly string _schema;
    private readonly string _table;
    private readonly ColumnStructure? _original;
    private readonly long _rowCount;
    private readonly TableStructure _structure;
    private readonly bool _originalInKey;
    private PrimaryKeyPlan _keyPlan = PrimaryKeyPlan.None;
    private readonly Func<IReadOnlyList<string>, Task> _execute;
    /// <summary>Combo item text → (schema, type) for user-defined types shown as [schema].[name].</summary>
    private readonly Dictionary<string, (string Schema, string Name)> _userTypes = new();
    private bool _ready;
    private bool _busy;
    private IReadOnlyList<string> _statements = Array.Empty<string>();

    public bool IsNew => _original is null;
    /// <summary>Definition that was applied (valid after DialogResult = true).</summary>
    public FieldDefinition? Result { get; private set; }

    public FieldEditorWindow(
        TableStructure structure, ColumnStructure? original,
        IReadOnlyList<string> collations, Func<IReadOnlyList<string>, Task> execute)
    {
        InitializeComponent();
        _structure = structure;
        _schema = structure.Schema;
        _table = structure.Name;
        var schema = _schema;
        var table = _table;
        _original = original;
        _rowCount = structure.RowCount;
        _originalInKey = original is not null && PrimaryKeyDdlBuilder.IsInPrimaryKey(structure, original.Name);
        _execute = execute;

        foreach (var t in SqlTypeCatalog.CommonTypes) TypeBox.Items.Add(t);
        CollationBox.Items.Add("");
        foreach (var c in collations) CollationBox.Items.Add(c);

        var f = original is null
            ? new FieldDefinition { Name = "", DataType = "int", IsNullable = true }
            : FieldDefinition.FromColumn(original);

        Title = IsNew ? "New field" : "Edit field";
        HeaderText.Title = IsNew
            ? $"Novo campo em {SqlText.Qualified(schema, table)}"
            : $"Campo {SqlText.Bracket(original!.Name)} de {SqlText.Qualified(schema, table)}";

        NameBox.Text = f.Name;
        var typeItem = f.DataType.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(f.TypeSchema))
        {
            typeItem = SqlText.Qualified(f.TypeSchema!, f.DataType);
            _userTypes[typeItem] = (f.TypeSchema!, f.DataType);
        }
        if (!TypeBox.Items.Contains(typeItem)) TypeBox.Items.Add(typeItem);
        TypeBox.SelectedItem = typeItem;

        MaxBox.IsChecked = f.Length is -1;
        SizeBox.Text = f.Length is > 0 ? f.Length.Value.ToString() : "";
        PrecisionBox.Text = SqlTypeCatalog.HasPrecisionAndScale(f.DataType) && f.Precision is int p ? p.ToString() : "";
        ScaleBox.Text = (SqlTypeCatalog.HasPrecisionAndScale(f.DataType) || SqlTypeCatalog.HasFractionalScale(f.DataType)) && f.Scale is int s ? s.ToString() : "";
        NullableBox.IsChecked = f.IsNullable;
        PrimaryKeyBox.IsChecked = _originalInKey;
        DefaultBox.Text = f.DefaultExpression ?? "";
        if (!string.IsNullOrEmpty(f.Collation) && !CollationBox.Items.Contains(f.Collation)) CollationBox.Items.Add(f.Collation);
        CollationBox.Text = f.Collation ?? "";
        IdentityBox.IsChecked = f.IsIdentity;
        SeedBox.Text = Num(f.IdentitySeed);
        IncrementBox.Text = Num(f.IdentityIncrement);
        DescriptionBox.Text = f.Description ?? "";

        if (!IsNew)
        {
            // IDENTITY cannot be added to or removed from an existing column.
            IdentityBox.IsEnabled = SeedBox.IsEnabled = IncrementBox.IsEnabled = false;
            IdentityBox.ToolTip = "IDENTITY não pode ser alterado em um campo existente.";
        }

        _ready = true;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
        Rebuild();
    }

    private static string Num(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);

    private string SelectedType => TypeBox.SelectedItem as string ?? "int";

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return; // fired during InitializeComponent / initial fill
        var t = SelectedType;
        if (SqlTypeCatalog.HasLength(t) && string.IsNullOrWhiteSpace(SizeBox.Text) && MaxBox.IsChecked != true)
            SizeBox.Text = SqlTypeCatalog.DefaultLength(t).ToString();
        if (SqlTypeCatalog.HasPrecisionAndScale(t))
        {
            if (string.IsNullOrWhiteSpace(PrecisionBox.Text)) PrecisionBox.Text = "18";
            if (string.IsNullOrWhiteSpace(ScaleBox.Text)) ScaleBox.Text = "0";
        }
        Rebuild();
    }

    private void AnyChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) Rebuild();
    }

    /// <summary>Reads the form; returns null with an error message when a number cannot be parsed.</summary>
    private FieldDefinition? ReadForm(out string? error)
    {
        error = null;
        var typeText = SelectedType;
        var f = new FieldDefinition
        {
            Name = NameBox.Text.Trim(),
            IsNullable = NullableBox.IsChecked == true,
            DefaultExpression = string.IsNullOrWhiteSpace(DefaultBox.Text) ? null : DefaultBox.Text.Trim(),
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
            IsIdentity = IdentityBox.IsChecked == true,
            IsPrimaryKey = PrimaryKeyBox.IsChecked == true
        };
        if (_userTypes.TryGetValue(typeText, out var ut)) { f.DataType = ut.Name; f.TypeSchema = ut.Schema; }
        else f.DataType = typeText;

        var userType = f.TypeSchema is not null;
        if (!userType && SqlTypeCatalog.HasLength(f.DataType))
        {
            if (MaxBox.IsChecked == true && SqlTypeCatalog.SupportsMax(f.DataType)) f.Length = -1;
            else if (!TryInt(SizeBox.Text, out var len)) { error = "Size inválido."; return null; }
            else f.Length = len;
        }
        if (!userType && SqlTypeCatalog.HasPrecisionAndScale(f.DataType))
        {
            if (!TryInt(PrecisionBox.Text, out var p)) { error = "Precision inválida."; return null; }
            if (!TryInt(ScaleBox.Text, out var s)) { error = "Scale inválido."; return null; }
            f.Precision = p; f.Scale = s ?? 0;
        }
        else if (!userType && SqlTypeCatalog.HasFractionalScale(f.DataType))
        {
            if (!TryInt(ScaleBox.Text, out var s)) { error = "Scale inválido."; return null; }
            f.Scale = s;
        }
        if (SqlTypeCatalog.IsCharacterType(f.DataType))
            f.Collation = string.IsNullOrWhiteSpace(CollationBox.Text) ? null : CollationBox.Text.Trim();

        if (f.IsIdentity)
        {
            if (!decimal.TryParse(SeedBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)) { error = "Seed inválido."; return null; }
            if (!decimal.TryParse(IncrementBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var inc)) { error = "Increment inválido."; return null; }
            f.IdentitySeed = seed; f.IdentityIncrement = inc;
        }
        else if (_original is not null)
        {
            f.IsIdentity = _original.IsIdentity;
        }
        return f;
    }

    private static bool TryInt(string text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) { value = v; return true; }
        return false;
    }

    private void UpdateEnabledState()
    {
        var t = SelectedType;
        var system = !_userTypes.ContainsKey(t);
        var hasLen = system && SqlTypeCatalog.HasLength(t);
        MaxBox.IsEnabled = hasLen && SqlTypeCatalog.SupportsMax(t);
        if (!MaxBox.IsEnabled && MaxBox.IsChecked == true) MaxBox.IsChecked = false;
        SizeBox.IsEnabled = hasLen && MaxBox.IsChecked != true;
        PrecisionBox.IsEnabled = system && SqlTypeCatalog.HasPrecisionAndScale(t);
        ScaleBox.IsEnabled = system && (SqlTypeCatalog.HasPrecisionAndScale(t) || SqlTypeCatalog.HasFractionalScale(t));
        CollationBox.IsEnabled = SqlTypeCatalog.IsCharacterType(t);
        var key = PrimaryKeyBox.IsChecked == true;
        if (IsNew)
        {
            var identity = IdentityBox.IsChecked == true;
            SeedBox.IsEnabled = IncrementBox.IsEnabled = identity;
            if ((identity || key) && NullableBox.IsChecked == true) NullableBox.IsChecked = false;
            NullableBox.IsEnabled = !identity && !key;
            DefaultBox.IsEnabled = !identity;
        }
        else
        {
            // A key field is always NOT NULL; leaving the key re-enables Nullable.
            if (key && NullableBox.IsChecked == true) NullableBox.IsChecked = false;
            NullableBox.IsEnabled = !key;
        }
        NullableBox.ToolTip = key ? "Campos da chave primária são sempre NOT NULL." : null;
    }

    private void Rebuild()
    {
        UpdateEnabledState();
        _statements = Array.Empty<string>();
        var f = ReadForm(out var error);
        if (f is not null)
            error = FieldDdlBuilder.Validate(f, IsNew);

        _keyPlan = PrimaryKeyPlan.None;
        if (f is not null && error is null)
        {
            var built = IsNew
                ? FieldDdlBuilder.BuildAddColumn(_structure, f)
                : FieldDdlBuilder.BuildAlterColumn(_structure, _original!, f);
            _statements = built.Statements;
            _keyPlan = built.Key;
            Result = f;
            if (_keyPlan.IsBlocked)
            {
                error = "Não é possível alterar a chave primária: " + string.Join("; ", _keyPlan.Blockers) +
                        ". Remova essas foreign keys antes (ou faça a alteração por script).";
                _statements = Array.Empty<string>();
            }
            else if (IsNew && FieldDdlBuilder.NeedsRowsWarning(f, _rowCount))
                error = $"Atenção: a tabela tem {_rowCount} registro(s). Um campo NOT NULL sem Default não pode ser adicionado (o SQL Server recusa). Marque Nullable ou informe um Default.";
            else if (IsNew && FieldDdlBuilder.NeedsDuplicateKeyWarning(f, _rowCount))
                error = $"Atenção: a tabela tem {_rowCount} registros e todos receberiam o mesmo valor neste campo, o que repete a chave primária (o SQL Server recusa). Use IDENTITY ou um Default como newid().";
        }
        PrimaryKeyNote.Text = _keyPlan.Summary ?? "";
        PrimaryKeyNote.Visibility = string.IsNullOrEmpty(_keyPlan.Summary) ? Visibility.Collapsed : Visibility.Visible;

        PreviewBox.Text = error is not null && _statements.Count == 0
            ? "-- Corrija os dados do campo para gerar o SQL."
            : FieldDdlBuilder.ToScript(_statements);
        ErrorText.WithTone(DialogTone.Error);
        ErrorText.Text = error ?? "";
        OkButton.IsEnabled = !_busy && _statements.Count > 0;
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _statements.Count == 0 || Result is null) return;

        if (_keyPlan.RecreatesExistingKey
            && MessageDialog.Show(this,
                _keyPlan.Summary + "\n\nA chave primária será excluída e criada de novo na mesma transação " +
                "(os índices da tabela são reconstruídos; em tabelas grandes isso pode demorar).\n\nContinuar?",
                "SqlExpert - Primary key", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (IsNew && FieldDdlBuilder.NeedsRowsWarning(Result, _rowCount)
            && MessageDialog.Show(this,
                $"A tabela {SqlText.Qualified(_schema, _table)} tem {_rowCount} registro(s).\n\n" +
                "Adicionar um campo NOT NULL sem Default deve falhar no SQL Server.\n\nTentar mesmo assim?",
                "SqlExpert - New field", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _busy = true;
        OkButton.IsEnabled = CancelButton.IsEnabled = false;
        ErrorText.WithTone(DialogTone.Info);
        ErrorText.Text = "Executando...";
        try
        {
            await _execute(_statements);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.WithTone(DialogTone.Error);
            ErrorText.Text = "Falhou (nada foi alterado, transação desfeita): " + ex.Message;
            App.ReportError(ex);
        }
        finally
        {
            _busy = false;
            if (IsLoaded && DialogResult != true)
            {
                CancelButton.IsEnabled = true;
                OkButton.IsEnabled = _statements.Count > 0;
            }
        }
    }
}
