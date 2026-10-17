using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TriQL.EntityFrameworkCore.ValueGeneration.Internal;

/// <summary>
/// Generates version 7 UUIDs (<see cref="Guid.CreateVersion7()"/>): unique, generated on the client,
/// and ordered by creation time, which keeps related rows close together in sorted Iceberg data files.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class GuidV7ValueGenerator : ValueGenerator<Guid>
{
    /// <summary>The shared instance; the generator has no state.</summary>
    public static GuidV7ValueGenerator Instance { get; } = new();

    /// <summary>Always <see langword="false"/>: the values are the ones saved.</summary>
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}
