using System.Windows;
using System.Windows.Threading;
using SCOverlay.Core.Application;
using SCOverlay.Core.Diagnostics;

namespace SCOverlay.App;

public partial class App : System.Windows.Application
{
    private AppLog? log;
    private SingleInstanceCoordinator? singleInstance;

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

        singleInstance = new SingleInstanceCoordinator(log);
        if (!singleInstance.TryClaimPrimary())
        {
            InstanceConflictChoice choice = ResolveInstanceConflictChoice(e.Args);
            if (choice == InstanceConflictChoice.FocusExisting)
            {
                _ = SingleInstanceCoordinator.SendCommandAsync("activate").GetAwaiter().GetResult();
                Shutdown(0);
                return;
            }

            if (choice == InstanceConflictChoice.QuitOldAndStart)
            {
                bool sent = SingleInstanceCoordinator.SendCommandAsync("shutdown").GetAwaiter().GetResult();
                bool claimed = sent &&
                    singleInstance.WaitForPrimaryClaimAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                if (!claimed)
                {
                    log.Error("Could not stop the existing SC Overlay instance before startup.", new TimeoutException("Timed out waiting for the existing instance to exit."));
                }
            }
            else
            {
                log.Info("Starting an additional SC Overlay instance by user choice.");
            }
        }

        var window = new MainWindow(paths, log);
        MainWindow = window;
        window.Show();
        singleInstance.StartServer(Dispatcher, window.BringToFront, window.Close);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        singleInstance?.Dispose();
        singleInstance = null;
        log?.Info("SC Overlay exiting.");
        base.OnExit(e);
    }

    private static InstanceConflictChoice ResolveInstanceConflictChoice(IReadOnlyCollection<string> args)
    {
        if (args.Contains("--new-instance", StringComparer.OrdinalIgnoreCase))
        {
            return InstanceConflictChoice.StartAnother;
        }

        if (args.Contains("--quit-existing", StringComparer.OrdinalIgnoreCase))
        {
            return InstanceConflictChoice.QuitOldAndStart;
        }

        if (args.Contains("--focus-existing", StringComparer.OrdinalIgnoreCase))
        {
            return InstanceConflictChoice.FocusExisting;
        }

        var window = new InstanceConflictWindow();
        return window.ShowDialog() == true
            ? window.Choice
            : InstanceConflictChoice.FocusExisting;
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
