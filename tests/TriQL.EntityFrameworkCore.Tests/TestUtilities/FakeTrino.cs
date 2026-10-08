using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using TriQL.Client;
using TriQL.Client.Tests.Fakes;
using TriQL.Data.ADO;

namespace TriQL.EntityFrameworkCore.Tests.TestUtilities;

/// <summary>
/// The SQL-baseline harness (EF1-T6): a scripted in-process coordinator behind a
/// <see cref="TrinoConnection"/>, plus capture of every command EF executes. Tests script the
/// server's answers with <see cref="EnqueueRows"/>/<see cref="EnqueueUpdate"/>, run EF, then
/// assert the exact SQL with <see cref="AssertSql"/>.
/// </summary>
internal sealed class FakeTrino : IDisposable
{
    private readonly List<string> _sql = [];
    private readonly List<(EventId Id, string Message)> _providerEvents = [];

    public FakeTrino() =>
        Connection = new TrinoConnection(
            new TrinoSessionOptions { Server = new Uri("https://trino.example.com/"), TimeZone = "UTC" },
            new StubHttpClientFactory(Coordinator));

    public FakeTrinoCoordinator Coordinator { get; } = new();

    /// <summary>A connection whose requests go to <see cref="Coordinator"/>.</summary>
    public TrinoConnection Connection { get; }

    /// <summary>The SQL text of every command EF executed, in order.</summary>
    public IReadOnlyList<string> Sql => _sql;

    /// <summary>
    /// Every Trino provider event logged (IDs from <see cref="CoreEventId.ProviderBaseId"/>). Tests read
    /// events here rather than adding their own <c>LogTo</c>, which would replace this one.
    /// </summary>
    public IReadOnlyList<(EventId Id, string Message)> ProviderEvents => _providerEvents;

    /// <summary>The body of every statement submitted to the coordinator (what Trino actually receives).</summary>
    public IReadOnlyList<string?> SubmittedBodies =>
        [.. Coordinator.ReceivedRequests.Where(r => r.Method == HttpMethod.Post).Select(r => r.Body)];

    /// <summary>Options for a context that talks to this fake and records its SQL.</summary>
    public DbContextOptions<TContext> CreateOptions<TContext>(Action<DbContextOptionsBuilder<TContext>>? configure = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>()
            .UseTrino(Connection)
            .LogTo(
                (eventId, _) => eventId == RelationalEventId.CommandExecuting || eventId.Id >= CoreEventId.ProviderBaseId,
                eventData =>
                {
                    if (eventData is CommandEventData command)
                    {
                        _sql.Add(command.Command.CommandText);
                    }
                    else
                    {
                        _providerEvents.Add((eventData.EventId, eventData.ToString()));
                    }
                });
        configure?.Invoke(builder);
        return builder.Options;
    }

    /// <summary>Scripts the next statement's response as a finished result set.</summary>
    public void EnqueueRows(IReadOnlyList<(string Name, string Type)> columns, params object?[][] rows)
    {
        var body = new
        {
            id = "q",
            columns = columns.Select(c => new { name = c.Name, type = c.Type }),
            data = rows,
        };
        Coordinator.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(body));
    }

    /// <summary>Scripts the next statement's response as a finished DML statement, shaped as a real coordinator sends it.</summary>
    public void EnqueueUpdate(string updateType, long updateCount) =>
        Coordinator.Enqueue(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new
            {
                id = "q",
                columns = new[] { new { name = "rows", type = "bigint" } },
                data = new[] { new[] { updateCount } },
                updateType,
                updateCount,
            }));

    /// <summary>
    /// Scripts the next statement's response as an Iceberg metadata-only <c>DELETE</c> that matched nothing:
    /// <c>rows</c> is <c>NULL</c> and there is no <c>updateCount</c> (measured on Trino 466).
    /// </summary>
    public void EnqueueUpdateWithoutCount(string updateType) =>
        Coordinator.Enqueue(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new
            {
                id = "q",
                columns = new[] { new { name = "rows", type = "bigint" } },
                data = new[] { new long?[] { null } },
                updateType,
            }));

    /// <summary>Scripts the next statement's response as a finished DDL statement (no result set).</summary>
    public void EnqueueDdl(string updateType) =>
        Coordinator.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(new { id = "q", updateType }));

    /// <summary>Scripts the next statement's response as a query failure, shaped as a real coordinator sends it.</summary>
    public void EnqueueError(string errorName, string errorType = "EXTERNAL", string message = "Query failed") =>
        Coordinator.Enqueue(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new
            {
                id = "q",
                error = new { message, errorCode = 65536, errorName, errorType },
            }));

    /// <summary>Asserts the SQL EF executed, exactly and in order.</summary>
    public void AssertSql(params string[] expected) =>
        Assert.Equal(string.Join(StatementSeparator, expected.Select(Normalize)), string.Join(StatementSeparator, _sql.Select(Normalize)));

    // Compared as one string so a mismatch shows the whole SQL text, not a truncated sequence.
    private const string StatementSeparator = "\n----\n";

    public void Dispose()
    {
        Connection.Dispose();
        Coordinator.Dispose();
    }

    private static string Normalize(string sql) => sql.ReplaceLineEndings("\n").Trim();

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
