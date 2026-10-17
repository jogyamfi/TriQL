using Microsoft.EntityFrameworkCore;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests.Query;

/// <summary>SQL baselines for catalogs (Phase 5): three-part names from <c>HasDefaultCatalog</c>/<c>HasCatalog</c>.</summary>
public sealed class CatalogSqlTests
{
    [Fact]
    public async Task NoCatalogOrSchema_UsesTheConnectionsCatalogAndSchema()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(ModelContext.CreateOptions(fake), b => b.Entity<Order>());
        fake.EnqueueRows([("Id", "integer")]);

        await db.Set<Order>().Select(o => o.Id).ToListAsync();

        fake.AssertSql(
            """
            SELECT "o"."Id"
            FROM "Order" AS "o"
            """);
    }

    [Fact]
    public async Task DefaultCatalog_And_DefaultSchema_GiveThreePartNames()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(
            ModelContext.CreateOptions(fake),
            b => b.HasDefaultCatalog("lake").HasDefaultSchema("sales").Entity<Order>().ToTable("orders"));
        fake.EnqueueRows([("Id", "integer")]);

        await db.Set<Order>().Select(o => o.Id).ToListAsync();

        fake.AssertSql(
            """
            SELECT "o"."Id"
            FROM "lake"."sales"."orders" AS "o"
            """);
    }

    [Fact]
    public async Task EntityCatalog_OverridesTheDefault_AndJoinsAcrossCatalogs()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(
            ModelContext.CreateOptions(fake),
            b =>
            {
                b.HasDefaultCatalog("lake");
                b.Entity<Order>().ToTable("orders", "sales");
                b.Entity<Customer>().HasCatalog("crm").ToTable("customers", "public");
            });
        fake.EnqueueRows([("Id", "integer"), ("Name", "varchar")]);

        await db.Set<Order>().Join(db.Set<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { o.Id, c.Name }).ToListAsync();

        fake.AssertSql(
            """
            SELECT "o"."Id", "c"."Name"
            FROM "lake"."sales"."orders" AS "o"
            INNER JOIN "crm"."public"."customers" AS "c" ON "o"."CustomerId" = "c"."Id"
            """);
    }

    [Fact]
    public async Task OwnedCollection_TableFollowsTheOwnersCatalog()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(
            ModelContext.CreateOptions(fake),
            b => b.HasDefaultSchema("sales").Entity<Invoice>(o =>
            {
                o.HasCatalog("lake");
                o.OwnsMany(x => x.Lines, l =>
                {
                    l.ToTable("order_lines");
                    l.Property<Guid>("Id");
                    l.HasKey("Id");
                });
            }));
        fake.EnqueueRows([("Id", "integer"), ("Id0", "uuid"), ("InvoiceId", "integer"), ("Product", "varchar")]);

        await db.Set<Invoice>().ToListAsync();

        Assert.Single(fake.Sql);
        Assert.Contains("FROM \"lake\".\"sales\".\"Invoice\" AS \"i\"", fake.Sql[0], StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN \"lake\".\"sales\".\"order_lines\" AS ", fake.Sql[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteDelete_UsesTheThreePartName()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(
            ModelContext.CreateOptions(fake),
            b => b.HasDefaultCatalog("lake").Entity<Order>().ToTable("orders", "sales"));
        fake.EnqueueUpdate("DELETE", 2);

        var deleted = await db.Set<Order>().Where(o => o.CustomerId == 7).ExecuteDeleteAsync();

        Assert.Equal(2, deleted);
        fake.AssertSql(
            """
            DELETE FROM "lake"."sales"."orders"
            WHERE "lake"."sales"."orders"."CustomerId" = 7
            """);
    }

    private sealed class Order
    {
        public int Id { get; set; }

        public int CustomerId { get; set; }
    }

    private sealed class Invoice
    {
        public int Id { get; set; }

        public List<OrderLine> Lines { get; set; } = [];
    }

    private sealed class OrderLine
    {
        public string Product { get; set; } = string.Empty;
    }

    private sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
