using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using TriQL.EntityFrameworkCore.Diagnostics.Internal;
using TriQL.EntityFrameworkCore.Infrastructure.Internal;
using TriQL.EntityFrameworkCore.Metadata.Conventions;
using TriQL.EntityFrameworkCore.Migrations.Internal;
using TriQL.EntityFrameworkCore.Query.Internal;
using TriQL.EntityFrameworkCore.Storage.Internal;
using TriQL.EntityFrameworkCore.Update.Internal;
using TriQL.EntityFrameworkCore.ValueGeneration.Internal;

// Lives in the DI namespace, next to the other AddEntityFramework* provider registrations.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Trino-specific extension methods for <see cref="IServiceCollection"/>.</summary>
public static class TrinoServiceCollectionExtensions
{
    /// <summary>Registers the services EF Core needs to use the Trino provider.</summary>
    /// <remarks>
    /// Applications do not normally call this: <c>UseTrino</c> registers the provider in EF's internal
    /// service provider. Call it only when supplying your own internal service provider via
    /// <c>DbContextOptionsBuilder.UseInternalServiceProvider</c>.
    /// </remarks>
    /// <param name="serviceCollection">The service collection to add services to.</param>
    /// <returns>The same service collection so that multiple calls can be chained.</returns>
    public static IServiceCollection AddEntityFrameworkTrino(this IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<LoggingDefinitions, TrinoLoggingDefinitions>()
            .TryAdd<IDatabaseProvider, DatabaseProvider<TrinoOptionsExtension>>()
            .TryAdd<IRelationalTypeMappingSource, TrinoTypeMappingSource>()
            .TryAdd<ISqlGenerationHelper, TrinoSqlGenerationHelper>()
            .TryAdd<IProviderConventionSetBuilder, TrinoConventionSetBuilder>()
            .TryAdd<IModelValidator, TrinoModelValidator>()
            .TryAdd<IValueGeneratorSelector, TrinoValueGeneratorSelector>()
            .TryAdd<IRelationalConnection, TrinoRelationalConnection>()
            .TryAdd<IRelationalDatabaseCreator, TrinoDatabaseCreator>()
            .TryAdd<IHistoryRepository, TrinoHistoryRepository>()
            .TryAdd<IMigrationsSqlGenerator, TrinoMigrationsSqlGenerator>()
            .TryAdd<IMigrator, TrinoMigrator>()
            .TryAdd<IQuerySqlGeneratorFactory, TrinoQuerySqlGeneratorFactory>()
            .TryAdd<IQueryCompilationContextFactory, TrinoQueryCompilationContextFactory>()
            .TryAdd<IQueryTranslationPostprocessorFactory, TrinoQueryTranslationPostprocessorFactory>()
            .TryAdd<IRelationalSqlTranslatingExpressionVisitorFactory, TrinoSqlTranslatingExpressionVisitorFactory>()
            .TryAdd<IMethodCallTranslatorProvider, TrinoMethodCallTranslatorProvider>()
            .TryAdd<IMemberTranslatorProvider, TrinoMemberTranslatorProvider>()
            .TryAdd<IAggregateMethodCallTranslatorProvider, TrinoAggregateMethodCallTranslatorProvider>()
            .TryAdd<IRelationalParameterBasedSqlProcessorFactory, TrinoParameterBasedSqlProcessorFactory>()
            .TryAdd<IUpdateSqlGenerator, TrinoUpdateSqlGenerator>()
            .TryAdd<IModificationCommandBatchFactory, TrinoModificationCommandBatchFactory>()
            .TryAddCoreServices();

        return serviceCollection;
    }
}
