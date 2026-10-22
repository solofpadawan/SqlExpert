using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SqlExpert.App.Services;

namespace SqlExpert.App.Views;

/// <summary>"Are you sure you wish to quit SqlExpert?" (Yes / No) shown before the program closes.</summary>
public sealed class QuitConfirmationWindow : Window
{
    public const string DialogTitle = "Confirmation";
    public const string Question = "Are you sure you wish to quit SqlExpert?";

    public QuitConfirmationWindow()
    {
        Title = DialogTitle;
        Icon = AppIcon.Image;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 300;

        var root = new DockPanel { Margin = new Thickness(14, 14, 14, 10) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        var yes = new Button { Content = "Yes", MinWidth = 75, IsDefault = true }.WithDialogStyle();
        yes.Click += (_, _) => DialogResult = true;
        var no = new Button { Content = "No", MinWidth = 75, IsCancel = true, Margin = new Thickness(6, 0, 0, 0) }.WithDialogStyle();
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var icon = QuestionIcon();
        DockPanel.SetDock(icon, Dock.Left);
        root.Children.Add(icon);
        root.Children.Add(new TextBlock
        {
            Text = Question,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 6, 0)
        });

        Content = root;
        Loaded += (_, _) => yes.Focus();
    }

    /// <summary>Asks the question over <paramref name="owner"/>; true = Yes (quit), false = No / Esc / closed.</summary>
    public static bool Ask(Window? owner)
    {
        var dialog = new QuitConfirmationWindow();
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    /// <summary>Vector question mark in a muted blue disc (32 px, like the system message-box icon).</summary>
    private static FrameworkElement QuestionIcon()
    {
        var grid = new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        var fill = new LinearGradientBrush(Color.FromRgb(0x6F, 0x93, 0xC4), Color.FromRgb(0x3E, 0x63, 0x96), 90);
        fill.Freeze();
        grid.Children.Add(new Ellipse { Fill = fill, Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x52, 0x7D)), StrokeThickness = 1 });
        grid.Children.Add(new TextBlock
        {
            Text = "?",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -2, 0, 0)
        });
        return grid;
    }
}
