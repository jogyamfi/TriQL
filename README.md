# TriQL — .NET client for Trino

[![NuGet](https://img.shields.io/nuget/v/TriQL.Client.svg)](https://www.nuget.org/packages/TriQL.Client)
[![Downloads](https://img.shields.io/nuget/dt/TriQL.Client.svg)](https://www.nuget.org/packages/TriQL.Client)
[![CI](https://github.com/jogyamfi/TriQL/actions/workflows/ci.yml/badge.svg)](https://github.com/jogyamfi/TriQL/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](https://github.com/jogyamfi/TriQL/blob/main/LICENSE)

TriQL lets .NET applications query [Trino](https://trino.io). Use it three ways, depending on how
much abstraction you want:

- a **streaming client** that hands you rows as the server produces them,
- an **ADO.NET provider** (`DbConnection`, `DbCommand`, `DbDataReader`) that works with Dapper,
  reporting tools and any other ADO.NET-based code,
- an **Entity Framework Core provider** for LINQ queries and `SaveChanges`.

## Packages

| Package | Use it for |
|---|---|
| [`TriQL.Client`](https://www.nuget.org/packages/TriQL.Client) | The async streaming client. No third-party dependencies beyond `Microsoft.Extensions.Logging.Abstractions`. |
| [`TriQL.Data.ADO`](https://www.nuget.org/packages/TriQL.Data.ADO) | The ADO.NET provider. |
| [`TriQL.EntityFrameworkCore`](https://www.nuget.org/packages/TriQL.EntityFrameworkCore) | The Entity Framework Core 10 provider. |
| [`TriQL.Client.Auth`](https://www.nuget.org/packages/TriQL.Client.Auth) | Optional. Microsoft Entra ID and OAuth 2.0 client-credentials authentication. |
| [`TriQL.Client.Compression`](https://www.nuget.org/packages/TriQL.Client.Compression) | Optional. LZ4 and Zstandard codecs for the spooling protocol. |

All packages share one version number and follow [Semantic Versioning](https://semver.org).

## Requirements

- .NET 10. (.NET Framework and `netstandard2.0` are not supported.)
- Trino 466 or later.

## Install

Install the package for the layer you want; each one brings in the layers beneath it.

```bash
dotnet add package TriQL.Client               # streaming client
dotnet add package TriQL.Data.ADO             # ADO.NET provider
dotnet add package TriQL.EntityFrameworkCore  # EF Core provider
```

## Quick start

### Streaming client

```csharp
using TriQL.Client;

var options = new TrinoSessionOptions
{
    Server = new Uri("https://trino.example.com/"),
    Catalog = "tpch",
    Schema = "tiny",
};

await using var client = new TrinoClient(options);
await using var resultSet = await client.ExecuteAsync(
    "SELECT nationkey, name FROM nation ORDER BY nationkey");

await foreach (var row in resultSet.ReadRowsAsync())
{
    Console.WriteLine($"{row.GetInt64(0)}: {row.GetString(1)}");
}
```

Rows are streamed, not buffered: memory use stays flat however large the result is.

### ADO.NET

```csharp
using TriQL.Data.ADO;

await using var connection = new TrinoConnection(
    "Server=https://trino.example.com/;Catalog=tpch;Schema=tiny");
await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = "SELECT nationkey, name FROM nation ORDER BY nationkey";

await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"{reader.GetInt64(0)}: {reader.GetString(1)}");
}
```

Every connection-string key is listed in the
[connection-string reference](https://github.com/jogyamfi/TriQL/blob/main/docs/connection-string-reference.md).

### Entity Framework Core

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

var names = await db.Nations
    .Where(n => n.RegionKey == 3)
    .OrderBy(n => n.Name)
    .Select(n => n.Name)
    .ToListAsync();
```

LINQ queries work against any Trino connector. Writes (`SaveChanges`, `ExecuteUpdate`,
`ExecuteDelete`) are verified on the Iceberg connector. Three things differ from a typical
relational provider, because Trino itself works differently:

- **`SaveChanges` is not atomic.** Trino has no multi-statement transactions, so each statement
  commits on its own.
- **Keys are generated in .NET.** Trino has no identity columns.
- **Migrations are not supported.** `EnsureCreated`/`EnsureDeleted` and
  `dotnet ef dbcontext scaffold` are.

The [EF Core guide](https://github.com/jogyamfi/TriQL/blob/main/docs/efcore.md) covers all of this
in detail.

## Parameters

Pass values as parameters instead of concatenating them into SQL. Both positional (`?`) and named
(`:name`) placeholders are supported:

```csharp
var parameters = new TrinoParameterCollection();
parameters.Add("region", 3);

await using var resultSet = await client.ExecuteAsync(
    "SELECT name FROM nation WHERE regionkey = :region", parameters);
```

The ADO.NET provider takes parameters through the usual `DbCommand.Parameters` collection.

## Authentication

| Method | Connection string | Streaming client |
|---|---|---|
| None (user name only) | `User=alice` | default |
| Password (Basic / LDAP) | `Auth=basic;User=alice;Password=…` | `new BasicAuthenticator(user, password)` |
| JWT | `Auth=jwt;AccessToken=…` | `new JwtAuthenticator(token)`, or a callback that refreshes the token |
| Client certificate (mutual TLS) | `Auth=certificate;ClientCertificatePath=…` | `ClientCertificateAuthenticator.FromFile(path)` |
| Microsoft Entra ID | `Auth=entra-id;…` | `new EntraIdAuthenticator(scopes, credential)` |
| OAuth 2.0 client credentials | `Auth=oauth2-client-credentials;…` | `new OAuth2ClientCredentialsAuthenticator(…)` |

With the streaming client, assign the authenticator to `TrinoSessionOptions.Authenticator`:

```csharp
var options = new TrinoSessionOptions
{
    Server = new Uri("https://trino.example.com/"),
    Authenticator = new BasicAuthenticator("alice", password),
};
```

Entra ID and OAuth 2.0 need the `TriQL.Client.Auth` package. Credentials are only sent over HTTPS
unless you explicitly allow otherwise. You can also plug in your own scheme by implementing
`ITrinoAuthenticator`.

## Spooling protocol

Trino's [spooling protocol](https://trino.io/docs/current/client/client-protocol.html) delivers
large results as compressed segments from object storage instead of through the coordinator. TriQL
supports it, but it is **off by default** — turn it on once your cluster is configured for it:

```csharp
// Needs the TriQL.Client.Compression package. Call once at startup.
TriQL.Client.Compression.CompressionCodecs.RegisterAll();

options.QueryDataEncodings = ["json+zstd", "json+lz4", "json"];
```

To use spooling without the extra package, set `QueryDataEncodings = ["json"]` (uncompressed).

Things to know:

- The cluster needs `protocol.spooling.enabled=true`, an object-storage spooling manager and a
  shared secret key. If the cluster is not set up for spooling, TriQL falls back to the direct
  protocol automatically.
- Both the coordinator and the object store must be served over **HTTPS**. TriQL refuses to fetch
  segments over plain HTTP.
- Tuning options on `TrinoSessionOptions`: `SegmentFetchParallelism` (default 4),
  `MaxDecompressedSegmentBytes` (default 256 MiB) and `SegmentHostAllowlist` (limits which hosts
  segments may be fetched from).
- Spooling is tested against MinIO. It has not yet been verified against AWS S3 or Azure Blob
  Storage, which is why it remains opt-in. A future minor release is expected to enable it by
  default; setting `QueryDataEncodings = []` will keep the direct protocol.

## Diagnostics

TriQL logs through `Microsoft.Extensions.Logging` and publishes OpenTelemetry-compatible metrics
and traces under the name `TriQL.Client`. Nothing is emitted unless you subscribe. See the
[troubleshooting guide](https://github.com/jogyamfi/TriQL/blob/main/docs/troubleshooting.md) for
the exception types, common errors and how to turn on detailed logging.

## Documentation

- [Entity Framework Core guide](https://github.com/jogyamfi/TriQL/blob/main/docs/efcore.md)
- [Connection-string reference](https://github.com/jogyamfi/TriQL/blob/main/docs/connection-string-reference.md)
- [Type-mapping reference](https://github.com/jogyamfi/TriQL/blob/main/docs/type-mapping-reference.md) — how Trino types map to .NET types
- [Troubleshooting guide](https://github.com/jogyamfi/TriQL/blob/main/docs/troubleshooting.md)
- [Samples](https://github.com/jogyamfi/TriQL/tree/main/samples) — runnable projects for each quick start and authentication method
- [Changelog](https://github.com/jogyamfi/TriQL/blob/main/CHANGELOG.md) and [release notes](https://github.com/jogyamfi/TriQL/releases)

## Getting help and contributing

Found a bug or have a question? [Open an issue](https://github.com/jogyamfi/TriQL/issues). Pull
requests are welcome — to build and run the unit tests you need only the .NET 10 SDK:

```bash
dotnet build
dotnet test
```

The integration tests start Trino in a container, so they also need Docker.

## License

[Apache-2.0](https://github.com/jogyamfi/TriQL/blob/main/LICENSE)

TriQL is an independent project and is not affiliated with the Trino Software Foundation.
