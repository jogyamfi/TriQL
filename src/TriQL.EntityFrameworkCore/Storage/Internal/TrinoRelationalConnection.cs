using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using TriQL.Data.ADO;
using TriQL.EntityFrameworkCore.Infrastructure.Internal;
using Transaction = System.Transactions.Transaction;

namespace TriQL.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// EF Core's connection to Trino over a <see cref="TrinoConnection"/>. The provider does not use
/// Trino transactions (most connectors, Iceberg included, only support autocommit writes), so every
/// way of starting or joining one throws <see cref="NotSupportedException"/>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoRelationalConnection : RelationalConnection
{
    internal const string TransactionsNotSupportedMessage =
        "The Trino EF Core provider does not support transactions: it cannot begin, use or enlist in one. "
        + "Each statement commits on its own, so SaveChanges is not atomic across the statements it executes.";

    private readonly TrinoOptionsExtension? _extension;

    /// <summary>Initializes a new instance.</summary>
    public TrinoRelationalConnection(RelationalConnectionDependencies dependencies)
        : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _extension = dependencies.ContextOptions.FindExtension<TrinoOptionsExtension>();
    }

    /// <inheritdoc />
    protected override DbConnection CreateDbConnection()
    {
        if (_extension?.DataSource is { } dataSource)
        {
            return dataSource.CreateConnection();
        }

        if (_extension?.SessionOptions is { } sessionOptions)
        {
            return new TrinoConnection(sessionOptions);
        }

        return new TrinoConnection(ApplyProviderDefaults(GetValidatedConnectionString(), _extension?.UseUtcSessionTimeZone ?? true));
    }

    /// <summary>
    /// Applies the provider's connection-string defaults: a <c>UTC</c> session time zone unless the
    /// connection string sets <c>TimeZone</c> or <paramref name="useUtcSessionTimeZone"/> is off.
    /// </summary>
    internal static string ApplyProviderDefaults(string connectionString, bool useUtcSessionTimeZone)
    {
        var builder = new TrinoConnectionStringBuilder(connectionString);
        if (!useUtcSessionTimeZone || builder.TimeZone is not null)
        {
            return connectionString;
        }

        builder.TimeZone = "UTC";
        return builder.ConnectionString;
    }

    /// <summary>Always throws: the Trino provider does not support transactions.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override IDbContextTransaction BeginTransaction() => throw TransactionsNotSupported();

    /// <summary>Always throws: the Trino provider does not support transactions.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override IDbContextTransaction BeginTransaction(IsolationLevel isolationLevel) => throw TransactionsNotSupported();

    /// <summary>Always throws: the Trino provider does not support transactions.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        throw TransactionsNotSupported();

    /// <summary>Always throws: the Trino provider does not support transactions.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default) =>
        throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override IDbContextTransaction? UseTransaction(DbTransaction? transaction) =>
        transaction is null ? base.UseTransaction(transaction) : throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override IDbContextTransaction? UseTransaction(DbTransaction? transaction, Guid transactionId) =>
        transaction is null ? base.UseTransaction(transaction, transactionId) : throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override Task<IDbContextTransaction?> UseTransactionAsync(
        DbTransaction? transaction,
        CancellationToken cancellationToken = default) =>
        transaction is null ? base.UseTransactionAsync(transaction, cancellationToken) : throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override Task<IDbContextTransaction?> UseTransactionAsync(
        DbTransaction? transaction,
        Guid transactionId,
        CancellationToken cancellationToken = default) =>
        transaction is null
            ? base.UseTransactionAsync(transaction, transactionId, cancellationToken)
            : throw TransactionsNotSupported();

    /// <summary>Throws unless <paramref name="transaction"/> is <see langword="null"/>, which clears nothing.</summary>
    /// <exception cref="NotSupportedException">A transaction is supplied.</exception>
    public override void EnlistTransaction(Transaction? transaction)
    {
        if (transaction is not null)
        {
            throw TransactionsNotSupported();
        }

        base.EnlistTransaction(transaction);
    }

    private static NotSupportedException TransactionsNotSupported() => new(TransactionsNotSupportedMessage);
}
