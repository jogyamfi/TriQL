using System.Runtime.CompilerServices;

namespace TriQL.Client.Internal;

/// <summary>
/// Drives the advance loop against a <see cref="StatementClient"/>: follows <c>nextUri</c>
/// iteratively (never recursively, FR-4.3.5), applies adaptive backoff on empty pages (FR-4.4),
/// and raises the page-level <c>error</c> as a <see cref="Exceptions.TrinoQueryException"/> (FR-12.2.2).
/// </summary>
internal static class PageReader
{
    public static async IAsyncEnumerable<TrinoPage> ReadPagesAsync(
        StatementClient client,
        TrinoPageEnvelope initial,
        PollingBackoff backoff,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var current = initial;

        while (true)
        {
            if (current.Error is not null)
            {
                throw FailureClassifier.ToException(current.Error, current.QueryId);
            }

            var isFinal = current.NextUri is null;

            if (current.Rows.Count > 0)
            {
                backoff.Reset();
                yield return BuildPage(current);
            }
            else if (isFinal)
            {
                // Surface the terminal page even with zero rows so schema/stats/updateType are never lost (FR-4.2.3, FR-6.12).
                yield return BuildPage(current);
            }

            if (isFinal)
            {
                yield break;
            }

            if (current.Rows.Count == 0)
            {
                // FR-4.3.4: an empty page never terminates enumeration; FR-4.4.2: back off before polling again.
                await backoff.DelayAsync(cancellationToken).ConfigureAwait(false);
            }

            // FR-4.4.1: a page with rows is followed by an immediate poll, no delay.
            current = await client.AdvanceAsync(current, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TrinoPage BuildPage(TrinoPageEnvelope envelope) =>
        new(envelope.Columns ?? [], envelope.Rows, envelope.Stats, envelope.UpdateType, envelope.UpdateCount);
}
