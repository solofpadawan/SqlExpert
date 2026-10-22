using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SqlExpert.App.Services;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Models;
using SqlExpert.Core.Registration;
using SqlExpert.Core.Services;

namespace SqlExpert.App.Views;

/// <summary>
/// "Database Properties" (Database Registration Info): General / Additional / Scripts / Color Mark pages.
/// Edits a copy of the registration; the caller saves <see cref="ResultProfile"/> and the password.
/// </summary>
public partial class DatabasePropertiesWindow : Window
{
    private readonly ISqlConnectionFactory _factory;
    private readonly ICredentialStore _credentials;
    private readonly ConnectionProfile _baseProfile;
    private readonly IReadOnlyList<ConnectionProfile> _others;
    private bool _passwordDirty;
    private bool _settingPassword;
    private bool _loadingDatabases;
    private bool _testing;

    public RegistrationForm Form { get; }

    /// <summary>Edited registration (same Id as the input). Set when the dialog returns true.</summary>
    public ConnectionProfile? ResultProfile { get; private set; }

    /// <summary>New password typed (or copied); null = keep the stored one.</summary>
    public string? ResultPassword { get; private set; }

    /// <param name="profile">Registration to edit (a copy is edited; the input is not changed).</param>
    /// <param name="others">Other registrations (Copy Registration Data..., recent servers).</param>
    /// <param name="initialPassword">Password pre-filled as "typed" (Clone Registration Info).</param>
    public DatabasePropertiesWindow(
        ISqlConnectionFactory factory,
        ICredentialStore credentials,
        ConnectionProfile profile,
        IReadOnlyList<ConnectionProfile> others,
        string? initialPassword = null)
    {
        _factory = factory;
        _credentials = credentials;
        _baseProfile = profile.Clone();
        _others = others;
        Form = RegistrationForm.FromProfile(_baseProfile);
        foreach (var s in RegistrationForm.BuildRecentServers(others.Prepend(_baseProfile)))
            Form.RecentServers.Add(s);
        if (!string.IsNullOrWhiteSpace(_baseProfile.InitialCatalog))
            Form.Databases.Add(_baseProfile.InitialCatalog);

        InitializeComponent();
        DataContext = Form;
        ShowPage("General");
        Title = string.IsNullOrWhiteSpace(profile.DisplayName) ? "Database Properties" : $"Database Properties - {profile.DisplayName}";
        CopyButton.IsEnabled = others.Count > 0;
        FetchRowsHint.Text = $"Vazio = padrão global ({UiSettings.Instance.SqlFetchRows:N0}).";
        BuildSwatches();

        if (initialPassword is not null)
        {
            SetPassword(initialPassword);
        }
        else if (_baseProfile.AuthMode == AuthMode.SqlLogin && !string.IsNullOrEmpty(_baseProfile.PasswordSecretId))
        {
            PasswordHint.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) =>
        {
            AliasBox.Focus();
            AliasBox.SelectAll();
        };
    }

    // ===== Navigation =====

