using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;

namespace TriQL.EntityFrameworkCore.Update.Internal;

/// <summary>
/// Creates the command batches <c>SaveChanges</c> executes. Each batch is one statement; consecutive
/// inserts into the same table are combined into one multi-row <c>INSERT</c> of up to <c>MaxBatchSize</c>
/// rows (default 1000; <c>MaxBatchSize(1)</c> turns combining off).
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    /// <summary>The most rows in one combined insert when <c>MaxBatchSize</c> is not configured.</summary>
    public const int DefaultMaxBatchSize = 1000;

    private readonly int _maxBatchSize;

    /// <summary>Initializes a new instance.</summary>
    public TrinoModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies, IDbContextOptions options)
    {
        Dependencies = dependencies;
        _maxBatchSize = Math.Max(1, RelationalOptionsExtension.Extract(options).MaxBatchSize ?? DefaultMaxBatchSize);
    }

    /// <summary>Dependencies for this service.</summary>
    protected virtual ModificationCommandBatchFactoryDependencies Dependencies { get; }

    /// <inheritdoc />
    public virtual ModificationCommandBatch Create() => new TrinoModificationCommandBatch(Dependencies, _maxBatchSize);
}
