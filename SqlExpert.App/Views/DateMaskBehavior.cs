using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SqlExpert.Core.DataForms;

namespace SqlExpert.App.Views;

/// <summary>
/// Fixed-position input mask on a date/time editor text box (<see cref="DateEditMask"/>): digits only, separators
/// in place, the caret skips them; Backspace / Delete / Cut clear positions, Paste fills them with the digits of the
/// clipboard text. A complete but invalid value gets a red border (and the message as tool tip); committing it is
/// refused by the owner (Data grid / Form View) with <see cref="ShowError"/>.
/// </summary>
internal static class DateMaskBehavior
{
    private static readonly Brush ErrorBrush = Frozen(Color.FromRgb(0xB0, 0x4A, 0x4A));

    private sealed class State(DateMaskKind kind, DateColumnKind dateKind, Type clrType, Func<bool> isActive)
    {
        public DateMaskKind Kind { get; } = kind;
        public DateColumnKind DateKind { get; } = dateKind;
        public Type ClrType { get; } = clrType;
        public Func<bool> IsActive { get; } = isActive;
        public bool Updating { get; set; }
        public bool Marked { get; set; }
        public Brush? Border { get; set; }
        public Thickness BorderThickness { get; set; }
        public object? ToolTip { get; set; }
    }

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("DateMaskState", typeof(State), typeof(DateMaskBehavior));

    private static State? StateOf(TextBox tb) => tb.GetValue(StateProperty) as State;

    /// <summary>Mask of the editor (None = not masked).</summary>
    public static DateMaskKind KindOf(TextBox tb) => StateOf(tb)?.Kind ?? DateMaskKind.None;

