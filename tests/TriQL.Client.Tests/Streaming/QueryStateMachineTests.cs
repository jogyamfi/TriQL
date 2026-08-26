using TriQL.Client.Internal;

namespace TriQL.Client.Tests.Streaming;

public sealed class QueryStateMachineTests
{
    [Fact]
    public void MarkRunning_TransitionsFromSubmitting()
    {
        var machine = new QueryStateMachine();

        machine.MarkRunning();

        Assert.Equal(TrinoQueryState.Running, machine.Current);
    }

    [Fact]
    public void TryFinish_TransitionsFromRunning()
    {
        var machine = new QueryStateMachine();
        machine.MarkRunning();

        Assert.True(machine.TryFinish());
        Assert.Equal(TrinoQueryState.Finished, machine.Current);
    }

    [Fact]
    public void OnlyOneOfCancelOrFinish_CanWin()
    {
        var machine = new QueryStateMachine();
        machine.MarkRunning();

        Assert.True(machine.TryFinish());
        Assert.False(machine.TryCancel());
        Assert.Equal(TrinoQueryState.Finished, machine.Current);
    }

    [Fact]
    public void TryCancel_TransitionsFromSubmitting_WhenCancelledBeforeRunning()
    {
        var machine = new QueryStateMachine();

        Assert.True(machine.TryCancel());
        Assert.Equal(TrinoQueryState.Cancelled, machine.Current);
    }

    [Fact]
    public void TryFail_DoesNotOverwriteATerminalState()
    {
        var machine = new QueryStateMachine();
        machine.MarkRunning();
        machine.TryTimeout();

        Assert.False(machine.TryFail());
        Assert.Equal(TrinoQueryState.TimedOut, machine.Current);
    }
}
