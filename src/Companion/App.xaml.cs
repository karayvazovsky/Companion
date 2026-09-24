using System.Windows;
using Companion.Core;

namespace Companion;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppHost? _host;

    public static ILog Log { get; private set; } = NullLog.Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Companion.Pet.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        var paths = AppPaths.Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("COMPANION_ROOT"));
        paths.EnsureCreated();
        var clock = new SystemClock();
        var log = new FileLog(paths.Logs, clock);
        Log = log;
        log.Info($"Starting. Root: {paths.Root}");
        DispatcherUnhandledException += (_, args) =>
        {
            log.Error("Unhandled UI exception.", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => log.Error("Unhandled exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        _host = new AppHost(paths, clock, log);
        _host.QuitRequested += Shutdown;
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _singleInstance?.Dispose();
        Log.Info("Exited.");
        base.OnExit(e);
    }
}
