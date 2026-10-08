using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using TriQL.EntityFrameworkCore.Query.Internal.Translators;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>Registers the Trino member translators.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoMemberTranslatorProvider : RelationalMemberTranslatorProvider
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoMemberTranslatorProvider(RelationalMemberTranslatorProviderDependencies dependencies, IRelationalTypeMappingSource typeMappingSource)
        : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var sqlExpressionFactory = dependencies.SqlExpressionFactory;

        AddTranslators(
        [
            new TrinoStringMemberTranslator(sqlExpressionFactory),
            new TrinoDateTimeMemberTranslator(sqlExpressionFactory, typeMappingSource),
        ]);
    }
}
