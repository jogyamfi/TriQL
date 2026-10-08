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
- `TriQL.EntityFrameworkCore` (**preview**) — an Entity Framework Core 10 provider: LINQ on any
  connector, `SaveChanges` and bulk updates on Iceberg, and `dotnet ef dbcontext scaffold`.

See [docs/requirements.md](https://github.com/jogyamfi/TriQL/blob/main/docs/requirements.md) for
the full requirements specification and
[docs/implementation-plan.md](https://github.com/jogyamfi/TriQL/blob/main/docs/implementation-plan.md)
for the phased delivery plan.

## Status

**Stable — 1.0.0.** TriQL follows [Semantic Versioning](https://semver.org): breaking public-API
changes only in a major version, additive API in minors, fixes in patches. The public API surface
of each shipping package is locked by `PublicAPI.Shipped.txt` baselines and enforced at build time.

The one deliberately conservative default in 1.x is the spooling protocol, which ships implemented
and tested but **opt-in** — see [Spooling protocol](#spooling-protocol-opt-in). Release notes for
every version are published on [GitHub Releases](https://github.com/jogyamfi/TriQL/releases).

## Requirements

- .NET 10.0. `netstandard2.0` and .NET Framework are not supported.
- **Trino server 466 or later** (27 Nov 2024, the release that introduced the spooling protocol).
  This is the minimum version the conformance suite verifies against; the CI matrix covers
  `{466, latest}`.

## Install

```bash
dotnet add package TriQL.Client        # streaming SDK
dotnet add package TriQL.Data.ADO      # ADO.NET provider
dotnet add package TriQL.Client.Auth   # optional: Entra ID / OAuth2 authentication
dotnet add package TriQL.Client.Compression  # optional: json+lz4 / json+zstd spooling codecs
dotnet add package TriQL.EntityFrameworkCore --prerelease  # preview: Entity Framework Core 10 provider
```

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

## Quick start: Entity Framework Core (preview)

```csharp
public class Nation
{
    public long NationKey { get; set; }
    public string Name { get; set; } = "";
    public long RegionKey { get; set; }
}

public class TpchContext(DbContextOptions<TpchContext> options) : DbContext(options)
{
    public DbSet<Nation> Nations => Set<Nation>();

    // Trino identifiers are case-insensitive, so "NationKey" matches the nationkey column.
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Nation>().ToTable("nation").HasKey(n => n.NationKey);
}

services.AddDbContext<TpchContext>(options => options.UseTrino(
    "Server=https://trino.example.com;Catalog=tpch;Schema=tiny;User=analyst",
    o => o.EnableRetryOnFailure()));

var europe = await db.Nations.Where(n => n.RegionKey == 3).OrderBy(n => n.Name).Select(n => n.Name).ToListAsync();
```

Trino has no multi-statement transactions, identity columns or enforced keys, so the provider sets
keys in .NET, commits each `SaveChanges` statement on its own, and rejects unsupported model
configuration with guidance. Writes are verified on the Iceberg connector. See the
[EF Core guide](https://github.com/jogyamfi/TriQL/blob/main/docs/efcore.md) and
[samples/TriQL.Samples.EntityFrameworkCore](https://github.com/jogyamfi/TriQL/blob/main/samples/TriQL.Samples.EntityFrameworkCore/Program.cs).

## Spooling protocol (opt-in)

Trino's spooling protocol (compressed, object-storage-backed result segments) is implemented but
ships **off by default and opt-in** (1.0 and 1.1): `TrinoSessionOptions.QueryDataEncodings` defaults to
an empty list, which forces the direct protocol. Enable it explicitly once your cluster is
configured for spooling:

```csharp
// The json+lz4 and json+zstd codecs live in the separate TriQL.Client.Compression package
// (kept out of TriQL.Client to stay dependency-free) and must be registered before executing
// a query that can select them, or the client throws TrinoProtocolException once the server
// picks an encoding it can't decode.
TriQL.Client.Compression.CompressionCodecs.RegisterAll();

options.QueryDataEncodings = ["json+zstd", "json+lz4", "json"];
```

Add a `<PackageReference Include="TriQL.Client.Compression" />` to your project, or set
`QueryDataEncodings = ["json"]` to enable spooling with the built-in uncompressed codec only,
which needs no extra package or registration call.

Cluster prerequisites:

- The coordinator must be spooling-configured (`protocol.spooling.enabled=true`, an object-storage
  spooling manager, and a shared secret key). A cluster without spooling simply returns
  direct-protocol data, which TriQL handles transparently.
- **The coordinator must be served over HTTPS**, as well as the object store. TriQL requires
  `https` on every segment and acknowledgement URI, so a plain-HTTP coordinator with spooling
  enabled fails with `TrinoProtocolException`.

Related options on `TrinoSessionOptions`: `SegmentFetchParallelism` (default 4),
`MaxDecompressedSegmentBytes` (default 256 MiB), and `SegmentHostAllowlist` (restricts which
object-storage hosts segments may be fetched from; empty allows any `https` host).

The spooled path is verified in CI against a real spooling-configured Trino coordinator backed by
MinIO (all three codecs, SSE-C encryption, segment acknowledgement, and cancellation cleanup). It
stays opt-in because it has not yet been checked against AWS S3 or Azure Blob Storage, and
throughput over the spooled path has not been benchmarked. Making it the default is planned for a
later minor release; that release will call out the behaviour change, and setting
`QueryDataEncodings = []` will keep the direct protocol.

## Documentation

- [Entity Framework Core guide](https://github.com/jogyamfi/TriQL/blob/main/docs/efcore.md) (preview provider)
- [Connection-string reference](https://github.com/jogyamfi/TriQL/blob/main/docs/connection-string-reference.md)
- [Type-mapping reference](https://github.com/jogyamfi/TriQL/blob/main/docs/type-mapping-reference.md)
- [Troubleshooting guide](https://github.com/jogyamfi/TriQL/blob/main/docs/troubleshooting.md)
- [Benchmarks](https://github.com/jogyamfi/TriQL/blob/main/docs/benchmarks.md)
- [API reference](https://github.com/jogyamfi/TriQL/blob/main/docs/api-reference.md) (generated from XML doc comments)
- [Release notes](https://github.com/jogyamfi/TriQL/releases)

## License

[Apache-2.0](https://github.com/jogyamfi/TriQL/blob/main/LICENSE)

