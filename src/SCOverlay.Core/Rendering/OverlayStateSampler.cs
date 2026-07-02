using SCOverlay.Core.Input;
using SCOverlay.Core.Profiles;

namespace SCOverlay.Core.Rendering;

public sealed class OverlayStateSampler : IAsyncDisposable
{
    private readonly OverlayProfile profile;
    private readonly IInputProvider inputProvider;
    private readonly IOverlayStateEngine stateEngine;
    private readonly TimeSpan interval;
    private CancellationTokenSource? cancellation;
    private Task? samplingTask;

    public OverlayStateSampler(
        OverlayProfile profile,
        IInputProvider inputProvider,
        IOverlayStateEngine stateEngine)
    {
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        this.inputProvider = inputProvider ?? throw new ArgumentNullException(nameof(inputProvider));
        this.stateEngine = stateEngine ?? throw new ArgumentNullException(nameof(stateEngine));
        interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(profile.Runtime.TargetHz, 1, 240));
        CurrentState = OverlayState.Empty(profile.Id);
    }

    public event EventHandler<OverlayState>? StateUpdated;

    public bool IsRunning => samplingTask is { IsCompleted: false };

    public OverlayState CurrentState { get; private set; }

    public Exception? LastError { get; private set; }

    public OverlayState SampleOnce()
    {
        InputSnapshot snapshot = inputProvider.Poll();
        OverlayState state = stateEngine.BuildState(profile, snapshot);
        CurrentState = state;
        StateUpdated?.Invoke(this, state);
        return state;
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        cancellation = new CancellationTokenSource();
        samplingTask = RunAsync(cancellation.Token);
    }

    public async ValueTask StopAsync()
    {
        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync();

        if (samplingTask is not null)
        {
            try
            {
                await samplingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation.Dispose();
        cancellation = null;
        samplingTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                LastError = null;
                SampleOnce();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LastError = exception;
            }
        }
    }
}
