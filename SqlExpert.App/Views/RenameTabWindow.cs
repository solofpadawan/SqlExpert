using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SqlExpert.App.Services;

namespace SqlExpert.App.Views;

/// <summary>
/// Small confirmation-style dialog that asks for a new Windows-bar name of a SQL Editor tab.
/// Empty input clears the custom name (back to "SQL Editor" / "SQL Editor N"). OK = Enter, Cancel = Esc.
/// </summary>
public sealed class RenameTabWindow : Window
{
    public const int MaxNameLength = 80;

    private readonly TextBox _name;
    private string? _result;

    private RenameTabWindow(string currentName)
    {
        Title = "Renomear";
        Icon = AppIcon.Image;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 360;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Dialog.Background");

        _name = new TextBox
        {
            Text = currentName ?? "",
            MinWidth = 280,
            Margin = new Thickness(0, 4, 0, 0),
            MaxLength = MaxNameLength
        };
        _name.SelectAll();

        var root = new DockPanel { Margin = new Thickness(14, 14, 14, 10) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = new Button { Content = "OK", MinWidth = 75, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) }.WithPrimaryStyle();
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancelar", MinWidth = 75, IsCancel = true }.WithDialogStyle();
        cancel.Click += (_, _) => { DialogResult = false; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Nome da aba:",
            Margin = new Thickness(0, 0, 0, 2)
        });
        body.Children.Add(_name);
        body.Children.Add(new TextBlock
        {
            Text = "Deixe em branco para voltar ao nome padrão (SQL Editor).",
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        });
        root.Children.Add(body);
        Content = root;

        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        };
    }

    private void Accept()
    {
        _result = _name.Text?.Trim() ?? "";
        DialogResult = true;
    }

    /// <summary>
    /// Asks for a new name over <paramref name="owner"/>. Returns the trimmed name (empty = clear custom title),
    /// or null when the user cancels / closes the dialog.
    /// </summary>
    public static string? Ask(Window? owner, string currentName)
    {
        var dlg = new RenameTabWindow(currentName);
        if (owner is not null) dlg.Owner = owner;
        return dlg.ShowDialog() == true ? dlg._result : null;
    }
}
