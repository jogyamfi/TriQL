using Microsoft.EntityFrameworkCore;
using TriQL.IntegrationTests.Fixtures;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>The model the function-translation suite runs against: one row type with a column of every translated type.</summary>
public sealed class FunctionsContext(DbContextOptions<FunctionsContext> options) : DbContext(options)
{
    public DbSet<FunctionRow> Rows => Set<FunctionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FunctionRow>(b =>
        {
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Amount).HasPrecision(10, 3);
        });
    }
}

public sealed class FunctionRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Note { get; set; }

    public string? Json { get; set; }

    public int Count { get; set; }

    public int? Rating { get; set; }

    public double Ratio { get; set; }

    public decimal Amount { get; set; }

    public bool Flag { get; set; }

    public bool? MaybeFlag { get; set; }

    public DateTime Created { get; set; }

    public DateTimeOffset Stamp { get; set; }

    public DateOnly Day { get; set; }

    public TimeOnly Time { get; set; }

    public Guid Key { get; set; }

    public byte[] Data { get; set; } = [];
}

/// <summary>
/// The seeded rows for the function suite: deterministic in-memory data (the LINQ-to-Objects oracle),
/// loaded once per test run into a fresh schema of the container's <c>memory</c> catalog. The values are
/// the edge cases the translations must get right: <c>LIKE</c> metacharacters, surrounding white space,
/// empty and <see langword="null"/> strings, rounding midpoints (2.5, -2.5, 0.125), a Sunday, a leap day,
/// and a non-UTC offset.
/// </summary>
public static class FunctionsDatabase
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _schema;

    public static IReadOnlyList<FunctionRow> Rows { get; } =
    [
        new()
        {
            Id = 1, Name = "Alpha", Note = "  padded\t", Json = "{\"a\":{\"b\":5}}", Count = 2, Rating = 5, Ratio = 2.5, Amount = 2.5m,
            Flag = true, MaybeFlag = true, Created = new DateTime(2024, 2, 29, 13, 45, 30, 123), Stamp = new DateTimeOffset(2026, 3, 1, 1, 30, 0, TimeSpan.FromHours(2)),
            Day = new DateOnly(2026, 10, 4), Time = new TimeOnly(23, 59, 59, 999), Key = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Data = [1, 2, 3],
        },
        new()
        {
            Id = 2, Name = "50%_off\\deal", Note = null, Json = "{\"a\":[1,2]}", Count = 0, Rating = null, Ratio = -2.5, Amount = -2.5m,
            Flag = false, MaybeFlag = null, Created = new DateTime(2025, 12, 31, 23, 59, 59), Stamp = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero),
            Day = new DateOnly(2025, 1, 1), Time = new TimeOnly(0, 0), Key = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"), Data = [],
        },
        new()
        {
            Id = 3, Name = string.Empty, Note = string.Empty, Json = null, Count = 7, Rating = 0, Ratio = 3.5, Amount = 0.125m,
            Flag = true, MaybeFlag = false, Created = new DateTime(2026, 1, 1), Stamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-5)),
            Day = new DateOnly(2024, 2, 29), Time = new TimeOnly(12, 30, 15, 250), Key = Guid.Parse("a8098c1a-f86e-11da-bd1a-00112444be1e"), Data = [255],
        },
        new()
        {
            Id = 4, Name = "  both ends  ", Note = "   ", Json = "{\"a\":{\"b\":\"text\"}}", Count = -3, Rating = 2, Ratio = 0.4, Amount = 1.375m,
            Flag = false, MaybeFlag = true, Created = new DateTime(2023, 7, 9, 6, 5, 4), Stamp = new DateTimeOffset(2023, 7, 9, 6, 5, 4, TimeSpan.FromMinutes(330)),
            Day = new DateOnly(2023, 7, 9), Time = new TimeOnly(6, 5, 4), Key = Guid.Parse("16fd2706-8baf-433b-82eb-8c7fada847da"), Data = [0, 0],
        },
        new()
        {
            Id = 5, Name = "xxTrimxx", Note = "Ünïcödé text", Json = "not json", Count = 12, Rating = 5, Ratio = 1e10, Amount = -0.05m,
            Flag = true, MaybeFlag = null, Created = new DateTime(2000, 1, 2, 0, 0, 0, 1), Stamp = new DateTimeOffset(2000, 1, 2, 0, 0, 0, TimeSpan.Zero),
            Day = new DateOnly(2000, 1, 2), Time = new TimeOnly(1, 2, 3), Key = Guid.Parse("886313e1-3b8a-5372-9b90-0c9aee199e5d"), Data = [9, 8, 7, 6],
        },
    ];

    /// <summary>Creates and seeds the schema on first use; returns a connection string scoped to it.</summary>
    public static async Task<string> GetConnectionStringAsync(TrinoContainerFixture fixture)
    {
        await Gate.WaitAsync();
        try
        {
            if (_schema is null)
            {
                var schema = $"ef_functions_{Guid.NewGuid():N}";
                await SeedAsync(ConnectionString(fixture, schema), schema);
                _schema = schema;
            }

            return ConnectionString(fixture, _schema);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string ConnectionString(TrinoContainerFixture fixture, string schema) =>
        $"Server={fixture.ServerUri};User=triql-ef;Catalog=memory;Schema={schema}";

    private static async Task SeedAsync(string connectionString, string schema)
    {
        await using var context = new FunctionsContext(new DbContextOptionsBuilder<FunctionsContext>().UseTrino(connectionString).Options);

        // Identifiers are generated here, not user input; every value is a bound parameter.
        Task ExecuteAsync(string sql, params object?[] parameters) => context.Database.ExecuteSqlRawAsync(sql, parameters!);

        await ExecuteAsync($"CREATE SCHEMA memory.{schema}");

        var table = Assert.Single(context.Model.GetRelationalModel().Tables);
        var columns = table.Columns.ToList();
        await ExecuteAsync(
            $"CREATE TABLE \"{table.Name}\" ({string.Join(", ", columns.Select(c => $"\"{c.Name}\" {c.StoreType}"))})");

        var insert = $"INSERT INTO \"{table.Name}\" ({string.Join(", ", columns.Select(c => $"\"{c.Name}\""))}) "
            + $"VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})";
        foreach (var row in Rows)
        {
            var values = columns.Select(c => typeof(FunctionRow).GetProperty(c.Name)!.GetValue(row)).ToArray();
            await ExecuteAsync(insert, values!);
        }
    }
}
