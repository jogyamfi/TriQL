using Microsoft.EntityFrameworkCore.Update;

namespace TriQL.EntityFrameworkCore.Update.Internal;

/// <summary>Creates the command batches <c>SaveChanges</c> executes: one statement each.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies) =>
        Dependencies = dependencies;

    /// <summary>Dependencies for this service.</summary>
    protected virtual ModificationCommandBatchFactoryDependencies Dependencies { get; }

    /// <inheritdoc />
    public virtual ModificationCommandBatch Create() => new TrinoModificationCommandBatch(Dependencies);
}
