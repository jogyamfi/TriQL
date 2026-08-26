using TriQL.Client.Internal;

namespace TriQL.Client.Tests.Streaming;

public sealed class ByteBudgetGateTests
{
    [Fact]
    public void Constructor_RejectsNonPositiveBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteBudgetGate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteBudgetGate(-1));
    }

    [Fact]
    public async Task ReserveAsync_AdmitsASingleReservation_EvenIfItAloneExceedsTheBudget()
    {
        var gate = new ByteBudgetGate(10);

        await gate.ReserveAsync(1_000, CancellationToken.None);

        Assert.Equal(1_000, gate.Occupied);
    }

    [Fact]
    public async Task ReserveAsync_SuspendsUntilSpaceIsReleased()
    {
        var gate = new ByteBudgetGate(100);
        await gate.ReserveAsync(80, CancellationToken.None);

        var reserveTask = gate.ReserveAsync(50, CancellationToken.None).AsTask();

        // The gate is occupied (80) and 80 + 50 > 100, so the second reservation must suspend.
        await Task.Delay(20);
        Assert.False(reserveTask.IsCompleted);

        gate.Release(80);
        await reserveTask;

        Assert.Equal(50, gate.Occupied);
    }

    [Fact]
    public async Task ReserveAsync_IsCancellable_WhileSuspended()
    {
        var gate = new ByteBudgetGate(10);
        await gate.ReserveAsync(10, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var reserveTask = gate.ReserveAsync(10, cts.Token).AsTask();

        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => reserveTask);
    }

    [Fact]
    public void Release_NeverGoesNegative()
    {
        var gate = new ByteBudgetGate(10);

        gate.Release(1_000);

        Assert.Equal(0, gate.Occupied);
    }
}
