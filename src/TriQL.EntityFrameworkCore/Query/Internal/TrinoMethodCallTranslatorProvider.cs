using Microsoft.EntityFrameworkCore.Query;
using TriQL.EntityFrameworkCore.Query.Internal.Translators;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>Registers the Trino method-call translators, which take precedence over EF's relational ones.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoMethodCallTranslatorProvider(RelationalMethodCallTranslatorProviderDependencies dependencies)
        : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var sqlExpressionFactory = dependencies.SqlExpressionFactory;
        var typeMappingSource = dependencies.RelationalTypeMappingSource;

        AddTranslators(
        [
            new TrinoStringMethodTranslator(sqlExpressionFactory),
            new TrinoMathTranslator(sqlExpressionFactory),
            new TrinoDateTimeMethodTranslator(sqlExpressionFactory, typeMappingSource),
            new TrinoConvertTranslator(sqlExpressionFactory),
            new TrinoObjectToStringTranslator(sqlExpressionFactory),
            new TrinoMiscMethodTranslator(sqlExpressionFactory),
            new TrinoDbFunctionsTranslator(sqlExpressionFactory, typeMappingSource),
        ]);
    }
}
