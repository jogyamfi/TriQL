---
name: "C# and .NET Coding Standards"
description: "Use when writing, reviewing, or refactoring C# code. Covers async/await and ConfigureAwait, cancellation, nullable reference types, exception design, IDisposable/IAsyncDisposable, Span and ArrayPool allocation, LINQ, culture-sensitive formatting, logging, and secure coding for .NET 8 and .NET 10."
applyTo: "**/*.cs"
---

# C# and .NET Coding Standards

Target frameworks are `net8.0` and `net10.0`. Use modern BCL APIs freely; do not write for
.NET Framework or `netstandard2.0` compatibility.

Builds run with `TreatWarningsAsErrors`, nullable enabled, and trim/AOT analyzers on. Code that
produces a warning does not compile.

## Async and concurrency

- **Async all the way.** Never block on async code: no `.Result`, `.Wait()`,
  `.GetAwaiter().GetResult()`. If a synchronous entry point is unavoidable (an interface contract),
  route it through one audited, documented bridge — never ad hoc at the call site.
- **Every `await` in library code uses `ConfigureAwait(false)`.**
- **Accept a `CancellationToken` on every async method** and pass it to every call that takes one.
  Never accept a token and ignore it.
- `async void` is only for event handlers. Everything else returns `Task` or `ValueTask`.
- Return `ValueTask` only on hot paths that usually complete synchronously. Never await a
  `ValueTask` twice, and never store one.
- Don't wrap CPU-bound work in `Task.Run` inside a library — that's the caller's decision.
- Prefer `IAsyncEnumerable<T>` for streaming, with `[EnumeratorCancellation]` on the token
  parameter.
- Use `SemaphoreSlim`, `Channel<T>`, or `Interlocked` for coordination. Never `lock` across an
  `await`.

```csharp
// ❌ deadlocks under a synchronization context
var rows = FetchAsync(ct).Result;

// ✅
var rows = await FetchAsync(ct).ConfigureAwait(false);
```

## Nullability and the public surface

- Public APIs are fully nullable-annotated. Don't suppress with `!` unless you can justify it in a
  one-line comment.
- Validate arguments at public boundaries only — not on every internal hop:
  `ArgumentNullException.ThrowIfNull(x)`, `ArgumentOutOfRangeException.ThrowIfNegative(x)`,
  `ObjectDisposedException.ThrowIf(_disposed, this)`.
- Public types are `sealed` unless designed and documented for inheritance.
- Prefer `internal` by default; make things `public` deliberately.
- Every public member carries XML documentation.
- Expose the narrowest useful type: `IReadOnlyList<T>` over `List<T>`, `IEnumerable<T>` over arrays.

## Exceptions

- Never `throw ex;` inside a `catch` — it resets the stack trace. Use bare `throw;` or
  `ExceptionDispatchInfo.Capture(ex).Throw()`.
- Always preserve the inner exception when wrapping.
- Never catch `Exception` broadly except at a genuine boundary where you log and rethrow.
- Never swallow an exception silently. An empty `catch` block is a defect.
- Don't use exceptions for control flow; prefer `TryParse`-style APIs on hot paths.
- Custom exceptions derive from a project base type, are `public sealed`, and expose structured
  data as properties rather than encoding it into the message string.
- `OperationCanceledException` from a cancelled token is expected flow — let it propagate, don't
  log it as an error.

```csharp
// ❌ stack trace destroyed
catch (Exception ex) { throw ex; }

// ✅
catch (HttpRequestException ex) { throw new TrinoConnectionException("...", ex); }
```

## Disposal and lifetime

- Implement `IAsyncDisposable` on anything owning async resources; implement both when a sync path
  is also required.
- `Dispose` must be idempotent and must never throw.
- `Dispose` must not block on network I/O. If cleanup needs I/O, bound it with a short timeout.
- Dispose what you own; do not dispose what was handed to you. Make ownership explicit in the
  constructor.
- Never create `HttpClient` per operation. Share one instance or use `IHttpClientFactory`, and set
  `PooledConnectionLifetime` for DNS rotation.
- Use `using` declarations over `using` blocks where scope allows.

## Allocation and performance

- On hot paths, prefer `Span<T>` / `ReadOnlySpan<T>` / `Memory<T>` over intermediate arrays and
  substrings.
- Rent large buffers from `ArrayPool<T>.Shared` and **return them on every path**, including
  exceptional ones (`try/finally`).
- Parse JSON with `Utf8JsonReader` and source-generated `JsonSerializerContext` — required for
  trim/AOT safety. Never use reflection-based serialization.
- Avoid `string.Format` and interpolation in hot loops; prefer `StringBuilder` or
  `ISpanFormattable`.
- Don't allocate closures in hot paths — pass state explicitly to static lambdas.
- Prefer `CollectionsMarshal`, `TryGetValue`, and capacity-preallocated collections over repeated
  lookups and growth.
- Measure before optimizing. Add a BenchmarkDotNet case rather than asserting a gain.

## Collections and LINQ

- LINQ is fine for clarity off the hot path; avoid it inside per-row loops.
- Never enumerate an `IEnumerable<T>` twice — materialize once.
- Prefer `Count`/`Length` properties over `Count()`.
- Use `FrozenDictionary`/`FrozenSet` for lookup tables built once and read many times.

## Culture, formatting, and time

- All parsing and formatting uses `CultureInfo.InvariantCulture`. CA1305 is an error.
- Use `StringComparison.Ordinal` or `OrdinalIgnoreCase` for identifiers and protocol strings;
  never culture-sensitive comparison for non-user-facing text.
- Prefer `DateTimeOffset` over `DateTime` for absolute time. Use `DateOnly`/`TimeOnly` for
  date-or-time-only values.
- Never use `DateTime.Now` — use `DateTimeOffset.UtcNow`, or better, an injected `TimeProvider`
  so tests are deterministic.

## Logging and diagnostics

- Use `ILogger` with `LoggerMessage` source generation. No string interpolation in log calls.
- Guard with `IsEnabled` where building arguments is non-trivial.
- **`Console.Write*` must not appear in shipping code.**
- Include correlating identifiers in a log scope rather than repeating them per message.
- Instrument with `Meter` and `ActivitySource`; both must be zero-cost when nothing is listening.

```csharp
// ❌ allocates even when Debug is disabled
_logger.LogDebug($"Fetched page {pageId} with {rows.Count} rows");

// ✅ source-generated, no allocation when disabled
[LoggerMessage(Level = LogLevel.Debug, Message = "Fetched page {PageId} with {RowCount} rows")]
partial void LogPageFetched(string pageId, int rowCount);
```

## Security

- **Never log credentials** — passwords, tokens, secrets, keys — at any level, including in
  exception messages and `ToString()` overrides.
- Build SQL with bound parameters. Never concatenate caller input into a statement.
- Validate and bound anything derived from a remote response: sizes, counts, depths, and URIs.
- Set `MaxDepth` and a size ceiling on JSON deserialization. Never enable polymorphic type
  resolution from the wire.
- Never write a blanket `return true` in a certificate validation callback. Permit specific
  `SslPolicyErrors` values only.
- Bound every network call with a timeout and a cancellation token.

## Style

- File-scoped namespaces, `var` when the type is obvious from the right-hand side.
- Pattern matching, switch expressions, `is null` / `is not null` over `== null`.
- Target-typed `new()` where the type is already stated.
- Prefer `record` for immutable data carriers; `readonly struct` for small values.
- Required members (`required`) over constructor-explosion for configuration objects.
- Comments explain *why*, never *what*. Do not restate the next line.
