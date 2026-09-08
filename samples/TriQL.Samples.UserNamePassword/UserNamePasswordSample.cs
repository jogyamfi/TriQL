using System.Data.Common;
using TriQL.Data.ADO;

namespace TriQL.Samples.UserNamePassword;

/// <summary>
/// Connects to the Trino coordinator over ADO.NET using username and password to query
/// </summary>
public static class UserNamePasswordSample
{
    private const string Host = "testhost";
    private const int Port = 443;
    private const string Catalog = "YourCatalog";
    private const string Schema = "YourSchema";
    private const string UserName = "YourUserName";


    private const string Query = "SELECT * FROM YOUR_SCHEMA.YOUR_TABLE LIMIT 1";

    public static async Task Main()
    {
        
        var password= "Your password";


        var connectionString = new TrinoConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            EnableSsl = true,
            Auth = "basic",
            User = UserName,
            Password = password,
            Catalog = Catalog,
            Schema = Schema,
        }.ConnectionString;

        await using DbConnection connection = new TrinoConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = Query;

        await using var reader = await command.ExecuteReaderAsync();

        var columnNames = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName);
        Console.WriteLine(string.Join(" | ", columnNames));

        while (await reader.ReadAsync())
        {
            var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString());
            Console.WriteLine(string.Join(" | ", values));
        }
    }
}
