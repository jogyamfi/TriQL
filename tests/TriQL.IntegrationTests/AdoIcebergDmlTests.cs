using TriQL.Data.ADO;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// EF0-T9: row-level DML affected counts through the ADO.NET provider, against the Iceberg catalog
/// the fixture creates — the connector EF Core's SaveChanges, ExecuteUpdate and ExecuteDelete
/// target. Complements <see cref="AdoDdlDmlTests"/>, whose memory catalog supports only INSERT.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class AdoIcebergDmlTests : IAsyncLifetime, IDisposable
{
    private readonly TrinoContainerFixture _fixture;
    private readonly string _schema = $"{TrinoContainerFixture.IcebergCatalog}.triql_it_{Guid.NewGuid():N}";
    private TrinoConnection _connection = null!;

    public AdoIcebergDmlTests(TrinoContainerFixture fixture) => _fixture = fixture;

    private string Table => $"{_schema}.widgets";

    public async Task InitializeAsync()
    {
        _connection = new TrinoConnection(new TrinoConnectionStringBuilder { Server = _fixture.ServerUri.ToString(), User = "triql-it" }.ConnectionString);
        await _connection.OpenAsync();
        await ExecuteAsync($"CREATE SCHEMA {_schema}");
        await ExecuteAsync($"CREATE TABLE {Table} (id INTEGER, name VARCHAR, version BIGINT)");
        await ExecuteAsync($"INSERT INTO {Table} VALUES (1, 'a', 1), (2, 'b', 1), (3, 'c', 1)");
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync($"DROP TABLE IF EXISTS {Table}");
        await ExecuteAsync($"DROP SCHEMA IF EXISTS {_schema}");
        await _connection.CloseAsync();
    }

    public void Dispose() => _connection.Dispose();

    private async Task<int> ExecuteAsync(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Update_WithMatchingConcurrencyToken_AffectsOneRow_AndAStaleTokenAffectsNone()
    {
        Assert.Equal(1, await ExecuteAsync($"UPDATE {Table} SET name = 'z', version = 2 WHERE id = 1 AND version = 1"));

        // The same statement again: the token is now stale. EF Core turns 0 into DbUpdateConcurrencyException.
        Assert.Equal(0, await ExecuteAsync($"UPDATE {Table} SET name = 'z', version = 2 WHERE id = 1 AND version = 1"));
    }

    [Fact]
    public async Task ParameterizedUpdate_ThroughAReader_ReportsRecordsAffected()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"UPDATE {Table} SET name = @name WHERE id IN (@a, @b)";
        command.Parameters.Add(new TrinoDbParameter { ParameterName = "name", Value = "y" });
        command.Parameters.Add(new TrinoDbParameter { ParameterName = "a", Value = 2 });
        command.Parameters.Add(new TrinoDbParameter { ParameterName = "b", Value = 3 });

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
        }

        Assert.Equal(2, reader.RecordsAffected);
    }

    [Fact]
    public async Task Delete_FilteredBySubquery_AndUnconditional_ReportCounts()
    {
        Assert.Equal(1, await ExecuteAsync($"DELETE FROM {Table} WHERE id IN (SELECT id FROM {Table} WHERE name = 'c')"));

        // An unconditional DELETE still reports its count rather than null (plan risk R4).
        Assert.Equal(2, await ExecuteAsync($"DELETE FROM {Table}"));
    }

    [Fact]
    public async Task Merge_ReportsTheNumberOfRowsItChanged()
    {
        var affected = await ExecuteAsync(
            $"""
            MERGE INTO {Table} t
            USING (VALUES (1, 'm'), (4, 'n')) s(id, name) ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET name = s.name
            WHEN NOT MATCHED THEN INSERT (id, name, version) VALUES (s.id, s.name, 1)
            """);

        Assert.Equal(2, affected);
    }
}
