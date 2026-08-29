# Migration Guide: from `trinoclient.net`

TriQL's ADO.NET provider (`TriQL.Data.ADO`) is a from-scratch reimplementation informed by
`trinoclient.net` (the reference C# client also in this repository), but it **intentionally**
behaves differently in a number of places. This guide lists the behavioral differences callers
migrating from `trinoclient.net` will observe. Undocumented differences read as bugs — these are
not bugs, they are corrections, each tied to the requirement that specifies the corrected
behavior (see [requirements.md § Appendix C](requirements.md#appendix-c--reference-implementation-notes)
for the full analysis).

| `trinoclient.net` behavior | TriQL behavior | Why it changed |
|---|---|---|
| A new `HttpClient` is constructed per statement client. | A single `HttpMessageInvoker`/`HttpClient` is built once per `TrinoClient`/`TrinoConnection` and reused for the connection's lifetime. | FR-3.1.1, FR-3.1.2 — avoids socket exhaustion under load. |
| Trusted **server** certificates are added to `handler.ClientCertificates`. | Server trust (`Tls.TrustedRootCertificates`) and client identity (`Tls.ClientCertificates` / a client-certificate authenticator) are separate, non-conflated options. | FR-3.2.4 |
| `nextUri` is mutated in place when appending `targetResultSize`, so the parameter accumulates across polls. | The `targetResultSize` query parameter is appended freshly on every poll from the current URI. | FR-4.3.3 |
| Empty-page skipping is implemented by recursive self-invocation. | The page-read loop is iterative; no stack growth proportional to the number of empty pages. | FR-4.3.5 |
| Buffer accounting estimates size from the raw response string length. | Buffer accounting estimates size from the materialized row payload, not the wire string. | FR-6.5 |
| Pervasive sync-over-async via a `SafeResult()` helper throughout the ADO.NET surface. | The **only** sync-over-async in TriQL is a single audited bridge (`Internal/SyncBridge.cs`) used exclusively by the synchronous `DbCommand`/`DbDataReader` members that ADO.NET itself requires to be synchronous; nothing else blocks on async work. | FR-9.2.14, NFR-REL-2 |
| `catch (Exception e) { … throw e; }` discards the original stack trace. | Every rethrow uses a bare `throw` (or `ExceptionDispatchInfo`), preserving the original stack trace and inner exception. | FR-12.1.3 |
| A stray `Console.WriteLine` in the statement client. | No `Console.Write*` anywhere in shipping code; use the standard `ILoggerFactory`/`ILogger` integration instead. | FR-11.1.4 |
| `tinyint` is mapped to `byte`. | `tinyint` maps to `sbyte`, since Trino's `tinyint` is **signed**. Code that stored a `tinyint` value in a `byte` will need to widen to `sbyte`/`short`/`int` instead. | FR-7.2 — see the [type-mapping reference](type-mapping-reference.md). |
| `GetBytes`/`GetChars` return the full value length regardless of bytes actually copied, and can index past the source. | `GetBytes`/`GetChars` follow the documented ADO.NET contract exactly: they return the number of bytes/chars actually copied into the caller's buffer and never read past the source value. | FR-9.3.2 |
| `CreateDbParameter()` implicitly adds the new parameter to the command's parameter collection. | `CreateParameter()` returns a detached parameter; you must add it to `Command.Parameters` yourself, matching the documented `DbCommand` contract. | FR-9.2.9 |
| `CommandTimeout` writes through to shared connection session state. | `CommandTimeout` is scoped to the individual `DbCommand`; setting it on one command does not affect others sharing the same `DbConnection`. | FR-9.2.3 |
| `GetSchema` restriction values are interpolated directly into generated SQL text. | Every `GetSchema` restriction value is bound as a real parameter (or passed through the audited literal encoder), never concatenated into SQL text. | FR-9.5.3, SEC-4 |
| No `IAsyncEnumerable` surface; no spooled protocol support. | `TrinoResultSet.ReadRowsAsync`/`ReadPagesAsync` return `IAsyncEnumerable<T>` directly, and the spooled protocol is implemented (opt-in in 1.0 — see the [README](../README.md#spooling-protocol-experimental)). | FR-6.6, FR-5 |

## Package and namespace changes

- `trinoclient.net`'s single `Trino.Client`/`Trino.Data.ADO` assemblies become three packages:
  `TriQL.Client` (SDK), `TriQL.Data.ADO` (ADO.NET provider), and `TriQL.Client.Auth` (Entra
  ID/OAuth2 — only needed if you use those authenticators, so it never adds a dependency for
  callers who don't).
- The ADO.NET provider invariant name is `TriQL.Data.Trino` (`TrinoProviderFactory.InvariantName`).
- Connection-string keys are mostly the same shape but see the
  [connection-string reference](connection-string-reference.md) for the authoritative list —
  some keys (e.g. `Auth=entra-id`/`Auth=oauth2-client-credentials`) now require referencing
  `TriQL.Client.Auth` explicitly rather than working out of the box.

## What did not change

The wire protocol itself, the streaming pipeline shape (statement client → page queue with
background read-ahead → page/row enumerator), byte-budget buffering, adaptive polling backoff, and
server-side `PREPARE`/`EXECUTE` for parameters are all patterns TriQL deliberately kept from
`trinoclient.net` because they worked well — only the defects above were corrected.
