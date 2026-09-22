using TriQL.Client.Exceptions;
using TriQL.Data.ADO;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// P4-T24: DDL/DML update counts through <see cref="TrinoCommand.ExecuteNonQuery"/> /
/// <see cref="TrinoCommand.ExecuteNonQueryAsync(CancellationToken)"/>, and the ADO.NET contract
/// that a <c>SELECT</c>'s <see cref="System.Data.Common.DbDataReader.RecordsAffected"/> is
/// <c>-1</c> (FR-9.3.6).
/// </summary>
/// <remarks>
/// <see cref="InitializeAsync"/> probes <c>SHOW CATALOGS</c> for a writable catalog (<c>memory</c>,
/// present in the default <c>trinodb/trino</c> image alongside the read-only <c>tpch</c>/<c>tpcds</c>
/// connectors) and the DDL/DML test adapts: when it is present, the test runs a full
/// CREATE SCHEMA/CREATE TABLE/INSERT/DROP flow and asserts every affected-row count; otherwise it
/// falls back to asserting <see cref="TrinoCommand.ExecuteNonQueryAsync(CancellationToken)"/>'s
/// contract against a real no-op session statement, since there is nothing writable to target.
/// <para>
/// UPDATE and DELETE (both a predicated, row-filtered form and an unconditional, whole-table form)
/// are asserted separately (see
/// <see cref="Ddl_UpdateAndDelete_OnAConnectorWithoutRowLevelSupport_ThrowTrinoQueryException"/>):
/// the Memory connector on this image implements none of the three ("This connector does not
/// support modifying table rows" in every case), so there is no writable connector in the default
/// image against which a successful UPDATE or DELETE affected-count can be demonstrated. This is a
/// genuine environment constraint, not a product bug — see the task report for detail.
/// </para>
/// </remarks>
[Collection(TrinoContainerCollection.Name)]
public sealed class AdoDdlDmlTests : IAsyncLifetime, IDisposable
{
    private readonly TrinoContainerFixture _fixture;
    private TrinoConnection _connection = null!;
    private string? _writableCatalog;

    public AdoDdlDmlTests(TrinoContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _connection = new TrinoConnection(new TrinoConnectionStringBuilder { Server = _fixture.ServerUri.ToString() }.ToString());
        await _connection.OpenAsync();
        _writableCatalog = await DetectWritableCatalogAsync(_connection);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // CA1001: the analyzer requires an IDisposable implementation on any type owning a
    // disposable field; xUnit actually drives cleanup through IAsyncLifetime.DisposeAsync above.
    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ExecuteReader_ForASelect_RecordsAffectedIsAlwaysMinusOne()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT nationkey FROM tpch.tiny.nation ORDER BY nationkey LIMIT 5";

        using var reader = await command.ExecuteReaderAsync();

        // FR-9.3.6: -1 for SELECT, both before and after fully reading the result.
        Assert.Equal(-1, reader.RecordsAffected);

        var rowCount = 0;
        while (await reader.ReadAsync())
        {
            rowCount++;
        }

        Assert.Equal(5, rowCount);
        Assert.Equal(-1, reader.RecordsAffected);
    }

    [Fact]
    public void ExecuteReader_Sync_ForASelect_RecordsAffectedIsAlwaysMinusOne()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT regionkey FROM tpch.tiny.region";

