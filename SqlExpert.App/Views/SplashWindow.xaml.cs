using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SqlExpert.App.ViewModels;
using SqlExpert.Core.About;

namespace SqlExpert.App.Views;

/// <summary>
/// Borderless banner shown at startup (topmost, not activated, ignores clicks and Alt+F4) and reused,
/// as a modal dialog closed by click/Esc, for Help &gt; About.
/// </summary>
public partial class SplashWindow : Window
{
    private readonly bool _aboutMode;
    private bool _allowClose;
    private bool _closed;

    public SplashWindow(SplashViewModel model, bool aboutMode = false)
    {
        InitializeComponent();
        _aboutMode = aboutMode;
        Model = model;
        DataContext = model;
        if (aboutMode)
        {
            _allowClose = true;
            Topmost = false;
            ShowActivated = true;
        }
        Closing += OnClosingGuard;
        Closed += (_, _) => _closed = true;
        PreviewMouseLeftButtonUp += (_, e) => { e.Handled = true; if (_aboutMode) Close(); };
        PreviewKeyDown += (_, e) =>
        {
            if (_aboutMode && e.Key is Key.Escape or Key.Enter or Key.Space) { e.Handled = true; Close(); }
        };
    }

    public SplashViewModel Model { get; }

    public bool IsClosed => _closed;

    private void OnClosingGuard(object? sender, CancelEventArgs e)
    {
        // The startup splash closes only through FadeOutAndCloseAsync / CloseNow (not Alt+F4).
        if (!_allowClose) e.Cancel = true;
    }

    /// <summary>Shows a startup step and yields so the splash actually repaints before the work continues.</summary>
    public async Task SetStatusAsync(string text)
    {
        if (_closed) return;
        Model.StatusText = text;
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
    }

    /// <summary>Short opacity fade, then close. Safe to call more than once.</summary>
    public Task FadeOutAndCloseAsync(TimeSpan duration)
    {
        if (_closed) return Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        try
        {
            var fade = new DoubleAnimation(Opacity, 0, new Duration(duration)) { FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (_, _) => { CloseNow(); tcs.TrySetResult(); };
            BeginAnimation(OpacityProperty, fade);
        }
        catch
        {
            CloseNow();
            tcs.TrySetResult();
        }
        return tcs.Task;
    }

    /// <summary>Closes immediately (used on startup errors so the error box is not hidden behind a topmost splash).</summary>
    public void CloseNow()
    {
        if (_closed) return;
        _allowClose = true;
        try { Close(); } catch (InvalidOperationException) { }
    }

    /// <summary>Help &gt; About: the same banner, modal, closed by a click or Esc.</summary>
    public static void ShowAbout(Window? owner)
    {
        var about = new SplashWindow(SplashViewModel.ForAbout(ProductVersionInfo.FromAssembly(typeof(App).Assembly)), aboutMode: true);
        if (owner is not null && owner.IsVisible)
        {
            about.Owner = owner;
            about.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        about.ShowDialog();
    }
}
