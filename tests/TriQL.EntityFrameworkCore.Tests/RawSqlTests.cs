using Microsoft.EntityFrameworkCore;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests;

/// <summary>EF1 exit criteria: raw SQL runs end to end through the provider, the ADO layer and the protocol.</summary>
public sealed class RawSqlTests
{
    [Fact]
    public async Task ExecuteSqlRaw_SendsTheSql_AndReturnsTheUpdateCount()
    {
        using var fake = new FakeTrino();
        await using var context = new EmptyContext(fake.CreateOptions<EmptyContext>());
        fake.EnqueueUpdate("DELETE", 3);

        var affected = await context.Database.ExecuteSqlRawAsync("DELETE FROM widgets WHERE name = 'x'");

        Assert.Equal(3, affected);
        fake.AssertSql("DELETE FROM widgets WHERE name = 'x'");
        Assert.Equal(["DELETE FROM widgets WHERE name = 'x'"], fake.SubmittedBodies);
    }

    [Fact]
    public async Task ExecuteSql_Interpolated_BindsValuesAsParameters_NotIntoTheSql()
    {
        using var fake = new FakeTrino();
        await using var context = new EmptyContext(fake.CreateOptions<EmptyContext>());
        fake.EnqueueUpdate("UPDATE", 1);
        var name = "O'Brien";
        var id = 42;

        var affected = await context.Database.ExecuteSqlAsync($"UPDATE widgets SET name = {name} WHERE id = {id}");

        Assert.Equal(1, affected);
        fake.AssertSql("UPDATE widgets SET name = @p0 WHERE id = @p1");
        var body = Assert.Single(fake.SubmittedBodies);
        Assert.StartsWith("EXECUTE triql_", body, StringComparison.Ordinal);
        Assert.EndsWith(" USING 'O''Brien', 42", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqlQueryRaw_OfInt_MaterializesTheScalarColumn()
    {
        using var fake = new FakeTrino();
        await using var context = new EmptyContext(fake.CreateOptions<EmptyContext>());
        fake.EnqueueRows([("Value", "integer")], [1], [2], [3]);

        var values = await context.Database.SqlQueryRaw<int>("SELECT x AS \"Value\" FROM t").ToListAsync();

        Assert.Equal([1, 2, 3], values);
    }

    [Fact]
    public async Task SqlQueryRaw_OfLong_WidensFromAnIntegerColumn()
    {
        using var fake = new FakeTrino();
        await using var context = new EmptyContext(fake.CreateOptions<EmptyContext>());
        fake.EnqueueRows([("Value", "integer")], [7]);

        var values = await context.Database.SqlQueryRaw<long>("SELECT x AS \"Value\" FROM t").ToListAsync();

        Assert.Equal([7L], values);
    }

    [Fact]
    public async Task SqlQuery_Interpolated_WithAStringParameter()
    {
        using var fake = new FakeTrino();
        await using var context = new EmptyContext(fake.CreateOptions<EmptyContext>());
        fake.EnqueueRows([("Value", "varchar")], ["a"], ["b"]);
        var prefix = "a%";

        var values = await context.Database.SqlQuery<string>($"SELECT name AS \"Value\" FROM t WHERE name LIKE {prefix}").ToListAsync();

        Assert.Equal(["a", "b"], values);
        fake.AssertSql("SELECT name AS \"Value\" FROM t WHERE name LIKE @p0");
        Assert.EndsWith(" USING 'a%'", fake.SubmittedBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanConnect_RunsSelectOne()
    {
        using var fake = new FakeTrino();
        await using var context = new EmptyContext(fake.CreateOptions<EmptyContext>());
        fake.EnqueueRows([("_col0", "integer")], [1]);

        Assert.True(await context.Database.CanConnectAsync());
        fake.AssertSql("SELECT 1");
    }
}
