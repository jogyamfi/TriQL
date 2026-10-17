using System.Transactions;
using Microsoft.EntityFrameworkCore;
using TriQL.Data.ADO;
using TriQL.EntityFrameworkCore.Storage.Internal;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests;

/// <summary>EF1-T5: TrinoRelationalConnection — connection defaults and the no-transactions policy.</summary>
public sealed class ConnectionBehaviourTests
{
    private const string ConnectionString = "Server=https://trino.example.com/;User=test";

    private static EmptyContext CreateContext(string connectionString, bool? useUtc = null) =>
        new(new DbContextOptionsBuilder<EmptyContext>()
            .UseTrino(connectionString, t =>
            {
                if (useUtc is { } value)
                {
                    t.UseUtcSessionTimeZone(value);
                }
            })
            .Options);

    private static string? TimeZoneOf(EmptyContext context) =>
        new TrinoConnectionStringBuilder(context.Database.GetDbConnection().ConnectionString).TimeZone;

    [Fact]
    public void ConnectionString_WithoutTimeZone_DefaultsToUtc()
    {
        using var context = CreateContext(ConnectionString);

        Assert.Equal("UTC", TimeZoneOf(context));
    }

    [Fact]
    public void ConnectionString_WithTimeZone_KeepsIt()
    {
        using var context = CreateContext(ConnectionString + ";TimeZone=Europe/London");

        Assert.Equal("Europe/London", TimeZoneOf(context));
    }

    [Fact]
    public void UseUtcSessionTimeZoneFalse_LeavesTheConnectionStringAlone()
    {
        using var context = CreateContext(ConnectionString, useUtc: false);

        Assert.Null(TimeZoneOf(context));
    }

    [Fact]
    public void ApplyProviderDefaults_PreservesEveryOtherKey()
    {
        var result = new TrinoConnectionStringBuilder(
            TrinoRelationalConnection.ApplyProviderDefaults(ConnectionString + ";Catalog=lake;ParameterBinding=ExecuteImmediate", useUtcSessionTimeZone: true));

        Assert.Equal("lake", result.Catalog);
        Assert.Equal(Client.TrinoParameterBinding.ExecuteImmediate, result.ParameterBinding);
        Assert.Equal("UTC", result.TimeZone);
    }

    [Fact]
    public async Task BeginTransaction_Throws_WithGuidance()
    {
        using var context = CreateContext(ConnectionString);

        var sync = Assert.Throws<NotSupportedException>(() => context.Database.BeginTransaction());
        var async = await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.BeginTransactionAsync());

        Assert.Equal(TrinoRelationalConnection.TransactionsNotSupportedMessage, sync.Message);
        Assert.Contains("SaveChanges is not atomic", async.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseTransaction_WithATransaction_Throws_ButNullIsAllowed()
    {
        using var context = CreateContext(ConnectionString);

        Assert.Null(context.Database.UseTransaction(null));
        Assert.Throws<NotSupportedException>(() => context.Database.EnlistTransaction(new CommittableTransaction()));
    }

    [Fact]
    public void AmbientTransactionScope_Throws_InsteadOfBeingIgnored()
    {
        using var fake = new FakeTrino();
        using var context = new EmptyContext(fake.CreateOptions<EmptyContext>());
        fake.EnqueueRows([("_col0", "integer")], [1]);

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        Assert.Throws<InvalidOperationException>(() => context.Database.ExecuteSqlRaw("SELECT 1"));
    }
}
