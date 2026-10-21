using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SqlExpert.Core.Updates;

namespace SqlExpert.App.Views;

public enum UpdateChoice { Later, Skip, Install }

/// <summary>
/// "Atualização disponível": new and current version, release notes, the pre-release option and the
/// Atualizar agora / Depois / Pular esta versão buttons. The download runs inside the dialog with progress and Cancel.
/// </summary>
public sealed class UpdateWindow : Window
{
    private readonly Func<IProgress<DownloadProgress>, CancellationToken, Task<bool>> _install;
    private readonly CheckBox _preRelease;
    private readonly ProgressBar _bar = new() { Height = 16, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _installButton, _laterButton, _skipButton, _cancelButton;
    private readonly bool _initialPreRelease;
    private CancellationTokenSource? _cts;

    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;
    public bool IncludePreReleases => _preRelease.IsChecked == true;
    public bool PreReleaseChanged => IncludePreReleases != _initialPreRelease;

    public UpdateWindow(UpdateCandidate candidate, SemVersion current, bool includePreReleases,
        Func<IProgress<DownloadProgress>, CancellationToken, Task<bool>> install)
    {
        _install = install;
        _initialPreRelease = includePreReleases;
        Title = "SqlExpert - Atualização disponível";
        Width = 620;
        Height = 520;
        MinWidth = 460;
        MinHeight = 380;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "Uma nova versão do SqlExpert está disponível.",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var facts = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var isPre = candidate.Release.IsPreRelease || candidate.Version.IsPreRelease;
        var published = candidate.Release.PublishedAt is { } d ? " — publicada em " + d.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";
        AddFact(facts, 0, "Nova versão:", candidate.Version + (isPre ? " (pre-release)" : "") + published, bold: true);
        AddFact(facts, 1, "Versão atual:", current.ToString(), bold: false);
        AddFact(facts, 2, "Pacote:", $"{candidate.Package.Name} ({FormatSize(candidate.Package.Size)})", bold: false);
        header.Children.Add(facts);
        header.Children.Add(new TextBlock { Text = "Notas da versão:", Margin = new Thickness(0, 0, 0, 3) });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        _preRelease = new CheckBox
        {
            Content = "Receber versões de teste (pre-release)",
            IsChecked = includePreReleases,
            ToolTip = "Também oferece versões marcadas como pre-release no GitHub."
        };
        bottom.Children.Add(_preRelease);
        bottom.Children.Add(_bar);
        bottom.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        _installButton = new Button { Content = "Atualizar agora", MinWidth = 110, IsDefault = true }.WithDialogStyle();
        _laterButton = new Button { Content = "Depois", MinWidth = 80, IsCancel = true, Margin = new Thickness(6, 0, 0, 0) }.WithDialogStyle();
        _skipButton = new Button { Content = "Pular esta versão", MinWidth = 110, Margin = new Thickness(6, 0, 0, 0) }.WithDialogStyle();
        _cancelButton = new Button { Content = "Cancelar", MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed }.WithDialogStyle();
        buttons.Children.Add(_installButton);
        buttons.Children.Add(_laterButton);
        buttons.Children.Add(_skipButton);
        buttons.Children.Add(_cancelButton);
        bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        root.Children.Add(new TextBox
        {
            Text = ReleaseSelector.NotesToPlainText(candidate.Release.Body),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            Padding = new Thickness(4)
        });
        Content = root;

        _installButton.Click += async (_, _) => await InstallAsync();
        _laterButton.Click += (_, _) => { Choice = UpdateChoice.Later; Close(); };
        _skipButton.Click += (_, _) => { Choice = UpdateChoice.Skip; Close(); };
        _cancelButton.Click += (_, _) => { _cts?.Cancel(); _status.Text = "Cancelando..."; };
        Closing += (_, e) =>
        {
            if (_cts is null) return;
            e.Cancel = true; // a download is running: Cancel first
            _cts.Cancel();
        };
    }

    private static void AddFact(Grid g, int row, string label, string value, bool bold)
    {
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = new TextBlock { Text = label, Margin = new Thickness(0, 1, 8, 1) };
        var v = new TextBlock { Text = value, Margin = new Thickness(0, 1, 0, 1), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(l, row);
        Grid.SetRow(v, row);
        Grid.SetColumn(v, 1);
        g.Children.Add(l);
        g.Children.Add(v);
    }

    public static string FormatSize(long bytes) =>
        bytes < 0 ? "?" : bytes >= 1024 * 1024
            ? (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) + " MB"
            : (bytes / 1024d).ToString("0", CultureInfo.GetCultureInfo("pt-BR")) + " KB";

    private void SetBusy(bool busy)
    {
        _installButton.IsEnabled = _laterButton.IsEnabled = _skipButton.IsEnabled = _preRelease.IsEnabled = !busy;
        _cancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _bar.Visibility = busy ? Visibility.Visible : _bar.Visibility;
    }

    private async Task InstallAsync()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        _status.Foreground = Brushes.DarkBlue;
        _status.Text = "Baixando...";
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (_cts?.IsCancellationRequested == true) return;
            if (p.Total > 0)
            {
                _bar.IsIndeterminate = false;
                _bar.Maximum = p.Total;
                _bar.Value = Math.Min(p.Received, p.Total);
                _status.Text = $"Baixando... {FormatSize(p.Received)} de {FormatSize(p.Total)}";
            }
            else
            {
                _bar.IsIndeterminate = true;
                _status.Text = $"Baixando... {FormatSize(p.Received)}";
            }
            if (p.Total > 0 && p.Received >= p.Total) _status.Text = "Conferindo e extraindo o pacote...";
        });
        var ok = false;
        try
        {
            ok = await _install(progress, _cts.Token).ConfigureAwait(true);
            if (!ok)
            {
                _status.Foreground = Brushes.DimGray;
                _status.Text = "Atualização não instalada.";
            }
        }
        catch (OperationCanceledException)
        {
            _status.Foreground = Brushes.DimGray;
            _status.Text = "Download cancelado.";
        }
        catch (Exception ex)
        {
            SqlExpert.Core.Updates.UpdateLog.Write("atualização falhou: " + ex);
            _status.Foreground = Brushes.DarkRed;
            _status.Text = "Falhou: " + ex.Message;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
        if (ok)
        {
            Choice = UpdateChoice.Install;
            Close();
        }
    }
}

/// <summary>Small window of the updater mode (SqlExpert.exe --apply-update): shows what is happening while files are replaced.</summary>
public sealed class UpdaterProgressWindow : Window
{
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public UpdaterProgressWindow(string version)
    {
        Title = "SqlExpert - Atualização";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock { Text = $"Instalando o SqlExpert {version}...", FontWeight = FontWeights.SemiBold });
        root.Children.Add(new ProgressBar { Height = 14, IsIndeterminate = true, Margin = new Thickness(0, 10, 0, 0) });
        root.Children.Add(_text);
        Content = root;
    }

    public void SetStatus(string text)
    {
        if (Dispatcher.CheckAccess()) _text.Text = text;
        else Dispatcher.BeginInvoke(() => _text.Text = text);
    }
}
