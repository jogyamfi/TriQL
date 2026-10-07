using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    public FakeTrino() =>
        Connection = new TrinoConnection(
            new TrinoSessionOptions { Server = new Uri("https://trino.example.com/"), TimeZone = "UTC" },
            new StubHttpClientFactory(Coordinator));

    public FakeTrinoCoordinator Coordinator { get; } = new();

    /// <summary>A connection whose requests go to <see cref="Coordinator"/>.</summary>
    public TrinoConnection Connection { get; }

    /// <summary>The SQL text of every command EF executed, in order.</summary>
    public IReadOnlyList<string> Sql => _sql;

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
                (eventId, _) => eventId == RelationalEventId.CommandExecuting,
                eventData => _sql.Add(((CommandEventData)eventData).Command.CommandText));
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

    /// <summary>Asserts the SQL EF executed, exactly and in order.</summary>
    public void AssertSql(params string[] expected) =>
        Assert.Equal(expected.Select(Normalize), _sql.Select(Normalize));

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
