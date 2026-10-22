using System.Windows;
using System.Windows.Controls;

namespace SqlExpert.App.Views;

/// <summary>
/// Gives code-built dialog buttons (OK / Cancel / ...) the shared "DialogButton" style of Themes/Controls.xaml, or the
/// accent "DialogPrimaryButton" style for the main action of a dialog.
/// </summary>
public static class DialogButtonStyle
{
    public const string Key = "DialogButton";
    public const string PrimaryKey = "DialogPrimaryButton";

    public static Button WithDialogStyle(this Button button)
    {
        button.SetResourceReference(FrameworkElement.StyleProperty, Key);
        return button;
    }

    public static Button WithPrimaryStyle(this Button button)
    {
        button.SetResourceReference(FrameworkElement.StyleProperty, PrimaryKey);
        return button;
    }
}
