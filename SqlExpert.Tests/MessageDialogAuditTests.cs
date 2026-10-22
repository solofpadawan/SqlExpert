using System.Text.RegularExpressions;

namespace SqlExpert.Tests;

/// <summary>Every message in the app goes through the styled MessageDialog (no Windows MessageBox).</summary>
public class MessageDialogAuditTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    private static IEnumerable<string> AppSources()
    {
        var root = AppDir();
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"));
    }

    private static string DialogText() => File.ReadAllText(Path.Combine(AppDir(), "Views", "MessageDialog.cs"));

    [Fact]
    public void NoMessageBoxShow_OutsideTheDialogFallback()
    {
        var offenders = AppSources()
            .Where(f => !f.EndsWith("MessageDialog.cs", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bMessageBox\s*\.\s*Show\b"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);

        // Inside the service only the no-application fallback may use it.
        Assert.Single(Regex.Matches(DialogText(), @"\bMessageBox\.Show\("));
        Assert.Contains("private static MessageBoxResult FallbackShow(", DialogText());
    }

    [Fact]
    public void Dialog_IsStyledCenteredCopiesAndScrolls()
    {
        var d = DialogText();
        Assert.Contains("Icon = AppIcon.Image", d);
        Assert.Contains("WindowStartupLocation.CenterOwner", d);
        Assert.Contains(".WithDialogStyle()", d);
        Assert.Contains("IsDefault = kind == defaultResult", d);
        Assert.Contains("IsCancel = kind == _escapeResult", d);
        Assert.Contains("ApplicationCommands.Copy", d);
        Assert.Contains("TextWrapping = TextWrapping.Wrap", d);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", d);
        // Esc: Cancel, else No, else OK.
        Assert.Matches(new Regex(@"OKCancel or MessageBoxButton\.YesNoCancel => MessageBoxResult\.Cancel,\s*MessageBoxButton\.YesNo => MessageBoxResult\.No,\s*_ => MessageBoxResult\.OK"), d);
    }

    [Fact]
    public void DialogIcons_AreMutedAndNotGreen()
    {
        var colors = Regex.Matches(DialogText(), @"Color\.FromRgb\(0x([0-9A-F]{2}), 0x([0-9A-F]{2}), 0x([0-9A-F]{2})\)")
            .Select(m => (R: Convert.ToInt32(m.Groups[1].Value, 16), G: Convert.ToInt32(m.Groups[2].Value, 16), B: Convert.ToInt32(m.Groups[3].Value, 16)))
            .ToList();
        Assert.True(colors.Count >= 12);
        foreach (var (r, g, b) in colors)
            Assert.False(g > r + 20 && g > b + 20, $"green-ish colour #{r:X2}{g:X2}{b:X2}");
    }

    [Fact]
    public void SavePrompts_UseExplicitButtonsWithoutTheOldHint()
    {
        foreach (var f in AppSources())
        {
            var text = File.ReadAllText(f);
            Assert.DoesNotContain("Sim = ", text);
            Assert.DoesNotContain("Não = ", text);
            Assert.DoesNotContain("Cancelar = voltar", text);
        }

        var vm = Path.Combine(AppDir(), "ViewModels");
        var editor = File.ReadAllText(Path.Combine(vm, "SqlEditorDocumentViewModel.cs"));
        Assert.Contains("UnsavedChangesTitle = \"Alterações não salvas\"", editor);
        Assert.Contains("new DialogLabels(\"Salvar\", \"Descartar\", \"Cancelar\"), UnsavedChangesTitle", editor);
        Assert.Contains("new DialogLabels(\"Gravar\", \"Descartar\", \"Cancelar\")", File.ReadAllText(Path.Combine(vm, "TableDataViewModel.cs")));
        Assert.Contains("new DialogLabels(\"Compilar\", \"Descartar\", \"Cancelar\")", File.ReadAllText(Path.Combine(vm, "ProcedureDocumentViewModel.cs")));
    }
}
