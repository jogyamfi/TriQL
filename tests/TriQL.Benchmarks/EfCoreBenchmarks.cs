using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TriQL.Data.ADO;

namespace TriQL.Benchmarks;

/// <summary>
/// EF7-T6: EF Core provider costs against a live coordinator. The server comes from the
/// <c>TRIQL_BENCH_SERVER</c> environment variable (default <c>http://localhost:8080/</c>), for example a
/// local <c>trinodb/trino</c> container; the benchmarks only use the built-in <c>memory</c> and <c>tpch</c>
/// catalogs. Every figure includes the HTTP round trips, which is the point: they show what the provider
/// saves or adds per statement and per row.
/// </summary>
internal static class EfBenchmarkServer
{
    public static string ConnectionString(string catalog, string schema) =>
        $"Server={Environment.GetEnvironmentVariable("TRIQL_BENCH_SERVER") ?? "http://localhost:8080/"};User=triql-bench;Catalog={catalog};Schema={schema}";
}

/// <summary>
/// <c>SaveChanges</c> of 1/100/1000 new rows, with consecutive inserts combined into multi-row
/// <c>INSERT</c>s (the default) and without (<c>MaxBatchSize(1)</c>: one statement per row). Writes go to
/// the <c>memory</c> catalog, so the cost is the statement round trips, not an Iceberg commit per statement
/// (which makes the uncombined case slower still).
/// </summary>
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5)]
public class EfSaveChangesBenchmarks
{
    private static long _nextId;
    private readonly string _schema = $"bench_{Guid.NewGuid():N}";
    private DbContextOptions<BenchContext> _options = null!;

    [Params(1, 100, 1000)]
    public int Rows { get; set; }

    [Params(true, false)]
    public bool CombineInserts { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var connectionString = EfBenchmarkServer.ConnectionString("memory", _schema);
        _options = new DbContextOptionsBuilder<BenchContext>()
            .UseTrino(connectionString, o => o.MaxBatchSize(CombineInserts ? 1000 : 1))
            .Options;

        using var db = new BenchContext(_options);
        // A generated identifier, never user input.
        var createSchema = "CREATE SCHEMA IF NOT EXISTS memory." + _schema;
        db.Database.ExecuteSqlRaw(createSchema);
        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS \"Items\" (\"Id\" bigint, \"Name\" varchar, \"Score\" double)");
    }

    [Benchmark]
    public int SaveChanges()
    {
        using var db = new BenchContext(_options);
        for (var i = 0; i < Rows; i++)
        {
            var id = Interlocked.Increment(ref _nextId);
            db.Items.Add(new BenchItem { Id = id, Name = "item " + id, Score = id * 0.5 });
        }

        return db.SaveChanges();
    }
}

/// <summary>
/// The cost of a context's connection: a tiny query through a new context each time (a new HTTP connection,
/// with its TCP handshake), through a pooled context (the connection is reused), and on one long-lived
/// context.
/// </summary>
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class EfConnectionBenchmarks : IDisposable
{
    private DbContextOptions<TpchContext> _options = null!;
    private PooledDbContextFactory<TpchContext> _pool = null!;
    private TpchContext _longLived = null!;

    [GlobalSetup]
    public void Setup()
    {
        _options = new DbContextOptionsBuilder<TpchContext>().UseTrino(EfBenchmarkServer.ConnectionString("tpch", "tiny")).Options;
        _pool = new PooledDbContextFactory<TpchContext>(_options);
        _longLived = new TpchContext(_options);
    }

    [GlobalCleanup]
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    // BenchmarkDotNet derives from benchmark classes, so this one cannot be sealed.
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _longLived?.Dispose();
        }
    }

    [Benchmark(Baseline = true)]
    public int NewContextPerQuery()
    {
        using var db = new TpchContext(_options);
        return db.Regions.Count();
    }

    [Benchmark]
    public int PooledContext()
    {
        using var db = _pool.CreateDbContext();
        return db.Regions.Count();
    }

    [Benchmark]
    public int LongLivedContext() => _longLived.Regions.Count();
}

/// <summary>
/// Materialising <c>tpch.tiny.orders</c> (15,000 rows, 5 columns) through EF (tracked and no-tracking)
/// compared with reading the same query through <see cref="TrinoDataReader"/> into the same objects.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 2, iterationCount: 8)]
public class EfMaterializationBenchmarks
{
    private const string Sql = "SELECT orderkey, custkey, orderstatus, totalprice, orderdate FROM orders";
    private string _connectionString = null!;
    private DbContextOptions<TpchContext> _options = null!;

    [GlobalSetup]
    public void Setup()
    {
        _connectionString = EfBenchmarkServer.ConnectionString("tpch", "tiny");
        _options = new DbContextOptionsBuilder<TpchContext>().UseTrino(_connectionString).Options;
    }

    [Benchmark(Baseline = true)]
    public int RawDataReader()
    {
        using var connection = new TrinoConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Sql;
        using var reader = command.ExecuteReader();
        var orders = new List<TpchOrder>();
        while (reader.Read())
        {
            orders.Add(new TpchOrder
            {
                OrderKey = reader.GetInt64(0),
                CustKey = reader.GetInt64(1),
                OrderStatus = reader.GetString(2),
                TotalPrice = reader.GetDouble(3),
                OrderDate = reader.GetFieldValue<DateOnly>(4),
            });
        }

        return orders.Count;
    }

    [Benchmark]
    public int EfNoTracking()
    {
        using var db = new TpchContext(_options);
        return db.Orders.AsNoTracking().ToList().Count;
    }

    [Benchmark]
    public int EfTracking()
    {
        using var db = new TpchContext(_options);
        return db.Orders.ToList().Count;
    }
}

public sealed class BenchContext(DbContextOptions<BenchContext> options) : DbContext(options)
{
    public DbSet<BenchItem> Items => Set<BenchItem>();
}

public sealed class BenchItem
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Score { get; set; }
}

public sealed class TpchContext(DbContextOptions<TpchContext> options) : DbContext(options)
{
    public DbSet<TpchRegion> Regions => Set<TpchRegion>();

    public DbSet<TpchOrder> Orders => Set<TpchOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Trino identifiers are case-insensitive, so the PascalCase names match tpch's lower-case columns.
        modelBuilder.Entity<TpchRegion>().ToTable("region").HasKey(r => r.RegionKey);
        modelBuilder.Entity<TpchOrder>().ToTable("orders").HasKey(o => o.OrderKey);
    }
}

public sealed class TpchRegion
{
    public long RegionKey { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class TpchOrder
{
    public long OrderKey { get; set; }

    public long CustKey { get; set; }

    public string OrderStatus { get; set; } = string.Empty;

    public double TotalPrice { get; set; }

    public DateOnly OrderDate { get; set; }
}
