# TriQL — Benchmark Results

| Field | Value |
|---|---|
| Document | Benchmark Results |
| Product | TriQL — .NET Trino Client & ADO.NET Provider |
| Harness | [BenchmarkDotNet](https://benchmarkdotnet.org/) v0.14.0 |
| Project | [tests/TriQL.Benchmarks](../tests/TriQL.Benchmarks) |
| Last run | 2026-10-08 (EF Core provider); 2026-08-27 (row decoding) |
| Status | Phase 3 results and EF Core provider (EF7-T6) results recorded; Phase 5 benchmarks (NFR-PERF-1/2/4) not yet written |

---

## Table of Contents

1. [Scope](#1-scope)
2. [Running the Benchmarks](#2-running-the-benchmarks)
3. [Environment](#3-environment)
4. [Row Decoding — NFR-PERF-3](#4-row-decoding--nfr-perf-3)
5. [Interpretation](#5-interpretation)
6. [Regression Guard](#6-regression-guard)
7. [Caveats](#7-caveats)
8. [Outstanding Benchmarks](#8-outstanding-benchmarks)
9. [EF Core Provider — EF7-T6](#9-ef-core-provider--ef7-t6)

---

## 1. Scope

This document records measured benchmark output, not estimates. Every figure below is copied
verbatim from a BenchmarkDotNet run.

Currently covered:

| Requirement | Benchmark | Status |
|---|---|---|
| **NFR-PERF-3** — allocation per row bounded by materialized values plus a fixed constant | `RowDecodingBenchmarks` | **Met** — see [§4](#4-row-decoding--nfr-perf-3) |
| NFR-PERF-1 — time-to-first-row | — | Not written (Phase 5, P5-T10) |
| NFR-PERF-2 — throughput vs JDBC | — | Not written (Phase 5, P5-T10) |
| NFR-PERF-4 — peak working set | — | Not written (Phase 5, P5-T10) |
| EF7-T6 — EF Core `SaveChanges`, connection reuse, materialization | `EfSaveChangesBenchmarks`, `EfConnectionBenchmarks`, `EfMaterializationBenchmarks` | Recorded — see [§9](#9-ef-core-provider--ef7-t6) |

The row-decoding benchmark exists to satisfy **P3-T6**, which requires the `Utf8JsonReader` decoder
to be *measured against the naive path before committing to it* rather than adopted on faith.

---

## 2. Running the Benchmarks

```bash
# All benchmarks
dotnet run -c Release --project tests/TriQL.Benchmarks --framework net10.0

# Just the row decoder, with the job used for the results below
dotnet run -c Release --project tests/TriQL.Benchmarks --framework net10.0 -- \
    --filter "*RowDecoding*" --job medium
```

Benchmarks **must** be run in `Release`. `--job medium` (2 launches × 10 warmup × 15 iterations) was
used for the recorded results; the default job is slower but tighter, and `--job short` is useful for
a quick signal but produces confidence intervals too wide to draw conclusions from.

`RowDecodingBenchmarks` reaches into `internal` types, which is why `TriQL.Client` grants
`InternalsVisibleTo("TriQL.Benchmarks")` in [AssemblyInfo.cs](../src/TriQL.Client/AssemblyInfo.cs).

---

## 3. Environment

```text
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9106)
Unknown processor
.NET SDK 10.0.303
  [Host]    : .NET 10.0.11 (10.0.1126.37416), X64 RyuJIT AVX2
  MediumRun : .NET 10.0.11 (10.0.1126.37416), X64 RyuJIT AVX2

Job=MediumRun  IterationCount=15  LaunchCount=2  WarmupCount=10
```

> This is a developer workstation, not a fixed CI runner class. NFR-PERF-5 requires the eventual
> perf gate to run on a fixed runner; these numbers establish a *relative* baseline between two
> implementations measured back to back on one machine, which is the comparison P3-T6 asked for.

---

## 4. Row Decoding — NFR-PERF-3

### 4.1 What is compared

Both benchmarks decode the same UTF-8 `data` payload into a fully materialized `object?[][]`, so the
comparison is like-for-like. The row shape is eight columns chosen to exercise every decoding
strategy:

| Column | Trino type | Materialized CLR type |
|---|---|---|
| `c0` | `bigint` | `long` |
| `c1` | `varchar` (18 chars) | `string` |
| `c2` | `double` | `double` |
| `c3` | `boolean` | `bool` |
| `c4` | `decimal(10,2)` | `decimal` |
| `c5` | `date` | `DateOnly` |
| `c6` | `timestamp(3)` | `DateTime` |
| `c7` | `uuid` | `Guid` |

- **`Naive`** — the Phase 2 path: parse a `JsonDocument` tree, produce a boxed raw value per column
  via `RawJsonValueConverter` (allocating an intermediate `string` for every text-shaped type), then
  make a second pass through `TrinoValueConverter` to reach the final type.
- **`Utf8Decoder`** — the Phase 3 path: a single `Utf8JsonReader` pass over the response bytes
  straight to final CLR values ([Utf8RowDecoder.cs](../src/TriQL.Client/Internal/Utf8RowDecoder.cs)).

### 4.2 Results

```text
| Method      | RowCount | Mean       | Error       | StdDev      | Median     | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated  | Alloc Ratio |
|------------ |--------- |-----------:|------------:|------------:|-----------:|------:|--------:|---------:|---------:|---------:|-----------:|------------:|
| Naive       | 1000     |   680.3 us |     5.19 us |     7.60 us |   679.0 us |  1.00 |    0.02 |  59.5703 |  35.1563 |        - |  734.47 KB |        1.00 |
| Utf8Decoder | 1000     |   548.8 us |     4.09 us |     5.99 us |   549.5 us |  0.81 |    0.01 |  28.3203 |  15.6250 |        - |  352.26 KB |        0.48 |
|             |          |            |             |             |            |       |         |          |          |          |            |             |
| Naive       | 10000    | 7,961.0 us |    82.89 us |   118.88 us | 7,967.1 us |  1.00 |    0.02 | 593.7500 | 406.2500 |        - | 7343.86 KB |        1.00 |
| Utf8Decoder | 10000    | 7,740.7 us | 1,055.08 us | 1,579.20 us | 6,905.5 us |  0.97 |    0.20 | 289.0625 | 281.2500 | 132.8125 | 3615.88 KB |        0.49 |
```

### 4.3 Headline figures

| Metric | Naive | `Utf8Decoder` | Change |
|---|---|---|---|
| Allocated @ 1 000 rows | 734.47 KB | **352.26 KB** | **−52 %** |
| Allocated @ 10 000 rows | 7 343.86 KB | **3 615.88 KB** | **−51 %** |
| Allocation per row | ~752 B | **~361 B** | **−52 %** |
| Mean @ 1 000 rows | 680.3 µs | **548.8 µs** | **−19 %** |
| Mean @ 10 000 rows | 7 961.0 µs | 7 740.7 µs | −3 % (within noise — see [§7](#7-caveats)) |

---

## 5. Interpretation

### 5.1 The allocation budget

NFR-PERF-3 states that allocation per row *"MUST NOT exceed the size of the materialized values plus
a fixed constant"*. Deriving the floor for the eight-column row on x64:

| Allocation | Bytes |
|---|---|
| `object?[]` of 8 references (24 B header + 8 × 8 B) | 88 |
| `long` box | 24 |
| `string` of 18 chars | 64 |
| `double` box | 24 |
| `bool` box | 24 |
| `decimal` box | 32 |
| `DateOnly` box | 24 |
| `DateTime` box | 24 |
| `Guid` box | 32 |
| **Materialized-value floor** | **336** |

Measured `Utf8Decoder` allocation is **~361 B/row**, i.e. the floor plus roughly **25 B/row** of
fixed overhead (the outer rows array and amortized `List<T>` growth).

> **NFR-PERF-3 is met.**

### 5.2 Why the naive path failed the requirement

The naive path allocated ~752 B/row — **2.2×** the materialized floor. Critically, the excess was not
a fixed constant: it scaled **per value**, because every text-shaped column (`decimal`, `date`,
`time`, `timestamp`, `uuid`, `varbinary`, `ipaddress`) allocated an intermediate `string` that was
parsed and immediately discarded, on top of a duplicate box for the raw value. A wider row paid more
per row, which is precisely the shape NFR-PERF-3 forbids.

The decoder removes both costs by parsing directly from the token's raw UTF-8 — using the reader's
native `GetBytesFromBase64`/`GetGuid`/`GetInt64` where available, and transcoding Trino's
ASCII-only temporal and decimal formats through a `stackalloc` buffer otherwise.

### 5.3 Throughput

Time was a secondary concern — the task was an allocation mitigation — but the decoder is not slower:
19 % faster at 1 000 rows and neutral at 10 000. Halving allocation also removes GC pressure that
does not show up in these micro-benchmark means but does affect sustained streaming, which
NFR-PERF-2 will measure in Phase 5.

---

## 6. Regression Guard

Benchmarks are not run per PR yet (that is P5-T12). The allocation result is instead locked in by a
unit test in
[Utf8RowDecoderTests.cs](../tests/TriQL.Client.Tests/Types/Utf8RowDecoderTests.cs):

`DecodeRows_AllocatesNoMoreThanTheMaterializedValuesPlusASmallConstant` decodes 2 000 rows of the
same shape and asserts allocation stays under **500 B/row** using
`GC.GetAllocatedBytesForCurrentThread()`.

That API returns a precise count rather than a sample, so the test is deterministic and satisfies
TEST-13. The 500 B ceiling sits comfortably above the measured 361 B while still failing loudly if
the intermediate-string path is ever reintroduced at ~752 B.

---

## 7. Caveats

| Caveat | Detail |
|---|---|
| **The 10 000-row timing is noisy** | `Utf8Decoder` shows StdDev 1 579 µs and `RatioSD` 0.20, with mean 7 740 µs against median 6 905 µs. Retaining 10 000 decoded rows triggers Gen2 collections (132.8 per 1 000 ops) whose timing lands unevenly across iterations. Treat the 10 000-row **allocation** figure as solid and the **timing** figure as "not slower", nothing stronger. |
| **Single machine, not a CI runner** | See [§3](#3-environment). Absolute numbers will differ elsewhere; the ratios are the meaningful output. |
| **Processor not identified** | BenchmarkDotNet reported `Unknown processor` on this host, so the results cannot be attributed to a specific CPU model. |
| **One row shape** | Eight columns covering the main decoding strategies. Rows dominated by wide `varchar` would narrow the gap (strings must be allocated either way); rows dominated by temporals would widen it. |
| **Complex types not measured** | `array`/`map`/`row` are deliberately left raw and materialized on first access (FR-7.2.7), so they are not exercised here. |

---

## 8. Outstanding Benchmarks

Phase 5 (P5-T10) must add:

- **NFR-PERF-1** — time-to-first-row, 95th percentile, versus coordinator latency.
- **NFR-PERF-2** — sustained throughput on a 10-million-row `tpch.sf1` scan, within 15 % of the
  Trino JDBC driver on the same host and network.
- **NFR-PERF-4** — peak working set attributable to buffering, within `ReadAheadBufferBytes × 1.5`.

P5-T12 then wires a CI perf gate failing beyond 10 % regression, per gate **G5**. Phase 7 (P7-T11)
re-baselines NFR-PERF-2 over the spooled path.

---

## 9. EF Core Provider — EF7-T6

These benchmarks run against a live coordinator, so every figure includes the HTTP round trips.
That is intentional: they show what the provider saves or adds per statement and per row.

### 9.1 Running

```bash
# A local coordinator; the benchmarks use only the built-in memory and tpch catalogs.
docker run -d -p 8080:8080 trinodb/trino:466
TRIQL_BENCH_SERVER=http://localhost:8080/ \
    dotnet run -c Release --project tests/TriQL.Benchmarks --framework net10.0 -- --filter "*Ef*"
```

Environment for the results below: Trino 466 in Docker Desktop (WSL 2) on the same machine, plain
HTTP, BenchmarkDotNet v0.15.8, .NET 10.0.12, Intel Core i5-1340P, Windows 11. Iteration counts are small
(see each job line) because single operations take up to minutes.

### 9.2 `SaveChanges` — multi-row inserts (EF7-T3)

`Rows` new entities (3 columns) saved in one `SaveChanges` to a `memory` catalog table, with consecutive
inserts combined into multi-row `INSERT`s (the default) or one statement per row (`MaxBatchSize(1)`).

```text
Job: RunStrategy=Monitoring, LaunchCount=1, WarmupCount=1, IterationCount=5
| Method      | Rows | CombineInserts | Mean         | Error      | StdDev      |
|------------ |----- |--------------- |-------------:|-----------:|------------:|
| SaveChanges | 1    | False          |     273.1 ms |   183.5 ms |    47.65 ms |
| SaveChanges | 1    | True           |     289.8 ms |   203.6 ms |    52.87 ms |
| SaveChanges | 100  | False          |  28,271.2 ms | 3,742.8 ms |   971.98 ms |
| SaveChanges | 100  | True           |     289.4 ms |   274.6 ms |    71.32 ms |
| SaveChanges | 1000 | False          | 283,421.1 ms | 4,702.9 ms | 1,221.32 ms |
| SaveChanges | 1000 | True           |     687.2 ms |   208.5 ms |    54.16 ms |
```

- Each statement costs ~280 ms here, almost all of it Trino's submit-and-poll cycle, not the network.
  One statement per row therefore scales linearly: **283 s** for 1,000 rows.
- Combined, 100 rows cost the same as 1 (one statement), and 1,000 rows (two statements of 666 and
  334 rows under the 2,000-parameter cap, as each row binds 3 parameters) take **0.69 s**:
  **~410× faster**.
- On Iceberg each statement is also a commit and a snapshot, so combining saves more there.

### 9.3 Connection reuse (EF7-T4)

A one-row query (`count(*)` of `tpch.tiny.region`) through a new context each time, a pooled context
(`PooledDbContextFactory`), and one long-lived context.

```text
Job: RunStrategy=Throughput, LaunchCount=1, WarmupCount=3, IterationCount=10
| Method             | Mean     | Error    | StdDev   | Ratio | RatioSD |
|------------------- |---------:|---------:|---------:|------:|--------:|
| NewContextPerQuery | 303.7 ms | 35.72 ms | 23.63 ms |  1.01 |    0.11 |
| PooledContext      | 302.8 ms | 29.90 ms | 19.78 ms |  1.00 |    0.10 |
| LongLivedContext   | 302.8 ms | 10.06 ms |  5.99 ms |  1.00 |    0.08 |
```

- The three are equal within noise. A new context opens a new HTTP connection (verified: 20 unpooled
  queries opened 20 TCP connections, 20 pooled ones opened 1, in `PerformanceBehaviourTests`), but over
  plain HTTP to a local coordinator the TCP handshake is a fraction of a millisecond against ~300 ms of
  statement latency.
- Reuse matters with TLS and a remote coordinator, where each new connection adds a TLS handshake (one
  or more network round trips). Pooling (`AddDbContextPool`) or a shared `TrinoDataSource` avoids it; this
  run does not measure that case.

### 9.4 Materialization

`tpch.tiny.orders` (15,000 rows, 5 columns) read through `TrinoDataReader` into objects by hand, and
through EF with and without change tracking.

```text
Job: RunStrategy=Throughput, LaunchCount=1, WarmupCount=2, IterationCount=8
| Method        | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated | Alloc Ratio |
|-------------- |---------:|----------:|---------:|------:|--------:|----------:|----------:|----------:|----------:|------------:|
| RawDataReader | 383.2 ms |  70.45 ms | 36.85 ms |  1.01 |    0.14 | 1000.0000 |         - |         - |  11.52 MB |        1.00 |
| EfNoTracking  | 380.8 ms | 113.52 ms | 59.37 ms |  1.00 |    0.18 | 2000.0000 | 1000.0000 |         - |  15.33 MB |        1.33 |
| EfTracking    | 413.1 ms | 102.58 ms | 53.65 ms |  1.09 |    0.18 | 4000.0000 | 2000.0000 | 1000.0000 |  22.65 MB |        1.97 |
```

- With `AsNoTracking`, EF costs no measurable time over the raw reader (the server and the transfer
  dominate) and allocates 33% more.
- Tracking adds ~9% time and doubles allocation (snapshots and the identity map), as in other EF
  providers.

### 9.5 Caveats

- Single machine, a coordinator in Docker on the same host, few iterations: the confidence intervals
  are wide (see `Error`). The ratios between methods measured back to back are the useful output.
- The ~280 ms per statement is this local single-node coordinator's figure; a production cluster's
  latency differs, and the benefit of combining inserts grows with it.

---

*End of document.*
