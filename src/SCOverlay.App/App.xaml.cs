using System.Windows;
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
}
