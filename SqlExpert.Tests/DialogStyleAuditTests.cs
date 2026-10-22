using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SqlExpert.Tests;

/// <summary>
/// Modern form controls and dialog chrome (Themes/Controls.xaml, Views/DialogChrome.cs): implicit styles for the
/// input controls, grid in-cell editors kept on the default look, and dialogs without hard-coded colors.
/// </summary>
public class DialogStyleAuditTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    private static string AppFile(params string[] parts) => Path.Combine(new[] { AppDir() }.Concat(parts).ToArray());

    private static XDocument Controls() => XDocument.Load(AppFile("Themes", "Controls.xaml"));

    private static string? Key(XElement e) => (string?)e.Attribute(X + "Key");

    private static XElement KeyedStyle(XDocument doc, string key) =>
        doc.Descendants().Single(e => e.Name.LocalName == "Style" && Key(e) == key);

    [Theory]
    [InlineData("TextBox", "SqlTextBox")]
    [InlineData("PasswordBox", "SqlPasswordBox")]
    [InlineData("ComboBox", "SqlComboBox")]
    [InlineData("ComboBoxItem", "SqlComboBoxItem")]
    [InlineData("ListBox", "SqlListBox")]
    [InlineData("GroupBox", "SqlGroupBox")]
    [InlineData("Label", "SqlLabel")]
    public void ImplicitStyle_ExistsAtTheRoot_BasedOnTheNamedStyle(string target, string key)
    {
        var doc = Controls();
        var implicitStyle = doc.Root!.Elements().Single(e => e.Name.LocalName == "Style" && Key(e) is null
                                                              && (string?)e.Attribute("TargetType") == target);
        Assert.Equal("{StaticResource " + key + "}", (string?)implicitStyle.Attribute("BasedOn"));
        KeyedStyle(doc, key); // the named style exists too
    }

    [Theory]
    [InlineData("SqlTextBox")]
    [InlineData("SqlPasswordBox")]
    [InlineData("SqlComboBox")]
    public void InputControls_AreRounded_WithFocusAndDisabledStates(string key)
    {
        var style = KeyedStyle(Controls(), key).ToString();
        Assert.Contains("CornerRadius=\"3\"", style);
        Assert.Contains("Input.Border", style);
        Assert.Contains("Input.FocusBorder", style);
        Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", style);
        Assert.Contains("Input.DisabledText", style);
    }

    [Fact]
    public void ComboBox_HasPopupAndEditableVariant()
    {
        var combo = KeyedStyle(Controls(), "SqlComboBox").ToString();
        Assert.Contains("PART_Popup", combo);
        Assert.Contains("PART_EditableTextBox", combo);
        Assert.Contains("Property=\"IsEditable\" Value=\"True\"", combo);
    }

    [Fact]
    public void DialogChromeStyles_Exist()
    {
        var doc = Controls();
        foreach (var key in new[] { "DialogPrimaryButton", "DialogHeader", "DialogButtonBar", "SqlPreviewFrame", "FormLabel", "FormSection", "FormHint", "FormError" })
            KeyedStyle(doc, key);
        var primary = KeyedStyle(doc, "DialogPrimaryButton").ToString();
        Assert.Contains("CornerRadius=\"3\"", primary);
        Assert.Contains("Property=\"MinHeight\" Value=\"24\"", primary);
        Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", primary);
    }

    [Fact]
    public void Toolbars_UseCompactInputStyles()
    {
        var text = File.ReadAllText(AppFile("Themes", "Controls.xaml"));
        Assert.Contains("ToolBar.TextBoxStyleKey", text);
        Assert.Contains("ToolBar.ComboBoxStyleKey", text);
    }

    /// <summary>Editing cells of the data grids keep the compact default editors.</summary>
    [Fact]
    public void DataGrid_ResetsImplicitInputStyles_ForInCellEditors()
    {
        var doc = XDocument.Load(AppFile("Resources", "Branding.xaml"));
        var grid = doc.Root!.Elements().Single(e => e.Name.LocalName == "Style" && Key(e) is null
                                                     && (string?)e.Attribute("TargetType") == "DataGrid");
        var resets = grid.Descendants().Where(e => e.Name.LocalName == "Style" && !e.HasElements && e.Attribute("BasedOn") is null)
            .Select(e => (string?)e.Attribute("TargetType")).ToList();
        Assert.Contains("TextBox", resets);
        Assert.Contains("ComboBox", resets);
        Assert.Contains("ComboBoxItem", resets);
    }

    [Fact]
    public void FieldEditor_UsesHeaderBand_AndSqlPreview()
    {
        var xaml = File.ReadAllText(AppFile("Views", "FieldEditorWindow.xaml"));
        Assert.Contains("views:DialogHeaderBar", xaml);
        Assert.Contains("views:SqlPreviewBox", xaml);
        Assert.Contains("{StaticResource DialogPrimaryButton}", xaml);
        Assert.Contains("DialogButtonBar", xaml);
    }

    public static IEnumerable<object[]> DialogFiles() => new[]
    {
        "FieldEditorWindow.xaml", "FieldEditorWindow.xaml.cs", "RegisterDatabaseWindow.xaml", "RegisterDatabaseWindow.xaml.cs",
        "DatabasePropertiesWindow.xaml", "DatabasePropertiesWindow.xaml.cs", "ReorderFieldsWindow.xaml", "ReorderFieldsWindow.xaml.cs",
        "PrimaryKeyWindow.cs", "ProcedureExecuteWindow.cs", "ReplaceWindow.cs", "ChooseRegistrationWindow.cs", "BatchProgressWindow.cs",
        "UpdateWindow.cs", "MemoEditorWindow.cs", "CalculatorPopup.cs", "DateCalendarPopup.cs", "ForeignKeyLookupPopup.cs", "DialogChrome.cs"
    }.Select(f => new object[] { f });

    /// <summary>Dialogs take their colors from theme resources; only data colors (key captions, calendar days, swatches) stay local.</summary>
    [Theory]
    [MemberData(nameof(DialogFiles))]
    public void Dialogs_DoNotHardCodeChromeColors(string file)
    {
        var src = File.ReadAllText(AppFile("Views", file));
        Assert.DoesNotMatch(@"(Background|Foreground|BorderBrush)=""#[0-9A-Fa-f]{3,8}""", src);
        Assert.DoesNotMatch(@"Brushes\.(Dark\w+|DimGray|Firebrick|Gray|Silver|LightGray|Red|Green)\b", src);
        Assert.DoesNotContain("SystemColors.", src);
    }

    [Fact]
    public void CodeBuiltDialogs_UseHeaderBand_AndPrimaryButton()
    {
        foreach (var file in new[] { "PrimaryKeyWindow.cs", "ProcedureExecuteWindow.cs", "ChooseRegistrationWindow.cs", "UpdateWindow.cs" })
        {
            var src = File.ReadAllText(AppFile("Views", file));
            Assert.Contains("new DialogHeaderBar(", src);
            Assert.Contains("DialogText.Compose(", src);
            Assert.Contains(".WithPrimaryStyle()", src);
        }
        Assert.Contains("SqlPreviewBox _preview", File.ReadAllText(AppFile("Views", "PrimaryKeyWindow.cs")));
    }
}
