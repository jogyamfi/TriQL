using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TriQL.EntityFrameworkCore.Metadata.Internal;

namespace TriQL.EntityFrameworkCore.Design.Internal;

/// <summary>
/// Generates the fluent API for the provider's annotations in scaffolded (and design-time) code:
/// <c>HasDefaultCatalog</c> for the model and <c>HasCatalog</c> for entity types.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoAnnotationCodeGenerator : AnnotationCodeGenerator
{
    private static readonly MethodInfo HasDefaultCatalogMethod = typeof(TrinoModelBuilderExtensions).GetMethod(
        nameof(TrinoModelBuilderExtensions.HasDefaultCatalog), [typeof(ModelBuilder), typeof(string)])!;

    private static readonly MethodInfo HasCatalogMethod = typeof(TrinoModelBuilderExtensions).GetMethod(
        nameof(TrinoModelBuilderExtensions.HasCatalog), [typeof(EntityTypeBuilder), typeof(string)])!;

    /// <summary>Initializes a new instance.</summary>
    public TrinoAnnotationCodeGenerator(AnnotationCodeGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    protected override MethodCallCodeFragment? GenerateFluentApi(IModel model, IAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        return annotation.Name == TrinoAnnotationNames.DefaultCatalog
            ? new MethodCallCodeFragment(HasDefaultCatalogMethod, annotation.Value)
            : base.GenerateFluentApi(model, annotation);
    }

    /// <inheritdoc />
    protected override MethodCallCodeFragment? GenerateFluentApi(IEntityType entityType, IAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        return annotation.Name == TrinoAnnotationNames.Catalog
            ? new MethodCallCodeFragment(HasCatalogMethod, annotation.Value)
            : base.GenerateFluentApi(entityType, annotation);
    }
}
