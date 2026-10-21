namespace SqlExpert.Core.Models;

/// <summary>Normal (restore) bounds of a window in device-independent pixels, plus whether it was maximized.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized)
{
    public bool IsValid =>
        double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height)
        && Width > 0 && Height > 0;
}

/// <summary>Screen rectangle in device-independent pixels.</summary>
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}
