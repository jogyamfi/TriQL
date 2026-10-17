using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using TriQL.EntityFrameworkCore.Scaffolding.Internal;

namespace TriQL.EntityFrameworkCore.Design.Internal;

/// <summary>
/// Registers the provider's design-time services for the EF Core tools (<c>dotnet ef dbcontext scaffold</c>):
/// the database model factory, the <c>UseTrino</c> code generator and the annotation code generator. Found
/// by the tools through <see cref="DesignTimeProviderServicesAttribute"/> on this assembly.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoDesignTimeServices : IDesignTimeServices
{
    /// <inheritdoc />
    public virtual void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddEntityFrameworkTrino();
        new EntityFrameworkRelationalDesignServicesBuilder(serviceCollection)
            .TryAdd<IAnnotationCodeGenerator, TrinoAnnotationCodeGenerator>()
            .TryAdd<IDatabaseModelFactory, TrinoDatabaseModelFactory>()
            .TryAdd<IProviderConfigurationCodeGenerator, TrinoCodeGenerator>()
            .TryAddCoreServices();
    }
}
