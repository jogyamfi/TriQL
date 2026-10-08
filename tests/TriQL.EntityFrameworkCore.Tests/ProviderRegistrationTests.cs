using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TriQL.Client;
using TriQL.Data.ADO;
using TriQL.EntityFrameworkCore.Infrastructure.Internal;
using TriQL.EntityFrameworkCore.Storage.Internal;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests;

/// <summary>EF1-T3/EF1-T4: UseTrino overloads, the options extension, and service registration.</summary>
public sealed class ProviderRegistrationTests
{
    private const string ConnectionString = "Server=https://trino.example.com/;User=test;Catalog=lake;Schema=sales";

    private static TrinoOptionsExtension Extension(DbContextOptions options) =>
        options.FindExtension<TrinoOptionsExtension>() ?? throw new InvalidOperationException("No Trino extension.");

    [Fact]
    public void UseTrino_WithConnectionString_BuildsAWorkingServiceProvider()
    {
        using var context = new EmptyContext(new DbContextOptionsBuilder<EmptyContext>().UseTrino(ConnectionString).Options);

        Assert.True(context.Database.IsTrino());
        Assert.True(context.Database.IsRelational());
        Assert.IsType<TrinoRelationalConnection>(context.GetService<IRelationalConnection>());
        Assert.IsType<TrinoSqlGenerationHelper>(context.GetService<ISqlGenerationHelper>());
        Assert.IsType<TrinoTypeMappingSource>(context.GetService<IRelationalTypeMappingSource>());
    }

    [Fact]
    public void UseTrino_WithAModel_BuildsTheModel()
    {
        using var context = new WidgetContext(new DbContextOptionsBuilder<WidgetContext>().UseTrino(ConnectionString).Options);

        var entityType = context.Model.FindEntityType(typeof(Widget));

        Assert.NotNull(entityType);
        Assert.Equal("Widgets", entityType.GetTableName());
        Assert.Equal("integer", entityType.FindProperty(nameof(Widget.Id))!.GetColumnType());
        Assert.Equal("varchar", entityType.FindProperty(nameof(Widget.Name))!.GetColumnType());
    }

    [Fact]
    public void UseTrino_WithoutConnection_ThenSetConnectionString_Works()
    {
        using var context = new EmptyContext(new DbContextOptionsBuilder<EmptyContext>().UseTrino().Options);

        context.Database.SetConnectionString(ConnectionString);

        Assert.Equal("https://trino.example.com/", context.Database.GetDbConnection().DataSource);
    }

    [Fact]
    public void UseTrino_WithSessionOptions_ConnectsWithThoseOptions()
    {
        var sessionOptions = new TrinoSessionOptions { Server = new Uri("https://options.example.com/"), Schema = "s" };
        var options = new DbContextOptionsBuilder<EmptyContext>().UseTrino(sessionOptions).Options;

        using var context = new EmptyContext(options);

        Assert.Same(sessionOptions, Extension(options).SessionOptions);
        Assert.Equal("https://options.example.com/", context.Database.GetDbConnection().DataSource);
        Assert.Equal("s", context.Database.GetDbConnection().Database);
    }

    [Fact]
    public void UseTrino_WithDataSource_CreatesConnectionsFromIt()
    {
        using var dataSource = new TrinoDataSource(ConnectionString);
        var options = new DbContextOptionsBuilder<EmptyContext>().UseTrino(dataSource).Options;

        using var context = new EmptyContext(options);

        Assert.Same(dataSource, Extension(options).DataSource);
        Assert.Equal(ConnectionString, context.Database.GetDbConnection().ConnectionString);
    }

    [Fact]
    public void UseTrino_WithConnection_UsesThatConnection()
    {
        using var connection = new TrinoConnection(ConnectionString);
        using var context = new EmptyContext(new DbContextOptionsBuilder<EmptyContext>().UseTrino(connection).Options);

        Assert.Same(connection, context.Database.GetDbConnection());
    }

    [Fact]
    public void UseTrino_CalledAgain_ReplacesTheEarlierConnectionSettings()
    {
        using var connection = new TrinoConnection(ConnectionString);
        using var dataSource = new TrinoDataSource(ConnectionString);
        var builder = new DbContextOptionsBuilder<EmptyContext>();

        builder.UseTrino(connection);
        builder.UseTrino(new TrinoSessionOptions { Server = new Uri("https://options.example.com/") });
        builder.UseTrino(dataSource);
        builder.UseTrino("Server=https://last.example.com/;User=test");

        var extension = Extension(builder.Options);
        Assert.Equal("Server=https://last.example.com/;User=test", extension.ConnectionString);
        Assert.Null(extension.Connection);
        Assert.Null(extension.SessionOptions);
        Assert.Null(extension.DataSource);

        builder.UseTrino(connection);
        extension = Extension(builder.Options);
        Assert.Same(connection, extension.Connection);
        Assert.Null(extension.ConnectionString);
    }

    [Fact]
    public void OptionsExtension_EveryWithConnectionOverload_ClearsTheOtherConnectionSettings()
    {
        using var connection = new TrinoConnection(ConnectionString);
        var withString = (TrinoOptionsExtension)new TrinoOptionsExtension().WithConnectionString(ConnectionString);

        var oneArgument = (TrinoOptionsExtension)withString.WithConnection(connection);
        var twoArguments = (TrinoOptionsExtension)withString.WithConnection(connection, owned: true);
        var backToString = (TrinoOptionsExtension)twoArguments.WithConnectionString(ConnectionString);

        Assert.Same(connection, oneArgument.Connection);
        Assert.Null(oneArgument.ConnectionString);
        Assert.Same(connection, twoArguments.Connection);
        Assert.True(twoArguments.IsConnectionOwned);
        Assert.Null(twoArguments.ConnectionString);
        Assert.Equal(ConnectionString, backToString.ConnectionString);
        Assert.Null(backToString.Connection);
    }

    [Fact]
    public void RelationalOptions_SetThroughTheTrinoBuilder_AreKept()
    {
        var options = new DbContextOptionsBuilder<EmptyContext>()
            .UseTrino(ConnectionString, t => t.CommandTimeout(120).UseUtcSessionTimeZone(false))
            .Options;

        var extension = Extension(options);
        Assert.Equal(120, extension.CommandTimeout);
        Assert.False(extension.UseUtcSessionTimeZone);
    }

    [Fact]
    public void LogFragment_NeverContainsTheConnectionString()
    {
        var options = new DbContextOptionsBuilder<EmptyContext>()
            .UseTrino("Server=https://trino.example.com/;User=test;Password=hunter2", t => t.UseUtcSessionTimeZone(false))
            .Options;

        var fragment = Extension(options).Info.LogFragment;

        Assert.DoesNotContain("hunter2", fragment, StringComparison.Ordinal);
        Assert.Contains("UseUtcSessionTimeZone=False", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void AddEntityFrameworkTrino_WithAnExternalServiceProvider_Works()
    {
        var serviceProvider = new ServiceCollection().AddEntityFrameworkTrino().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<EmptyContext>()
            .UseInternalServiceProvider(serviceProvider)
            .UseTrino(ConnectionString)
            .Options;

        using var context = new EmptyContext(options);

        Assert.True(context.Database.IsTrino());
    }
}
