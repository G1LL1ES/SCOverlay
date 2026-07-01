using SCOverlay.Core.Profiles;

namespace SCOverlay.BrowserSource;

public sealed class BrowserSourceServer
{
    public BrowserSourceServer(RuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Host = settings.BrowserSourceHost;
        Port = settings.BrowserSourcePort;
    }

    public string Host { get; }

    public int Port { get; }

    public bool IsRunning { get; private set; }

    public string Url => $"http://{Host}:{Port}/obs.html";

    public void Start()
    {
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
    }
}
