using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SqlExpert.Core.DataForms;

namespace SqlExpert.App.Views;

/// <summary>Form View: the current record of the Data tab, one row per column (name | type | null | value).</summary>
public partial class TableDataGrid
{
    private static readonly Brush ReadOnlyBackground = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
    private static readonly Brush TypeForeground = Frozen(new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)));

    private readonly List<FormEditor> _formEditors = new();

    private sealed record FormEditor(FormFieldSpec Spec, Control Focus, TextBox? Text);

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private void RebuildForm()
    {
        FormGrid.Children.Clear();
        FormGrid.RowDefinitions.Clear();
        FormGrid.ColumnDefinitions.Clear();
        _formEditors.Clear();
        if (_vm is null || _vm.Columns.Count == 0)
        {
            FormGrid.Children.Add(new TextBlock { Text = "Sem dados carregados.", Foreground = TypeForeground, Margin = new Thickness(4) });
            return;
        }

        FormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 260 });
        FormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 150 });
        FormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        FormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });

        var row = 0;
        foreach (var col in _vm.Columns)
        {
            var spec = FormFieldLayout.Build(col.Field, col.Info, col.DataType, col.IsReadOnly, _vm.IsReadOnly);
            FormGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var name = new TextBlock
            {
                Text = spec.Name,
                FontWeight = spec.IsBold ? FontWeights.Bold : FontWeights.Normal,
                ToolTip = spec.ToolTip,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 10, 3)
            };
            Place(name, row, 0);

            var type = new TextBlock
            {
                Text = spec.TypeText,
                ToolTip = spec.TypeText,
                Foreground = TypeForeground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 8, 3)
            };
            Place(type, row, 1);

            var nullBox = new CheckBox
            {
                IsEnabled = spec.CanToggleNull,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = spec.IsNullable ? "null (marque para gravar NULL)" : "NOT NULL",
                Focusable = false
            };
            nullBox.SetBinding(ToggleButton.IsCheckedProperty, new Binding(spec.Field) { Mode = BindingMode.OneWay, Converter = new IsDbNullConverter() });
            nullBox.Click += (_, _) => ToggleNull(spec, nullBox);
            Place(nullBox, row, 2);

            var editor = CreateEditor(spec, col.HasEditableLookup && !_vm.IsReadOnly ? col.Lookup : null);
            Place(editor.Host, row, 3);
            _formEditors.Add(editor.Editor);
            row++;
        }
    }

    private void Place(UIElement element, int row, int column)
    {
        System.Windows.Controls.Grid.SetRow(element, row);
        System.Windows.Controls.Grid.SetColumn(element, column);
        FormGrid.Children.Add(element);
    }

    private (FrameworkElement Host, FormEditor Editor) CreateEditor(FormFieldSpec spec, Core.Models.ForeignKeyLookupTarget? lookup = null)
    {
        if (spec.Kind == FormFieldKind.Boolean)
        {
            var cb = new CheckBox
            {
                IsThreeState = spec.IsNullable,
                IsEnabled = !spec.IsReadOnly,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 4, 0, 4),
                ToolTip = spec.ToolTip
            };
            cb.SetBinding(ToggleButton.IsCheckedProperty, new Binding(spec.Field)
            {
                Converter = new NullableBoolConverter(),
                Mode = spec.IsReadOnly ? BindingMode.OneWay : BindingMode.TwoWay
            });
            return (cb, new FormEditor(spec, cb, null));
        }

        var tb = new TextBox
        {
            IsReadOnly = spec.IsReadOnly,
            Background = spec.IsReadOnly ? ReadOnlyBackground : Brushes.White,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 22,
            Margin = new Thickness(0, 2, 0, 2),
            MinLines = 1,
            MaxLines = 1,
            TextWrapping = TextWrapping.NoWrap,
            ToolTip = spec.ToolTip
        };
        if (spec.Kind == FormFieldKind.Number)
        {
            tb.TextAlignment = TextAlignment.Right;
            tb.MaxWidth = 260;
            tb.HorizontalAlignment = HorizontalAlignment.Left;
            tb.MinWidth = 120;
            tb.PreviewTextInput += (_, e) =>
            {
                var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
                if (!FormFieldLayout.IsAcceptableNumericInput(proposed, spec.ClrType, CultureInfo.CurrentCulture))
                    e.Handled = true;
            };
        }
        if (spec.Kind is FormFieldKind.Binary or FormFieldKind.ReadOnlyText)
            tb.FontFamily = spec.Kind == FormFieldKind.Binary ? new FontFamily("Consolas") : tb.FontFamily;

        tb.SetBinding(TextBox.TextProperty, new Binding(spec.Field)
        {
            Converter = new FormValueConverter(spec.ClrType),
            Mode = spec.IsReadOnly ? BindingMode.OneWay : BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
            ValidatesOnExceptions = true
        });
        tb.LostKeyboardFocus += (_, _) => UpdateIfDirty(tb);
        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                UpdateIfDirty(tb);
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                e.Handled = true;
            }
        };
        var editor = new FormEditor(spec, tb, tb);

        if (lookup is not null && !spec.IsReadOnly)
        {
            // Foreign key: "▾" opens the same "Data of [table]" popup as the grid (also Alt+↓ / F4).
            var button = SideButton("▾", $"Escolher em {lookup.ForeignKey.ReferencedTitle} (Alt+↓ / F4)");
            var host = WithSideButton(tb, button);
            button.Click += (_, _) => OpenFormLookup(lookup, tb, host);
            tb.PreviewKeyDown += (_, e) =>
            {
                if (!ForeignKeyTextColumn.IsLookupGesture(e)) return;
                OpenFormLookup(lookup, tb, host);
                e.Handled = true;
            };
            tb.ToolTip = lookup.ToolTip;
            if (spec.Kind == FormFieldKind.Number)
            {
                tb.MaxWidth = double.PositiveInfinity;
                host.MaxWidth = 290;
                host.MinWidth = 150;
                host.HorizontalAlignment = HorizontalAlignment.Left;
            }
            return (host, editor);
        }

        switch (spec.Kind)
        {
            case FormFieldKind.Memo:
            {
                var button = SideButton("...", "Editar o texto completo (memo)");
                button.Click += (_, _) => OpenMemo(spec, tb);
                return (WithSideButton(tb, button), editor);
            }
            case FormFieldKind.Date or FormFieldKind.DateTime:
            {
                tb.MaxWidth = 260;
                var button = SideButton("▾", "Escolher a data no calendário");
                button.IsEnabled = !spec.IsReadOnly;
                var calendar = new System.Windows.Controls.Calendar { DisplayMode = CalendarMode.Month };
                var popup = new Popup
                {
                    Child = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White, Child = calendar },
                    PlacementTarget = tb,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false
                };
                button.Click += (_, _) =>
                {
                    UpdateIfDirty(tb);
                    var current = CurrentValue(spec);
                    var shown = current switch { DateTime d => d, DateTimeOffset o => o.DateTime, _ => DateTime.Today };
                    calendar.SelectedDate = current is DateTime or DateTimeOffset ? shown.Date : null;
                    calendar.DisplayDate = shown.Date;
                    popup.IsOpen = true;
                };
                calendar.SelectedDatesChanged += (_, _) =>
                {
                    if (!popup.IsOpen || calendar.SelectedDate is not { } picked) return;
                    popup.IsOpen = false;
                    SetValue(spec, FormFieldLayout.ReplaceDate(CurrentValue(spec), picked, spec.ClrType));
                    Mouse.Capture(null);
                };
                var host = WithSideButton(tb, button);
                host.MaxWidth = 290;
                host.HorizontalAlignment = HorizontalAlignment.Left;
                host.MinWidth = 160;
                return (host, editor);
            }
            case FormFieldKind.Time:
                tb.MaxWidth = 200;
                tb.MinWidth = 120;
                tb.HorizontalAlignment = HorizontalAlignment.Left;
                return (tb, editor);
            default:
                return (tb, editor);
        }
    }

    /// <summary>Ctrl+0 in the form: same as ticking the "null" checkbox of the focused field (typed text is dropped).</summary>
    private void SetFocusedFormFieldNull()
    {
        if (_vm is null || FormRow is not { } row) return;
        var editor = _formEditors.FirstOrDefault(x => x.Focus.IsKeyboardFocusWithin);
        var col = editor is null ? null : _vm.Columns.FirstOrDefault(c => c.Field == editor.Spec.Field);
        if (editor is null || col is null) return;
        _vm.SetNull(row, col, editor.Spec.IsReadOnly);
        editor.Text?.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private void OpenFormLookup(Core.Models.ForeignKeyLookupTarget lookup, TextBox tb, FrameworkElement anchor)
    {
        CommitFormEdit();
        if (_vm is null || FormRow is not { } row) return;
        OpenLookup(lookup, row, anchor,
            picked =>
            {
                if (_vm?.ApplyLookup(row, lookup, picked) == true)
                    foreach (var e in _formEditors)
                        e.Text?.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                tb.Focus();
                tb.SelectAll();
            },
            () => tb.Focus());
    }

    private static Button SideButton(string text, string tip) => new()
    {
        Content = text,
        Width = 24,
        Margin = new Thickness(2, 2, 0, 2),
        Padding = new Thickness(0),
        ToolTip = tip,
        Focusable = false
    };

    private static DockPanel WithSideButton(TextBox tb, Button button)
    {
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(button, Dock.Right);
        panel.Children.Add(button);
        panel.Children.Add(tb);
        return panel;
    }

    private DataRowView? FormRow => FormGrid.DataContext as DataRowView;

    private object? CurrentValue(FormFieldSpec spec)
    {
        try { return FormRow?[spec.Field]; }
        catch (Exception) { return null; }
    }

    private void SetValue(FormFieldSpec spec, object value)
    {
        if (FormRow is not { } rv || spec.IsReadOnly) return;
        try
        {
            rv[spec.Field] = value;
        }
        catch (Exception ex)
        {
            App.ReportError(ex);
        }
    }

    private void ToggleNull(FormFieldSpec spec, CheckBox box)
    {
        if (FormRow is null || !spec.CanToggleNull)
        {
            box.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
            return;
        }

        // Typed but not yet applied text of this field is superseded by the checkbox.
        var editor = _formEditors.FirstOrDefault(e => e.Spec.Field == spec.Field);
        SetValue(spec, box.IsChecked == true ? DBNull.Value : FormFieldLayout.DefaultValueFor(spec.ClrType));
        editor?.Text?.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        box.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
    }

    private void OpenMemo(FormFieldSpec spec, TextBox tb)
    {
        UpdateIfDirty(tb);
        if (FormRow is null) return;
        var value = CurrentValue(spec);
        var text = value as string ?? (DataValueText.IsNull(value) ? "" : value?.ToString() ?? "");
        var result = MemoEditorWindow.Edit(Window.GetWindow(this), spec.Name, text, spec.IsReadOnly);
        if (result is null || spec.IsReadOnly) return;
        if (result == text && !DataValueText.IsNull(value)) return;
        SetValue(spec, result);
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private static void UpdateIfDirty(TextBox tb)
    {
        var expr = tb.GetBindingExpression(TextBox.TextProperty);
        if (expr is null || !expr.IsDirty || tb.IsReadOnly) return;
        try { expr.UpdateSource(); }
        catch (Exception ex) { App.ReportError(ex); }
    }

    /// <summary>Applies the text being typed in the form (Post, tab switch, navigation).</summary>
    private void CommitFormEdit()
    {
        foreach (var e in _formEditors)
            if (e.Text is not null) UpdateIfDirty(e.Text);
    }

    /// <summary>Cancel: drop typed text and show the record values again.</summary>
    private void CancelFormEdit()
    {
        foreach (var e in _formEditors)
            e.Text?.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private void FocusFirstFormEditor(bool editableOnly)
    {
        if (_mode != DataViewMode.Form) return;
        if (FormHost.IsKeyboardFocusWithin && !editableOnly) return;
        var target = _formEditors.FirstOrDefault(e => !editableOnly || !e.Spec.IsReadOnly) ?? _formEditors.FirstOrDefault();
        if (target is null) { FormHost.Focus(); return; }
        target.Focus.Focus();
        target.Text?.SelectAll();
    }

    private void Form_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (IsSetNullGesture(e))
        {
            SetFocusedFormFieldNull();
            e.Handled = true;
            return;
        }
        System.Windows.Input.ICommand? command = null;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            command = e.Key switch
            {
                Key.Up or Key.PageUp => _vm.PriorCommand,
                Key.Down or Key.PageDown => _vm.NextCommand,
                Key.Home => _vm.FirstCommand,
                Key.End => _vm.LastCommand,
                Key.Delete => _vm.DeleteCommand,
                Key.Insert => _vm.InsertCommand,
                _ => null
            };
        }

        if (command is null) return;
        CommitFormEdit();
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }
}
