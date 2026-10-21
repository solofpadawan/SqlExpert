using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using SqlExpert.Core.DataForms;

namespace SqlExpert.Tests;

/// <summary>
/// Editing a NULL cell of the Data grid: the editor starts empty, leaving it blank keeps the NULL (no empty string,
/// row not modified), typed text is used, and Ctrl+0 sets NULL explicitly. No GUI is started.
/// </summary>
public class NullCellEditingTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ===== null → editor text =====

    [Fact]
    public void EditorText_Null_StartsEmpty_NotTheNullMarker()
    {
        Assert.Equal("", DataValueText.EditorText(DBNull.Value, Inv));
        Assert.Equal("", DataValueText.EditorText(null, Inv));
        Assert.Equal(DataValueText.NullText, DataValueText.FormatCell(DBNull.Value, Inv)); // display keeps "<null>"
    }

    [Fact]
    public void EditorText_NonNull_IsTheDisplayText()
    {
        Assert.Equal("abc", DataValueText.EditorText("abc", Inv));
        Assert.Equal("", DataValueText.EditorText("", Inv));
        Assert.Equal("42", DataValueText.EditorText(42, Inv));
        Assert.Equal("1.5", DataValueText.EditorText(1.5m, Inv));
        Assert.Equal(DataValueText.FormatCell(new DateTime(2026, 10, 21), Inv), DataValueText.EditorText(new DateTime(2026, 10, 21), Inv));
    }

    // ===== left blank → no change =====

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void KeepsNullWhenBlank_NullLeftBlank_IsTrue(string? text)
    {
        Assert.True(DataValueText.KeepsNullWhenBlank(DBNull.Value, text));
        Assert.True(DataValueText.KeepsNullWhenBlank(null, text));
    }

    [Theory]
    [InlineData("x")]
    [InlineData(" ")]
    [InlineData("0")]
    public void KeepsNullWhenBlank_TypedText_IsFalse(string text) =>
        Assert.False(DataValueText.KeepsNullWhenBlank(DBNull.Value, text));

    [Fact]
    public void KeepsNullWhenBlank_NonNullValue_IsFalse_SoClearingBehavesAsBefore()
    {
        Assert.False(DataValueText.KeepsNullWhenBlank("abc", ""));
        Assert.False(DataValueText.KeepsNullWhenBlank(5, ""));
        // As before: a cleared text column is an empty string, a cleared non-text column is NULL.
        Assert.Equal("", DataValueText.Parse("", typeof(string), Inv));
        Assert.Equal(DBNull.Value, DataValueText.Parse("", typeof(int), Inv));
    }

    /// <summary>Mirrors CellEditorValueConverter.ConvertBack: blank on NULL writes nothing; anything else is parsed.</summary>
    private static void Commit(DataRow row, string column, string editedText, object? originalValue)
    {
        if (DataValueText.KeepsNullWhenBlank(originalValue, editedText)) return;
        row[column] = DataValueText.Parse(editedText, row.Table.Columns[column]!.DataType, Inv);
    }

    private static DataTable Table()
    {
        var t = new DataTable();
        t.Columns.Add("Id", typeof(int));
        t.Columns.Add("Nome", typeof(string));
        t.Columns.Add("Qtd", typeof(int));
        t.Columns.Add("Obrig", typeof(string)).AllowDBNull = false;
        t.Rows.Add(1, DBNull.Value, DBNull.Value, "x");
        t.AcceptChanges();
        return t;
    }

    [Theory]
    [InlineData("Nome")]
    [InlineData("Qtd")]
    public void NullCell_LeftBlank_StaysNull_AndRowIsNotModified(string column)
    {
        var t = Table();
        var row = t.Rows[0];
        var original = row[column];
        var text = DataValueText.EditorText(original, Inv);

        Commit(row, column, text, original);

        Assert.Equal(DBNull.Value, row[column]);
        Assert.Equal(DataRowState.Unchanged, row.RowState);
        Assert.Null(t.GetChanges());
    }

    [Fact]
    public void NullCell_TypedValue_IsUsed()
    {
        var t = Table();
        var row = t.Rows[0];
        Commit(row, "Nome", "Maria", DBNull.Value);
        Commit(row, "Qtd", "7", DBNull.Value);
        Assert.Equal("Maria", row["Nome"]);
        Assert.Equal(7, row["Qtd"]);
        Assert.Equal(DataRowState.Modified, row.RowState);
    }

    [Fact]
    public void NullCell_TypedNullMarker_StillMeansNull()
    {
        var t = Table();
        var row = t.Rows[0];
        row["Nome"] = "abc";
        t.AcceptChanges();
        Commit(row, "Nome", DataValueText.NullText, "abc");
        Assert.Equal(DBNull.Value, row["Nome"]);
    }

    // ===== explicit set-null shortcut (Ctrl+0) =====

    [Theory]
    [InlineData("D0", true, false, false, true)]
    [InlineData("NumPad0", true, false, false, true)]
    [InlineData("D0", false, false, false, false)]
    [InlineData("D0", true, true, false, false)]
    [InlineData("D0", true, false, true, false)]
    [InlineData("Delete", true, false, false, false)]
    [InlineData("D1", true, false, false, false)]
    public void SetNullGesture_IsCtrl0(string key, bool ctrl, bool shift, bool alt, bool expected)
    {
        Assert.Equal("Ctrl+0", CellNullEditing.SetNullShortcut);
        Assert.Equal(expected, CellNullEditing.IsSetNullGesture(key, ctrl, shift, alt));
    }

    [Fact]
    public void TrySetNull_NonNullValue_BecomesNull_PendingChange()
    {
        var t = Table();
        var row = t.Rows[0];
        row["Nome"] = "abc";
        t.AcceptChanges();

        Assert.Equal(CellNullEditing.Result.SetToNull, CellNullEditing.TrySetNull(row, "Nome", isNullable: true, isReadOnly: false));
        Assert.Equal(DBNull.Value, row["Nome"]);
        Assert.Equal(DataRowState.Modified, row.RowState);
        Assert.Equal("abc", row["Nome", DataRowVersion.Original]);
    }

    [Fact]
    public void TrySetNull_AlreadyNull_DoesNotModifyTheRow()
    {
        var t = Table();
        Assert.Equal(CellNullEditing.Result.AlreadyNull, CellNullEditing.TrySetNull(t.Rows[0], "Qtd", true, false));
        Assert.Equal(DataRowState.Unchanged, t.Rows[0].RowState);
    }

    [Fact]
    public void TrySetNull_NotNullOrReadOnly_LeavesTheValue()
    {
        var t = Table();
        var row = t.Rows[0];
        Assert.Equal(CellNullEditing.Result.NotNullable, CellNullEditing.TrySetNull(row, "Obrig", isNullable: false, isReadOnly: false));
        Assert.Equal(CellNullEditing.Result.ReadOnly, CellNullEditing.TrySetNull(row, "Id", isNullable: true, isReadOnly: true));
        Assert.Equal("x", row["Obrig"]);
        Assert.Equal(1, row["Id"]);
        Assert.Equal(DataRowState.Unchanged, row.RowState);
    }

    // ===== static audit of the WPF wiring (source only, no GUI) =====

    private static string Views()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App", "Views");
    }

    private static string Src(string file) => File.ReadAllText(Path.Combine(Views(), file));

    [Fact]
    public void DataGrid_TextAndForeignKeyColumns_UseTheNullAwareEditor()
    {
        var grid = Src("TableDataGrid.xaml.cs");
        Assert.Contains("new NullAwareTextColumn(spec.DataType, dateKind)", grid);
        Assert.Contains("new ForeignKeyTextColumn(spec.Lookup!, spec.DataType)", grid);
        Assert.DoesNotContain("new DataGridTextColumn", grid);
        Assert.Matches(@"class ForeignKeyTextColumn\s*:\s*NullAwareTextColumn", Src("ForeignKeyTextColumn.cs"));

        var column = Src("NullAwareTextColumn.cs");
        Assert.Contains("GenerateEditingElement", column);
        Assert.Contains("new CellEditorValueConverter(dataType, dateKind)", column);

        var conv = Src("CellValueConverters.cs");
        var editor = Regex.Match(conv, @"class CellEditorValueConverter[\s\S]*?\n}\n").Value;
        Assert.Contains("DataValueText.EditorText(", editor);
        Assert.Contains("DataValueText.KeepsNullWhenBlank(", editor);
        Assert.Contains("Binding.DoNothing", editor);
    }

    [Fact]
    public void GridAndForm_HandleCtrl0_AndCtrlDelStillDeletes()
    {
        var grid = Src("TableDataGrid.xaml.cs");
        Assert.Contains("CellNullEditing.IsSetNullGesture(", grid);
        Assert.Contains("if (IsSetNullGesture(e))", grid);
        Assert.Contains("e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.Control && _vm.DeleteCommand", grid);
        var form = Src("TableDataGrid.Form.cs");
        Assert.Contains("if (IsSetNullGesture(e))", form);
        Assert.Contains("new FormValueConverter(spec.ClrType, dateKind)", form); // form keeps its empty box + null checkbox
    }
}
