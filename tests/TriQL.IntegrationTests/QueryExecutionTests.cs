using System.Net.Http.Json;
using System.Text.Json;
using TriQL.Client;
using TriQL.Client.Exceptions;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// Phase 2 exit criteria: a <c>SELECT</c> streams to completion against a real container, and
/// cancellation is confirmed to terminate the query server-side via <c>/v1/query/{id}</c>.
/// Replaces the throwaway <c>WalkingSkeletonTests</c> (deleted per P2-T4).
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class QueryExecutionTests(TrinoContainerFixture fixture)
{
    [Fact]
    public async Task SelectQuery_StreamsToCompletion_WithSchemaAndAllRows()
    {
        var options = new TrinoSessionOptions { Server = fixture.ServerUri };
        await using var client = new TrinoClient(options);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey, name FROM tpch.tiny.nation ORDER BY nationkey");

        var columns = await resultSet.WaitForSchemaAsync();
        Assert.Equal(["nationkey", "name"], columns.Select(c => c.Name));

        var rows = new List<object?[]>();
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            rows.Add(row.ToArray());
        }

        Assert.Equal(25, rows.Count);
        Assert.Equal(0L, rows[0][0]);
        Assert.Equal(TrinoQueryState.Finished, resultSet.State);
    }

    [Fact]
    public async Task Cancellation_TerminatesTheQuery_ServerSide()
    {
        var options = new TrinoSessionOptions { Server = fixture.ServerUri };
        await using var client = new TrinoClient(options);

        // A cross join against itself is slow enough that cancellation reliably lands mid-query.
        await using var resultSet = await client.ExecuteAsync(
            "SELECT count(*) FROM tpch.sf1.lineitem l1, tpch.sf1.lineitem l2 WHERE l1.orderkey = l2.orderkey");

        using var cts = new CancellationTokenSource();
        var readTask = Task.Run(async () =>
        {
            await foreach (var _ in resultSet.ReadRowsAsync(cts.Token))
            {
                await cts.CancelAsync();
            }
        });

        // Ensure at least one page has been observed before cancelling, then cancel shortly after.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);

        var queryId = resultSet.QueryId;
        using var httpClient = new HttpClient { BaseAddress = fixture.ServerUri };

        var terminated = false;
        for (var attempt = 0; attempt < 20 && !terminated; attempt++)
        {
            using var response = await httpClient.GetAsync($"v1/query/{queryId}");
            if (response.IsSuccessStatusCode)
            {
                var info = await response.Content.ReadFromJsonAsync<JsonElement>();
                var state = info.GetProperty("state").GetString();
                terminated = state is "FAILED" or "FINISHED";
            }
            else
            {
                // The coordinator purges query info after a delay; a 404 here also confirms the
                // query is no longer tracked as running.
                terminated = true;
            }

            if (!terminated)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }

        Assert.True(terminated, $"Query {queryId} did not terminate server-side after cancellation.");
    }
}