    /// <summary>
    /// Puts the mask on <paramref name="tb"/>. <paramref name="isActive"/> (default: always) tells when the text box is
    /// in mask mode (the Form View shows the ISO text while the field is not focused).
    /// </summary>
    public static void Attach(TextBox tb, DateMaskKind kind, DateColumnKind dateKind, Type clrType, Func<bool>? isActive = null)
    {
        if (kind == DateMaskKind.None || StateOf(tb) is not null) return;
        var state = new State(kind, dateKind, clrType, isActive ?? (() => true));
        tb.SetValue(StateProperty, state);
        tb.IsUndoEnabled = false;
        tb.AllowDrop = false;

        tb.PreviewTextInput += (_, e) =>
        {
            if (!state.IsActive() || tb.IsReadOnly) return;
            e.Handled = true;
            foreach (var ch in e.Text)
                Apply(tb, state, DateEditMask.Type(tb.Text, tb.SelectionStart, tb.SelectionLength, ch, kind));
        };
        tb.PreviewKeyDown += (_, e) =>
        {
            if (!state.IsActive() || tb.IsReadOnly || e.Handled) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Back)
            {
                Apply(tb, state, DateEditMask.Backspace(tb.Text, tb.SelectionStart, tb.SelectionLength, kind));
                e.Handled = true;
            }
            else if (key == Key.Delete && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control)
            {
                // (Ctrl+Del on the grid deletes the record before it gets here; in the editor it only clears.)
                Apply(tb, state, DateEditMask.Delete(tb.Text, tb.SelectionStart, tb.SelectionLength, kind));
                e.Handled = true;
            }
            else if (key == Key.Space)
            {
                Apply(tb, state, DateEditMask.Type(tb.Text, tb.SelectionStart, tb.SelectionLength, ' ', kind));
                e.Handled = true;
            }
        };
        CommandManager.AddPreviewExecutedHandler(tb, (_, e) =>
        {
            if (!state.IsActive() || tb.IsReadOnly) return;
            if (e.Command == ApplicationCommands.Paste)
            {
                e.Handled = true;
                string clip;
                try { clip = Clipboard.GetText(); }
                catch (System.Runtime.InteropServices.COMException) { return; }
                Apply(tb, state, DateEditMask.Paste(tb.Text, tb.SelectionStart, tb.SelectionLength, clip, kind));
            }
            else if (e.Command == ApplicationCommands.Cut)
            {
                e.Handled = true;
                if (tb.SelectionLength > 0)
                {
                    try { Clipboard.SetText(tb.SelectedText); }
                    catch (System.Runtime.InteropServices.COMException) { return; }
                }
                Apply(tb, state, DateEditMask.Cut(tb.Text, tb.SelectionStart, tb.SelectionLength, kind));
            }
            else if (e.Command == ApplicationCommands.Undo || e.Command == ApplicationCommands.Redo
                     || e.Command == EditingCommands.DeleteNextWord || e.Command == EditingCommands.DeletePreviousWord)
            {
                e.Handled = true;
            }
        });
        tb.TextChanged += (_, _) =>
        {
            if (state.Updating) return;
            if (!state.IsActive())
            {
                Unmark(tb, state);
                return;
            }
            // Text set by the grid (first typed key), the binding or the calendar: put it in mask shape.
            var shaped = DateEditMask.Normalize(tb.Text, kind);
            if (shaped != tb.Text)
            {
                var caret = tb.CaretIndex;
                state.Updating = true;
                try { tb.Text = shaped; }
                finally { state.Updating = false; }
                tb.Select(Math.Min(caret, shaped.Length), 0);
            }
            Refresh(tb, state);
        };
        if (state.IsActive()) Refresh(tb, state);
    }

    private static void Apply(TextBox tb, State state, MaskEdit edit)
    {
        state.Updating = true;
        try
        {
            if (tb.Text != edit.Text) tb.Text = edit.Text;
            tb.Select(Math.Clamp(edit.Caret, 0, tb.Text.Length), 0);
        }
        finally { state.Updating = false; }
        Refresh(tb, state);
    }

    /// <summary>Red border while a complete text is not a valid value.</summary>
    private static void Refresh(TextBox tb, State state)
    {
        var error = DateEditMask.LiveError(tb.Text, state.Kind, state.DateKind, state.ClrType);
        if (error is null) Unmark(tb, state);
        else Mark(tb, state, error);
    }

    private static void Mark(TextBox tb, State state, string error)
    {
        if (!state.Marked)
        {
            state.Border = tb.BorderBrush;
            state.BorderThickness = tb.BorderThickness;
            state.ToolTip = tb.ToolTip;
            state.Marked = true;
        }
        tb.BorderBrush = ErrorBrush;
        tb.BorderThickness = new Thickness(1);
        tb.ToolTip = error;
    }

    private static void Unmark(TextBox tb, State state)
    {
        if (!state.Marked) return;
        tb.BorderBrush = state.Border;
        tb.BorderThickness = state.BorderThickness;
        tb.ToolTip = state.ToolTip;
        state.Marked = false;
    }

    /// <summary>
    /// Error of committing the editor text (incomplete or invalid; null = can be committed, the empty mask included).
    /// Marks the editor red when there is one.
    /// </summary>
    public static string? CommitError(TextBox tb)
    {
        if (StateOf(tb) is not { } state || !state.IsActive()) return null;
        if (DateEditMask.IsEmpty(tb.Text, state.Kind)) return null;
        if (DateEditMask.TryParse(tb.Text, state.Kind, state.DateKind, state.ClrType, null, out _, out var error)) return null;
        Mark(tb, state, error!);
        return error;
    }

    /// <summary>The refused-commit message (modern dialog), then the caret goes back to the editor.</summary>
    public static void ShowError(TextBox tb, string error)
    {
        var kind = KindOf(tb);
        var caption = kind == DateMaskKind.Time ? "Hora inválida" : "Data inválida";
        MessageDialog.Show(Window.GetWindow(tb), error, caption, MessageBoxButton.OK, MessageBoxImage.Warning,
            detail: "Digite no formato " + DateEditMask.Hint(kind) + " (ex.: " + Example(kind) + "). Esc desfaz a edição.");
        tb.Focus();
        var first = DateEditMask.NextSlot(DateEditMask.Template(kind == DateMaskKind.None ? DateMaskKind.DateTime : kind), 0);
        tb.Select(Math.Max(0, first), 0);
    }

    /// <summary>Shows <see cref="ShowError"/> after the current input event (never re-entrant).</summary>
    public static void ShowErrorLater(TextBox tb, string error)
    {
        if (_showing) return;
        _showing = true;
        tb.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            try { ShowError(tb, error); }
            finally { _showing = false; }
        });
    }

    private static bool _showing;

    /// <summary>True while the refused-commit message is open or about to open.</summary>
    public static bool IsShowingError => _showing;

    private static string Example(DateMaskKind kind) => kind switch
    {
        DateMaskKind.Date => "28/04/2038",
        DateMaskKind.Time => "14:53:13",
        _ => "28/04/2038 14:53:13"
    };

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
