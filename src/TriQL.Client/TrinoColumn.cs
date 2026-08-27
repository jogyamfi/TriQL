using TriQL.Client.Internal;
using TriQL.Client.Types;

namespace TriQL.Client;

/// <summary>
/// Column metadata for a <see cref="TrinoResultSet"/>. See FR-4.2.1, FR-4.2.3, FR-7.1.
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="TypeName">The raw Trino type signature string, e.g. <c>bigint</c> or <c>decimal(38,10)</c>.</param>
public sealed record TrinoColumn(string Name, string TypeName)
{
    /// <summary>The parsed type signature for <see cref="TypeName"/>. Parsed once and cached per distinct string (FR-7.1.3).</summary>
    public TrinoTypeSignature TypeSignature => TrinoTypeSignature.Parse(TypeName);

    /// <summary>The default CLR type this column materializes to (FR-7.2.1).</summary>
    public Type ClrType => TrinoValueConverter.GetDefaultClrType(TypeSignature);
}
