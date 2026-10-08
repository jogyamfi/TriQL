using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.Client.Exceptions;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests.Update;

/// <summary>
/// Phase 6: the SQL <c>SaveChanges</c> generates, rows-affected checks, the non-transactional executor
/// (partial failure, the warning) and per-statement retries, against the scripted coordinator.
/// </summary>
public sealed class SaveChangesTests
{
    // ---- DML baselines --------------------------------------------------------------------

    [Fact]
    public async Task Insert_Update_Delete_GenerateOneStatementEach()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake);
        var added = new Item { Id = 1, Name = "a" };
        var modified = new Item { Id = 2, Name = "b", Version = 7 };
        var deleted = new Item { Id = 3, Name = "c", Version = 1 };
        db.Add(added);
        db.Attach(modified).Entity.Name = "b2";
        db.Entry(modified).Entity.Version = 8;
        db.Remove(db.Attach(deleted).Entity);
        fake.EnqueueUpdate("DELETE", 1);
        fake.EnqueueUpdate("UPDATE", 1);
        fake.EnqueueUpdate("INSERT", 1);

        var saved = await db.SaveChangesAsync();

        Assert.Equal(3, saved);
        fake.AssertSql(
            """
            DELETE FROM "Item"
            WHERE "Id" = @p0 AND "Version" = @p1
            """,
            """
            UPDATE "Item" SET "Name" = @p0, "Version" = @p1
            WHERE "Id" = @p2 AND "Version" = @p3
            """,
            """
            INSERT INTO "Item" ("Id", "Name", "Version")
            VALUES (@p0, @p1, @p2)
            """);
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
    }

    [Fact]
    public async Task Insert_IntoACatalogTable_UsesTheThreePartName()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake, b => b.HasDefaultCatalog("lake").Entity<Item>().ToTable("items", "sales"));
        db.Add(new Item { Id = 1, Name = "a" });
        fake.EnqueueUpdate("INSERT", 1);

        await db.SaveChangesAsync();

        fake.AssertSql(
            """
            INSERT INTO "lake"."sales"."items" ("Id", "Name", "Version")
            VALUES (@p0, @p1, @p2)
            """);
    }

    [Fact]
    public async Task GuidKey_IsGeneratedBeforeTheInsert_AndSentAsAParameter()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(ModelContext.CreateOptions(fake), b => b.Entity<Event>());
        var entity = db.Add(new Event { Payload = "x" }).Entity;
        fake.EnqueueUpdate("INSERT", 1);

        await db.SaveChangesAsync();

        Assert.Equal(7, entity.Id.Version);
        Assert.Contains(entity.Id.ToString(), Assert.Single(fake.SubmittedBodies), StringComparison.Ordinal);
    }

    // ---- rows affected and concurrency ----------------------------------------------------

    [Fact]
    public async Task UpdateAffectingNoRows_ThrowsConcurrencyException()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake);
        db.Attach(new Item { Id = 1, Name = "a", Version = 1 }).Entity.Name = "b";
        fake.EnqueueUpdate("UPDATE", 0);

        var ex = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());

        Assert.Single(ex.Entries);
        Assert.Equal(EntityState.Modified, db.ChangeTracker.Entries().Single().State);
    }

    [Fact]
    public async Task DeleteReportingANullCount_IsAConcurrencyConflict()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake);
        db.Remove(db.Attach(new Item { Id = 1, Name = "a" }).Entity);
        fake.EnqueueUpdateWithoutCount("DELETE");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrencyConflict_IsNotRetried()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake, configureOptions: o => o.UseTrino(fake.Connection, t => t.EnableRetryOnFailure(3, TimeSpan.Zero)));
        db.Attach(new Item { Id = 1, Name = "a" }).Entity.Name = "b";
        fake.EnqueueUpdate("UPDATE", 0);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());

        Assert.Single(fake.Sql);
    }

    // ---- non-atomic execution -------------------------------------------------------------

    [Fact]
    public async Task FailureOfTheThirdOfFiveStatements_KeepsTheFirstTwoSaved_AndWarnsOnce()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake);
        var items = Enumerable.Range(1, 5).Select(i => new Item { Id = i, Name = $"n{i}" }).ToList();
        db.AddRange(items);
        fake.EnqueueUpdate("INSERT", 1);
        fake.EnqueueUpdate("INSERT", 1);
        fake.EnqueueError("CONSTRAINT_VIOLATION", "USER_ERROR");

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.IsType<TrinoQueryException>(ex.InnerException);
        Assert.Equal(3, fake.Sql.Count);
        var states = items.Select(i => db.Entry(i).State).ToList();
        Assert.Equal(2, states.Count(s => s == EntityState.Unchanged));
        Assert.Equal(3, states.Count(s => s == EntityState.Added));
        var warning = Assert.Single(fake.ProviderEvents);
        Assert.Equal(TrinoEventId.NonAtomicSaveChanges, warning.Id);
        Assert.Contains("earlier ones stay committed", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleStatementSave_DoesNotWarn()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake);
        db.Add(new Item { Id = 1, Name = "a" });
        fake.EnqueueUpdate("INSERT", 1);

        await db.SaveChangesAsync();

        Assert.Single(fake.Sql);
        Assert.Empty(fake.ProviderEvents);
    }

    // ---- retries --------------------------------------------------------------------------

    [Fact]
    public async Task TransientStatementFailure_IsRetriedOnItsOwn_WhenRetryIsEnabled()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake, configureOptions: o => o.UseTrino(fake.Connection, t => t.EnableRetryOnFailure(3, TimeSpan.Zero)));
        db.AddRange(new Item { Id = 1, Name = "a" }, new Item { Id = 2, Name = "b" });
        fake.EnqueueUpdate("INSERT", 1);
        fake.EnqueueError("ICEBERG_COMMIT_ERROR");
        fake.EnqueueError("ICEBERG_COMMIT_ERROR");
        fake.EnqueueUpdate("INSERT", 1);

        var saved = await db.SaveChangesAsync();

        Assert.Equal(2, saved);
        // The second statement (Id 2) ran three times; the first was not repeated.
        // Bodies are "EXECUTE <random name> USING <values>"; compare the values.
        var bodies = fake.SubmittedBodies.Select(body => body![body!.IndexOf(" USING ", StringComparison.Ordinal)..]).ToList();
        Assert.Equal(4, bodies.Count);
        Assert.Equal(bodies[1], bodies[2]);
        Assert.Equal(bodies[2], bodies[3]);
        Assert.NotEqual(bodies[0], bodies[1]);
    }

    [Fact]
    public async Task TransientStatementFailure_IsNotRetried_WithoutEnableRetryOnFailure()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake);
        db.Add(new Item { Id = 1, Name = "a" });
        fake.EnqueueError("ICEBERG_COMMIT_ERROR");

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Single(fake.Sql);
    }

    [Fact]
    public async Task ExhaustedStatementRetries_AreNotRetriedAgainAsAWholeSave()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake, configureOptions: o => o.UseTrino(fake.Connection, t => t.EnableRetryOnFailure(2, TimeSpan.Zero)));
        db.Add(new Item { Id = 1, Name = "a" });
        fake.EnqueueError("ICEBERG_COMMIT_ERROR");
        fake.EnqueueError("ICEBERG_COMMIT_ERROR");
        fake.EnqueueError("ICEBERG_COMMIT_ERROR");

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Equal("ICEBERG_COMMIT_ERROR", Assert.IsType<TrinoQueryException>(ex.InnerException).ErrorName);
        Assert.Equal(3, fake.Sql.Count); // the first attempt and 2 statement retries; the strategy adds none
    }

    [Fact]
    public async Task TransientQueryFailure_IsRetriedByTheExecutionStrategy()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake, configureOptions: o => o.UseTrino(fake.Connection, t => t.EnableRetryOnFailure(3, TimeSpan.Zero)));
        fake.EnqueueError("CLUSTER_OUT_OF_MEMORY", "INSUFFICIENT_RESOURCES");
        fake.EnqueueRows([("Id", "integer"), ("Name", "varchar"), ("Version", "bigint")], [1, "a", 0L]);

        var items = await db.Set<Item>().ToListAsync();

        Assert.Single(items);
        Assert.Equal(2, fake.Sql.Count);
    }

    [Fact]
    public async Task NonTransientQueryFailure_IsNotRetried()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake, configureOptions: o => o.UseTrino(fake.Connection, t => t.EnableRetryOnFailure(3, TimeSpan.Zero)));
        fake.EnqueueError("TABLE_NOT_FOUND", "USER_ERROR");

        await Assert.ThrowsAsync<TrinoQueryException>(() => db.Set<Item>().ToListAsync());

        Assert.Single(fake.Sql);
    }

    // ---- raw DML --------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSql_ReturnsRowsAffected()
    {
        using var fake = new FakeTrino();
        await using var db = CreateContext(fake);
        fake.EnqueueUpdate("UPDATE", 4);

        var affected = await db.Database.ExecuteSqlAsync($"UPDATE \"Item\" SET \"Name\" = {"x"}");

        Assert.Equal(4, affected);
    }

    // ---- helpers --------------------------------------------------------------------------

    private static ModelContext CreateContext(
        FakeTrino fake,
        Action<ModelBuilder>? configureModel = null,
        Action<DbContextOptionsBuilder<ModelContext>>? configureOptions = null) =>
        new(
            ModelContext.CreateOptions(fake, configureOptions),
            configureModel ?? (b => b.Entity<Item>().Property(i => i.Version).IsConcurrencyToken()));

    private sealed class Item
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public long Version { get; set; }
    }

    private sealed class Event
    {
        public Guid Id { get; set; }

        public string Payload { get; set; } = string.Empty;
    }
}
