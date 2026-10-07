using Microsoft.EntityFrameworkCore;
using TriQL.Client;
using TriQL.IntegrationTests.Fixtures;

namespace TriQL.EntityFrameworkCore.FunctionalTests;

/// <summary>
/// EF1-T7: the provider end to end against a real coordinator (the shared Trino container), for
/// what Phase 1 supports: connecting, raw SQL queries and raw SQL DML. Later phases add LINQ and
/// SaveChanges suites alongside.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class RawSqlFunctionalTests(TrinoContainerFixture fixture)
{
    private sealed class Context(DbContextOptions<Context> options) : DbContext(options);

    private Context CreateContext(string? extraKeywords = null, Action<Infrastructure.TrinoDbContextOptionsBuilder>? trino = null) =>
        new(new DbContextOptionsBuilder<Context>()
            .UseTrino($"Server={fixture.ServerUri};User=triql-ef;Catalog=tpch;Schema=tiny;{extraKeywords}", trino)
            .Options);

    [Fact]
    [Trait("Category", "EfRead")]
    public async Task CanConnect_IsTrue()
    {
        await using var context = CreateContext();

        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    [Trait("Category", "EfRead")]
    public async Task SqlQueryRaw_ReadsFromTpch_UsingTheConnectionsCatalogAndSchema()
    {
        await using var context = CreateContext();

        var count = await context.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM nation").SingleAsync();

        Assert.Equal(25L, count);
    }

    [Theory]
    [Trait("Category", "EfRead")]
    [InlineData("")]
    [InlineData("ParameterBinding=ExecuteImmediate")]
    public async Task SqlQuery_Interpolated_BindsParameters_WithEitherBindingMode(string extraKeywords)
    {
        await using var context = CreateContext(extraKeywords);
        var region = 1;
        var prefix = "C%";

        var names = await context.Database
            .SqlQuery<string>($"SELECT name AS \"Value\" FROM nation WHERE regionkey = {region} AND name LIKE {prefix} ORDER BY name")
            .ToListAsync();

        Assert.Equal(["CANADA"], names);
    }

    [Fact]
    [Trait("Category", "EfRead")]
    public async Task SessionTimeZone_DefaultsToUtc_AndAnExplicitZoneIsKept()
    {
        await using var defaulted = CreateContext();
        await using var optedOut = CreateContext(trino: t => t.UseUtcSessionTimeZone(false));
        await using var explicitZone = CreateContext("TimeZone=Asia/Tokyo");

        Assert.Equal("UTC", await CurrentTimeZoneAsync(defaulted));
        Assert.Equal("Asia/Tokyo", await CurrentTimeZoneAsync(explicitZone));
        Assert.Equal(new TrinoSessionOptions().TimeZone, await CurrentTimeZoneAsync(optedOut));

        static Task<string> CurrentTimeZoneAsync(Context context) =>
            context.Database.SqlQueryRaw<string>("SELECT current_timezone() AS \"Value\"").SingleAsync();
    }

    [Fact]
    [Trait("Category", "EfIceberg")]
    public async Task ExecuteSql_RunsParameterizedDml_OnIceberg_AndReturnsRowsAffected()
    {
        await using var context = CreateContext();

        // Identifiers cannot be bound as parameters, so the test-generated schema name is spliced into
        // the SQL text (through these helpers, which EF1002 would otherwise flag); every value is bound.
        var schema = $"{TrinoContainerFixture.IcebergCatalog}.ef_it_{Guid.NewGuid():N}";
        Task<int> ExecuteAsync(string sql, params object[] parameters) => context.Database.ExecuteSqlRawAsync(sql, parameters);
        Task<List<string>> QueryAsync(string sql) => context.Database.SqlQueryRaw<string>(sql).ToListAsync();

        await ExecuteAsync($"CREATE SCHEMA {schema}");
        try
        {
            await ExecuteAsync($"CREATE TABLE {schema}.widgets (id INTEGER, name VARCHAR)");

            var inserted = await ExecuteAsync($"INSERT INTO {schema}.widgets VALUES (@p0, @p1), (@p2, @p3)", 1, "a", 2, "O'Brien");
            var updated = await ExecuteAsync($"UPDATE {schema}.widgets SET name = @p0 WHERE id = @p1", "z", 2);
            var deleted = await ExecuteAsync($"DELETE FROM {schema}.widgets WHERE id = @p0", 99);

            Assert.Equal(2, inserted);
            Assert.Equal(1, updated);
            Assert.Equal(0, deleted);
            Assert.Equal(["a", "z"], await QueryAsync($"SELECT name AS \"Value\" FROM {schema}.widgets ORDER BY id"));
        }
        finally
        {
            await ExecuteAsync($"DROP TABLE IF EXISTS {schema}.widgets");
            await ExecuteAsync($"DROP SCHEMA IF EXISTS {schema}");
        }
    }
}
