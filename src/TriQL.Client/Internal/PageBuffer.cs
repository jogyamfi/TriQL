using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace TriQL.Client.Internal;

/// <summary>
/// The bounded read-ahead buffer between the background page-fetch pump and the consumer. Bounded
/// by a byte budget rather than a page count (FR-6.2); the queue itself never blocks the consumer
/// side (it awaits a signal, per <see cref="Channel{T}"/> semantics), and production suspends via
/// <see cref="ByteBudgetGate"/> when the budget is exhausted. See FR-6.1—FR-6.5.
/// </summary>
internal sealed class PageBuffer
{
    private readonly Channel<(TrinoPage Page, long SizeBytes)> _channel =
        Channel.CreateUnbounded<(TrinoPage, long)>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly ByteBudgetGate _gate;

    public PageBuffer(long budgetBytes)
    {
        _gate = new ByteBudgetGate(budgetBytes);
    }

    /// <summary>The bytes currently held in the buffer, awaiting consumption.</summary>
    public long OccupiedBytes => _gate.Occupied;

    /// <summary>Reserves space for and enqueues a page, suspending under backpressure if necessary.</summary>
    public async ValueTask EnqueueAsync(TrinoPage page, long sizeBytes, CancellationToken cancellationToken)
    {
        await _gate.ReserveAsync(sizeBytes, cancellationToken).ConfigureAwait(false);
        await _channel.Writer.WriteAsync((page, sizeBytes), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks the buffer complete. When <paramref name="error"/> is non-null, the consumer's
    /// enumeration rethrows it (preserving its original stack) at the point enumeration reaches
    /// the end of the buffered pages. See FR-6.8.
    /// </summary>
    public void Complete(Exception? error) => _channel.Writer.TryComplete(error);

    public IAsyncEnumerable<TrinoPage> ReadAllAsync(CancellationToken cancellationToken) => ReadCoreAsync(cancellationToken);

    private async IAsyncEnumerable<TrinoPage> ReadCoreAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var (page, sizeBytes) in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            _gate.Release(sizeBytes);
            yield return page;
        }
    }
}
