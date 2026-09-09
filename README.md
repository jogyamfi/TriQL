# TriQL — .NET Client for Trino

TriQL is a from-scratch .NET client library for [Trino](https://trino.io). It provides two
complementary programming surfaces over the same core engine:

- **TriQL SDK** (`TriQL.Client`) — a low-level, async-first client that maps directly onto the
  Trino client protocol (sessions, statements, pages, rows). Intended for high-throughput data
  movement, custom tooling, and callers who want explicit control over paging and buffering.
- **TriQL ADO.NET Provider** (`TriQL.Data.ADO`) — a complete `System.Data.Common` provider
  (`DbConnection`, `DbCommand`, `DbDataReader`, `DbParameter`, `DbProviderFactory`) so that Trino
  can be used from existing ADO.NET-based applications, ORMs, reporting tools, and BI connectors.

Additional packages:

- `TriQL.Client.Auth` — cloud/enterprise authentication providers (Microsoft Entra ID, OAuth 2.0
  client credentials) kept isolated so their dependencies never leak into the core client.
- `TriQL.Client.Compression` — support for compressed spooled protocol payloads.

See [docs/requirements.md](https://github.com/jogyamfi/TriQL/blob/main/docs/requirements.md) for
the full requirements specification and
[docs/implementation-plan.md](https://github.com/jogyamfi/TriQL/blob/main/docs/implementation-plan.md)
for the phased delivery plan.

## Status

⚠️ **Pre-release.** TriQL has not yet shipped a stable 1.0.0. Published `1.0.0-preview.*`
packages are functional but their APIs and package layout may still change before 1.0.0. From
1.0.0 onward TriQL follows [Semantic Versioning](https://semver.org): breaking public-API changes
only in a major version, additive API in minors, fixes in patches. The public API surface of each
shipping package is locked by `PublicAPI.Shipped.txt` baselines and enforced at build time.

## Requirements

- .NET 8.0 or .NET 10.0. `netstandard2.0` and .NET Framework are not supported.
- **Trino server 466 or later** (27 Nov 2024, the release that introduced the spooling protocol).
  This is the minimum version the conformance suite verifies against; the CI matrix covers
  `{466, latest}`.

## Install

```bash
dotnet add package TriQL.Client        # streaming SDK
dotnet add package TriQL.Data.ADO      # ADO.NET provider
dotnet add package TriQL.Client.Auth   # optional: Entra ID / OAuth2 authentication
```

While TriQL is in preview, add `--prerelease` to each command.

## Quick start: TriQL SDK (streaming)

```csharp
using TriQL.Client;

var options = new TrinoSessionOptions
{
    Server = new Uri("https://trino.example.com:443/"),
    Catalog = "tpch",
    Schema = "tiny",
};

await using var client = new TrinoClient(options);
await using var resultSet = await client.ExecuteAsync("SELECT nationkey, name FROM tpch.tiny.nation ORDER BY nationkey");

await foreach (var row in resultSet.ReadRowsAsync())
{
    Console.WriteLine($"{row.GetInt64(0)}: {row.GetString(1)}");
}
```

## Quick start: ADO.NET provider

```csharp
using System.Data.Common;
using TriQL.Data.ADO;

var connectionString = new TrinoConnectionStringBuilder
{
    Server = "https://trino.example.com:443/",
    Catalog = "tpch",
    Schema = "tiny",
}.ConnectionString;

await using DbConnection connection = new TrinoConnection(connectionString);
await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = "SELECT nationkey, name FROM tpch.tiny.nation ORDER BY nationkey";

await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"{reader.GetInt64(0)}: {reader.GetString(1)}");
}
```

More runnable examples — including Microsoft Entra ID authentication and `IAsyncEnumerable`
consumption — live in
[samples/TriQL.Samples.Console](https://github.com/jogyamfi/TriQL/blob/main/samples/TriQL.Samples.Console/Program.cs).

## Spooling protocol (experimental)

Trino's spooling protocol (compressed, object-storage-backed result segments) is implemented but
ships **off by default and opt-in** in 1.0: `TrinoSessionOptions.QueryDataEncodings` defaults to
an empty list, which forces the direct protocol. Enable it explicitly once your cluster is
configured for spooling:

```csharp
options.QueryDataEncodings = ["json+zstd", "json+lz4", "json"];
```

Spooling is opt-in in 1.0 because it is verified against a scripted test double rather than a
real spooling-configured cluster; see
[docs/implementation-plan.md](https://github.com/jogyamfi/TriQL/blob/main/docs/implementation-plan.md#phase-7--spooling-validation-and-promotion)
for the plan to validate it against real object storage and promote it to the default in 1.1.0.

## Documentation

- [Connection-string reference](https://github.com/jogyamfi/TriQL/blob/main/docs/connection-string-reference.md)
- [Type-mapping reference](https://github.com/jogyamfi/TriQL/blob/main/docs/type-mapping-reference.md)
- [Troubleshooting guide](https://github.com/jogyamfi/TriQL/blob/main/docs/troubleshooting.md)
- [Benchmarks](https://github.com/jogyamfi/TriQL/blob/main/docs/benchmarks.md)
- [API reference](https://github.com/jogyamfi/TriQL/blob/main/docs/api-reference.md) (generated from XML doc comments)
- [Publishing guide](https://github.com/jogyamfi/TriQL/blob/main/docs/publishing.md) (maintainers: release and NuGet process)

## License

[Apache-2.0](https://github.com/jogyamfi/TriQL/blob/main/LICENSE)

