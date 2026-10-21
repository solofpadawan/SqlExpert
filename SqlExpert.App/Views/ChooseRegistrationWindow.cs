using System.Windows;
using System.Windows.Controls;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.App.Views;

/// <summary>Small picker of a registered database (Copy Registration Data...).</summary>
public sealed class ChooseRegistrationWindow : Window
{
    private readonly ListBox _list;
    private readonly IReadOnlyList<ConnectionProfile> _profiles;

    private ChooseRegistrationWindow(IReadOnlyList<ConnectionProfile> profiles, string title, string prompt)
    {
        _profiles = profiles;
        Title = title;
        Width = 460;
        Height = 340;
        MinWidth = 320;
        MinHeight = 220;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new DockPanel { Margin = new Thickness(10) };
        var text = new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(text, Dock.Top);
        root.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "OK", MinWidth = 75, IsDefault = true };
        ok.Click += (_, _) => { if (_list!.SelectedIndex >= 0) DialogResult = true; };
        var cancel = new Button { Content = "Cancel", MinWidth = 75, IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _list = new ListBox();
        foreach (var p in profiles)
        {
            var server = ServerNameText.Format(p);
            _list.Items.Add($"{p.DisplayName}   —   {server} : {p.InitialCatalog}");
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.MouseDoubleClick += (_, _) => { if (_list.SelectedIndex >= 0) DialogResult = true; };
        root.Children.Add(_list);
        Content = root;
    }

    /// <summary>Returns the chosen registration or null (cancelled / none).</summary>
    public static ConnectionProfile? Choose(Window owner, IReadOnlyList<ConnectionProfile> profiles, string title, string prompt)
    {
        if (profiles.Count == 0) return null;
        var dlg = new ChooseRegistrationWindow(profiles, title, prompt) { Owner = owner };
        return dlg.ShowDialog() == true && dlg._list.SelectedIndex >= 0 ? dlg._profiles[dlg._list.SelectedIndex] : null;
    }
}
