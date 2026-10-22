using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace SqlExpert.App.Views;

/// <summary>
/// Light header band of a dialog: 16px icon, title and an optional subtitle ("DialogHeader" styles of Themes/Controls.xaml).
/// Usable from XAML (<c>&lt;views:DialogHeaderBar Title="..." Icon="{StaticResource Icon.Table}"/&gt;</c>) and from code.
/// </summary>
public sealed class DialogHeaderBar : Border
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DialogHeaderBar), new PropertyMetadata("", (d, e) => ((DialogHeaderBar)d)._title.Text = (string?)e.NewValue ?? ""));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(DialogHeaderBar), new PropertyMetadata(null, (d, _) => ((DialogHeaderBar)d).UpdateSubtitle()));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(ImageSource), typeof(DialogHeaderBar), new PropertyMetadata(null, (d, _) => ((DialogHeaderBar)d).UpdateIcon()));

    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = new() { Visibility = Visibility.Collapsed };
    private readonly IconImage _icon = new() { Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

    public DialogHeaderBar()
    {
        SetResourceReference(StyleProperty, "DialogHeader");
        _title.SetResourceReference(StyleProperty, "DialogHeaderText");
        _subtitle.SetResourceReference(StyleProperty, "DialogHeaderSubText");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_title);
        text.Children.Add(_subtitle);
        var row = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Left);
        row.Children.Add(_icon);
        row.Children.Add(text);
        Child = row;
    }

    public DialogHeaderBar(string title, string? iconKey = null, string? subtitle = null) : this()
    {
        Title = title;
        if (iconKey is not null) _icon.SetResourceReference(IconImage.IconProperty, iconKey);
        _icon.Visibility = iconKey is null ? Visibility.Collapsed : Visibility.Visible;
        if (subtitle is not null) Subtitle = subtitle;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    private void UpdateSubtitle()
    {
        _subtitle.Text = Subtitle ?? "";
        _subtitle.Visibility = string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateIcon()
    {
        _icon.Icon = Icon;
        _icon.Visibility = Icon is null ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>
/// Read-only T-SQL preview of a dialog ("SQL que será executado"): monospace AvalonEdit with the T-SQL highlighting
/// of the SQL Editor, light background and a rounded soft border. <see cref="Text"/> works like a TextBox's.
/// </summary>
public sealed class SqlPreviewBox : Border
{
    private readonly TextEditor _editor;

    public SqlPreviewBox()
    {
        SetResourceReference(StyleProperty, "SqlPreviewFrame");
        _editor = new TextEditor
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            ShowLineNumbers = false,
            WordWrap = false,
            Background = Brushes.Transparent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            SyntaxHighlighting = TSqlHighlighting.TryGet()
        };
        _editor.SetResourceReference(Control.ForegroundProperty, "Ui.Text");
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
        Child = _editor;
    }

    public string Text
    {
        get => _editor.Text;
        set => _editor.Text = value ?? "";
    }

    /// <summary>Underlying editor (copy / selection).</summary>
    public TextEditor Editor => _editor;
}

/// <summary>Kinds of dialog status text; each maps to a palette brush of Themes/Controls.xaml.</summary>
public enum DialogTone { Normal, Error, Info, Success, Muted, Note }

/// <summary>Colors dialog status lines from the shared palette instead of hard-coded brushes.</summary>
public static class DialogText
{
    public static string BrushKey(DialogTone tone) => tone switch
    {
        DialogTone.Error => "Ui.ErrorText",
        DialogTone.Info => "Ui.InfoText",
        DialogTone.Success => "Ui.SuccessText",
        DialogTone.Muted => "Ui.MutedText",
        DialogTone.Note => "Ui.NoteText",
        _ => "Ui.Text"
    };

    public static T WithTone<T>(this T element, DialogTone tone) where T : FrameworkElement
    {
        element.SetResourceReference(TextElement.ForegroundProperty, BrushKey(tone));
        return element;
    }

    /// <summary>Sets the text and its tone in one call.</summary>
    public static void Show(this TextBlock block, string text, DialogTone tone)
    {
        block.SetResourceReference(TextBlock.ForegroundProperty, BrushKey(tone));
        block.Text = text;
    }

    /// <summary>A form label using the shared FormLabel style (aligned, greyed when disabled).</summary>
    public static TextBlock Label(string text)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(FrameworkElement.StyleProperty, "FormLabel");
        return block;
    }

    /// <summary>A section caption (e.g. above a list or a preview) using the shared FormSection style.</summary>
    public static TextBlock Section(string text)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(FrameworkElement.StyleProperty, "FormSection");
        return block;
    }

    /// <summary>
    /// Wraps a dialog body in the shared chrome: header band at the top, the body with the standard margin, and the
    /// buttons (if any) in the button band at the bottom.
    /// </summary>
    public static DockPanel Compose(DialogHeaderBar? header, UIElement body, UIElement? buttons, Thickness? bodyMargin = null)
    {
        var root = new DockPanel { LastChildFill = true };
        if (header is not null)
        {
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
        }
        if (buttons is not null)
        {
            var band = new Border { Child = buttons };
            band.SetResourceReference(FrameworkElement.StyleProperty, "DialogButtonBar");
            DockPanel.SetDock(band, Dock.Bottom);
            root.Children.Add(band);
        }
        if (body is FrameworkElement fe) fe.Margin = bodyMargin ?? new Thickness(14, 12, 14, 12);
        root.Children.Add(body);
        return root;
    }
}
