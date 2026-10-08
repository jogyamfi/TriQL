using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace TriQL.EntityFrameworkCore.Tests.TestUtilities;

/// <summary>
/// A context whose model is built by the test (<paramref name="configure"/>), and never cached, so that
/// each test validates its own model.
/// </summary>
internal sealed class ModelContext(DbContextOptions<ModelContext> options, Action<ModelBuilder> configure) : DbContext(options)
{
    /// <summary>Options for a context that talks to <paramref name="fake"/> and does not cache models.</summary>
    public static DbContextOptions<ModelContext> CreateOptions(FakeTrino fake, Action<DbContextOptionsBuilder<ModelContext>>? configure = null) =>
        fake.CreateOptions<ModelContext>(b =>
        {
            b.ReplaceService<IModelCacheKeyFactory, UncachedModelCacheKeyFactory>();
            configure?.Invoke(b);
        });

    protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);

    private sealed class UncachedModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => new object();
    }
}
