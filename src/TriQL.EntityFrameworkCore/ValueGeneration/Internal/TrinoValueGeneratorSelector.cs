using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TriQL.EntityFrameworkCore.ValueGeneration.Internal;

/// <summary>Selects the client-side value generator for properties generated on add: UUIDv7 for <see cref="Guid"/>.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoValueGeneratorSelector : RelationalValueGeneratorSelector
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoValueGeneratorSelector(ValueGeneratorSelectorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    protected override ValueGenerator? FindForType(IProperty property, ITypeBase typeBase, Type clrType) =>
        clrType == typeof(Guid) ? GuidV7ValueGenerator.Instance : base.FindForType(property, typeBase, clrType);
}
