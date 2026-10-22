using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SqlExpert.Core.DataForms;

namespace SqlExpert.App.Views;

/// <summary>
/// Drop-down calendar of date columns (Data grid "▾" and Form View), classic style: "◀ abril 2011 ▶" header,
/// pt-BR day initials (d s t q q s s), 6 weeks with the adjacent months' days greyed, today framed, the selected
/// date highlighted, [Today] and [Clear] at the bottom. A day click picks and closes; Esc or a click outside
/// closes without changing anything. Layout rules live in <see cref="DateValueText"/>.
/// </summary>
internal sealed class DateCalendarPopup
{
    private static readonly Brush SelectedBack = Frozen(new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)));
    private static readonly Brush HoverBack = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0xF1, 0xFB)));
    private static readonly Brush TodayBorder = Frozen(new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00)));
    private static readonly Brush OtherMonth = Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)));
    private static readonly ControlTemplate FlatTemplate = BuildFlatTemplate();

    private readonly Popup _popup;
    private readonly Border _root;
    private readonly TextBlock _title;
    private readonly UniformGrid _days;
    private readonly DateTime? _selected;
    private DateTime _month;
    private bool _done;

    /// <summary>Picked day (date part only), Today (today's date) or Clear (null).</summary>
    public event Action<DateCalendarResult>? Picked;
    /// <summary>Closed without a pick (Esc, click outside).</summary>
    public event Action? Cancelled;

    public bool IsOpen => _popup.IsOpen;

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    public DateCalendarPopup(DateTime? selected, bool canClear)
    {
        _selected = selected?.Date;
        var start = _selected ?? DateTime.Today;
        _month = new DateTime(start.Year, start.Month, 1);

        var prev = ArrowButton("◀", "Mês anterior");
        prev.Click += (_, _) => { _month = _month.AddMonths(-1); Fill(); };
        var next = ArrowButton("▶", "Próximo mês");
        next.Click += (_, _) => { _month = _month.AddMonths(1); Fill(); };
        _title = new TextBlock { FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(prev, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        header.Children.Add(prev);
        header.Children.Add(next);
        header.Children.Add(_title);

        var initials = new UniformGrid { Columns = 7, Rows = 1 };
        foreach (var d in DateValueText.DayInitials())
            initials.Children.Add(new TextBlock { Text = d, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) }.WithTone(DialogTone.Muted));
        var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = initials };
        line.SetResourceReference(Border.BorderBrushProperty, "Input.Border");

        _days = new UniformGrid { Columns = 7, Rows = 6 };

        var today = new Button { Content = "Today", MinWidth = 64, Margin = new Thickness(2), Focusable = false, ToolTip = DateTime.Today.ToString("dd/MM/yyyy", DateValueText.PtBr) }.WithDialogStyle();
        today.Click += (_, _) => Finish(new DateCalendarResult(DateCalendarAction.Today, DateTime.Today));
        var clear = new Button { Content = "Clear", MinWidth = 64, Margin = new Thickness(2), Focusable = false, IsEnabled = canClear, ToolTip = canClear ? "Gravar NULL" : "Coluna NOT NULL" }.WithDialogStyle();
        ToolTipService.SetShowOnDisabled(clear, true);
        clear.Click += (_, _) => Finish(new DateCalendarResult(DateCalendarAction.Clear, null));
        var buttons = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(today);
        buttons.Children.Add(clear);

        var stack = new StackPanel { Margin = new Thickness(4) };
        stack.Children.Add(header);
        stack.Children.Add(line);
        stack.Children.Add(_days);
        stack.Children.Add(buttons);

        _root = new Border
        {
            BorderThickness = new Thickness(1),
            Child = stack,
            Focusable = true,
            MinWidth = 190
        };
        // Square corners: the popup is not transparent.
        _root.SetResourceReference(Border.BackgroundProperty, "Input.PopupBackground");
        _root.SetResourceReference(Border.BorderBrushProperty, "Ui.Border");
        _root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };

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
        Fill();
    }

    public void Open(UIElement anchor)
    {
        _popup.PlacementTarget = anchor;
        if (anchor is Control c) TextElement.SetFontSize(_root, Math.Clamp(c.FontSize, 11, 16));
        _popup.IsOpen = true;
    }

    public void Close() => _popup.IsOpen = false;

    private void Finish(DateCalendarResult result)
    {
        _done = true;
        _popup.IsOpen = false;
        Picked?.Invoke(result);
    }

    private void Fill()
    {
        _title.Text = DateValueText.MonthTitle(_month.Year, _month.Month);
        _days.Children.Clear();
        var today = DateTime.Today;
        foreach (var day in DateValueText.MonthDays(_month.Year, _month.Month))
        {
            var other = day.Month != _month.Month;
            var selected = _selected == day;
            var b = new Button
            {
                Content = day.Day.ToString(DateValueText.PtBr),
                Template = FlatTemplate,
                Focusable = false,
                Padding = new Thickness(4, 1, 4, 1),
                Background = selected ? SelectedBack : Brushes.Transparent,
                Foreground = selected ? Brushes.White : other ? OtherMonth : Brushes.Black,
                BorderBrush = day == today ? TodayBorder : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                ToolTip = day.ToString("dddd, dd/MM/yyyy", DateValueText.PtBr)
            };
            if (!selected)
            {
                b.MouseEnter += (_, _) => b.Background = HoverBack;
                b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            }
            var picked = day;
            b.Click += (_, _) => Finish(new DateCalendarResult(DateCalendarAction.Day, picked));
            _days.Children.Add(b);
        }
    }

    private static Button ArrowButton(string glyph, string tip) => new()
    {
        Content = glyph,
        Width = 20,
        Template = FlatTemplate,
        Background = Brushes.Transparent,
        BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(1),
        FontFamily = new FontFamily("Segoe UI Symbol"),
        Focusable = false,
        ToolTip = tip
    };

    private static ControlTemplate BuildFlatTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        template.Seal();
        return template;
    }
}

internal enum DateCalendarAction { Day, Today, Clear }

internal sealed record DateCalendarResult(DateCalendarAction Action, DateTime? Date);
