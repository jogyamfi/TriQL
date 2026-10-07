using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TriQL.IntegrationTests.Fixtures;

namespace TriQL.EntityFrameworkCore.FunctionalTests;

/// <summary>
/// EF2 live check: every mapped CLR type, at its edge values, survives three paths against a real
/// coordinator — the mapping's SQL literal, an EF parameter, and a write to/read from a table whose
/// column uses the mapping's store type (the memory connector, which keeps exact types, and Iceberg,
/// which widens some).
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class TypeRoundTripFunctionalTests(TrinoContainerFixture fixture)
{
    private sealed class Context(DbContextOptions<Context> options) : DbContext(options);

    /// <summary>(value, expected value read back). The expectation differs only where the store type is coarser than .NET.</summary>
    public static TheoryData<object, object> Values => new()
    {
        { true, true },
        { false, false },
        { sbyte.MinValue, sbyte.MinValue },
        { sbyte.MaxValue, sbyte.MaxValue },
        { byte.MaxValue, byte.MaxValue },
        { short.MinValue, short.MinValue },
        { ushort.MaxValue, ushort.MaxValue },
        { int.MinValue, int.MinValue },
        { uint.MaxValue, uint.MaxValue },
        { long.MinValue, long.MinValue },
        { long.MaxValue, long.MaxValue },
        { ulong.MaxValue, ulong.MaxValue },
        { 0.1f, 0.1f },
        { float.MaxValue, float.MaxValue },
        { float.NaN, float.NaN },
        { float.NegativeInfinity, float.NegativeInfinity },
        { 0.1d, 0.1d },
        { double.Epsilon, double.Epsilon },
        { double.MaxValue, double.MaxValue },
        { double.PositiveInfinity, double.PositiveInfinity },
        { -1.50m, -1.50m },
        { 1234567890123456.78m, 1234567890123456.78m },
        { string.Empty, string.Empty },
        { "it's 'quoted' -- and \\ back", "it's 'quoted' -- and \\ back" },
        { "😀 ünïcödé", "😀 ünïcödé" },
        { 'x', 'x' },
        { '\'', '\'' },
        { Array.Empty<byte>(), Array.Empty<byte>() },
        { new byte[] { 0, 1, 127, 128, 255 }, new byte[] { 0, 1, 127, 128, 255 } },
        { Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e") },
        { DateOnly.MinValue, DateOnly.MinValue },
        { DateOnly.MaxValue, DateOnly.MaxValue },
        { new TimeOnly(0, 0), new TimeOnly(0, 0) },
        { TimeOnly.MaxValue, new TimeOnly(23, 59, 59).Add(TimeSpan.FromTicks(9_999_990)) },
        { DateTime.MinValue, DateTime.MinValue },
        { new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1_234_567), new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1_234_560) },
        { DateTime.MaxValue, DateTime.MaxValue.AddTicks(-9) },
        {
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(1_234_560),
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(1_234_560)
        },
        { TimeSpan.FromDays(-3.25), TimeSpan.FromDays(-3.25) },
        { TimeSpan.MaxValue, TimeSpan.MaxValue },
    };

    private Context CreateContext() =>
        new(new DbContextOptionsBuilder<Context>().UseTrino($"Server={fixture.ServerUri};User=triql-ef").Options);

    [Theory]
    [Trait("Category", "EfRead")]
    [MemberData(nameof(Values))]
    public async Task Literal_RoundTrips(object value, object expected)
    {
        await using var context = CreateContext();
        var literal = Mapping(context, value.GetType()).GenerateSqlLiteral(value);

        var actual = await QueryScalarAsync(context, value.GetType(), $"SELECT {literal} AS \"Value\"");

        AssertEqual(expected, actual);
    }

    [Theory]
    [Trait("Category", "EfRead")]
    [MemberData(nameof(Values))]
    public async Task Parameter_RoundTrips(object value, object expected)
    {
        await using var context = CreateContext();

        var actual = await QueryScalarAsync(context, value.GetType(), "SELECT @p0 AS \"Value\"", value);

        AssertEqual(expected, actual);
    }

    [Theory]
    [Trait("Category", "EfRead")]
    [MemberData(nameof(Values))]
    public Task MemoryTableColumn_RoundTrips(object value, object expected) => TableRoundTripAsync("memory", value, expected, storesUtcInstants: false);

    [Theory]
    [Trait("Category", "EfIceberg")]
    [MemberData(nameof(Values))]
    public Task IcebergTableColumn_RoundTrips(object value, object expected) =>
        TableRoundTripAsync(TrinoContainerFixture.IcebergCatalog, value, expected, storesUtcInstants: true);

    private async Task TableRoundTripAsync(string catalog, object value, object expected, bool storesUtcInstants)
    {
        await using var context = CreateContext();
        var schema = $"{catalog}.ef_types_{Guid.NewGuid():N}";
        var storeType = Mapping(context, value.GetType()).StoreType;

        await ExecuteAsync(context, $"CREATE SCHEMA {schema}");
        try
        {
            await ExecuteAsync(context, $"CREATE TABLE {schema}.t (v {storeType})");
            await ExecuteAsync(context, $"INSERT INTO {schema}.t VALUES (@p0)", value);

            var actual = await QueryScalarAsync(context, value.GetType(), $"SELECT v AS \"Value\" FROM {schema}.t");

            // Iceberg stores timestamp with time zone as a UTC instant: same instant, UTC offset.
            AssertEqual(storesUtcInstants && expected is DateTimeOffset dto ? dto.ToUniversalTime() : expected, actual);
        }
        finally
        {
            await ExecuteAsync(context, $"DROP TABLE IF EXISTS {schema}.t");
            await ExecuteAsync(context, $"DROP SCHEMA IF EXISTS {schema}");
        }
    }

    private static RelationalTypeMapping Mapping(Context context, Type clrType) =>
        context.GetService<IRelationalTypeMappingSource>().FindMapping(clrType)
        ?? throw new InvalidOperationException($"No mapping for {clrType}.");

    // Identifiers and generated literals are spliced into the SQL text by design here (EF1002 would
    // flag the direct call); values under test are bound as parameters where the test says so.
    private static Task<int> ExecuteAsync(Context context, string sql, params object[] parameters) =>
        context.Database.ExecuteSqlRawAsync(sql, parameters);

    private static async Task<object?> QueryScalarAsync(Context context, Type clrType, string sql, params object[] parameters)
    {
        var method = typeof(TypeRoundTripFunctionalTests)
            .GetMethod(nameof(QueryScalarCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(clrType);
        var task = (Task)method.Invoke(null, [context, sql, parameters])!;
        await task;
        return task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task);
    }

    private static async Task<T> QueryScalarCoreAsync<T>(Context context, string sql, object[] parameters) =>
        await context.Database.SqlQueryRaw<T>(sql, parameters).SingleAsync();

    private static void AssertEqual(object expected, object? actual)
    {
        Assert.NotNull(actual);
        Assert.IsType(expected.GetType(), actual);
        switch (expected)
        {
            case DateTimeOffset dto:
                // Equal instant and equal offset (DateTimeOffset equality compares instants only).
                Assert.Equal(dto, (DateTimeOffset)actual);
                Assert.Equal(dto.Offset, ((DateTimeOffset)actual).Offset);
                break;
            default:
                Assert.Equal(expected, actual);
                break;
        }
    }
}
