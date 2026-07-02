using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Threading;
using SCOverlay.Core.Diagnostics;

namespace SCOverlay.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\SCOverlay.App.SingleInstance";
    private const string PipeName = "SCOverlay.App.SingleInstance";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(800);

    private readonly AppLog log;
    private readonly Mutex mutex = new(false, MutexName);
    private readonly CancellationTokenSource cancellation = new();
    private Task? serverTask;
    private bool ownsMutex;

    public SingleInstanceCoordinator(AppLog log)
    {
        this.log = log;
    }

    public bool TryClaimPrimary()
    {
        try
        {
            ownsMutex = mutex.WaitOne(0);
            return ownsMutex;
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
            return true;
        }
    }

    public void StartServer(Dispatcher dispatcher, Action activate, Action shutdown)
    {
        if (!ownsMutex || serverTask is not null)
        {
            return;
        }

        serverTask = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(cancellation.Token).ConfigureAwait(false);
                    using var reader = new StreamReader(pipe);
                    using var writer = new StreamWriter(pipe)
                    {
                        AutoFlush = true
                    };

                    string? command = await reader.ReadLineAsync(cancellation.Token).ConfigureAwait(false);
                    if (string.Equals(command, "activate", StringComparison.OrdinalIgnoreCase))
                    {
                        await dispatcher.InvokeAsync(activate);
                        await writer.WriteLineAsync("ok").ConfigureAwait(false);
                    }
                    else if (string.Equals(command, "shutdown", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("ok").ConfigureAwait(false);
                        await dispatcher.InvokeAsync(shutdown);
                    }
                    else
                    {
                        await writer.WriteLineAsync("unknown").ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException exception)
                {
                    log.Error("Single-instance pipe I/O failed.", exception);
                }
                catch (Exception exception)
                {
                    log.Error("Single-instance server failed.", exception);
                }
            }
        }, cancellation.Token);
    }

    public static async Task<bool> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = new CancellationTokenSource(ConnectTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(linked.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe)
            {
                AutoFlush = true
            };

            await writer.WriteLineAsync(command).ConfigureAwait(false);
            string? response = await reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
            return string.Equals(response, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> WaitForPrimaryClaimAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (TryClaimPrimary())
            {
                return true;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public void Dispose()
    {
        cancellation.Cancel();
        try
        {
            serverTask?.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch (AggregateException)
        {
        }

        if (ownsMutex)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        cancellation.Dispose();
        mutex.Dispose();
    }
}
