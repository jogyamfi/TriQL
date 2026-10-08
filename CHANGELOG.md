# Changelog

Notable changes to the TriQL packages. Versions follow [Semantic Versioning](https://semver.org);
the core packages (`TriQL.Client`, `TriQL.Client.Auth`, `TriQL.Client.Compression`, `TriQL.Data.ADO`)
share one version, and `TriQL.EntityFrameworkCore` ships as a preview with its own version. GitHub
Releases carry the same notes.

## 1.1.0 — core packages; `TriQL.EntityFrameworkCore` 1.1.0-preview.1 (unreleased)

### New: `TriQL.EntityFrameworkCore` (preview)

An Entity Framework Core 10 provider for Trino: LINQ queries on any connector, raw SQL,
`SaveChanges` with optimistic concurrency and per-statement retries, `ExecuteUpdate`/`ExecuteDelete`
(with `MERGE` for updates that join other tables), multi-row inserts, three-part
`catalog.schema.table` names, `EnsureCreated`/`EnsureDeleted` for development, and
`dotnet ef dbcontext scaffold`. Writes are verified on the Iceberg connector. Trino has no
multi-statement transactions, so `SaveChanges` is not atomic across statements; migrations are not
supported. See [docs/efcore.md](docs/efcore.md).

Requires EF Core `[10.0.12, 11.0.0)` and `TriQL.Data.ADO` 1.1.0.

### Added (core)

- `TrinoSessionOptions.ParameterBinding` and the connection-string keyword `ParameterBinding`:
  `ExecuteImmediate` sends parameterized statements in the request body
  (`EXECUTE IMMEDIATE '…' USING …`) instead of the `X-Trino-Prepared-Statement` header, for
  deployments behind proxies or gateways with small header limits. The default stays
  `PreparedStatementHeader`, which has the higher limit at the coordinator (~1.7 MB, measured on 466).
- `TrinoQueryException.IsTransient`: whether resubmitting the statement may succeed (cluster
  starting or out of memory, a full queue, lost workers, `ICEBERG_COMMIT_ERROR`, …), for callers'
  retry policies. TriQL itself still never resubmits a statement.

### Changed (core)

- `GetFieldValue<T>` (on `TrinoRow` and `TrinoDataReader`) converts when the requested type differs
  from the column's default .NET type: checked numeric widening and narrowing (including unsigned
  types, enums and whole-valued decimals), `TrinoBigDecimal`/`TrinoTimestamp`/`TrinoTimestampWithTimeZone`
  to `decimal`/`DateTime`/`DateTimeOffset`, `date`/`timestamp with time zone` to `DateTime`, and
  `varchar` to `Guid`/`char`. Lossy conversions throw `OverflowException`. `GetDateTime`/`GetGuid`
  apply the same rules.
- `DbDataReader.RecordsAffected` returns the update count for DML statements (it returned -1,
  because Trino also answers DML with a one-column result set). Queries still return -1.
- A connection reuses its HTTP handler across `Open`/`Close` until it is disposed or its
  `ConnectionString` changes, and a `TrinoDataSource` shares one handler among its connections, so
  opening and closing around each command no longer costs a TCP + TLS handshake.
- A `NULL` parameter whose `TrinoType` or `DbType` is known is sent as `CAST(NULL AS <type>)`, so
  overloaded functions resolve. `DbType.Time`/`DateTime`/`DateTime2`/`DateTimeOffset` map to
  `time(6)`/`timestamp(6)`/`timestamp(6) with time zone` (the bare types mean precision 3).
- `float`/`double` parameters are sent as `REAL '…'`/`DOUBLE '…'` (a bare `1.5` is a `decimal` in
  Trino), and `byte`/`ushort`/`uint`/`ulong` parameters are encoded instead of throwing.

### Fixed (core)

- `TrinoDbParameter.Precision`/`Scale` override `DbParameter.Precision`/`Scale` instead of hiding
  them, so values set through a `DbParameter` reference are no longer lost.
- Parameters named with an `@` or `:` prefix (`@id`) bind to their placeholders.

## 1.0.0

First stable release. See [GitHub Releases](https://github.com/jogyamfi/TriQL/releases).
