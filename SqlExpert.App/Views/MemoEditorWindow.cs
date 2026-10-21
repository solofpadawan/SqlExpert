using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SqlExpert.App.Views;

/// <summary>Modal multiline editor for long text values (Form View "..." button). Built in code; opened only by the user.</summary>
public sealed class MemoEditorWindow : Window
{
    private readonly TextBox _text;
    private bool _accepted;

    private MemoEditorWindow(string field, string text, bool readOnly)
    {
        Title = (readOnly ? "Memo (somente leitura): " : "Memo: ") + field;
        Width = 680;
        Height = 440;
        MinWidth = 320;
        MinHeight = 200;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        _text = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            AcceptsTab = true,
            IsReadOnly = readOnly,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Margin = new Thickness(6)
        };

        var wrap = new CheckBox { Content = "Quebrar linhas", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        wrap.Click += (_, _) => _text.TextWrapping = wrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
        var info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Foreground = Brushes.DimGray };
        void UpdateInfo() => info.Text = $"{_text.Text.Length:N0} caractere(s)";
        _text.TextChanged += (_, _) => UpdateInfo();
        UpdateInfo();

        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(4), IsDefault = false, Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible };
        ok.Click += (_, _) => { _accepted = true; Close(); };
        var cancel = new Button { Content = readOnly ? "Fechar" : "Cancelar", Width = 80, Margin = new Thickness(4), IsCancel = true };

        var bottom = new DockPanel { Margin = new Thickness(4, 0, 4, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { wrap, info } });

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_text);
        Content = root;

        Loaded += (_, _) => { _text.Focus(); _text.CaretIndex = 0; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && !readOnly)
            {
                _accepted = true;
                Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>Returns the edited text, or null when cancelled (always null when read-only).</summary>
    public static string? Edit(Window? owner, string field, string text, bool readOnly)
    {
        var w = new MemoEditorWindow(field, text, readOnly);
        if (owner is not null) w.Owner = owner;
        w.ShowDialog();
        return w._accepted && !readOnly ? w._text.Text : null;
    }
}
