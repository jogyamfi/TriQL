namespace TriQL.Client.Internal;

/// <summary>
/// Maintains <see cref="TrinoQueryState"/> transitions via compare-and-swap so that concurrent
/// cancellation and completion cannot both take effect. See FR-4.5.1.
/// </summary>
internal sealed class QueryStateMachine
{
    private int _state = (int)TrinoQueryState.Submitting;

    public TrinoQueryState Current => (TrinoQueryState)Volatile.Read(ref _state);

    public bool TryTransition(TrinoQueryState from, TrinoQueryState to) =>
        Interlocked.CompareExchange(ref _state, (int)to, (int)from) == (int)from;

    public void MarkRunning() => TryTransition(TrinoQueryState.Submitting, TrinoQueryState.Running);

    public bool TryFinish() => TryTransition(TrinoQueryState.Running, TrinoQueryState.Finished);

    public bool TryFail() =>
        TryTransition(TrinoQueryState.Running, TrinoQueryState.Failed) ||
        TryTransition(TrinoQueryState.Submitting, TrinoQueryState.Failed);

    public bool TryCancel() =>
        TryTransition(TrinoQueryState.Running, TrinoQueryState.Cancelled) ||
        TryTransition(TrinoQueryState.Submitting, TrinoQueryState.Cancelled);

    public bool TryTimeout() =>
        TryTransition(TrinoQueryState.Running, TrinoQueryState.TimedOut) ||
        TryTransition(TrinoQueryState.Submitting, TrinoQueryState.TimedOut);
}
