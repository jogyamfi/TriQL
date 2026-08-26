namespace TriQL.Client;

/// <summary>
/// Column metadata for a <see cref="TrinoResultSet"/>. See FR-4.2.1, FR-4.2.3.
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="TypeName">
/// The raw Trino type signature string, e.g. <c>bigint</c> or <c>decimal(38,10)</c>. Parsing this
/// into a structured <c>TrinoTypeSignature</c> is introduced in Phase 3.
/// </param>
public sealed record TrinoColumn(string Name, string TypeName);
