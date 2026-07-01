namespace SCOverlay.Core.Input;

public sealed record EvaluatedInputState(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, double> Axes,
    IReadOnlyDictionary<string, bool> Buttons)
{
    public double GetAxis(string sourceId)
    {
        return Axes.TryGetValue(sourceId, out double value) ? value : 0.0;
    }

    public bool GetButton(string sourceId)
    {
        return Buttons.TryGetValue(sourceId, out bool value) && value;
    }
}
