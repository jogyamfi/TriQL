using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TriQL.Client;
using TriQL.Data.ADO;
using TriQL.EntityFrameworkCore.Infrastructure;
using TriQL.EntityFrameworkCore.Infrastructure.Internal;

// Extension methods live in EF's namespace so UseTrino is discoverable next to UseSqlServer etc.
namespace Microsoft.EntityFrameworkCore;

/// <summary>Trino-specific extension methods for <see cref="DbContextOptionsBuilder"/>.</summary>
public static class TrinoDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures the context to connect to Trino. The connection string must be set later, for
    /// example with <c>context.Database.SetConnectionString</c>.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="trinoOptionsAction">An optional action to allow additional Trino-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseTrino(
        this DbContextOptionsBuilder optionsBuilder,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(GetOrCreateExtension(optionsBuilder));
        return ApplyConfiguration(optionsBuilder, trinoOptionsAction);
    }

    /// <summary>Configures the context to connect to Trino with a TriQL connection string.</summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connectionString">
    /// The TriQL connection string, e.g. <c>Server=https://trino.example.com;Catalog=lake;Schema=sales;User=analyst</c>.
    /// </param>
    /// <param name="trinoOptionsAction">An optional action to allow additional Trino-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseTrino(
        this DbContextOptionsBuilder optionsBuilder,
        string? connectionString,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var extension = (TrinoOptionsExtension)GetOrCreateExtension(optionsBuilder).WithConnectionString(connectionString);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return ApplyConfiguration(optionsBuilder, trinoOptionsAction);
    }

    /// <summary>
    /// Configures the context to use an existing <see cref="DbConnection"/>, which must be a
    /// <see cref="TrinoConnection"/>. The context does not dispose it.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">The connection to use.</param>
    /// <param name="trinoOptionsAction">An optional action to allow additional Trino-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseTrino(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null) =>
        UseTrino(optionsBuilder, connection, contextOwnsConnection: false, trinoOptionsAction);

    /// <summary>
    /// Configures the context to use an existing <see cref="DbConnection"/>, which must be a
    /// <see cref="TrinoConnection"/>.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">The connection to use.</param>
    /// <param name="contextOwnsConnection"><see langword="true"/> to dispose the connection when the context is disposed.</param>
    /// <param name="trinoOptionsAction">An optional action to allow additional Trino-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseTrino(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(connection);

        var extension = (TrinoOptionsExtension)GetOrCreateExtension(optionsBuilder).WithConnection(connection, contextOwnsConnection);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return ApplyConfiguration(optionsBuilder, trinoOptionsAction);
    }

    /// <summary>Configures the context to connect to Trino with session options built in code.</summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="sessionOptions">The session options (server, authentication, catalog, schema, …). Used as given, including <see cref="TrinoSessionOptions.TimeZone"/>.</param>
    /// <param name="trinoOptionsAction">An optional action to allow additional Trino-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseTrino(
        this DbContextOptionsBuilder optionsBuilder,
        TrinoSessionOptions sessionOptions,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(sessionOptions);

        var extension = GetOrCreateExtension(optionsBuilder).WithSessionOptions(sessionOptions);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return ApplyConfiguration(optionsBuilder, trinoOptionsAction);
    }

    /// <summary>
    /// Configures the context to create its connections from <paramref name="dataSource"/>. Every
    /// context configured with the same data source shares its HTTP connection pool. The context
    /// does not dispose the data source.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="dataSource">The data source to create connections from.</param>
    /// <param name="trinoOptionsAction">An optional action to allow additional Trino-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseTrino(
        this DbContextOptionsBuilder optionsBuilder,
        TrinoDataSource dataSource,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(dataSource);

        var extension = GetOrCreateExtension(optionsBuilder).WithDataSource(dataSource);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return ApplyConfiguration(optionsBuilder, trinoOptionsAction);
    }

    /// <inheritdoc cref="UseTrino(DbContextOptionsBuilder, Action{TrinoDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseTrino<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseTrino((DbContextOptionsBuilder)optionsBuilder, trinoOptionsAction);

    /// <inheritdoc cref="UseTrino(DbContextOptionsBuilder, string?, Action{TrinoDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseTrino<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string? connectionString,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseTrino((DbContextOptionsBuilder)optionsBuilder, connectionString, trinoOptionsAction);

    /// <inheritdoc cref="UseTrino(DbContextOptionsBuilder, DbConnection, Action{TrinoDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseTrino<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseTrino((DbContextOptionsBuilder)optionsBuilder, connection, trinoOptionsAction);

    /// <inheritdoc cref="UseTrino(DbContextOptionsBuilder, DbConnection, bool, Action{TrinoDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseTrino<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseTrino(
            (DbContextOptionsBuilder)optionsBuilder, connection, contextOwnsConnection, trinoOptionsAction);

    /// <inheritdoc cref="UseTrino(DbContextOptionsBuilder, TrinoSessionOptions, Action{TrinoDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseTrino<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        TrinoSessionOptions sessionOptions,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseTrino((DbContextOptionsBuilder)optionsBuilder, sessionOptions, trinoOptionsAction);

    /// <inheritdoc cref="UseTrino(DbContextOptionsBuilder, TrinoDataSource, Action{TrinoDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseTrino<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        TrinoDataSource dataSource,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseTrino((DbContextOptionsBuilder)optionsBuilder, dataSource, trinoOptionsAction);

    private static TrinoOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.Options.FindExtension<TrinoOptionsExtension>() ?? new TrinoOptionsExtension();

    private static DbContextOptionsBuilder ApplyConfiguration(
        DbContextOptionsBuilder optionsBuilder,
        Action<TrinoDbContextOptionsBuilder>? trinoOptionsAction)
    {
        // Trino cannot take part in System.Transactions; fail instead of silently ignoring a scope.
        var coreOptionsExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>() ?? new CoreOptionsExtension();
        coreOptionsExtension = coreOptionsExtension.WithWarningsConfiguration(
            coreOptionsExtension.WarningsConfiguration.TryWithExplicit(
                RelationalEventId.AmbientTransactionWarning, WarningBehavior.Throw));
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);

        trinoOptionsAction?.Invoke(new TrinoDbContextOptionsBuilder(optionsBuilder));
        return optionsBuilder;
    }
}
