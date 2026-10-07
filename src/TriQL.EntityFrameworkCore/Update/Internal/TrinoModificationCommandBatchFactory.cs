using Microsoft.EntityFrameworkCore.Update;

namespace TriQL.EntityFrameworkCore.Update.Internal;

/// <summary>
/// Creates the command batches <c>SaveChanges</c> executes. Until Phase 6 adds non-transactional,
/// rows-affected-aware batches, <c>SaveChanges</c> throws <see cref="NotSupportedException"/>
/// instead of running through EF's default (transactional) pipeline.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    internal const string NotYetSupportedMessage =
        "SaveChanges is not supported yet by the Trino EF Core provider. Use Database.ExecuteSql for INSERT, UPDATE and DELETE.";

    /// <summary>Initializes a new instance.</summary>
    public TrinoModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies) =>
        Dependencies = dependencies;

    /// <summary>Dependencies for this service.</summary>
    protected virtual ModificationCommandBatchFactoryDependencies Dependencies { get; }

    /// <summary>Always throws until Phase 6.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual ModificationCommandBatch Create() => throw new NotSupportedException(NotYetSupportedMessage);
}
