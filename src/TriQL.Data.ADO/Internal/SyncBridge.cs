namespace TriQL.Data.ADO.Internal;

/// <summary>
/// The single, audited blocking bridge from the ADO.NET synchronous surface to the async core
/// (FR-9.2.14). Running the async work via <see cref="Task.Run(Func{Task})"/> schedules it on the
/// thread pool with no captured <see cref="SynchronizationContext"/>, so the blocking wait below
/// cannot deadlock even when the caller holds a UI or classic ASP.NET synchronization context —
/// this is the repo's one documented exception to the sync-over-async ban (NFR-REL-2, P4-T5).
/// Every other call site MUST await instead of using this type.
/// </summary>
internal static class SyncBridge
{
    /// <summary>Blocks the calling thread until <paramref name="action"/> completes.</summary>
    public static void Run(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Run(async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        });
    }

    /// <summary>Blocks the calling thread until <paramref name="action"/> completes and returns its result.</summary>
    public static T Run<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var task = Task.Run(action);

#pragma warning disable RS0030 // Banned symbol: this is the repo's one documented sync-over-async bridge (NFR-REL-2, P4-T5).
        return task.GetAwaiter().GetResult();
#pragma warning restore RS0030
    }
}
