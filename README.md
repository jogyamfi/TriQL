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

- `TriQL.Client.Auth` — cloud/enterprise authentication providers (e.g. Microsoft Entra ID) kept
  isolated so their dependencies never leak into the core client.
- `TriQL.Client.Compression` — support for compressed spooled protocol payloads.

See [docs/requirements.md](docs/requirements.md) for the full requirements specification and
[docs/implementation-plan.md](docs/implementation-plan.md) for the phased delivery plan.

## Status

⚠️ **Experimental — work in progress.** TriQL is under active development and has not yet
reached a 1.0 release. APIs, behavior, and package layout may change without notice at any time
before version 1.0.0 ships. It is not recommended for production use until then.
