using System.Windows;
using System.Windows.Controls;

namespace SqlExpert.App.Views;

/// <summary>Gives code-built dialog buttons (OK / Cancel / ...) the shared "DialogButton" style of Themes/Controls.xaml.</summary>
public static class DialogButtonStyle
{
    public const string Key = "DialogButton";

    public static Button WithDialogStyle(this Button button)
    {
        button.SetResourceReference(FrameworkElement.StyleProperty, Key);
        return button;
    }
}
