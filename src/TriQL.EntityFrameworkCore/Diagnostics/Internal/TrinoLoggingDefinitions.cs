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
}
