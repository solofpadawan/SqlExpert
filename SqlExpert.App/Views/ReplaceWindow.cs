using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using TextSearch = SqlExpert.Core.SqlEditor.TextSearch;

namespace SqlExpert.App.Views;

/// <summary>Modeless Find/Replace box for one SQL Editor (literal text, match case, whole word).</summary>
public sealed class ReplaceWindow : Window
{
    private readonly TextEditor _editor;
    private readonly TextBox _find = new() { Margin = new Thickness(0, 2, 0, 4), MinWidth = 260 };
    private readonly TextBox _with = new() { Margin = new Thickness(0, 2, 0, 4), MinWidth = 260 };
    private readonly CheckBox _case = new() { Content = "Diferenciar maiúsculas/minúsculas", Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox _word = new() { Content = "Palavra inteira", Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBlock _info = new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap }.WithTone(DialogTone.Muted);

    public ReplaceWindow(TextEditor editor)
    {
        _editor = editor;
        Title = "Replace";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Add(UIElement e, int row, int col, int span = 1)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            Grid.SetColumnSpan(e, span);
            grid.Children.Add(e);
        }

        Add(new Label { Content = "_Localizar:", Target = _find, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        Add(_find, 0, 1);
        Add(new Label { Content = "_Substituir por:", Target = _with, VerticalAlignment = VerticalAlignment.Center }, 1, 0);
        Add(_with, 1, 1);
        Add(_case, 2, 1);
        Add(_word, 3, 1);
        Add(_info, 4, 0, 2);

        var buttons = new StackPanel { Margin = new Thickness(14, 2, 0, 0) };
        Button B(string text, RoutedEventHandler click, bool isDefault = false)
        {
            var b = new Button { Content = text, MinWidth = 110, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 2, 8, 2), IsDefault = isDefault };
            if (isDefault) b.WithPrimaryStyle(); else b.WithDialogStyle();
            b.Click += click;
            buttons.Children.Add(b);
            return b;
        }
        B("Localizar próxima", (_, _) => FindNext(), isDefault: true);
        B("Substituir", (_, _) => ReplaceOne());
        B("Substituir tudo", (_, _) => ReplaceAll());
        B("Fechar", (_, _) => Close()).IsCancel = true;
        Grid.SetRow(buttons, 0);
        Grid.SetColumn(buttons, 2);
        Grid.SetRowSpan(buttons, 5);
        grid.Children.Add(buttons);

        Content = grid;
        SetResourceReference(BackgroundProperty, "Dialog.Background");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    public void Prepare(string? initial)
    {
        if (!string.IsNullOrEmpty(initial)) _find.Text = initial;
        Activate();
        _find.Focus();
        _find.SelectAll();
    }

    private bool FindNext()
    {
        var term = _find.Text;
        if (string.IsNullOrEmpty(term)) { _info.Text = "Informe o texto a localizar."; return false; }
        var text = _editor.Document.Text;
        var start = _editor.SelectionLength > 0 ? _editor.SelectionStart + _editor.SelectionLength : _editor.CaretOffset;
        var (index, length) = TextSearch.FindNext(text, term, start, _case.IsChecked == true, _word.IsChecked == true);
        if (index < 0) { _info.Text = $"'{term}' não encontrado."; return false; }
        _editor.Select(index, length);
        var line = _editor.Document.GetLineByOffset(index).LineNumber;
        _editor.ScrollToLine(line);
        _info.Text = index < start ? $"Linha {line} (recomeçou do início)." : $"Linha {line}.";
        return true;
    }

    private void ReplaceOne()
    {
        var term = _find.Text;
        if (string.IsNullOrEmpty(term)) { _info.Text = "Informe o texto a localizar."; return; }
        if (_editor.SelectionLength > 0 && TextSearch.IsMatch(_editor.SelectedText, term, _case.IsChecked == true, _word.IsChecked == true))
        {
            var start = _editor.SelectionStart;
            _editor.Document.Replace(start, _editor.SelectionLength, _with.Text ?? "");
            _editor.Select(start + (_with.Text ?? "").Length, 0);
        }
        FindNext();
    }

    private void ReplaceAll()
    {
        var term = _find.Text;
        if (string.IsNullOrEmpty(term)) { _info.Text = "Informe o texto a localizar."; return; }
        var (newText, count) = TextSearch.ReplaceAll(_editor.Document.Text, term, _with.Text ?? "", _case.IsChecked == true, _word.IsChecked == true);
        if (count > 0)
            _editor.Document.Replace(0, _editor.Document.TextLength, newText); // one undo step
        _info.Text = $"{count} substituição(ões).";
    }
}