        using var reader = command.ExecuteReader();
        Assert.Equal(-1, reader.RecordsAffected);
    }

    [Fact]
    public async Task Ddl_UpdateAndDelete_OnAConnectorWithoutRowLevelSupport_ThrowTrinoQueryException()
    {
        if (_writableCatalog is null)
        {
            return;
        }

        var schemaName = $"triql_it_{Guid.NewGuid():N}";
        var qualifiedTable = $"{_writableCatalog}.{schemaName}.widgets";

        using var createSchema = _connection.CreateCommand();
        createSchema.CommandText = $"CREATE SCHEMA {_writableCatalog}.{schemaName}";
        await createSchema.ExecuteNonQueryAsync();

        try
        {
            using var createTable = _connection.CreateCommand();
            createTable.CommandText = $"CREATE TABLE {qualifiedTable} (id BIGINT, name VARCHAR)";
            await createTable.ExecuteNonQueryAsync();

            using var insert = _connection.CreateCommand();
            insert.CommandText = $"INSERT INTO {qualifiedTable} (id, name) VALUES (1, 'a')";
            await insert.ExecuteNonQueryAsync();

            using (var update = _connection.CreateCommand())
            {
                update.CommandText = $"UPDATE {qualifiedTable} SET name = 'z' WHERE id = 1";
                var ex = await Assert.ThrowsAsync<TrinoQueryException>(() => update.ExecuteNonQueryAsync());
                Assert.Contains("does not support modifying table rows", ex.Message, StringComparison.OrdinalIgnoreCase);
            }

            using (var filteredDelete = _connection.CreateCommand())
            {
                filteredDelete.CommandText = $"DELETE FROM {qualifiedTable} WHERE id = 1";
                var ex = await Assert.ThrowsAsync<TrinoQueryException>(() => filteredDelete.ExecuteNonQueryAsync());
                Assert.Contains("does not support modifying table rows", ex.Message, StringComparison.OrdinalIgnoreCase);
            }

            using (var unconditionalDelete = _connection.CreateCommand())
            {
                unconditionalDelete.CommandText = $"DELETE FROM {qualifiedTable}";
                var ex = await Assert.ThrowsAsync<TrinoQueryException>(() => unconditionalDelete.ExecuteNonQueryAsync());
                Assert.Contains("does not support modifying table rows", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            using var dropTable = _connection.CreateCommand();
            dropTable.CommandText = $"DROP TABLE IF EXISTS {qualifiedTable}";
            await dropTable.ExecuteNonQueryAsync();

            using var dropSchema = _connection.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS {_writableCatalog}.{schemaName}";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Ddl_CreateAndInsert_ReportsCorrectAffectedCounts()
    {
        if (_writableCatalog is null)
        {
            // No writable catalog is available in this environment (see class remarks). Smoke-test
            // ExecuteNonQueryAsync's contract with a genuine, universally valid no-op session
            // statement instead of silently skipping the assertion.
            using var noOpCommand = _connection.CreateCommand();
            noOpCommand.CommandText = "SET SESSION query_max_run_time = '1h'";
            var noOpAffected = await noOpCommand.ExecuteNonQueryAsync();
            Assert.Equal(0, noOpAffected);
            return;
        }

        var schemaName = $"triql_it_{Guid.NewGuid():N}";
        const string tableName = "widgets";
        var qualifiedTable = $"{_writableCatalog}.{schemaName}.{tableName}";

        try
        {
            using (var createSchema = _connection.CreateCommand())
            {
                createSchema.CommandText = $"CREATE SCHEMA {_writableCatalog}.{schemaName}";
                var affected = await createSchema.ExecuteNonQueryAsync();
                Assert.Equal(0, affected);
            }

            using (var createTable = _connection.CreateCommand())
            {
                createTable.CommandText = $"CREATE TABLE {qualifiedTable} (id BIGINT, name VARCHAR)";
                var affected = await createTable.ExecuteNonQueryAsync();
                Assert.Equal(0, affected);
            }

            using (var insert = _connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO {qualifiedTable} (id, name) VALUES (1, 'a'), (2, 'b'), (3, 'c')";
                var affected = await insert.ExecuteNonQueryAsync();
                Assert.Equal(3, affected);
            }

            using (var insertMore = _connection.CreateCommand())
            {
                insertMore.CommandText = $"INSERT INTO {qualifiedTable} (id, name) VALUES (4, 'd')";
                var affected = await insertMore.ExecuteNonQueryAsync();
                Assert.Equal(1, affected);
            }

            using (var verify = _connection.CreateCommand())
            {
                verify.CommandText = $"SELECT count(*) FROM {qualifiedTable}";
                var total = await verify.ExecuteScalarAsync();
                Assert.Equal(4L, total);
            }
        }
        finally
        {
            using var dropTable = _connection.CreateCommand();
            dropTable.CommandText = $"DROP TABLE IF EXISTS {qualifiedTable}";
            await dropTable.ExecuteNonQueryAsync();

            using var dropSchema = _connection.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS {_writableCatalog}.{schemaName}";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void Ddl_ExecuteNonQuery_Sync_WorksThroughTheSyncBridge()
    {
        // Covers the sync ExecuteNonQuery() overload (routed through SyncBridge) with a real,
        // universally valid no-op session statement.
        using var command = _connection.CreateCommand();
        command.CommandText = "SET SESSION query_max_run_time = '2h'";
        var affected = command.ExecuteNonQuery();
        Assert.Equal(0, affected);
    }

    private static async Task<string?> DetectWritableCatalogAsync(TrinoConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SHOW CATALOGS";
        using var reader = await command.ExecuteReaderAsync();

        var catalogs = new List<string>();
        while (await reader.ReadAsync())
        {
            catalogs.Add(reader.GetString(0));
        }

        return catalogs.Contains("memory", StringComparer.OrdinalIgnoreCase) ? "memory" : null;
    }
}
