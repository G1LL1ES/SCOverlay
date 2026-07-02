using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SCOverlay.Core.Profiles;
using SCOverlay.Core.Rendering;

namespace SCOverlay.BrowserSource;

public sealed class BrowserSourceServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
    private readonly object stateLock = new();
    private CancellationTokenSource? cancellation;
    private Task? serverTask;
    private OverlayState currentState;

    public BrowserSourceServer(RuntimeSettings settings)
        : this(settings, OverlayState.Empty("not-loaded"))
    {
    }

    public BrowserSourceServer(RuntimeSettings settings, OverlayState initialState)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(initialState);
        Host = settings.BrowserSourceHost;
        Port = settings.BrowserSourcePort;
        currentState = initialState;
        listener.Prefixes.Add($"http://{Host}:{Port}/");
    }

    public string Host { get; }

    public int Port { get; }

    public bool IsRunning => listener.IsListening;

    public string Url => $"http://{Host}:{Port}/obs.html";

    public OverlayState CurrentState
    {
        get
        {
            lock (stateLock)
            {
                return currentState;
            }
        }
    }

    public void UpdateState(OverlayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (stateLock)
        {
            currentState = state;
        }
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        cancellation = new CancellationTokenSource();
        listener.Start();
        serverTask = RunAsync(cancellation.Token);
    }

    public void Stop()
    {
        if (!IsRunning && serverTask is null)
        {
            return;
        }

        cancellation?.Cancel();
        listener.Stop();

        try
        {
            serverTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }

        cancellation?.Dispose();
        cancellation = null;
        serverTask = null;
    }

    public void Dispose()
    {
        Stop();
        listener.Close();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            if (path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/obs.html", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextAsync(context.Response, BrowserSourcePage.Html, "text/html; charset=utf-8", cancellationToken);
            }
            else if (path.Equals("/state", StringComparison.OrdinalIgnoreCase))
            {
                OverlayState state = CurrentState;
                byte[] body = JsonSerializer.SerializeToUtf8Bytes(state, jsonOptions);
                await WriteBytesAsync(context.Response, body, "application/json; charset=utf-8", cancellationToken);
            }
            else if (path.Equals("/assets", StringComparison.OrdinalIgnoreCase) ||
                     path.Equals("/assets/", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextAsync(context.Response, BrowserSourcePage.AssetManifestJson, "application/json; charset=utf-8", cancellationToken);
            }
            else if (path.Equals("/assets/roll-indicator-default.svg", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextAsync(context.Response, BrowserSourcePage.RollIndicatorSvg, "image/svg+xml; charset=utf-8", cancellationToken);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteTextAsync(context.Response, "Not found", "text/plain; charset=utf-8", cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static async Task WriteTextAsync(
        HttpListenerResponse response,
        string text,
        string contentType,
        CancellationToken cancellationToken)
    {
        await WriteBytesAsync(response, Encoding.UTF8.GetBytes(text), contentType, cancellationToken);
    }

    private static async Task WriteBytesAsync(
        HttpListenerResponse response,
        byte[] body,
        string contentType,
        CancellationToken cancellationToken)
    {
        response.ContentType = contentType;
        response.ContentLength64 = body.Length;
        response.Headers["Cache-Control"] = "no-store";
        await response.OutputStream.WriteAsync(body, cancellationToken);
        response.Close();
    }

    public static int FindAvailablePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}
