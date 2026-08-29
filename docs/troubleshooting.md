# Troubleshooting Guide

## Exception hierarchy

Every exception TriQL raises derives from `TrinoException`, which exposes `QueryId` (nullable)
and `IsRetryable`. Catch the most specific type you need, or `TrinoException` to handle all of
them uniformly.

| Exception | Typical cause | What to check |
|---|---|---|
| `TrinoConfigurationException` | Invalid `TrinoSessionOptions`/connection string. | `Server` must be an absolute `http`/`https` URI; a credential-transmitting authenticator over `http` requires `Tls.AllowPlaintextCredentials = true`. |
| `TrinoConnectionException` | Cannot reach the coordinator, or a request exceeded `RequestTimeout`. | Network/firewall/DNS to `Server`; consider raising `RequestTimeout` for a slow network. |
| `TrinoAuthenticationException` | 401/403, or credential acquisition failed. | Verify the configured authenticator's credentials; for `EntraIdAuthenticator`/`OAuth2ClientCredentialsAuthenticator`, check the token endpoint/scopes are reachable and correct. |
| `TrinoProtocolException` | Malformed/unsupported/inconsistent server response. | Confirm the server is Trino 466+ (`NFR-COMPAT-2`); if using the spooled protocol, confirm the cluster is actually spooling-configured (see below). |
| `TrinoQueryException` | The server reported a query failure. | Inspect `ErrorCode`/`ErrorName`/`ErrorType`/`FailureInfo` — these mirror the server's own error reporting. |
| `TrinoParameterException` | Parameter binding/count/type mismatch. | Check placeholder count/names match the supplied `TrinoParameterCollection`. |
| `TrinoTimeoutException` | The client-side `QueryTimeout` deadline was exceeded. | Raise `QueryTimeout`, or investigate why the query is slow server-side (`ConfiguredTimeout`/`Elapsed` are on the exception). |
| `TrinoTypeConversionException` | A value could not be converted to the requested CLR type. | Check the [type-mapping reference](type-mapping-reference.md); widen the requested type or use `GetValue`/`GetFieldValue<T>` with a compatible type. |

## Connection issues

- **`TrinoConfigurationException: Server must be set to an absolute URI.`** — `Server` is
  required; the connection-string `Host`/`Port`/`EnableSsl` keys build it for you, or set
  `TrinoSessionOptions.Server` directly.
- **TLS certificate validation failures** — TLS validation is on by default (SEC-2). Do not
  disable it broadly; use the narrowly-scoped `TrinoTlsOptions` flags (`AllowSelfSignedCertificate`,
  `AllowHostNameMismatch`, `TrustedRootCertificatePath`) for the specific failure mode you have,
  each of which logs a `Warning` when enabled.
- **Everything hangs** — every network operation is cancellable and bounded by `RequestTimeout`/
  `QueryTimeout`; if a call still appears to hang, check whether a custom `IProgress<T>`/`Progress`
  event handler is throwing repeatedly or blocking (callbacks must not block; exceptions from them
  are caught and logged at `Warning`, never propagated to the query).

## Authentication issues

- **`Auth=entra-id` or `Auth=oauth2-client-credentials` throws naming a missing package** — these
  providers live in `TriQL.Client.Auth`; add a package reference to it (it self-registers on load).
  If you already reference it and still see this in a **trimmed or Native-AOT** app, confirm the
  trimmer is honouring the package's `ILLink.Descriptors.xml`: selecting a provider purely by name
  in a connection string creates no static reference to the assembly, so the descriptor is what
  keeps its self-registration alive. Referencing any type from the package (for example
  `_ = typeof(EntraIdAuthenticator);` at startup) is a reliable workaround.
- **401 loops** — an authenticator gets exactly one refresh attempt per request; a second 401
  after that refresh surfaces as `TrinoAuthenticationException` rather than retrying indefinitely.
  Confirm the credential is actually valid, not just refreshable.

## Spooling protocol

Spooling is opt-in in 1.0 (`TrinoSessionOptions.QueryDataEncodings` defaults to empty). If you
enable it (`["json+zstd","json+lz4","json"]`) and still see the direct protocol, the cluster
itself is not spooling-configured — Trino falls back per query, not per client, when spooling
isn't available for a given result (FR-5.1.3). This is expected and does not indicate a client bug.

## Observability

TriQL emits an OpenTelemetry-conventional `Meter` (`TriQL.Client`, ten instruments — queries
submitted/failed/cancelled, query duration, time-to-first-row, rows/bytes/pages, buffer occupancy,
HTTP retries) and `ActivitySource` (`TriQL.Client`, a `trino.query` span per query and a
`trino.request` span per HTTP request, both tagged per OpenTelemetry semantic conventions).
Nothing is emitted, and no `traceparent` header is added, unless a listener is actually attached —
attach an OpenTelemetry `MeterProvider`/`TracerProvider` (or a raw `MeterListener`/
`ActivityListener`) subscribed to the `TriQL.Client` name to see them. Set
`TrinoSessionOptions.RedactStatementInTraces = true` if the `db.statement` trace tag (the
submitted SQL text) must not leave the process.

## Still stuck?

Enable `Trace`-level logging via the `ILoggerFactory` passed to `TrinoClient`/`TrinoConnection` —
it logs redacted request/response headers and per-row diagnostics (FR-11.1.3), which is usually
enough to tell a client-side problem from a server-side one.
