# Type-Mapping Reference

Every Trino type maps to a default CLR type, returned by `TrinoRow.GetValue`/`GetFieldType` (SDK)
and `DbDataReader.GetFieldType`/`GetValue` (ADO.NET). Typed accessors (`GetInt64`, `GetString`, …)
additionally permit numeric widening (e.g. `GetInt64` succeeds on an `integer` column).

| Trino type | Default CLR type | Notes |
|---|---|---|
| `boolean` | `bool` | |
| `tinyint` | `sbyte` | Trino `tinyint` is **signed** — unlike the reference C# client, which maps it to `byte`. |
| `smallint` | `short` | |
| `integer` | `int` | |
| `bigint` | `long` | |
| `real` | `float` | |
| `double` | `double` | |
| `decimal(p,s)` where `p ≤ 28` | `decimal` | |
| `decimal(p,s)` where `p > 28` | `TrinoBigDecimal` | `decimal` cannot represent `p` up to 38 without loss. |
| `varchar`, `varchar(n)`, `char(n)` | `string` | `char(n)` is space-padded by the server; padding is preserved. |
| `varbinary` | `byte[]` | Wire form is base64. |
| `json` | `string` | Also retrievable as `GetFieldValue<JsonDocument>()`. |
| `date` | `DateOnly` | |
| `time(p)` where `p ≤ 7` | `TimeOnly` | |
| `time(p)` where `p > 7` | `TrinoTime` | Sub-100 ns precision exceeds `TimeOnly`. |
| `time(p) with time zone` | `TrinoTimeWithTimeZone` | No CLR equivalent for a zoned time-of-day. |
| `timestamp(p)` where `p ≤ 7` | `DateTime` | `Kind = Unspecified`. |
| `timestamp(p)` where `p > 7` | `TrinoTimestamp` | Trino supports up to `p = 12` (picoseconds). |
| `timestamp(p) with time zone`, `p ≤ 7` | `DateTimeOffset` | |
| `timestamp(p) with time zone`, `p > 7` | `TrinoTimestampWithTimeZone` | |
| `interval year to month` | `TrinoIntervalYearToMonth` | Not representable as `TimeSpan`. |
| `interval day to second` | `TimeSpan` | |
| `uuid` | `Guid` | |
| `ipaddress` | `IPAddress` | |
| `array(T)` | `T[]` for value-typed `T`, else `object?[]` | Element type resolved recursively. |
| `map(K,V)` | `IReadOnlyDictionary<K,V>` for value-typed `K`/`V`, else `IReadOnlyDictionary<object,object?>` | |
| `row(...)` | `ITrinoRowValue` | Named field access plus positional access. |
| unknown / `null` column type | `object` | Value is always `null`. |

## Custom precision types

`TrinoBigDecimal`, `TrinoTime`, `TrinoTimeWithTimeZone`, `TrinoTimestamp`,
`TrinoTimestampWithTimeZone`, and `TrinoIntervalYearToMonth` all implement `IEquatable<T>`,
`IComparable<T>`, `IFormattable`, `ISpanFormattable`, and `IParsable<T>`, and provide explicit
narrowing conversions to the nearest BCL type that throw `OverflowException` on loss (rather than
silently truncating).

## Nulls

SQL `NULL` maps to CLR `null` for reference types, and to `DBNull.Value` at the ADO.NET boundary
(`DbDataReader.IsDBNull`). Calling a non-nullable typed accessor (e.g. `GetInt64`) on a `NULL`
value throws `InvalidCastException`.

## Laziness

`array`/`map`/`row` values are materialized lazily and cached per row: reading only scalar columns
from a row that also contains a large nested column does not pay the cost of converting it.
