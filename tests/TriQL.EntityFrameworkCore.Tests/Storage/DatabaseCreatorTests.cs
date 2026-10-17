using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests.Storage;

/// <summary>Phase 8 (EF8-T1): the development DDL behind EnsureCreated, EnsureDeleted and GenerateCreateScript.</summary>
public sealed class DatabaseCreatorTests
{
    private static readonly (string Name, string Type)[] Count = [("_col0", "bigint")];

    [Fact]
    public void GenerateCreateScript_HasSchemaAndTables_WithoutKeysOrIndexes()
    {
        using var fake = new FakeTrino();
        using var db = new ModelContext(ModelContext.CreateOptions(fake), Configure);

        var script = db.Database.GenerateCreateScript();

        Assert.Equal(
            """
            CREATE SCHEMA IF NOT EXISTS "lake"."sales";

            CREATE TABLE "lake"."sales"."Customers" (
                "Id" integer NOT NULL,
                "Name" varchar NOT NULL COMMENT 'The customer''s name',
                "Nickname" varchar
            )
            COMMENT 'People who buy';

            CREATE TABLE "lake"."sales"."Orders" (
                "Id" uuid NOT NULL,
                "CustomerId" integer NOT NULL,
                "Total" decimal(12,2) NOT NULL,
                "Placed" timestamp(6) NOT NULL
            );
            """.ReplaceLineEndings("\n"),
            script.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public async Task EnsureCreated_CreatesTheSchemaAndTables_WhenNoneOfTheTablesExists()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(ModelContext.CreateOptions(fake), Configure);
        fake.EnqueueRows([("_col0", "integer")], [1]);
        fake.EnqueueRows(Count, [0L]);
        fake.EnqueueDdl("CREATE SCHEMA");
        fake.EnqueueDdl("CREATE TABLE");
        fake.EnqueueDdl("CREATE TABLE");

        Assert.True(await db.Database.EnsureCreatedAsync());

        Assert.Equal(5, fake.Sql.Count);
        Assert.Equal("SELECT 1", fake.Sql[0]);
        Assert.Equal(
            "SELECT count(*) FROM \"lake\".information_schema.tables WHERE (table_schema = 'sales' AND table_name = 'customers') "
            + "OR (table_schema = 'sales' AND table_name = 'orders')",
            fake.Sql[1]);
        Assert.StartsWith("CREATE SCHEMA IF NOT EXISTS \"lake\".\"sales\"", fake.Sql[2], StringComparison.Ordinal);
        Assert.StartsWith("CREATE TABLE \"lake\".\"sales\".\"Customers\"", fake.Sql[3], StringComparison.Ordinal);
        Assert.StartsWith("CREATE TABLE \"lake\".\"sales\".\"Orders\"", fake.Sql[4], StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureCreated_DoesNothing_WhenATableExists()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(ModelContext.CreateOptions(fake), Configure);
        fake.EnqueueRows([("_col0", "integer")], [1]);
        fake.EnqueueRows(Count, [1L]);

        Assert.False(await db.Database.EnsureCreatedAsync());
        Assert.Equal(2, fake.Sql.Count);
    }

    [Fact]
    public async Task EnsureDeleted_DropsOnlyTheModelsTables()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(ModelContext.CreateOptions(fake), Configure);
        fake.EnqueueRows(Count, [2L]);
        fake.EnqueueDdl("DROP TABLE");
        fake.EnqueueDdl("DROP TABLE");

        Assert.True(await db.Database.EnsureDeletedAsync());

        Assert.Equal(
            ["DROP TABLE IF EXISTS \"lake\".\"sales\".\"Customers\"", "DROP TABLE IF EXISTS \"lake\".\"sales\".\"Orders\""],
            fake.Sql.Skip(1).Select(s => s.Trim()));
        Assert.DoesNotContain(fake.Sql, s => s.Contains("SCHEMA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureDeleted_ReturnsFalse_WhenNoTableExists()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(ModelContext.CreateOptions(fake), Configure);
        fake.EnqueueRows(Count, [0L]);

        Assert.False(await db.Database.EnsureDeletedAsync());
        Assert.Single(fake.Sql);
    }

    [Fact]
    public void TablesWithoutASchema_AreLookedUpInTheConnectionsSchema()
    {
        using var fake = new FakeTrino();
        using var db = new ModelContext(ModelContext.CreateOptions(fake), b => b.Entity<Customer>());
        fake.EnqueueRows(Count, [0L]);

        Assert.False(db.Database.EnsureDeleted());

        Assert.Equal(
            "SELECT count(*) FROM information_schema.tables WHERE (table_schema = current_schema AND table_name = 'customer')",
            Assert.Single(fake.Sql));
    }

    [Fact]
    public void OtherMigrationOperations_Throw()
    {
        using var fake = new FakeTrino();
        using var db = new ModelContext(ModelContext.CreateOptions(fake), Configure);
        var generator = db.GetService<IMigrationsSqlGenerator>();

        var ex = Assert.Throws<NotSupportedException>(() =>
            generator.Generate([new AddColumnOperation { Name = "X", Table = "Orders", ClrType = typeof(int) }]));

        Assert.Contains("AddColumnOperation", ex.Message, StringComparison.Ordinal);
    }

    private static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultCatalog("lake").HasDefaultSchema("sales");
        modelBuilder.Entity<Customer>(c =>
        {
            c.ToTable("Customers", t => t.HasComment("People who buy"));
            c.Property(x => x.Name).HasComment("The customer's name");
        });
        modelBuilder.Entity<Order>(o =>
        {
            o.ToTable("Orders");
            o.Property(x => x.Total).HasPrecision(12, 2);
            o.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        });
    }

    private sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Nickname { get; set; }
    }

    private sealed class Order
    {
        public Guid Id { get; set; }

        public int CustomerId { get; set; }

        public decimal Total { get; set; }

        public DateTime Placed { get; set; }
    }
}
