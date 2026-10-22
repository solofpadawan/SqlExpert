using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SqlExpert.App.Services;

namespace SqlExpert.App.Views;

/// <summary>Button captions of a <see cref="MessageDialog"/>; null keeps the default (Sim / Não / Cancelar / OK).</summary>
public sealed record DialogLabels(string? Yes = null, string? No = null, string? Cancel = null, string? Ok = null)
{
    public static DialogLabels English { get; } = new("Yes", "No", "Cancel", "OK");
}

/// <summary>
/// The app's message box: styled like the other dialogs (app icon, DialogButton buttons, vector icon by kind),
/// with the same buttons and results as <see cref="MessageBox"/>. Long text wraps and scrolls; an optional detail
/// (e.g. the server error) is shown in a selectable box; Ctrl+C copies title, message and detail.
/// Enter = the default button; Esc / close = Cancel, else No, else OK.
/// </summary>
public sealed class MessageDialog : Window
{
    private readonly string _message;
    private readonly string? _detail;
    private readonly MessageBoxResult _escapeResult;
    private MessageBoxResult _result;

    private MessageDialog(string message, string caption, MessageBoxButton buttons, MessageBoxImage image,
        MessageBoxResult defaultResult, DialogLabels? labels, string? detail)
    {
        _message = message ?? "";
        _detail = string.IsNullOrWhiteSpace(detail) ? null : detail;
        Title = caption;
        Icon = AppIcon.Image;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 300;
        MaxWidth = 640;
        UseLayoutRounding = true;

        var kinds = ResultsOf(buttons);
        _escapeResult = EscapeResult(buttons);
        _result = _escapeResult;
        if (!kinds.Contains(defaultResult)) defaultResult = kinds[0];

        var root = new DockPanel { Margin = new Thickness(14, 14, 14, 10) };

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        Button? focus = null;
        foreach (var kind in kinds)
        {
            var b = new Button
            {
                Content = Caption(kind, labels),
                MinWidth = 75,
                IsDefault = kind == defaultResult,
                IsCancel = kind == _escapeResult,
                Margin = new Thickness(buttonRow.Children.Count == 0 ? 0 : 6, 0, 0, 0),
                Tag = kind
            }.WithDialogStyle();
            b.Click += (_, _) => { _result = kind; DialogResult = true; };
            if (kind == defaultResult) focus = b;
            buttonRow.Children.Add(b);
        }
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);

        var icon = KindIcon(image);
        if (icon is not null)
        {
            DockPanel.SetDock(icon, Dock.Left);
            root.Children.Add(icon);
        }

        var texts = new StackPanel { Margin = new Thickness(icon is null ? 0 : 12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = _message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
        if (_detail is not null)
            texts.Children.Add(new TextBox
            {
                Text = _detail,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 160,
                MaxWidth = 520,
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(3),
                Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7))
            });
        root.Children.Add(new ScrollViewer
        {
            Content = texts,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 420,
            Focusable = false
        });

