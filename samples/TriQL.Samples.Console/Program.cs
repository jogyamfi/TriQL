// TriQL quick-start samples (P6-T10, README quick starts point here). Every sample reads the
// coordinator address from TRINO_SERVER (defaulting to a local dev cluster) so this project can
// be run against a real Trino coordinator: `TRINO_SERVER=https://host:8443 dotnet run`.
using System.Data.Common;
using Azure.Identity;
using TriQL.Client;
using TriQL.Client.Auth;
using TriQL.Data.ADO;

var server = Environment.GetEnvironmentVariable("TRINO_SERVER") ?? "http://localhost:8080/";

Console.WriteLine("== SDK streaming quick start ==");
await SdkStreamingQuickStartAsync(server);

Console.WriteLine();
Console.WriteLine("== ADO.NET quick start ==");
await AdoNetQuickStartAsync(server);

Console.WriteLine();
Console.WriteLine("== IAsyncEnumerable consumption ==");
await AsyncEnumerableQuickStartAsync(server);

Console.WriteLine();
Console.WriteLine("== Microsoft Entra ID authentication ==");
EntraIdQuickStart(server);

return;

// The low-level TriQL.Client SDK: submit a statement and stream rows directly, with explicit
// control over the column schema and query progress (FR-4, FR-6).
static async Task SdkStreamingQuickStartAsync(string server)
{
    var options = new TrinoSessionOptions
    {
        Server = new Uri(server),
        Catalog = "tpch",
        Schema = "tiny",
    };

    await using var client = new TrinoClient(options);
    await using var resultSet = await client.ExecuteAsync("SELECT nationkey, name FROM tpch.tiny.nation ORDER BY nationkey LIMIT 5");

    var columns = await resultSet.WaitForSchemaAsync();
    Console.WriteLine($"Columns: {string.Join(", ", columns.Select(c => c.Name))}");

    await foreach (var row in resultSet.ReadRowsAsync())
    {
        Console.WriteLine($"  {row.GetInt64(0)}: {row.GetString(1)}");
    }
}

// The ADO.NET provider: use TriQL.Data.ADO like any other System.Data.Common provider, e.g. from
// an ORM, reporting tool, or existing ADO.NET-based application (FR-9).
static async Task AdoNetQuickStartAsync(string server)
{
    var connectionString = new TrinoConnectionStringBuilder
    {
        Server = server,
        Catalog = "tpch",
        Schema = "tiny",
    }.ConnectionString;

    await using DbConnection connection = new TrinoConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT nationkey, name FROM tpch.tiny.nation ORDER BY nationkey LIMIT 5";

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"  {reader.GetInt64(0)}: {reader.GetString(1)}");
    }
}

// TrinoResultSet.ReadRowsAsync returns IAsyncEnumerable<TrinoRow>, so it composes directly with
// `await foreach` and any hand-written async iterator, without any buffering the caller controls.
static async Task AsyncEnumerableQuickStartAsync(string server)
{
    var options = new TrinoSessionOptions { Server = new Uri(server), Catalog = "tpch", Schema = "tiny" };
    await using var client = new TrinoClient(options);
    await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");

    var total = 0L;
    await foreach (var nationKey in SelectNationKeysAsync(resultSet))
    {
        total += nationKey;
    }

    Console.WriteLine($"Sum of nationkey: {total}");
}

static async IAsyncEnumerable<long> SelectNationKeysAsync(TrinoResultSet resultSet)
{
    await foreach (var row in resultSet.ReadRowsAsync())
    {
        yield return row.GetInt64(0);
    }
}

// Microsoft Entra ID authentication via TriQL.Client.Auth (FR-2.3.2). This only builds the
// authenticator (no live acquisition happens here) since it needs real Entra ID app registration
// details to run end to end; wire the result into TrinoSessionOptions.Authenticator.
static void EntraIdQuickStart(string server)
{
    var authenticator = new EntraIdAuthenticator(
        scopes: ["https://<your-trino-app-id>/.default"],
        credential: new DefaultAzureCredential());

    var options = new TrinoSessionOptions
    {
        Server = new Uri(server),
        Authenticator = authenticator,
    };

    Console.WriteLine($"Configured EntraIdAuthenticator for {options.Server}. Supply a real scope and run a query as usual.");
}

