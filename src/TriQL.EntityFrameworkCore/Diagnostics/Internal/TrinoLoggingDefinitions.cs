using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TriQL.EntityFrameworkCore.Diagnostics.Internal;

/// <summary>Logging definitions for the Trino provider's diagnostic events.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoLoggingDefinitions : RelationalLoggingDefinitions
{
    private EventDefinition<string, string>? _uniqueIndexNotEnforced;
    private EventDefinition? _nonAtomicSaveChanges;
    private EventDefinition<string, string, string>? _columnSkipped;

    /// <summary>The definition of <see cref="TrinoEventId.UniqueIndexNotEnforced"/>, created on first use.</summary>
    internal EventDefinition<string, string> UniqueIndexNotEnforced(ILoggingOptions options) =>
        LazyInitializer.EnsureInitialized(
            ref _uniqueIndexNotEnforced,
            () => new EventDefinition<string, string>(
                options,
                TrinoEventId.UniqueIndexNotEnforced,
                LogLevel.Warning,
                "TrinoEventId.UniqueIndexNotEnforced",
                level => LoggerMessage.Define<string, string>(
                    level,
                    TrinoEventId.UniqueIndexNotEnforced,
                    "The {Uniqueness} on entity type '{EntityType}' is not enforced by Trino: Trino and its connectors "
                    + "(Iceberg included) do not check uniqueness, so duplicate values can be written. Check for "
                    + "duplicates in the application before saving if they matter.")));

    /// <summary>The definition of <see cref="TrinoEventId.NonAtomicSaveChanges"/>, created on first use.</summary>
    internal EventDefinition NonAtomicSaveChanges(ILoggingOptions options) =>
        LazyInitializer.EnsureInitialized(
            ref _nonAtomicSaveChanges,
            () => new EventDefinition(
                options,
                TrinoEventId.NonAtomicSaveChanges,
                LogLevel.Warning,
                "TrinoEventId.NonAtomicSaveChanges",
                level => LoggerMessage.Define(
                    level,
                    TrinoEventId.NonAtomicSaveChanges,
                    "SaveChanges is executing more than one statement. Trino commits each statement on its own, so if "
                    + "a later statement fails, earlier ones stay committed; their entities are marked as saved.")));

    /// <summary>The definition of <see cref="TrinoEventId.ColumnSkipped"/>, created on first use.</summary>
    internal EventDefinition<string, string, string> ColumnSkipped(ILoggingOptions options) =>
        LazyInitializer.EnsureInitialized(
            ref _columnSkipped,
            () => new EventDefinition<string, string, string>(
                options,
                TrinoEventId.ColumnSkipped,
                LogLevel.Warning,
                "TrinoEventId.ColumnSkipped",
                level => LoggerMessage.Define<string, string, string>(
                    level,
                    TrinoEventId.ColumnSkipped,
                    "The column '{Column}' of '{Table}' was skipped: its Trino type '{StoreType}' has no .NET mapping. "
                    + "Read it with a query that converts it (for example CAST(... AS json) and json_format) if you need it.")));
}
