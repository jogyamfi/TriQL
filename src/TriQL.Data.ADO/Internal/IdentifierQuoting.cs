namespace TriQL.Data.ADO.Internal;

/// <summary>
/// Validates and double-quotes SQL identifiers built from caller-supplied strings (catalog names,
/// schema names) so they can be safely interpolated into generated <c>information_schema</c>
/// <c>FROM</c> clauses. Restriction *values* are never interpolated this way — those are always
/// bound parameters (FR-9.5.3, FR-9.5.7, SEC-4).
/// </summary>
internal static class IdentifierQuoting
{
    /// <summary>Double-quotes <paramref name="identifier"/>, doubling any embedded <c>"</c> character.</summary>
    public static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
