using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SqlExpert.App.Services;

/// <summary>
/// Application icon (Assets/sqlexpert.ico, also the exe icon through ApplicationIcon in the csproj).
/// <see cref="ApplyToAllWindows"/> gives every window and dialog the same title-bar / taskbar icon:
/// windows that set no Icon of their own get it when they load.
/// </summary>
public static class AppIcon
{
    public const string PackUri = "pack://application:,,,/SqlExpert;component/Assets/sqlexpert.ico";

    private static ImageSource? _image;
    private static bool _registered;

    /// <summary>The multi-size icon; Windows picks the 16/20/24 px frame for the title bar and 32/40/48 for Alt+Tab.</summary>
    public static ImageSource Image => _image ??= Load();

    private static ImageSource Load()
    {
        var frame = BitmapFrame.Create(new Uri(PackUri, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    public static void ApplyToAllWindows()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window { Icon: null } window) window.Icon = Image;
    }
}