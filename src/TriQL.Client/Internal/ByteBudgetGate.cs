namespace TriQL.Client.Internal;

/// <summary>
/// A byte-budget gate used to implement <see cref="PageBuffer"/> backpressure: suspends without
/// spinning when the budget is exhausted, and resumes as soon as the consumer releases enough
/// space. See FR-6.2, FR-6.4.
/// </summary>
internal sealed class ByteBudgetGate
{
    private readonly long _budget;
    private readonly object _gate = new();
    private long _occupied;
    private TaskCompletionSource? _waiter;

    public ByteBudgetGate(long budget)
    {
        if (budget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "The byte budget must be positive.");
        }

        _budget = budget;
    }

    public long Occupied
    {
        get { lock (_gate) { return _occupied; } }
    }

    /// <summary>
    /// Reserves <paramref name="size"/> bytes, awaiting a release signal if the budget is
    /// currently exhausted. A single reservation is always admitted when nothing else is
    /// occupying space, even if it alone exceeds the budget (FR-6.3: at least one maximal page
    /// always fits).
    /// </summary>
    public async ValueTask ReserveAsync(long size, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;
            lock (_gate)
            {
                if (_occupied == 0 || _occupied + size <= _budget)
                {
                    _occupied += size;
                    return;
                }

                _waiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _waiter.Task;
            }

            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Releases <paramref name="size"/> bytes and signals any suspended reservation.</summary>
    public void Release(long size)
    {
        TaskCompletionSource? toSignal = null;
        lock (_gate)
        {
            _occupied = Math.Max(0, _occupied - size);
            if (_waiter is not null)
            {
                toSignal = _waiter;
                _waiter = null;
            }
        }

        toSignal?.TrySetResult();
    }
}
