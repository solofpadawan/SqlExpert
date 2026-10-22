using System.Windows;

namespace SqlExpert.App.Views;

/// <summary>"Are you sure you wish to quit SqlExpert?" (Yes / No) shown before the program closes.</summary>
public static class QuitConfirmationWindow
{
    public const string DialogTitle = "Confirmation";
    public const string Question = "Are you sure you wish to quit SqlExpert?";

    /// <summary>The dialog (Yes = default, No = Esc) without showing it.</summary>
    public static MessageDialog Create() => MessageDialog.Create(Question, DialogTitle, MessageBoxButton.YesNo,
        MessageBoxImage.Question, MessageBoxResult.Yes, DialogLabels.English);

    /// <summary>Asks the question over <paramref name="owner"/>; true = Yes (quit), false = No / Esc / closed.</summary>
    public static bool Ask(Window? owner) =>
        MessageDialog.Show(owner, Question, DialogTitle, MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.Yes, DialogLabels.English) == MessageBoxResult.Yes;
}
