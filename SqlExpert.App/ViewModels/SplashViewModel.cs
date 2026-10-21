using System.ComponentModel;
using System.Runtime.CompilerServices;
using SqlExpert.Core.About;

namespace SqlExpert.App.ViewModels;

/// <summary>Texts of the splash / About banner (Views/SplashBanner.xaml).</summary>
public sealed class SplashViewModel : INotifyPropertyChanged
{
    private string _statusText = "";

    public SplashViewModel(ProductVersionInfo version, string versionText, string statusText)
    {
        Version = version;
        VersionText = versionText;
        _statusText = statusText;
    }

    public ProductVersionInfo Version { get; }

    /// <summary>Top-right line, e.g. "SqlExpert 1.0.0 (build 2026-10-20) - x64 - .NET 8.0.21".</summary>
    public string VersionText { get; }

    /// <summary>Bottom-left line: the current startup step ("Loading settings...").</summary>
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public static SplashViewModel ForStartup(ProductVersionInfo version) =>
        new(version, version.DisplayText, "Starting...");

    public static SplashViewModel ForAbout(ProductVersionInfo version) =>
        new(version, version.DetailedText, "Click or press Esc to close");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
