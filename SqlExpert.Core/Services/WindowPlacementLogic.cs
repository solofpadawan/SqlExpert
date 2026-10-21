using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>Pure rules for where the main window opens (first run default, restore, off-screen guard).</summary>
public static class WindowPlacementLogic
{
    public const double DefaultWidth = 1280;
    public const double DefaultHeight = 800;
    public const double MinWidth = 640;
    public const double MinHeight = 400;
    /// <summary>How much of the window's top strip (title bar) must be visible to accept saved bounds.</summary>
    public const double MinVisibleWidth = 120;
    public const double TitleStrip = 30;

    /// <summary>First-run size: 1280x800 clamped to the work area, centered (never maximized).</summary>
    public static WindowPlacement Default(ScreenRect workArea)
    {
        var w = Math.Min(DefaultWidth, workArea.Width);
        var h = Math.Min(DefaultHeight, workArea.Height);
        return new WindowPlacement(
            workArea.Left + (workArea.Width - w) / 2,
            workArea.Top + (workArea.Height - h) / 2,
            w, h, false);
    }

    /// <summary>
    /// Returns the saved placement when it is valid and its title strip is visible on the desktop
    /// (size clamped to the desktop); otherwise null, meaning "use <see cref="Default"/> centered".
    /// </summary>
    public static WindowPlacement? Restore(WindowPlacement? saved, ScreenRect desktop)
    {
        if (saved is null || !saved.IsValid) return null;
        var w = Math.Clamp(saved.Width, Math.Min(MinWidth, desktop.Width), desktop.Width);
        var h = Math.Clamp(saved.Height, Math.Min(MinHeight, desktop.Height), desktop.Height);

        // Title strip [Left, Left+w] x [Top, Top+TitleStrip] must overlap the desktop enough to be grabbed.
        var overlapW = Math.Min(saved.Left + w, desktop.Right) - Math.Max(saved.Left, desktop.Left);
        var overlapH = Math.Min(saved.Top + TitleStrip, desktop.Bottom) - Math.Max(saved.Top, desktop.Top);
        if (overlapW < Math.Min(MinVisibleWidth, w) || overlapH < TitleStrip / 2) return null;

        return saved with { Width = w, Height = h };
    }
}