        Content = root;
        Loaded += (_, _) => focus?.Focus();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
        {
            if (e.OriginalSource is TextBox { SelectionLength: > 0 }) return; // copying a selection of the detail
            CopyToClipboard();
            e.Handled = true;
        }));
    }

    /// <summary>Result of a closed dialog (title-bar X / Alt+F4 = the Esc result).</summary>
    public MessageBoxResult Result => _result;

    /// <summary>"[Title]\n\nmessage\n\ndetail", the text Ctrl+C copies.</summary>
    public string ClipboardText => _detail is null ? $"[{Title}]{Environment.NewLine}{Environment.NewLine}{_message}"
        : $"[{Title}]{Environment.NewLine}{Environment.NewLine}{_message}{Environment.NewLine}{Environment.NewLine}{_detail}";

    private void CopyToClipboard()
    {
        try { Clipboard.SetText(ClipboardText); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy */ }
    }

    // ===== API (same shape as MessageBox.Show) =====

    /// <summary>Shows the dialog over <paramref name="owner"/> (null = the active window of the app).</summary>
    public static MessageBoxResult Show(Window? owner, string message, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None, DialogLabels? labels = null, string? detail = null)
    {
        var app = Application.Current;
        if (app is null || !app.Dispatcher.CheckAccess())
            return FallbackShow(message, caption, buttons, image, defaultResult, detail);

        var dialog = Create(message, caption, buttons, image, defaultResult, labels, detail);
        owner ??= ActiveWindow(app);
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, dialog)) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog.Result;
    }

    /// <summary>Shows the dialog over the active window of the app.</summary>
    public static MessageBoxResult Show(string message, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None, DialogLabels? labels = null, string? detail = null)
        => Show(null, message, caption, buttons, image, defaultResult, labels, detail);

    /// <summary>The dialog without showing it (off-screen previews and tests).</summary>
    public static MessageDialog Create(string message, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None, DialogLabels? labels = null, string? detail = null)
        => new(message, caption, buttons, image, defaultResult, labels, detail);

    /// <summary>Only when no WPF application is running on this thread (nothing to style the dialog with).</summary>
    private static MessageBoxResult FallbackShow(string message, string caption, MessageBoxButton buttons, MessageBoxImage image,
        MessageBoxResult defaultResult, string? detail)
        => MessageBox.Show(detail is null ? message : message + "\n\n" + detail, caption, buttons, image, defaultResult);

    private static Window? ActiveWindow(Application app)
    {
        foreach (Window w in app.Windows)
            if (w.IsActive && w.IsVisible) return w;
        return app.MainWindow is { IsVisible: true } main ? main : null;
    }

    // ===== Buttons =====

    public static IReadOnlyList<MessageBoxResult> ResultsOf(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
        MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
        MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
        _ => new[] { MessageBoxResult.OK }
    };

    /// <summary>What Esc / the close button answers: Cancel when offered, else No, else OK.</summary>
    public static MessageBoxResult EscapeResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        _ => MessageBoxResult.OK
    };

    public static string Caption(MessageBoxResult kind, DialogLabels? labels) => kind switch
    {
        MessageBoxResult.Yes => labels?.Yes ?? "Sim",
        MessageBoxResult.No => labels?.No ?? "Não",
        MessageBoxResult.Cancel => labels?.Cancel ?? "Cancelar",
        _ => labels?.Ok ?? "OK"
    };

    // ===== Icons (vector, muted) =====

    /// <summary>Disc colour and glyph of each kind (question / information: blue, warning: amber, error: red).</summary>
    public static (Color Top, Color Bottom, Color Stroke, string Glyph)? IconStyle(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Question => (Color.FromRgb(0x6F, 0x93, 0xC4), Color.FromRgb(0x3E, 0x63, 0x96), Color.FromRgb(0x33, 0x52, 0x7D), "?"),
        MessageBoxImage.Information => (Color.FromRgb(0x7C, 0x9C, 0xC8), Color.FromRgb(0x4A, 0x6E, 0x9E), Color.FromRgb(0x3B, 0x5A, 0x84), "i"),
        MessageBoxImage.Warning => (Color.FromRgb(0xE3, 0xBE, 0x6A), Color.FromRgb(0xC2, 0x93, 0x38), Color.FromRgb(0x9C, 0x75, 0x2A), "!"),
        MessageBoxImage.Error => (Color.FromRgb(0xC9, 0x6E, 0x66), Color.FromRgb(0xA4, 0x48, 0x41), Color.FromRgb(0x84, 0x38, 0x33), "\u2715"),
        _ => null
    };

    private static FrameworkElement? KindIcon(MessageBoxImage image)
    {
        if (IconStyle(image) is not { } s) return null;
        var grid = new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        var fill = new LinearGradientBrush(s.Top, s.Bottom, 90);
        fill.Freeze();
        var stroke = new SolidColorBrush(s.Stroke);
        stroke.Freeze();
        if (image == MessageBoxImage.Warning)
            grid.Children.Add(new Polygon
            {
                Points = new PointCollection { new(16, 2), new(31, 29), new(1, 29) },
                Fill = fill, Stroke = stroke, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round
            });
        else
            grid.Children.Add(new Ellipse { Fill = fill, Stroke = stroke, StrokeThickness = 1 });
        grid.Children.Add(new TextBlock
        {
            Text = s.Glyph,
            Foreground = Brushes.White,
            FontFamily = new FontFamily(image == MessageBoxImage.Information ? "Georgia" : "Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = image switch { MessageBoxImage.Error => 15, MessageBoxImage.Warning => 18, _ => 22 },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = image == MessageBoxImage.Warning ? new Thickness(0, 6, 0, 0) : new Thickness(0, -2, 0, 0)
        });
        return grid;
    }
}
