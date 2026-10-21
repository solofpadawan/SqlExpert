using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SqlExpert.Core.Procedures;

namespace SqlExpert.App.Views;

/// <summary>
/// Input values of Execute (F9) in the procedure editor: one row per parameter with Value / NULL / Default.
/// OUTPUT parameters may get an initial value; READONLY table-valued parameters are passed as empty tables.
/// </summary>
public sealed class ProcedureExecuteWindow : Window
{
    private static readonly Dictionary<string, (ArgumentMode Mode, string? Value)> LastValues = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<(ProcedureParameter Parameter, ComboBox Mode, TextBox Value)> _rows = new();
    private readonly string _procedure;

    private ProcedureExecuteWindow(string procedure, IReadOnlyList<ProcedureParameter> parameters)
    {
        _procedure = procedure;
        Title = "Execute procedure " + procedure;
        Width = 720;
        Height = Math.Min(160 + 32 * parameters.Count, 620);
        MinWidth = 480;
        MinHeight = 200;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new DockPanel { Margin = new Thickness(10) };
        var text = new TextBlock
        {
            Text = "Valores dos parâmetros de entrada. \"Default\" omite o parâmetro (usa o valor padrão da procedure); OUTPUT aparece no resultado \"Output / RETURN\".",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(text, Dock.Top);
        root.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "Execute", MinWidth = 85, IsDefault = true }.WithDialogStyle();
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", MinWidth = 75, IsCancel = true, Margin = new Thickness(6, 0, 0, 0) }.WithDialogStyle();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddHeader(grid, 0, "Parâmetro");
        AddHeader(grid, 1, "Tipo");
        AddHeader(grid, 2, "Passar");
        AddHeader(grid, 3, "Valor");

        var r = 1;
        foreach (var p in parameters)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = p.Name + (p.IsOutput ? "  (OUTPUT)" : ""), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2) };
            var type = new TextBlock { Text = p.FullType + (p.Default is null ? "" : " = " + p.Default), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2), ToolTip = p.Description };
            var mode = new ComboBox { Margin = new Thickness(2), ItemsSource = new[] { "Valor", "NULL", "Default" } };
            var value = new TextBox { Margin = new Thickness(2), FontFamily = new FontFamily("Consolas") };
            if (p.IsReadOnly)
            {
                mode.SelectedIndex = 0;
                mode.IsEnabled = false;
                value.IsEnabled = false;
                value.Text = "(tabela vazia)";
            }
            else
            {
                var key = Key(p);
                if (LastValues.TryGetValue(key, out var last))
                {
                    mode.SelectedIndex = (int)last.Mode;
                    value.Text = last.Value ?? "";
                }
                else
                {
                    mode.SelectedIndex = p.Default is not null ? 2 : p.IsOutput ? 1 : 0;
                }
                value.IsEnabled = mode.SelectedIndex == 0;
                mode.SelectionChanged += (_, _) => value.IsEnabled = mode.SelectedIndex == 0;
            }
            Place(grid, name, r, 0);
            Place(grid, type, r, 1);
            Place(grid, mode, r, 2);
            Place(grid, value, r, 3);
            _rows.Add((p, mode, value));
            r++;
        }
        grid.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Loaded += (_, _) => _rows.FirstOrDefault(x => x.Value.IsEnabled).Value?.Focus();
    }

    private string Key(ProcedureParameter p) => _procedure + "|" + p.Name;

    private static void AddHeader(Grid grid, int column, string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 0, 2, 4) };
        Place(grid, t, 0, column);
    }

    private static void Place(Grid grid, UIElement e, int row, int column)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        grid.Children.Add(e);
    }

    /// <summary>Asks the values; null when cancelled. No parameters: returns an empty list without showing the dialog.</summary>
    public static IReadOnlyList<ProcedureArgument>? Ask(Window? owner, string procedure, IReadOnlyList<ProcedureParameter> parameters)
    {
        if (parameters.Count == 0) return Array.Empty<ProcedureArgument>();
        var dlg = new ProcedureExecuteWindow(procedure, parameters);
        if (owner is not null) dlg.Owner = owner;
        if (dlg.ShowDialog() != true) return null;
        var list = new List<ProcedureArgument>();
        foreach (var (p, mode, value) in dlg._rows)
        {
            var m = p.IsReadOnly ? ArgumentMode.Value : (ArgumentMode)Math.Max(0, mode.SelectedIndex);
            var v = m == ArgumentMode.Value && !p.IsReadOnly ? value.Text : null;
            if (!p.IsReadOnly) LastValues[dlg.Key(p)] = (m, v);
            list.Add(new ProcedureArgument(p, m, v));
        }
        return list;
    }
}
