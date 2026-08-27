using TriQL.Data.ADO.Internal;

namespace TriQL.Data.ADO.Tests.Internal;

public sealed class SyncBridgeTests
{
    [Fact]
    public void Run_ReturnsResult_ForSimpleAsyncWork()
    {
        var result = SyncBridge.Run(async () =>
        {
            await Task.Delay(1);
            return 42;
        });

        Assert.Equal(42, result);
    }

    [Fact]
    public void Run_PropagatesExceptions()
    {
        Assert.Throws<InvalidOperationException>(() => SyncBridge.Run<int>(async () =>
        {
            await Task.Delay(1);
            throw new InvalidOperationException("boom");
        }));
    }

    [Fact]
    public void Run_DoesNotDeadlock_UnderACapturedSynchronizationContextThatNeverPumps()
    {
        // Simulates a UI/ASP.NET-style synchronization context whose message loop is not running:
        // a naive `someTask.GetAwaiter().GetResult()` without first escaping via Task.Run would hang
        // forever waiting for a continuation that is never invoked (P4-T5, P4-T22, NFR-REL-2).
        var task = Task.Run(() =>
        {
            var original = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                return SyncBridge.Run(async () =>
                {
                    await Task.Delay(10);
                    return 42;
                });
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }
        });

        // xUnit1031: a bounded Task.Wait from the TEST thread is the deliberate mechanism here — it
        // detects a real deadlock inside SyncBridge.Run (running on a plain, un-contexted background
        // task) via timeout, rather than hanging the whole suite if the bridge regresses.
#pragma warning disable xUnit1031
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "SyncBridge.Run deadlocked under a captured SynchronizationContext.");
        Assert.Equal(42, task.Result);
#pragma warning restore xUnit1031
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Deliberately never invoke `d`: a real deadlock-prone context whose message loop never pumps.
        }
    }
}
