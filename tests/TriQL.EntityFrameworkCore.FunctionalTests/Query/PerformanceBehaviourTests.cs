using System.Net;
using System.Net.NetworkInformation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>
/// Phase 7 live checks: HTTP connections are reused across pooled contexts (EF7-T4), and compiled queries
/// run with fresh parameter values (EF7-T5).
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfRead")]
public sealed class PerformanceBehaviourTests(TrinoContainerFixture fixture, ITestOutputHelper output)
{
    private const int Leases = 20;

    [Fact]
    public async Task PooledContexts_ReuseTheirHttpConnection_UnpooledContextsDoNot()
    {
        var options = new DbContextOptionsBuilder<BloggingContext>()
            .UseTrino(await BloggingDatabase.GetConnectionStringAsync(fixture))
            .Options;

        var pooled = await CountNewConnectionsAsync(async () =>
        {
            var factory = new PooledDbContextFactory<BloggingContext>(options);
            for (var i = 0; i < Leases; i++)
            {
                await using var db = await factory.CreateDbContextAsync();
                await db.Blogs.CountAsync();
            }
        });

        var unpooled = await CountNewConnectionsAsync(async () =>
        {
            for (var i = 0; i < Leases; i++)
            {
                await using var db = new BloggingContext(options);
                await db.Blogs.CountAsync();
            }
        });

        output.WriteLine($"{Leases} queries: {pooled} new TCP connections with a pooled context, {unpooled} without pooling.");
        Assert.InRange(pooled, 1, 2);
        Assert.True(unpooled >= Leases, $"Expected a connection per unpooled context, saw {unpooled}.");
    }

    [Fact]
    public async Task CompiledQuery_RunsWithEachCallsParameters()
    {
        var query = EF.CompileAsyncQuery((BloggingContext db, long minViews) =>
            db.Posts.Where(p => p.Views > minViews).OrderBy(p => p.Id).Select(p => p.Id));
        await using var db = new BloggingContext(new DbContextOptionsBuilder<BloggingContext>()
            .UseTrino(await BloggingDatabase.GetConnectionStringAsync(fixture))
            .Options);

        foreach (var minViews in new[] { 0L, 1_000L, 1_000_000L })
        {
            var actual = new List<int>();
            await foreach (var id in query(db, minViews))
            {
                actual.Add(id);
            }

            Assert.Equal(BloggingDatabase.Posts.Where(p => p.Views > minViews).OrderBy(p => p.Id).Select(p => p.Id), actual);
        }
    }

    /// <summary>The client-side TCP connections to the coordinator that <paramref name="action"/> opened.</summary>
    private async Task<int> CountNewConnectionsAsync(Func<Task> action)
    {
        var before = LocalPortsToServer();
        await action();
        return LocalPortsToServer().Except(before).Count();
    }

    // Every state (including TIME_WAIT), so connections opened and already closed are counted too.
    private HashSet<int> LocalPortsToServer()
    {
        var port = fixture.ServerUri.Port;
        return [.. IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
            .Where(c => c.RemoteEndPoint.Port == port && IPAddress.IsLoopback(c.RemoteEndPoint.Address))
            .Select(c => c.LocalEndPoint.Port)];
    }
}
