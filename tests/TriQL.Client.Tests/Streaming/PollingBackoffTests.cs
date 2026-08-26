using TriQL.Client.Internal;

namespace TriQL.Client.Tests.Streaming;

public sealed class PollingBackoffTests
{
    [Fact]
    public void NextDelay_GrowsGeometrically_UpToCap()
    {
        var backoff = new PollingBackoff(TimeSpan.FromMilliseconds(50), 2.0, TimeSpan.FromMilliseconds(180));

        var delays = new[]
        {
            backoff.NextDelay(),
            backoff.NextDelay(),
            backoff.NextDelay(),
            backoff.NextDelay(),
        };

        Assert.Equal(
            [
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(180),
                TimeSpan.FromMilliseconds(180),
            ],
            delays);
    }

    [Fact]
    public void Reset_RestoresInitialDelay()
    {
        var backoff = new PollingBackoff(TimeSpan.FromMilliseconds(50), 2.0, TimeSpan.FromSeconds(5));
        backoff.NextDelay();
        backoff.NextDelay();

        backoff.Reset();

        Assert.Equal(TimeSpan.FromMilliseconds(50), backoff.NextDelay());
    }

    [Fact]
    public async Task DelayAsync_IsCancellable()
    {
        var backoff = new PollingBackoff(TimeSpan.FromSeconds(30), 2.0, TimeSpan.FromMinutes(1));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => backoff.DelayAsync(cts.Token));
    }
}
