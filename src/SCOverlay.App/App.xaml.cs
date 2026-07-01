using System.Windows;
using System.Windows.Threading;
using SCOverlay.Core.Application;
using SCOverlay.Core.Diagnostics;

namespace SCOverlay.App;

public partial class App : Application
{
    private AppLog? log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths paths = AppPathProvider.Create();
        AppPathProvider.EnsureCreated(paths);
        log = new AppLog(paths);
        log.Info("SC Overlay starting.");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            log.Info("Smoke test startup completed.");
            Shutdown(0);
            return;
        }

        var window = new MainWindow(paths, log);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        log?.Info("SC Overlay exiting.");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        log?.Error("Unhandled dispatcher exception.", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            log?.Error("Unhandled application exception.", exception);
        }
        else
        {
            log?.Error("Unhandled application exception object.", new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Unknown exception object."));
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        log?.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
