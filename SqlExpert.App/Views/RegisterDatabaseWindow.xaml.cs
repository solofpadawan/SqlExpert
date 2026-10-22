using System.Windows;
using System.Windows.Controls;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Models;

namespace SqlExpert.App.Views;

public partial class RegisterDatabaseWindow : Window
{
    private readonly ISqlConnectionFactory _factory;

    public ConnectionProfile? ResultProfile { get; private set; }
    public string? ResultPassword { get; private set; }

    public RegisterDatabaseWindow(ISqlConnectionFactory factory, ConnectionProfile? existing = null)
    {
        _factory = factory;
        InitializeComponent();
        UpdateAuthUi();

        if (existing is not null)
        {
            DisplayNameBox.Text = existing.DisplayName;
            ServerBox.Text = existing.Server;
            InstanceBox.Text = existing.Instance ?? "";
            PortBox.Text = existing.Port.ToString();
            DatabaseBox.Text = existing.InitialCatalog;
            AuthBox.SelectedIndex = existing.AuthMode == AuthMode.SqlLogin ? 1 : 0;
            UserBox.Text = existing.UserName ?? "";
            EncryptBox.IsChecked = existing.Encrypt;
            TrustBox.IsChecked = existing.TrustServerCertificate;
            TimeoutBox.Text = existing.ConnectTimeoutSeconds.ToString();
            ResultProfile = existing;
            UpdateAuthUi();
        }
    }

    private void AuthBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAuthUi();

    private void UpdateAuthUi()
    {
        if (AuthBox is null || UserBox is null || PasswordBox is null) return; // fired during InitializeComponent
        var sqlAuth = AuthBox.SelectedIndex == 1;
        UserBox.IsEnabled = sqlAuth;
        PasswordBox.IsEnabled = sqlAuth;
    }

    private ConnectionProfile BuildProfile()
    {
        _ = int.TryParse(PortBox.Text, out var port);
        _ = int.TryParse(TimeoutBox.Text, out var timeout);
        if (port <= 0) port = 1433;
        if (timeout <= 0) timeout = 15;

        var profile = ResultProfile ?? new ConnectionProfile();
        profile.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text)
            ? $"{ServerBox.Text}/{DatabaseBox.Text}"
            : DisplayNameBox.Text.Trim();
        profile.Server = ServerBox.Text.Trim();
        profile.Instance = string.IsNullOrWhiteSpace(InstanceBox.Text) ? null : InstanceBox.Text.Trim();
        profile.Port = port;
        profile.InitialCatalog = string.IsNullOrWhiteSpace(DatabaseBox.Text) ? "master" : DatabaseBox.Text.Trim();
        profile.AuthMode = AuthBox.SelectedIndex == 1 ? AuthMode.SqlLogin : AuthMode.Windows;
        profile.UserName = profile.AuthMode == AuthMode.SqlLogin ? UserBox.Text.Trim() : null;
        profile.Encrypt = EncryptBox.IsChecked == true;
        profile.TrustServerCertificate = TrustBox.IsChecked == true;
        profile.ConnectTimeoutSeconds = timeout;
        return profile;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        StatusText.WithTone(DialogTone.Info);
        StatusText.Text = "Testando...";
        try
        {
            var profile = BuildProfile();
            var password = profile.AuthMode == AuthMode.SqlLogin ? PasswordBox.Password : null;
            await _factory.TestConnectionAsync(profile, password);
            StatusText.WithTone(DialogTone.Success);
            StatusText.Text = "Conexão OK.";
        }
        catch (Exception ex)
        {
            StatusText.WithTone(DialogTone.Error);
            StatusText.Text = "Falha: " + ex.Message;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ResultProfile = BuildProfile();
            ResultPassword = ResultProfile.AuthMode == AuthMode.SqlLogin ? PasswordBox.Password : null;
            if (ResultProfile.AuthMode == AuthMode.SqlLogin && string.IsNullOrEmpty(ResultProfile.UserName))
            {
                StatusText.WithTone(DialogTone.Error);
                StatusText.Text = "Informe o login SQL.";
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.WithTone(DialogTone.Error);
            StatusText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
