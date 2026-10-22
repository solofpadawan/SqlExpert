using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SqlExpert.Core.DataForms;

namespace SqlExpert.App.Views;

/// <summary>
/// Drop-down calculator of numeric fields (Data grid "▾" and Form View), classic look: display on top, then
///   [Back][CE][C]
///   MC  7  8  9  /  sqrt
///   MR  4  5  6  *  %
///   MS  1  2  3  -  1/x
///   M+  0 +/- ,  +  =
/// "=" (or Enter) writes the result into the field; Esc or a click outside closes without changing anything.
/// The keyboard works too: digits, ',' / '.', + - * /, %, Backspace, Delete (CE), F9 (+/-), r (1/x), @ (sqrt),
/// Ctrl+L / Ctrl+R / Ctrl+M / Ctrl+P (MC / MR / MS / M+). Logic lives in <see cref="CalculatorEngine"/>.
/// </summary>
internal sealed class CalculatorPopup
{
    private static readonly Brush DigitBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x3F, 0x8F)));
    private static readonly Brush OperatorBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0x30, 0x30)));
    private static readonly Brush MemoryBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x3A, 0x6A)));

    private readonly CalculatorEngine _engine;
    private readonly Func<decimal, string?> _accept;
    private readonly Popup _popup;
    private readonly Border _root;
    private readonly TextBlock _display;
    private readonly TextBlock _pending;
    private readonly TextBlock _memoryMark;
    private readonly TextBlock _message;
    private readonly Button _mc;
    private readonly Button _mr;
    private bool _done;

    /// <summary>Closed without writing (Esc, click outside).</summary>
    public event Action? Cancelled;
    /// <summary>The result was accepted and written (after <c>accept</c> returned null).</summary>
    public event Action? Accepted;

    public bool IsOpen => _popup.IsOpen;

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    /// <param name="initial">Value of the field (null = NULL / empty: starts at 0).</param>
    /// <param name="accept">Called with the result on "="; returns null when it was written, or a message (overflow) to show.</param>
    public CalculatorPopup(decimal? initial, CultureInfo culture, Func<decimal, string?> accept)
    {
        _engine = new CalculatorEngine(culture, initial);
        _accept = accept;

        _display = new TextBlock { FontSize = 14, FontFamily = new FontFamily("Consolas, Segoe UI"), TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
        _pending = new TextBlock { FontSize = 10, TextAlignment = TextAlignment.Right, MinHeight = 12 }.WithTone(DialogTone.Muted);
        _memoryMark = new TextBlock { Text = "M", FontWeight = FontWeights.Bold, Foreground = MemoryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Memória em uso" };
        var displayStack = new StackPanel();
        displayStack.Children.Add(_pending);
        displayStack.Children.Add(_display);
        var displayDock = new DockPanel();
        DockPanel.SetDock(_memoryMark, Dock.Left);
        displayDock.Children.Add(_memoryMark);
        displayDock.Children.Add(displayStack);
        var displayBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 2, 5, 3),
            Margin = new Thickness(2, 2, 2, 5),
            Child = displayDock
        };
        displayBorder.SetResourceReference(Border.BackgroundProperty, "Input.Background");
        displayBorder.SetResourceReference(Border.BorderBrushProperty, "Input.Border");

        var keys = new Grid();
        for (var c = 0; c < 6; c++) keys.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < 5; r++) keys.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new UniformGrid { Columns = 3, Margin = new Thickness(0) };
        top.Children.Add(KeyButton("Back", CalcKey.Back, OperatorBrush, "Apaga o último dígito (Backspace)"));
        top.Children.Add(KeyButton("CE", CalcKey.ClearEntry, OperatorBrush, "Limpa o número digitado (Del)"));
        top.Children.Add(KeyButton("C", CalcKey.Clear, OperatorBrush, "Limpa tudo (Esc fecha sem gravar)"));
        Grid.SetColumn(top, 1);
        Grid.SetColumnSpan(top, 5);
        keys.Children.Add(top);

        _mc = KeyButton("MC", CalcKey.MemoryClear, MemoryBrush, "Limpa a memória (Ctrl+L)");
        _mr = KeyButton("MR", CalcKey.MemoryRecall, MemoryBrush, "Mostra a memória (Ctrl+R)");
        var layout = new (string Text, CalcKey Key, Brush Brush, string Tip)[,]
        {
            { ("MC", CalcKey.MemoryClear, MemoryBrush, ""), ("7", CalcKey.D7, DigitBrush, ""), ("8", CalcKey.D8, DigitBrush, ""), ("9", CalcKey.D9, DigitBrush, ""), ("/", CalcKey.Divide, OperatorBrush, "Dividir"), ("sqrt", CalcKey.Sqrt, DigitBrush, "Raiz quadrada (@)") },
            { ("MR", CalcKey.MemoryRecall, MemoryBrush, ""), ("4", CalcKey.D4, DigitBrush, ""), ("5", CalcKey.D5, DigitBrush, ""), ("6", CalcKey.D6, DigitBrush, ""), ("*", CalcKey.Multiply, OperatorBrush, "Multiplicar"), ("%", CalcKey.Percent, DigitBrush, "Porcentagem") },
            { ("MS", CalcKey.MemoryStore, MemoryBrush, "Guarda na memória (Ctrl+M)"), ("1", CalcKey.D1, DigitBrush, ""), ("2", CalcKey.D2, DigitBrush, ""), ("3", CalcKey.D3, DigitBrush, ""), ("-", CalcKey.Subtract, OperatorBrush, "Subtrair"), ("1/x", CalcKey.Reciprocal, DigitBrush, "Inverso (r)") },
            { ("M+", CalcKey.MemoryAdd, MemoryBrush, "Soma na memória (Ctrl+P)"), ("0", CalcKey.D0, DigitBrush, ""), ("+/-", CalcKey.Negate, DigitBrush, "Troca o sinal (F9)"), (culture.NumberFormat.NumberDecimalSeparator, CalcKey.Decimal, DigitBrush, "Separador decimal"), ("+", CalcKey.Add, OperatorBrush, "Somar"), ("=", CalcKey.Equals, OperatorBrush, "Grava o resultado no campo (Enter)") },
        };
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 6; c++)
            {
                var (text, key, brush, tip) = layout[r, c];
                var b = key switch
                {
                    CalcKey.MemoryClear => _mc,
                    CalcKey.MemoryRecall => _mr,
                    _ => KeyButton(text, key, brush, tip.Length == 0 ? null : tip)
                };
                if (c == 0) b.Margin = new Thickness(1, 1, 6, 1);
                Grid.SetRow(b, r + 1);
                Grid.SetColumn(b, c);
                keys.Children.Add(b);
            }
        }

        _message = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 250, Margin = new Thickness(2, 4, 2, 0), Visibility = Visibility.Collapsed }.WithTone(DialogTone.Error);

        var stack = new StackPanel { Margin = new Thickness(5) };
        stack.Children.Add(displayBorder);
        stack.Children.Add(keys);
        stack.Children.Add(_message);

        _root = new Border
        {
            BorderThickness = new Thickness(1),
            Child = stack,
            Focusable = true,
            MinWidth = 250
        };
        // Square corners: the popup is not transparent, so a rounded root would show its corners.
        _root.SetResourceReference(Border.BackgroundProperty, "Dialog.PanelBackground");
        _root.SetResourceReference(Border.BorderBrushProperty, "Ui.Border");
        _root.PreviewKeyDown += OnKeyDown;
        _root.PreviewTextInput += OnTextInput;

        _popup = new Popup
        {
            Child = _root,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = false
        };
        _popup.Opened += (_, _) => _root.Focus();
        _popup.Closed += (_, _) =>
        {
            if (_done) return;
            _done = true;
            Cancelled?.Invoke();
        };
        Refresh();
    }

    public void Open(UIElement anchor)
    {
        _popup.PlacementTarget = anchor;
        if (anchor is Control c) TextElement.SetFontSize(_root, Math.Clamp(c.FontSize, 11, 15));
        _popup.IsOpen = true;
    }

    public void Close() => _popup.IsOpen = false;

    private Button KeyButton(string text, CalcKey key, Brush brush, string? tip)
    {
        var b = new Button
        {
            Content = text,
            Foreground = brush,
            MinWidth = 34,
            MinHeight = 26,
            Margin = new Thickness(1),
            Padding = new Thickness(2, 0, 2, 0),
            Focusable = false,
            ToolTip = tip
        }.WithDialogStyle();
        if (tip is not null) ToolTipService.SetShowOnDisabled(b, true);
        b.Click += (_, _) => Press(key);
        return b;
    }

    private void Press(CalcKey key)
    {
        _message.Visibility = Visibility.Collapsed;
        _engine.Press(key);
        Refresh();
        if (key == CalcKey.Equals && !_engine.HasError) TryAccept();
        _root.Focus();
    }

    private void TryAccept()
    {
        var error = _accept(_engine.Value);
        if (error is not null)
        {
            // Overflow: keep the calculator open so the value can be fixed.
            _message.Text = error;
            _message.Visibility = Visibility.Visible;
            return;
        }
        _done = true;
        _popup.IsOpen = false;
        Accepted?.Invoke();
    }

    private void Refresh()
    {
        _display.Text = _engine.Display;
        _display.WithTone(_engine.HasError ? DialogTone.Error : DialogTone.Normal);
        _pending.Text = _engine.PendingText;
        _memoryMark.Visibility = _engine.HasMemory ? Visibility.Visible : Visibility.Hidden;
        _mc.IsEnabled = _engine.HasMemory;
        _mr.IsEnabled = _engine.HasMemory;
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var ch in e.Text)
            if (CalculatorEngine.MapChar(ch) is { } key)
                Press(key);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        CalcKey? key = e.Key switch
        {
            Key.Enter => CalcKey.Equals,
            Key.Back => CalcKey.Back,
            Key.Delete => CalcKey.ClearEntry,
            Key.F9 => CalcKey.Negate,
            Key.L when ctrl => CalcKey.MemoryClear,
            Key.R when ctrl => CalcKey.MemoryRecall,
            Key.M when ctrl => CalcKey.MemoryStore,
            Key.P when ctrl => CalcKey.MemoryAdd,
            _ => null
        };
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (key is null) return;
        Press(key.Value);
        e.Handled = true;
    }
}
