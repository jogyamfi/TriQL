using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using TriQL.EntityFrameworkCore.Design.Internal;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Scaffolding;

/// <summary>
/// Phase 9 live: scaffolds a <c>DbContext</c> from an Iceberg schema this test creates (snake_case names,
/// comments, a view, a column of an unmappable type), compiles the generated code with Roslyn in memory, and
/// queries through it.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfIceberg")]
public sealed class ScaffoldingFunctionalTests(TrinoContainerFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private readonly string _schema = $"efct_scaffold_{Guid.NewGuid():N}"[..30];

    private string ConnectionString => $"Server={fixture.ServerUri};User=triql-ef;Catalog={TrinoContainerFixture.IcebergCatalog};Schema={_schema}";

    public async Task InitializeAsync()
    {
        await using var db = new DbContext(new DbContextOptionsBuilder().UseTrino(ConnectionString).Options);
        foreach (var sql in new[]
        {
            $"CREATE SCHEMA {TrinoContainerFixture.IcebergCatalog}.{_schema}",
            "CREATE TABLE order_lines (order_id bigint NOT NULL COMMENT 'the order', line_no integer NOT NULL, unit_price decimal(10,2), "
                + "shipped_at timestamp(6) with time zone, tags array(varchar)) COMMENT 'Order lines'",
            "CREATE VIEW big_orders AS SELECT order_id, unit_price FROM order_lines WHERE unit_price > 100",
            "INSERT INTO order_lines VALUES (1, 1, 12.50, TIMESTAMP '2026-10-08 10:00:00 UTC', ARRAY['a']), (2, 1, 150.00, NULL, NULL), (2, 2, 250.25, NULL, NULL)",
        })
        {
            await DdlAsync(db, sql);
        }
    }

    public async Task DisposeAsync()
    {
        await using var db = new DbContext(new DbContextOptionsBuilder().UseTrino(ConnectionString).Options);
        await DdlAsync(db, "DROP VIEW IF EXISTS big_orders");
        await DdlAsync(db, "DROP TABLE IF EXISTS order_lines");
        await DdlAsync(db, $"DROP SCHEMA IF EXISTS {TrinoContainerFixture.IcebergCatalog}.{_schema}");
    }

    [Fact]
    public async Task Scaffold_Compile_AndQuery()
    {
        var services = new ServiceCollection().AddEntityFrameworkDesignTimeServices();
        new TrinoDesignTimeServices().ConfigureDesignTimeServices(services);
        await using var provider = services.BuildServiceProvider();

        var scaffolded = provider.GetRequiredService<IReverseEngineerScaffolder>().ScaffoldModel(
            ConnectionString,
            new DatabaseModelFactoryOptions(tables: [], schemas: [_schema]),
            new ModelReverseEngineerOptions(),
            new ModelCodeGenerationOptions
            {
                ContextName = "ScaffoldedContext",
                ContextNamespace = "Scaffolded",
                ModelNamespace = "Scaffolded",
                ConnectionString = ConnectionString,
                Language = "C#",
                UseNullableReferenceTypes = true,
            });

        var sources = new[] { scaffolded.ContextFile }.Concat(scaffolded.AdditionalFiles).Select(f => f.Code).ToList();
        sources.ForEach(output.WriteLine);
        Assert.Equal(["BigOrder.cs", "OrderLine.cs"], scaffolded.AdditionalFiles.Select(f => f.Path).Order(StringComparer.Ordinal));

        const string probe = """
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            namespace Scaffolded;
            public static class Probe
            {
                public static async Task<object> RunAsync()
                {
                    await using var db = new ScaffoldedContext();
                    var lines = await db.OrderLines.OrderBy(l => l.OrderId).ThenBy(l => l.LineNo)
                        .Select(l => l.OrderId + "/" + l.LineNo + "/" + l.UnitPrice).ToListAsync();
                    var big = await db.BigOrders.CountAsync();
                    return string.Join(";", lines) + "|" + big;
                }
            }
            """;
        var assembly = Compile([.. sources, probe]);

        var run = assembly.GetType("Scaffolded.Probe")!.GetMethod("RunAsync")!;
        var result = await (Task<object>)run.Invoke(null, null)!;

        Assert.Equal("1/1/12.50;2/1/150.00;2/2/250.25|2", result);
    }

    // DDL with generated identifiers (never user input), so raw SQL is safe here.
    private static Task<int> DdlAsync(DbContext db, string sql) => db.Database.ExecuteSqlRawAsync(sql);

    /// <summary>Compiles <paramref name="sources"/> against every assembly the test process can load.</summary>
    private static Assembly Compile(IEnumerable<string> sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "Scaffolded_" + Guid.NewGuid().ToString("N"),
            sources.Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest))),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }
}
