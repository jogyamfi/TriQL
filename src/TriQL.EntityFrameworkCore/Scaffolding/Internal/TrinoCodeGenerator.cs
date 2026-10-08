using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using TriQL.EntityFrameworkCore.Infrastructure;

namespace TriQL.EntityFrameworkCore.Scaffolding.Internal;

/// <summary>Generates the <c>UseTrino(connectionString)</c> call in a scaffolded context's <c>OnConfiguring</c>.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoCodeGenerator : ProviderCodeGenerator
{
    private static readonly MethodInfo UseTrinoMethod = typeof(TrinoDbContextOptionsBuilderExtensions).GetMethod(
        nameof(TrinoDbContextOptionsBuilderExtensions.UseTrino),
        [typeof(DbContextOptionsBuilder), typeof(string), typeof(Action<TrinoDbContextOptionsBuilder>)])!;

    /// <summary>Initializes a new instance.</summary>
    public TrinoCodeGenerator(ProviderCodeGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override MethodCallCodeFragment GenerateUseProvider(string connectionString, MethodCallCodeFragment? providerOptions) =>
        new(
            UseTrinoMethod,
            providerOptions is null
                ? [connectionString]
                : [connectionString, new NestedClosureCodeFragment("x", providerOptions)]);
}