    private void NavTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: string page }) return;
        ShowPage(page);
    }

    private void ShowPage(string page)
    {
        // SelectedItemChanged can fire while InitializeComponent is still building the tree (pages not created yet).
        if (PageGeneral is null || PageDbExplorer is null || PageSqlEditor is null || PageAfterConnect is null
            || PageBeforeDisconnect is null || PageColorMark is null) return;
        PageGeneral.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageDbExplorer.Visibility = page == "DbExplorer" ? Visibility.Visible : Visibility.Collapsed;
        PageSqlEditor.Visibility = page == "SqlEditor" ? Visibility.Visible : Visibility.Collapsed;
        PageAfterConnect.Visibility = page == "AfterConnect" ? Visibility.Visible : Visibility.Collapsed;
        PageBeforeDisconnect.Visibility = page == "BeforeDisconnect" ? Visibility.Visible : Visibility.Collapsed;
        PageColorMark.Visibility = page == "ColorMark" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectPage(string page)
    {
        foreach (var item in AllItems(NavTree.Items))
        {
            if (item.Tag as string == page && item.IsEnabled)
            {
                item.IsSelected = true;
                return;
            }
        }
        ShowPage(page);
    }

    private static IEnumerable<TreeViewItem> AllItems(ItemCollection items)
    {
        foreach (var o in items)
        {
            if (o is not TreeViewItem t) continue;
            yield return t;
            foreach (var c in AllItems(t.Items)) yield return c;
        }
    }

    // ===== Password =====

    private void SetPassword(string password)
    {
        _settingPassword = true;
        try { PasswordBox.Password = password; }
        finally { _settingPassword = false; }
        _passwordDirty = true;
        PasswordHint.Visibility = Visibility.Collapsed;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_settingPassword) return;
        _passwordDirty = true;
        PasswordHint.Visibility = Visibility.Collapsed;
    }

    /// <summary>Password for a connection attempt: the typed one, or null (= stored secret).</summary>
    private string? PasswordForConnect() => Form.IsSqlLogin && _passwordDirty ? PasswordBox.Password : null;

    // ===== Status =====

    private void SetStatus(string text, DialogTone tone)
    {
        StatusText.WithTone(tone);
        StatusText.Text = text;
        StatusText.ToolTip = text;
    }

    /// <summary>Profile built from the form for Test Connect / database list (null + message when invalid).</summary>
    private ConnectionProfile? BuildConnectionProfile()
    {
        var errors = Form.Validate(connectionOnly: true);
        if (errors.Count > 0)
        {
            SetStatus(errors[0], DialogTone.Error);
            return null;
        }
        try
        {
            var p = Form.ApplyTo(_baseProfile.Clone());
            // Validate the extra parameters through the real builder before going to the network.
            _factory.BuildConnectionString(p, "x");
            return p;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            SetStatus(ex.Message, DialogTone.Error);
            return null;
        }
    }

    // ===== Test Connect / database list =====

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_testing) return;
        var profile = BuildConnectionProfile();
        if (profile is null)
        {
            SelectPage("General");
            return;
        }

        _testing = true;
        TestButton.IsEnabled = false;
        SetStatus($"Conectando a {SqlConnectionFactory.BuildDataSource(profile)}...", DialogTone.Info);
        try
        {
            await using var conn = await _factory.OpenAsync(profile, PasswordForConnect()).ConfigureAwait(true);
            var version = await RegistrationScripts.GetServerVersionAsync(conn).ConfigureAwait(true);
            Form.ServerVersion = version;
            SetStatus("Conexão OK — " + version, DialogTone.Success);
            MessageDialog.Show(this, $"Conexão OK.\n\n{version}\nDatabase: {profile.InitialCatalog}", "Test Connect",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus("Falha: " + ex.Message, DialogTone.Error);
            MessageDialog.Show(this, "Não foi possível conectar.", "Test Connect",
                MessageBoxButton.OK, MessageBoxImage.Warning, detail: ex.Message);
        }
        finally
        {
            _testing = false;
            TestButton.IsEnabled = true;
        }
    }

    private async void DatabaseCombo_OnDropDownOpened(object? sender, EventArgs e)
    {
        if (_loadingDatabases) return;
        var profile = BuildConnectionProfile();
        if (profile is null) return;

        _loadingDatabases = true;
        SetStatus("Lendo os databases do servidor...", DialogTone.Info);
        try
        {
            profile.InitialCatalog = "master";
            await using var conn = await _factory.OpenAsync(profile, PasswordForConnect()).ConfigureAwait(true);
            var databases = await RegistrationScripts.ListDatabasesAsync(conn).ConfigureAwait(true);
            Form.ServerVersion = await RegistrationScripts.GetServerVersionAsync(conn).ConfigureAwait(true);
            var current = Form.Database;
            Form.Databases.Clear();
            foreach (var d in databases) Form.Databases.Add(d);
            Form.Database = current;
            DatabaseCombo.Text = current;
            SetStatus($"{databases.Count} database(s) em {SqlConnectionFactory.BuildDataSource(profile)}.", DialogTone.Success);
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível listar os databases: " + ex.Message, DialogTone.Error);
        }
        finally
        {
            _loadingDatabases = false;
        }
    }

    // ===== Copy Registration Data... =====

    private async void CopyRegistration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var source = ChooseRegistrationWindow.Choose(this, _others, "Copy Registration Data",
                "Copiar os dados de conexão de qual registro? (o Database Alias e a cor deste registro são mantidos)");
            if (source is null) return;

            Form.CopyFrom(source);
            if (source.AuthMode == AuthMode.SqlLogin && !string.IsNullOrEmpty(source.PasswordSecretId))
                SetPassword(await _credentials.GetSecretAsync(source.PasswordSecretId).ConfigureAwait(true) ?? "");
            else
                SetPassword("");
            SelectPage("General");
            SetStatus($"Dados copiados de '{source.DisplayName}'. O alias foi mantido.", DialogTone.Success);
        }
        catch (Exception ex)
        {
            SetStatus("Falha ao copiar: " + ex.Message, DialogTone.Error);
        }
    }

    // ===== Color Mark =====

    private void BuildSwatches()
    {
        foreach (var (name, hex) in ColorMarks.Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var button = new Button
            {
                Width = 28,
                Height = 22,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(color),
                ToolTip = $"{name} ({hex})",
                Tag = hex
            };
            button.SetResourceReference(BorderBrushProperty, "Ui.Border");
            button.Click += (_, _) => Form.ColorMark = hex;
            SwatchPanel.Children.Add(button);
        }
    }

    private void NoColor_Click(object sender, RoutedEventArgs e) => Form.ColorMark = "";

    // ===== OK / Cancel =====

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var errors = Form.Validate();
        if (errors.Count > 0)
        {
            var first = errors[0];
            SelectPage(first.StartsWith("Color", StringComparison.Ordinal) ? "ColorMark"
                : first.StartsWith("Additional >", StringComparison.Ordinal) ? "SqlEditor"
                : "General");
            MessageDialog.Show(this, "Corrija antes de salvar:\n\n• " + string.Join("\n• ", errors), "Database Properties",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = Form.ApplyTo(_baseProfile.Clone());
            _factory.BuildConnectionString(result, "x"); // extra parameters accepted by SqlClient
            ResultProfile = result;
            ResultPassword = Form.IsSqlLogin && _passwordDirty ? PasswordBox.Password : null;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            SelectPage("General");
            MessageDialog.Show(this, ex.Message, "Database Properties", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
