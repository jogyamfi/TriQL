using Microsoft.EntityFrameworkCore;
using TriQL.EntityFrameworkCore.Migrations.Internal;
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
}
