namespace TriQL.Client.Internal;

/// <summary>
/// Adaptive polling delay: immediate after a page with rows, otherwise an exponentially
/// increasing delay reset to <c>initial</c> the moment a page with rows is seen again. See FR-4.4.
/// </summary>
internal sealed class PollingBackoff
{
    private readonly TimeSpan _initial;
    private readonly double _multiplier;
    private readonly TimeSpan _cap;
    private TimeSpan _current;

    public PollingBackoff(TimeSpan initial, double multiplier, TimeSpan cap)
    {
        _initial = initial;
        _multiplier = multiplier;
        _cap = cap;
        _current = initial;
    }

    public void Reset() => _current = _initial;

    /// <summary>Returns the delay to use for the current poll and advances the internal state for the next call.</summary>
    public TimeSpan NextDelay()
    {
        var delay = _current;
        var nextMillis = Math.Min(_current.TotalMilliseconds * _multiplier, _cap.TotalMilliseconds);
        _current = TimeSpan.FromMilliseconds(nextMillis);
        return delay;
    }

    public Task DelayAsync(CancellationToken cancellationToken) => Task.Delay(NextDelay(), cancellationToken);
}
