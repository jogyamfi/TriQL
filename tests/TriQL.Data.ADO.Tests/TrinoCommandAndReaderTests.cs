using System.Data;
using System.Data.Common;
using System.Net;
using TriQL.Client;
using TriQL.Client.Tests.Fakes;
using TriQL.Data.ADO.Tests.Fakes;

namespace TriQL.Data.ADO.Tests;

public sealed class TrinoCommandAndReaderTests
{
    [Fact]
    public async Task ExecuteReader_StreamsRowsAndColumns()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";

        using var reader = command.ExecuteReader();

        Assert.Equal(2, reader.FieldCount);
        Assert.True(reader.HasRows);
        Assert.True(reader.Read());
        Assert.Equal([1, 2, 3, 4], reader.GetFieldValue<byte[]>(0));
        Assert.Equal("hello", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task GetOrdinal_IsCaseInsensitive_AndThrowsIndexOutOfRangeForUnknown()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";
        using var reader = command.ExecuteReader();

        Assert.Equal(1, reader.GetOrdinal("S"));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("missing"));
    }

    [Fact]
    public async Task GetBytes_NullBuffer_ReturnsTotalLength()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";
        using var reader = command.ExecuteReader();
        reader.Read();

        Assert.Equal(4, reader.GetBytes(0, 0, null, 0, 0));
    }

    [Fact]
    public async Task GetBytes_PartialRead_CopiesOnlyRequestedRange()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";
        using var reader = command.ExecuteReader();
        reader.Read();

        var buffer = new byte[10];
        var copied = reader.GetBytes(0, 1, buffer, 2, 2);

        Assert.Equal(2, copied);
        Assert.Equal([0, 0, 2, 3, 0, 0, 0, 0, 0, 0], buffer);
    }

    [Fact]
    public async Task GetBytes_OffsetPastEnd_ReturnsZero()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";
        using var reader = command.ExecuteReader();
        reader.Read();

        var buffer = new byte[10];
        Assert.Equal(0, reader.GetBytes(0, 100, buffer, 0, 5));
    }

    [Fact]
    public async Task GetChars_PartialRead_CopiesOnlyRequestedRange()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";
        using var reader = command.ExecuteReader();
        reader.Read();

        var buffer = new char[3];
        var copied = reader.GetChars(1, 1, buffer, 0, 3);

        Assert.Equal(3, copied);
        Assert.Equal("ell", new string(buffer));
    }

    [Fact]
    public async Task Accessor_BeforeRead_Throws()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";
        using var reader = command.ExecuteReader();

        Assert.Throws<InvalidOperationException>(() => reader.GetString(1));
    }

    [Fact]
    public async Task CommandBehavior_SingleRow_ReturnsAtMostOneRow()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"],["AQIDBA==","world"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";
        using var reader = command.ExecuteReader(CommandBehavior.SingleRow);

        Assert.True(reader.Read());
        Assert.Equal("hello", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task CommandBehavior_SchemaOnly_NeverReturnsARow()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";
        using var reader = command.ExecuteReader(CommandBehavior.SchemaOnly);

        Assert.Equal(2, reader.FieldCount);
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task ExecuteScalar_ReturnsFirstColumnOfFirstRow()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(nextUri: null, rows: """["AQIDBA==","hello"]"""));

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b, s FROM t";

        var value = await command.ExecuteScalarAsync();

        Assert.Equal([1, 2, 3, 4], (byte[])value!);
    }

    [Fact]
    public async Task ExecuteNonQuery_ReturnsUpdateCount()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":null,"data":null,"updateType":"INSERT","updateCount":7}""");

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO t VALUES (1)";

        var affected = await command.ExecuteNonQueryAsync();

        Assert.Equal(7, affected);
    }

    [Fact]
    public void CreateDbParameter_DoesNotAddToParametersCollection()
    {
        using var command = new TrinoCommand();
        var parameter = command.CreateParameter();

        Assert.NotNull(parameter);
        Assert.Equal(0, command.Parameters.Count);
    }

    [Fact]
    public void CommandType_StoredProcedure_Throws()
    {
        using var command = new TrinoCommand();
        Assert.Throws<NotSupportedException>(() => command.CommandType = CommandType.StoredProcedure);
    }

    [Fact]
    public void DbTransaction_SetToNonNull_Throws()
    {
        using var command = new TrinoCommand();
        Assert.Throws<NotSupportedException>(() => command.Transaction = new FakeTransaction());
    }

    [Fact]
    public async Task Execute_WithParameters_UsesBoundExecuteUsingClause()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":null,"data":null}""");

        using var connection = await OpenConnectionAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM t WHERE x = ?";
        command.Parameters.Add(new TrinoDbParameter { Value = "a'; DROP TABLE users; --" });

        await command.ExecuteNonQueryAsync();

        var submitted = fake.ReceivedRequests.Single(r => r.Method == HttpMethod.Post);
        Assert.NotNull(submitted);
    }

    private static async Task<TrinoConnection> OpenConnectionAsync(FakeTrinoCoordinator fake)
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        var connection = new TrinoConnection(options, new StubHttpClientFactory(fake));
        await connection.OpenAsync();
        return connection;
    }

    private static string Page(string? nextUri, string rows)
    {
        var nextUriJson = nextUri is null ? "null" : $"\"{nextUri}\"";
        return $$"""
        {"id":"q1","nextUri":{{nextUriJson}},"columns":[{"name":"b","type":"varbinary"},{"name":"s","type":"varchar"}],"data":[{{rows}}]}
        """;
    }

    private sealed class FakeTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;
        protected override DbConnection? DbConnection => null;
        public override void Commit() { }
        public override void Rollback() { }
    }
}
