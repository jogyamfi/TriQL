using System.Net;
using TriQL.Client.Exceptions;
using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests.Streaming;

public sealed class AdvanceLoopTests
{
    [Fact]
    public async Task Advance_AppendsTargetResultSize_ExactlyOnce_AcrossManyPolls()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, SubmitPage(nextUri: "http://coordinator/v1/statement/executing/q1/1"));
        for (var i = 2; i <= 5; i++)
        {
            var next = i < 5 ? $"http://coordinator/v1/statement/executing/q1/{i}" : null;
            fake.Enqueue(HttpStatusCode.OK, EmptyPage(next, id: "q1"));
        }

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            TargetResultSizeBytes = 5_242_880,
            PollingBackoffInitialDelay = TimeSpan.FromMilliseconds(1),
            PollingBackoffMaxDelay = TimeSpan.FromMilliseconds(1),
        };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT 1");
        await foreach (var _ in resultSet.ReadRowsAsync())
        {
        }

        var pollRequests = fake.ReceivedRequests.Where(r => r.Method == HttpMethod.Get).ToList();
        Assert.NotEmpty(pollRequests);
        foreach (var request in pollRequests)
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            var occurrences = query.Split("targetResultSize=").Length - 1;
            Assert.True(occurrences <= 1, $"Expected at most one targetResultSize parameter, found {occurrences} in '{query}'.");
        }

        Assert.Contains(pollRequests, r => r.RequestUri!.Query.Contains("targetResultSize=5MB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Advance_SkipsManyEmptyPages_IterativelyWithoutError()
    {
        const int EmptyPageCount = 2000;

        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, SubmitPage(nextUri: "http://coordinator/v1/statement/queued/1"));
        for (var i = 2; i <= EmptyPageCount; i++)
        {
            fake.Enqueue(HttpStatusCode.OK, EmptyPage($"http://coordinator/v1/statement/queued/{i}", id: "q1"));
        }

        fake.Enqueue(HttpStatusCode.OK, FinalPageWithRows());

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            PollingBackoffInitialDelay = TimeSpan.FromMilliseconds(1),
            PollingBackoffMaxDelay = TimeSpan.FromMilliseconds(1),
        };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT 1");
        var rows = new List<TrinoRow>();
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            rows.Add(row.Clone());
        }

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Advance_RaisesTrinoQueryException_WhenAPageCarriesAnError()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, SubmitPage(nextUri: "http://coordinator/v1/statement/queued/1"));
        fake.Enqueue(HttpStatusCode.OK, ErrorPage());

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            PollingBackoffInitialDelay = TimeSpan.FromMilliseconds(1),
            PollingBackoffMaxDelay = TimeSpan.FromMilliseconds(1),
        };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT 1 / 0");

        var exception = await Assert.ThrowsAsync<TrinoQueryException>(async () =>
        {
            await foreach (var _ in resultSet.ReadRowsAsync())
            {
            }
        });

        Assert.Equal("DIVISION_BY_ZERO", exception.ErrorName);
        Assert.Equal(TrinoErrorType.UserError, exception.ErrorType);
    }

    private static string SubmitPage(string nextUri) =>
        $$"""
        {"id":"q1","infoUri":"http://coordinator/ui/q1","nextUri":"{{nextUri}}","columns":[{"name":"nationkey","type":"bigint"}],"data":[]}
        """;

    private static string EmptyPage(string? nextUri, string id)
    {
        var nextUriJson = nextUri is null ? "null" : $"\"{nextUri}\"";
        return $$"""
        {"id":"{{id}}","nextUri":{{nextUriJson}},"data":[]}
        """;
    }

    private static string FinalPageWithRows() =>
        """
        {"id":"q1","nextUri":null,"columns":[{"name":"nationkey","type":"bigint"}],"data":[[1],[2]]}
        """;

    private static string ErrorPage() =>
        """
        {"id":"q1","nextUri":null,"error":{"message":"Division by zero","errorCode":8,"errorName":"DIVISION_BY_ZERO","errorType":"USER_ERROR"}}
        """;
}
