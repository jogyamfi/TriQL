using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TriQL.EntityFrameworkCore.Design.Internal;
using TriQL.EntityFrameworkCore.Metadata.Internal;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests.Scaffolding;

/// <summary>Phase 9: the database model factory against scripted metadata pages, and the generated code.</summary>
public sealed class ScaffoldingTests
{
    private static readonly (string, string)[] CommentColumns = [("schema_name", "varchar"), ("table_name", "varchar"), ("comment", "varchar")];
    private static readonly (string, string)[] TableColumns = [("table_schem", "varchar"), ("table_name", "varchar"), ("table_type", "varchar")];
    private static readonly (string, string)[] ColumnColumns =
        [("table_schem", "varchar"), ("table_name", "varchar"), ("column_name", "varchar"), ("type_name", "varchar"), ("is_nullable", "varchar"), ("remarks", "varchar")];

    [Fact]
    public void Factory_ReadsTablesViewsColumnsAndComments_AndSkipsUnmappableColumns()
    {
        using var fake = new FakeTrino();
        var logs = new List<(EventId Id, string Message)>();
        ScriptSalesSchema(fake);

        using var services = CreateServices(logs);

        var model = services.GetRequiredService<IDatabaseModelFactory>()
            .Create(fake.Connection, new DatabaseModelFactoryOptions(tables: [], schemas: ["lake.sales"]));

        Assert.Equal("lake", model.DatabaseName);
        Assert.Equal("lake", model[TrinoAnnotationNames.DefaultCatalog]);
        var lines = Assert.Single(model.Tables, t => t.Name == "order_lines");
        Assert.IsNotType<DatabaseView>(lines);
        Assert.Equal("Order lines", lines.Comment);
        Assert.Null(lines.PrimaryKey);
        Assert.Equal(["order_id", "unit_price", "shipped_at"], lines.Columns.Select(c => c.Name));
        Assert.Equal(("bigint", false, "the order"), (lines.Columns[0].StoreType, lines.Columns[0].IsNullable, lines.Columns[0].Comment));
        Assert.Equal(("decimal(10,2)", true, null), (lines.Columns[1].StoreType, lines.Columns[1].IsNullable, lines.Columns[1].Comment));
        Assert.IsType<DatabaseView>(Assert.Single(model.Tables, t => t.Name == "big_orders"));

        var skipped = Assert.Single(logs, l => l.Id == TrinoEventId.ColumnSkipped);
        Assert.Contains("'tags' of 'sales.order_lines'", skipped.Message, StringComparison.Ordinal);
        Assert.Contains("'array(varchar)'", skipped.Message, StringComparison.Ordinal);

        // Catalog and schema are bound parameters: the SQL text names neither.
        Assert.All(fake.Sql, sql =>
        {
            Assert.Contains("@catalog", sql, StringComparison.Ordinal);
            Assert.Contains("@schema0", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("'lake'", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("'sales'", sql, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Factory_TableFilter_AcceptsBareSchemaAndCatalogQualifiedNames()
    {
        using var fake = new FakeTrino();
        ScriptSalesSchema(fake);

        using var services = CreateServices([]);

        var model = services.GetRequiredService<IDatabaseModelFactory>()
            .Create(fake.Connection, new DatabaseModelFactoryOptions(tables: ["lake.sales.BIG_ORDERS"], schemas: ["lake.sales"]));

        Assert.Equal(["big_orders"], model.Tables.Select(t => t.Name));
    }

    [Fact]
    public void Factory_RejectsSchemasFromSeveralCatalogs_AndAMissingCatalog()
    {
        using var fake = new FakeTrino();
        using var services = CreateServices([]);
        var factory = services.GetRequiredService<IDatabaseModelFactory>();

        var several = Assert.Throws<InvalidOperationException>(() =>
            factory.Create(fake.Connection, new DatabaseModelFactoryOptions(tables: [], schemas: ["a.x", "b.y"])));
        var none = Assert.Throws<InvalidOperationException>(() =>
            factory.Create(fake.Connection, new DatabaseModelFactoryOptions(tables: [], schemas: ["sales"])));

        Assert.Contains("one catalog at a time", several.Message, StringComparison.Ordinal);
        Assert.Contains("needs a catalog", none.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldedCode_IsKeyless_PascalCased_AndConfiguresTheProviderAndCatalog()
    {
        using var fake = new FakeTrino();
        ScriptSalesSchema(fake);
        using var services = CreateServices([]);

        // The steps of IReverseEngineerScaffolder, with the database model read through the fake connection.
        var databaseModel = services.GetRequiredService<IDatabaseModelFactory>()
            .Create(fake.Connection, new DatabaseModelFactoryOptions(tables: [], schemas: ["lake.sales"]));
        var model = services.GetRequiredService<IScaffoldingModelFactory>()
            .Create(databaseModel, new ModelReverseEngineerOptions());
        var code = services.GetRequiredService<IModelCodeGeneratorSelector>()
            .Select(new ModelCodeGenerationOptions { Language = "C#" })
            .GenerateModel(model, new ModelCodeGenerationOptions
            {
                ContextName = "SalesContext",
                ContextNamespace = "Sales",
                ModelNamespace = "Sales",
                ConnectionString = "Server=https://trino.example.com;Catalog=lake;Schema=sales",
                Language = "C#",
            });

        var context = code.ContextFile.Code;
        Assert.Contains(".UseTrino(\"Server=https://trino.example.com;Catalog=lake;Schema=sales\")", context, StringComparison.Ordinal);
        Assert.Contains(".HasDefaultCatalog(\"lake\")", context, StringComparison.Ordinal);
        Assert.Equal(2, context.Split(".HasNoKey()").Length - 1);
        Assert.Contains(".ToTable(\"order_lines\", \"sales\", tb => tb.HasComment(\"Order lines\"))", context, StringComparison.Ordinal);
        Assert.Contains(".ToView(\"big_orders\", \"sales\")", context, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"order_id\")", context, StringComparison.Ordinal);
        Assert.Contains("public virtual DbSet<OrderLine> OrderLines", context, StringComparison.Ordinal);
        var entity = Assert.Single(code.AdditionalFiles, f => f.Path == "OrderLine.cs").Code;
        Assert.Contains("public long OrderId { get; set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public decimal? UnitPrice { get; set; }", entity, StringComparison.Ordinal);
        Assert.Contains("public DateTimeOffset? ShippedAt { get; set; }", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("Tags", entity, StringComparison.Ordinal);

        // decimal(10,2) scaffolds as HasPrecision(10): the scale matches the provider's default of 2.
        Assert.Contains(".HasPrecision(10)", context, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldedHasPrecisionWithoutScale_KeepsTheDefaultScale()
    {
        using var fake = new FakeTrino();
        using var db = new ModelContext(ModelContext.CreateOptions(fake), b => b.Entity<Priced>().Property(p => p.Price).HasPrecision(10));

        Assert.Equal("decimal(10,2)", db.Model.FindEntityType(typeof(Priced))!.FindProperty(nameof(Priced.Price))!.GetColumnType());
    }

    // ---- helpers --------------------------------------------------------------------------

    private sealed class Priced
    {
        public int Id { get; set; }

        public decimal Price { get; set; }
    }

    /// <summary>The three metadata queries the factory runs, in order: table comments, tables, columns.</summary>
    private static void ScriptSalesSchema(FakeTrino fake)
    {
        fake.EnqueueRows(CommentColumns, ["sales", "order_lines", "Order lines"]);
        fake.EnqueueRows(TableColumns, ["sales", "big_orders", "VIEW"], ["sales", "order_lines", "TABLE"]);
        fake.EnqueueRows(
            ColumnColumns,
            ["sales", "big_orders", "order_id", "bigint", "YES", ""],
            ["sales", "order_lines", "order_id", "bigint", "NO", "the order"],
            ["sales", "order_lines", "unit_price", "decimal(10,2)", "YES", ""],
            ["sales", "order_lines", "tags", "array(varchar)", "YES", ""],
            ["sales", "order_lines", "shipped_at", "timestamp(6) with time zone", "YES", null]);
    }

    private static ServiceProvider CreateServices(List<(EventId Id, string Message)> logs)
    {
        var services = new ServiceCollection()
            .AddEntityFrameworkDesignTimeServices()
            .AddSingleton<ILoggerFactory>(_ => new CapturingLoggerFactory(logs));
        new TrinoDesignTimeServices().ConfigureDesignTimeServices(services);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingLoggerFactory(List<(EventId Id, string Message)> logs) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new Logger(logs);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Logger(List<(EventId Id, string Message)> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Add((eventId, formatter(state, exception)));
        }
    }
}
