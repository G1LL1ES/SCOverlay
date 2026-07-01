using System.Windows;
using SCOverlay.BrowserSource;
using SCOverlay.Core.Application;
using SCOverlay.Core.Diagnostics;
using SCOverlay.Core.Profiles;
using SCOverlay.Input;

namespace SCOverlay.App;

public partial class MainWindow : Window
{
    private readonly AppLog log;
    private readonly OverlayProfile profile;
    private readonly FoundationInputProvider inputProvider;
    private readonly BrowserSourceServer browserSourceServer;

    public MainWindow(AppPaths paths, AppLog log)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        profile = OverlayProfile.CreateFoundationDefault();
        inputProvider = new FoundationInputProvider();
        browserSourceServer = new BrowserSourceServer(profile.Runtime);

        InitializeComponent();

        StatusText.Text = "Typed profiles, default layouts, validation, JSON serialization, input, browser-source, and overlay-state boundaries are ready for the next phase.";
        DataRootText.Text = $"Runtime data: {paths.DataRoot}";
        BrowserSourceText.Text = $"OBS placeholder URL: {browserSourceServer.Url}";
        this.log.Info($"Main shell initialized with {inputProvider.Name}.");
    }
}
