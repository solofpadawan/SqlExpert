using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SqlExpert.App.Views;

/// <summary>Progress of a run over many objects (Recompile all…, Recompute selectivity…), with Cancel.</summary>
public sealed class BatchProgressWindow : Window
{
    private readonly ProgressBar _bar = new() { Height = 18, Minimum = 0, Maximum = 1 };
    private readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CancellationTokenSource _cts = new();
    private bool _finished;

    public BatchProgressWindow(string title)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(_bar);
        root.Children.Add(_text);
        var cancel = new Button { Content = "Cancel", MinWidth = 75, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), IsCancel = true };
        cancel.Click += (_, _) => { _cts.Cancel(); _text.Text = "Cancelando (termina o objeto atual)..."; };
        root.Children.Add(cancel);
        Content = root;
        Closing += (_, e) =>
        {
            if (_finished) return;
            e.Cancel = true;
            _cts.Cancel();
        };
    }

    public CancellationToken Token => _cts.Token;

    public IProgress<(int Done, int Total, string Current)> Progress => new Progress<(int Done, int Total, string Current)>(p =>
    {
        _bar.Maximum = Math.Max(1, p.Total);
        _bar.Value = p.Done;
        if (!_cts.IsCancellationRequested)
            _text.Text = p.Current.Length == 0 ? $"{p.Done} de {p.Total}" : $"{p.Done + 1} de {p.Total}: {p.Current}";
    });

    /// <summary>Closes the window at the end of the run.</summary>
    public void Finish()
    {
        _finished = true;
        Close();
        _cts.Dispose();
    }
}

/// <summary>Read-only text report (failures of a batch run) with Copy.</summary>
public sealed class TextReportWindow : Window
{
    private TextReportWindow(string title, string text)
    {
        Title = title;
        Width = 760;
        Height = 460;
        MinWidth = 400;
        MinHeight = 220;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new DockPanel { Margin = new Thickness(10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var copy = new Button { Content = "Copiar", MinWidth = 75 };
        copy.Click += (_, _) => Clipboard.SetText(text);
        var close = new Button { Content = "Fechar", MinWidth = 75, IsCancel = true, IsDefault = true, Margin = new Thickness(6, 0, 0, 0) };
        buttons.Children.Add(copy);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new TextBox
        {
            Text = text,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        Content = root;
    }

    public static void ShowReport(Window? owner, string title, string text)
    {
        var w = new TextReportWindow(title, text);
        if (owner is not null) w.Owner = owner;
        w.ShowDialog();
    }
}
