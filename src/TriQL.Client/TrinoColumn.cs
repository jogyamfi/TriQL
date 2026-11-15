using TriQL.Client.Internal;
using TriQL.Client.Types;

namespace TriQL.Client;

/// <summary>
/// Column metadata for a <see cref="TrinoResultSet"/>.
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="TypeName">The raw Trino type signature string, e.g. <c>bigint</c> or <c>decimal(38,10)</c>.</param>
public sealed record TrinoColumn(string Name, string TypeName)
{
    /// <summary>The parsed type signature for <see cref="TypeName"/>. Parsed once and cached per distinct string.</summary>
    public TrinoTypeSignature TypeSignature => TrinoTypeSignature.Parse(TypeName);

    /// <summary>The default CLR type this column materializes to.</summary>
    public Type ClrType => TrinoValueConverter.GetDefaultClrType(TypeSignature);
}
