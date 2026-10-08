using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TriQL.EntityFrameworkCore.Diagnostics.Internal;

/// <summary>Logs the Trino provider's diagnostic events.</summary>
internal static class TrinoLoggerExtensions
{
    /// <summary>Logs <see cref="TrinoEventId.UniqueIndexNotEnforced"/>.</summary>
    /// <param name="diagnostics">The logger.</param>
    /// <param name="uniqueness">What is not enforced, e.g. <c>unique index {'Email'}</c>.</param>
    /// <param name="entityTypeName">The display name of the entity type.</param>
    public static void UniqueIndexNotEnforced(
        this IDiagnosticsLogger<DbLoggerCategory.Model.Validation> diagnostics,
        string uniqueness,
        string entityTypeName)
    {
        var definition = ((TrinoLoggingDefinitions)diagnostics.Definitions).UniqueIndexNotEnforced(diagnostics.Options);

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, uniqueness, entityTypeName);
        }

        if (diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled))
        {
            var eventData = new EventData(definition, (d, _) => ((EventDefinition<string, string>)d).GenerateMessage(uniqueness, entityTypeName));
            diagnostics.DispatchEventData(definition, eventData, diagnosticSourceEnabled, simpleLogEnabled);
        }
    }
}
