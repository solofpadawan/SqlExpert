using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SqlExpert.App.Services;
using SqlExpert.App.ViewModels;
using SqlExpert.App.Views;
using SqlExpert.Core.About;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Services;
using SqlExpert.Core.Updates;

namespace SqlExpert.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>Minimum time the startup splash stays on screen.</summary>
    public static readonly TimeSpan MinimumSplashTime = TimeSpan.FromMilliseconds(3500);
    private static readonly TimeSpan SplashFadeTime = TimeSpan.FromMilliseconds(250);
    private static SplashWindow? _splash;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Same icon on every window and dialog (splash, main window, editors, updater).
        AppIcon.ApplyToAllWindows();
        DispatcherUnhandledException += (_, args) => { ReportError(args.Exception); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) ReportError(ex); };
        TaskScheduler.UnobservedTaskException += (_, args) => { ReportError(args.Exception); args.SetObserved(); };

        // Updater mode (started by a previous instance after "Atualizar agora"): no splash, no settings, no main window.
        if (UpdaterCommandLine.TryParse(e.Args) is { } update)
        {
            UpdaterMode.Run(this, update);
            return;
        }

        // The splash is the first window: keep the app alive until the main window exists, whatever happens to it.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var clock = Stopwatch.StartNew();
        MainWindow? main = null;
        try
        {
            var splash = new SplashWindow(SplashViewModel.ForStartup(ProductVersionInfo.FromAssembly(typeof(App).Assembly)));
            _splash = splash;
            splash.Show();
            await splash.SetStatusAsync("Loading settings...");

            UiSettings.Instance.Initialize(new UserSettingsStore());

            await splash.SetStatusAsync("Initializing services...");
            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();

            await splash.SetStatusAsync("Loading registered databases...");
            var vm = _services.GetRequiredService<MainViewModel>();
            await vm.Initialization.ConfigureAwait(true);

            await splash.SetStatusAsync("Opening main window...");
            main = new MainWindow { DataContext = vm };
            MainWindow = main;
            // An update prepared with "Atualizar agora" is installed once the main window has really closed.
            main.Closed += (_, _) => UpdateService.LaunchPendingUpdater();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            main.Show();

            await splash.SetStatusAsync("Ready");
            var remaining = MinimumSplashTime - clock.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining).ConfigureAwait(true);
            await splash.FadeOutAndCloseAsync(SplashFadeTime).ConfigureAwait(true);
            _splash = null;
            if (main.IsVisible) main.Activate();
            _ = UpdateService.CheckOnStartupAsync(main);
        }
        catch (Exception ex)
        {
            CloseSplash();
            ReportError(ex);
            if (main is null || !main.IsVisible)
                Shutdown(1);
            else
                ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
    }

    /// <summary>Closes the startup splash at once (no fade) if it is still open.</summary>
    private static void CloseSplash()
    {
        var splash = _splash;
        _splash = null;
        if (splash is null) return;
        try
        {
            if (splash.Dispatcher.CheckAccess()) splash.CloseNow();
            else splash.Dispatcher.Invoke(splash.CloseNow);
        }
        catch { }
    }

    /// <summary>Test seam for headless checks: when set, errors go here instead of error.log + MessageBox.</summary>
    public static Action<Exception>? ErrorSinkOverride { get; set; }

    public static void ReportError(Exception ex)
    {
        // Never leave a topmost splash over the error box.
        CloseSplash();
        if (ErrorSinkOverride is { } sink)
        {
            sink(ex);
            return;
        }
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlExpert");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
        MessageBox.Show(ex.Message, "SqlExpert - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ConnectionSession>();
        services.AddSingleton<IConnectionProfileStore, ConnectionProfileStore>();
        services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
        services.AddSingleton<ISqlConnectionFactory>(sp =>
        {
            var session = sp.GetRequiredService<ConnectionSession>();
            var creds = sp.GetRequiredService<ICredentialStore>();
            return new SqlConnectionFactory(id => session.Find(id), creds);
        });
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<ITableDataService, TableDataService>();
        services.AddSingleton<ITableStructureService, TableStructureService>();
        services.AddSingleton<MainViewModel>();
    }
}
