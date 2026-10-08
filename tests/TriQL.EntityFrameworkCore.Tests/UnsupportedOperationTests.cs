using Microsoft.EntityFrameworkCore;
using TriQL.EntityFrameworkCore.Migrations.Internal;
using TriQL.EntityFrameworkCore.Storage.Internal;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests;

/// <summary>Operations the provider does not support (or does not support yet) fail with guidance, before any SQL is sent.</summary>
public sealed class UnsupportedOperationTests
{
    [Fact]
    public async Task Migrate_Throws_WithGuidance()
    {
        using var fake = new FakeTrino();
        await using var context = new WidgetContext(fake.CreateOptions<WidgetContext>());

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.MigrateAsync());

        Assert.Equal(TrinoHistoryRepository.NotSupportedMessage, ex.Message);
        Assert.Empty(fake.Sql);
    }

    [Fact]
    public async Task EnsureCreated_EnsureDeleted_AndGenerateCreateScript_ThrowUntilImplemented()
    {
        using var fake = new FakeTrino();
        await using var context = new WidgetContext(fake.CreateOptions<WidgetContext>());

        Assert.Equal(TrinoDatabaseCreator.NotYetSupportedMessage, Assert.Throws<NotSupportedException>(() => context.Database.GenerateCreateScript()).Message);

        // EnsureCreated/EnsureDeleted check reachability first (SELECT 1), then reach the unsupported step.
        fake.EnqueueRows([("_col0", "integer")], [1]);
        await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureCreatedAsync());
        fake.EnqueueRows([("_col0", "integer")], [1]);
        await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureDeletedAsync());
    }
}
