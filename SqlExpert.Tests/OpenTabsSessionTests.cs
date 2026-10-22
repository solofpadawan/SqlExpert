using System.Data.Common;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SqlExpert.Core.Documents;
using Xunit.Abstractions;

namespace SqlExpert.Tests;

/// <summary>Open tabs saved per registration (sessions\&lt;id&gt;.json) and reopened on the next connect.</summary>
public class OpenTabsSessionTests
{
    private readonly ITestOutputHelper _out;
    private static readonly string? ConnStr = Environment.GetEnvironmentVariable("SQLEXPERT_IT_CONNSTR");

    public OpenTabsSessionTests(ITestOutputHelper output) => _out = output;

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "SqlExpert-tests", Guid.NewGuid().ToString("N"), "sessions");

    private const string ProfileA = "0123456789abcdef0123456789abcdef";
    private const string ProfileB = "fedcba9876543210fedcba9876543210";

    private static OpenTabsSession Sample(string profileId) => new()
    {
        ProfileId = profileId,
        Tabs =
        {
            new SessionTab { Kind = SessionTabKind.Table, Database = "Sales", Schema = "dbo", Name = "Customer", SelectedTab = 5 },
            new SessionTab { Kind = SessionTabKind.Procedure, Database = "Sales", Schema = "dbo", Name = "usp_Totals", SelectedTab = 8 },
            new SessionTab { Kind = SessionTabKind.SqlEditor, Number = 2, Database = "Sales", SqlText = "select 1;\r\n-- não executado", IsModified = true }
        },
        ActiveIndex = 1
    };

    // ===== Store =====

    [Fact]
    public void SaveAndLoad_RoundTripsOrderActiveTabAndText()
    {
        var store = new OpenTabsSessionStore(TempDir());
        store.Save(Sample(ProfileA));

        var loaded = store.Load(ProfileA);
        Assert.NotNull(loaded);
        Assert.Equal(new[] { SessionTabKind.Table, SessionTabKind.Procedure, SessionTabKind.SqlEditor }, loaded!.Tabs.Select(t => t.Kind));
        Assert.Equal(1, loaded.ActiveIndex);
        Assert.Equal(5, loaded.Tabs[0].SelectedTab);
        Assert.Equal("usp_Totals", loaded.Tabs[1].Name);
        Assert.Equal("select 1;\r\n-- não executado", loaded.Tabs[2].SqlText);
        Assert.True(loaded.Tabs[2].IsModified);
        Assert.Equal(2, loaded.Tabs[2].Number);
    }

    [Fact]
    public void File_IsReadableJsonWithKindNamesAndNoPassword()
    {
        var store = new OpenTabsSessionStore(TempDir());
        store.Save(Sample(ProfileA));
        var json = File.ReadAllText(store.PathFor(ProfileA));
        Assert.Contains("\"Table\"", json);
        Assert.Contains("\"SqlEditor\"", json);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Identity", json);
    }

    [Fact]
    public void MissingFile_GivesNull() => Assert.Null(new OpenTabsSessionStore(TempDir()).Load(ProfileA));

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("{\"Tabs\":[{\"Kind\":\"Spaceship\"}]}")]
    [InlineData("[1,2,3]")]
    public void CorruptFile_IsIgnoredAndLogged(string content)
    {
        var store = new OpenTabsSessionStore(TempDir());
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.PathFor(ProfileA), content);

        Assert.Null(store.Load(ProfileA));
        Assert.True(File.Exists(store.LogPath));
        Assert.Contains(OpenTabsSessionStore.FileNameFor(ProfileA), File.ReadAllText(store.LogPath));
    }

    [Fact]
    public void Sessions_AreKeptPerRegistration()
    {
        var store = new OpenTabsSessionStore(TempDir());
        store.Save(Sample(ProfileA));
        var other = new OpenTabsSession { ProfileId = ProfileB, Tabs = { new SessionTab { Kind = SessionTabKind.SqlEditor, Number = 1, SqlText = "B" } } };
        store.Save(other);

        Assert.Equal(3, store.Load(ProfileA)!.Tabs.Count);
        Assert.Equal("B", Assert.Single(store.Load(ProfileB)!.Tabs).SqlText);
        store.Delete(ProfileB);
        Assert.Null(store.Load(ProfileB));
        Assert.NotNull(store.Load(ProfileA));
    }

    [Fact]
    public void FileOfAnotherRegistration_IsIgnored()
    {
        var store = new OpenTabsSessionStore(TempDir());
        store.Save(Sample(ProfileA));
        Directory.CreateDirectory(store.Directory);
        File.Copy(store.PathFor(ProfileA), store.PathFor(ProfileB));
        Assert.Null(store.Load(ProfileB));
    }

    [Fact]
    public void EmptySession_DeletesTheFile()
    {
        var store = new OpenTabsSessionStore(TempDir());
        store.Save(Sample(ProfileA));
        Assert.True(File.Exists(store.PathFor(ProfileA)));
        store.Save(new OpenTabsSession { ProfileId = ProfileA });
        Assert.False(File.Exists(store.PathFor(ProfileA)));
        Assert.Null(store.Load(ProfileA));
    }

    [Fact]
    public void Save_OverwritesPreviousSession_ClosedTabsDisappear()
    {
        var store = new OpenTabsSessionStore(TempDir());
        store.Save(Sample(ProfileA));
        var s = Sample(ProfileA);
        s.Tabs.RemoveAt(0);
        s.ActiveIndex = 0;
        store.Save(s);
        var loaded = store.Load(ProfileA)!;
        Assert.Equal(2, loaded.Tabs.Count);
        Assert.DoesNotContain(loaded.Tabs, t => t.Kind == SessionTabKind.Table);
        Assert.False(File.Exists(store.PathFor(ProfileA) + ".tmp"));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(3)]
    [InlineData(99)]
    public void Save_InvalidActiveIndex_BecomesNone(int active)
    {
        var store = new OpenTabsSessionStore(TempDir());
        var s = Sample(ProfileA);
        s.ActiveIndex = active;
        store.Save(s);
        Assert.Equal(-1, store.Load(ProfileA)!.ActiveIndex);
    }

    [Fact]
    public void FileName_IsTheRegistrationIdOrAHash()
    {
        Assert.Equal(ProfileA + ".json", OpenTabsSessionStore.FileNameFor(ProfileA.ToUpperInvariant()));
        var odd = OpenTabsSessionStore.FileNameFor(@"..\..\x:y*?");
        Assert.Matches("^[0-9a-f]{32}\\.json$", odd);
        Assert.Equal(odd, OpenTabsSessionStore.FileNameFor(@"..\..\x:y*?"));
        Assert.Throws<ArgumentException>(() => OpenTabsSessionStore.FileNameFor(" "));
    }

    [Fact]
    public void DefaultDirectory_IsUnderAppDataSqlExpert()
    {
        var dir = new OpenTabsSessionStore().Directory;
        Assert.EndsWith(Path.Combine("SqlExpert", "sessions"), dir);
    }

    // ===== Restore plan =====

    [Fact]
    public void Normalize_DropsInvalidAndDuplicateTabs_AndRemapsActive()
    {
        var log = new List<string>();
        var s = new OpenTabsSession
        {
            ProfileId = ProfileA,
            Tabs =
            {
                new SessionTab { Kind = SessionTabKind.Table, Database = "Sales", Schema = "dbo", Name = "" },          // no name
                null!,
                new SessionTab { Kind = SessionTabKind.Table, Database = "Sales", Schema = "dbo", Name = "Customer", SelectedTab = -3 },
                new SessionTab { Kind = SessionTabKind.Table, Database = "SALES", Schema = "DBO", Name = "customer" },  // duplicate
                new SessionTab { Kind = SessionTabKind.SqlEditor, Number = 0 },                                        // bad number
                new SessionTab { Kind = (SessionTabKind)42, Database = "x", Schema = "y", Name = "z" },                // unknown kind
                new SessionTab { Kind = SessionTabKind.SqlEditor, Number = 3, SqlText = "go" }
            },
            ActiveIndex = 6
        };

        var plan = OpenTabsRestorePlan.Normalize(s, log.Add);

        Assert.Equal(2, plan.Tabs.Count);
        Assert.Equal("Customer", plan.Tabs[0].Name);
        Assert.Equal(0, plan.Tabs[0].SelectedTab);
        Assert.Equal(3, plan.Tabs[1].Number);
        Assert.Equal(1, plan.ActiveIndex);
        Assert.Equal(4, log.Count);
    }

    [Fact]
    public void Normalize_ActiveTabDropped_GivesNone()
    {
        var s = Sample(ProfileA);
        s.Tabs[1].Name = null;
        var plan = OpenTabsRestorePlan.Normalize(s);
        Assert.Equal(2, plan.Tabs.Count);
        Assert.Equal(-1, plan.ActiveIndex);
    }

    [Fact]
    public void Normalize_NullSession_IsEmpty() => Assert.Empty(OpenTabsRestorePlan.Normalize(null).Tabs);

    [Fact]
    public void Normalize_HugeSqlText_IsNotKept()
    {
        var s = new OpenTabsSession { ProfileId = ProfileA, Tabs = { new SessionTab { Kind = SessionTabKind.SqlEditor, Number = 1, SqlText = new string('x', OpenTabsRestorePlan.MaxSqlTextLength + 1) } } };
        Assert.Null(Assert.Single(OpenTabsRestorePlan.Normalize(s).Tabs).SqlText);
    }

    [Theory]
    [InlineData(SessionTabKind.Table, "U", true)]
    [InlineData(SessionTabKind.Table, "V", false)]
    [InlineData(SessionTabKind.Table, null, false)]
    [InlineData(SessionTabKind.Procedure, "P", true)]
    [InlineData(SessionTabKind.Procedure, "PC", true)]
    [InlineData(SessionTabKind.Procedure, "FN", false)]
    [InlineData(SessionTabKind.Procedure, null, false)]
    [InlineData(SessionTabKind.SqlEditor, null, true)]
    public void Matches_ObjectType(SessionTabKind kind, string? type, bool expected)
        => Assert.Equal(expected, OpenTabsExistence.Matches(kind, type));

    [Fact]
    public async Task FilterExisting_UnavailableDatabase_SkipsItsTabsButKeepsSqlEditors()
    {
        var tabs = Sample(ProfileA).Tabs;
        var log = new List<string>();
        var kept = await OpenTabsExistence.FilterExistingAsync(tabs,
            (_, _) => Task.FromException<DbConnection>(new InvalidOperationException("offline")), log.Add);
        Assert.Equal(SessionTabKind.SqlEditor, Assert.Single(kept).Kind);
        Assert.Equal(2, log.Count);
        Assert.All(log, l => Assert.Contains("offline", l));
    }

    // ===== Live (SQLEXPERT_IT_CONNSTR) =====

    [Fact]
    public async Task FilterExisting_Live_KeepsExistingObjectsInOrder_SkipsMissingOnes()
    {
        if (string.IsNullOrWhiteSpace(ConnStr))
        {
            _out.WriteLine("SQLEXPERT_IT_CONNSTR não definido: teste de integração ignorado.");
            return;
        }
        var database = new SqlConnectionStringBuilder(ConnStr).InitialCatalog;
        string schema, table;
        await using (var c = new SqlConnection(ConnStr))
        {
            await c.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TOP (1) SCHEMA_NAME(schema_id), name FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name;", c);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                _out.WriteLine("Nenhuma tabela de usuário no database de teste: teste ignorado.");
                return;
            }
            schema = r.GetString(0);
            table = r.GetString(1);
        }
        var missing = "sqlexpert_it_missing_" + Guid.NewGuid().ToString("N")[..8];
        var tabs = new List<SessionTab>
        {
            new() { Kind = SessionTabKind.SqlEditor, Number = 1, SqlText = "select 1" },
            new() { Kind = SessionTabKind.Table, Database = database, Schema = schema, Name = table },
            new() { Kind = SessionTabKind.Table, Database = database, Schema = schema, Name = missing },
            new() { Kind = SessionTabKind.Procedure, Database = database, Schema = schema, Name = table }, // a table is not a procedure
            new() { Kind = SessionTabKind.Table, Database = "sqlexpert_it_nodb_" + Guid.NewGuid().ToString("N")[..8], Schema = "dbo", Name = "t" }
        };
        var log = new List<string>();

        var kept = await OpenTabsExistence.FilterExistingAsync(tabs, async (db, ct) =>
        {
            var c = new SqlConnection(ConnStr);
            await c.OpenAsync(ct);
            try { await c.ChangeDatabaseAsync(db, ct); }
            catch { await c.DisposeAsync(); throw; }
            return c;
        }, log.Add);

        Assert.Equal(new[] { tabs[0], tabs[1] }, kept);
        Assert.Equal(3, log.Count);
        foreach (var l in log) _out.WriteLine(l);
    }

    // ===== App wiring (source audits) =====

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string AppText(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root(), "SqlExpert.App" }.Concat(parts).ToArray()));

    [Fact]
    public void QuitConfirmation_TextButtonsAndUpdateException()
    {
        var dialog = AppText("Views", "QuitConfirmationWindow.cs");
        Assert.Contains("\"Confirmation\"", dialog);
        Assert.Contains("\"Are you sure you wish to quit SqlExpert?\"", dialog);
        // Yes (default) / No (Esc) through the shared styled message dialog.
        Assert.Contains("MessageBoxButton.YesNo, MessageBoxImage.Question,", dialog);
        Assert.Contains("MessageBoxResult.Yes, DialogLabels.English", dialog);
        Assert.Contains("MessageDialog.Show(owner,", dialog);

        var main = AppText("MainWindow.xaml.cs");
        Assert.Contains("UiSettings.Instance.ConfirmOnExit && UpdateService.Pending is null", main);
        Assert.Contains("QuitConfirmationWindow.Ask(this)", main);
        // Question first, then the unsaved-text / transaction prompts.
        Assert.True(main.IndexOf("QuitConfirmationWindow.Ask(this)", StringComparison.Ordinal)
                    < main.IndexOf("CloseAllForExitAsync()", StringComparison.Ordinal));
        Assert.Contains("_vm.SaveOpenTabsForExit()", main);
        Assert.Contains("SessionEnding += OnSessionEnding", main);
    }

    [Fact]
    public void OptionsMenu_HasConfirmOnExitAndRestoreTabs()
    {
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var doc = XDocument.Parse(AppText("MainWindow.xaml"));
        var options = doc.Descendants(p + "MenuItem").First(m => (string?)m.Attribute("Header") == "_Options");
        var items = options.Elements(p + "MenuItem").ToDictionary(m => (string)m.Attribute("Header")!, m => m);
        Assert.Contains("ConfirmOnExit", (string)items["Confirm on exit"].Attribute("IsChecked")!);
        Assert.Equal("True", (string?)items["Confirm on exit"].Attribute("IsCheckable"));
        Assert.Contains("RestoreOpenTabs", (string)items["Restore open windows on connect"].Attribute("IsChecked")!);
    }

    [Fact]
    public void Session_IsSavedOnDisconnectSwitchReconnectAndExit_RestoredAfterConnect()
    {
        var vm = AppText("ViewModels", "MainViewModel.cs");
        Assert.Equal(4, Regex.Matches(vm, "CloseProfileDocumentsAsync\\(").Count); // switch, reconnect, disconnect, exit
        Assert.Contains("await RestoreOpenTabsAsync(profile)", vm);
        Assert.Contains("DeleteSavedTabs(profile.Id)", vm);
        var session = AppText("ViewModels", "MainViewModel.Session.cs");
        Assert.Contains("UiSettings.Instance.RestoreOpenTabs", session);
        Assert.DoesNotContain("ExecuteAsync", session); // SQL is put back, never run
        Assert.DoesNotContain("Execute", AppText("ViewModels", "SqlEditorDocumentViewModel.cs")
            .Split("public void RestoreSessionText")[1].Split("// ===== History")[0]);
    }

    [Theory]
    [InlineData("MainWindow.xaml", "TableDocumentViewModel.TabCount = 13")]
    [InlineData("Views/ProcedureEditorView.xaml", "ProcedureDocumentViewModel.TabCount = 10")]
    public void TabCounts_MatchTheEditors(string xaml, string expected)
    {
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var doc = XDocument.Parse(AppText(xaml.Split('/')));
        var tc = doc.Descendants(p + "TabControl").Single(t => ((string?)t.Attribute("SelectedIndex") ?? "").Contains("SelectedTabIndex"));
        var count = tc.Elements(p + "TabItem").Count();
        var parts = expected.Split(" = ");
        Assert.Equal(int.Parse(parts[1]), count);
        var file = parts[0].StartsWith("Table") ? AppText("ViewModels", "DocumentViewModels.cs") : AppText("ViewModels", "ProcedureDocumentViewModel.cs");
        Assert.Contains($"public const int TabCount = {count};", file);
    }
}
