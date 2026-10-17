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

    /// <summary>Logs <see cref="TrinoEventId.ColumnSkipped"/>.</summary>
    /// <param name="diagnostics">The logger.</param>
    /// <param name="table">The table's display name.</param>
    /// <param name="column">The column's name.</param>
    /// <param name="storeType">The column's Trino type.</param>
    public static void ColumnSkipped(
        this IDiagnosticsLogger<DbLoggerCategory.Scaffolding> diagnostics,
        string table,
        string column,
        string storeType)
    {
        var definition = ((TrinoLoggingDefinitions)diagnostics.Definitions).ColumnSkipped(diagnostics.Options);

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, column, table, storeType);
        }

        if (diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled))
        {
            var eventData = new EventData(definition, (d, _) => ((EventDefinition<string, string, string>)d).GenerateMessage(column, table, storeType));
            diagnostics.DispatchEventData(definition, eventData, diagnosticSourceEnabled, simpleLogEnabled);
        }
    }

    /// <summary>Logs <see cref="TrinoEventId.NonAtomicSaveChanges"/>.</summary>
    /// <param name="diagnostics">The logger.</param>
    public static void NonAtomicSaveChanges(this IDiagnosticsLogger<DbLoggerCategory.Update> diagnostics)
    {
        var definition = ((TrinoLoggingDefinitions)diagnostics.Definitions).NonAtomicSaveChanges(diagnostics.Options);

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics);
        }

        if (diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled))
        {
            var eventData = new EventData(definition, (d, _) => ((EventDefinition)d).GenerateMessage());
            diagnostics.DispatchEventData(definition, eventData, diagnosticSourceEnabled, simpleLogEnabled);
        }
    }
}
