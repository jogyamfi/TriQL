using TriQL.Client.Exceptions;
using TriQL.Client.Internal;

namespace TriQL.Client.Tests.Security;

/// <summary>Injection-safety suite for the parameter literal encoder and placeholder rewriter (SEC-4, FR-8.3).</summary>
public sealed class ParameterInjectionTests
{
    [Fact]
    public void Encode_StringWithSingleQuote_EscapesByDoubling()
    {
        var parameter = new TrinoParameter(null, "O'Brien");

        Assert.Equal("'O''Brien'", SqlLiteralEncoder.Encode(parameter));
    }

    [Fact]
    public void Encode_ClassicInjectionPayload_IsEscapedNotExecuted()
    {
        var parameter = new TrinoParameter(null, "'; DROP TABLE users; --");

        var encoded = SqlLiteralEncoder.Encode(parameter);

        // The payload's quote is doubled so it stays inside one string literal, never breaking out to a new statement.
        Assert.Equal("'''; DROP TABLE users; --'", encoded);
        Assert.StartsWith("'''", encoded, StringComparison.Ordinal);
        Assert.EndsWith("--'", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_Null_RendersBareNullKeyword()
    {
        Assert.Equal("NULL", SqlLiteralEncoder.Encode(new TrinoParameter(null, null)));
    }

    [Fact]
    public void Encode_Boolean_RendersBareKeyword()
    {
        Assert.Equal("TRUE", SqlLiteralEncoder.Encode(new TrinoParameter(null, true)));
        Assert.Equal("FALSE", SqlLiteralEncoder.Encode(new TrinoParameter(null, false)));
    }

    [Fact]
    public void Encode_Integer_RendersInvariantNumericLiteral()
    {
        Assert.Equal("42", SqlLiteralEncoder.Encode(new TrinoParameter(null, 42)));
        Assert.Equal("-7", SqlLiteralEncoder.Encode(new TrinoParameter(null, (sbyte)-7)));
    }

    [Fact]
    public void Encode_Varbinary_RendersHexLiteral()
    {
        Assert.Equal("X'010203'", SqlLiteralEncoder.Encode(new TrinoParameter(null, new byte[] { 1, 2, 3 })));
    }

    [Fact]
    public void Encode_DateOnly_RendersTypePrefixedLiteral()
    {
        Assert.Equal("DATE '2001-08-22'", SqlLiteralEncoder.Encode(new TrinoParameter(null, new DateOnly(2001, 8, 22))));
    }

    [Fact]
    public void Encode_UnsupportedType_ThrowsRatherThanFallingBackToToString()
    {
        var parameter = new TrinoParameter(null, new object());

        Assert.Throws<TrinoParameterException>(() => SqlLiteralEncoder.Encode(parameter));
    }

    [Fact]
    public void Encode_ExplicitTrinoTypeOverride_UsesCast()
    {
        var parameter = new TrinoParameter(null, "10.5") { TrinoType = "decimal(10,2)" };

        Assert.Equal("CAST('10.5' AS decimal(10,2))", SqlLiteralEncoder.Encode(parameter));
    }

    [Fact]
    public void Rewrite_NamedPlaceholderInsideStringLiteral_IsNotRewritten()
    {
        var (sql, names) = ParameterRewriter.Rewrite("SELECT * FROM t WHERE name = ':not_a_param' AND id = :id");

        Assert.Equal("SELECT * FROM t WHERE name = ':not_a_param' AND id = ?", sql);
        Assert.Equal([("id")], names);
    }

    [Fact]
    public void Rewrite_PlaceholderInsideLineComment_IsNotRewritten()
    {
        var (sql, names) = ParameterRewriter.Rewrite("SELECT * FROM t -- WHERE id = :id\nWHERE id = :real_id");

        Assert.Equal("SELECT * FROM t -- WHERE id = :id\nWHERE id = ?", sql);
        Assert.Equal([("real_id")], names);
    }

    [Fact]
    public void Rewrite_PlaceholderInsideBlockComment_IsNotRewritten()
    {
        var (sql, names) = ParameterRewriter.Rewrite("SELECT * FROM t /* :id */ WHERE id = :real_id");

        Assert.Equal("SELECT * FROM t /* :id */ WHERE id = ?", sql);
        Assert.Equal([("real_id")], names);
    }

    [Fact]
    public void Rewrite_PositionalPlaceholders_AreLeftAsQuestionMarksInOrder()
    {
        var (sql, names) = ParameterRewriter.Rewrite("SELECT * FROM t WHERE a = ? AND b = ?");

        Assert.Equal("SELECT * FROM t WHERE a = ? AND b = ?", sql);
        Assert.Equal([null, null], names);
    }

    [Fact]
    public void Rewrite_EscapedQuoteInStringLiteral_DoesNotTerminateTheLiteralEarly()
    {
        var (sql, names) = ParameterRewriter.Rewrite("SELECT * FROM t WHERE name = 'O''Brien: not :id' AND x = :id");

        Assert.Equal("SELECT * FROM t WHERE name = 'O''Brien: not :id' AND x = ?", sql);
        Assert.Equal([("id")], names);
    }

    [Fact]
    public void Bind_PlaceholderCountMismatch_ThrowsBeforeAnyNetworkCall()
    {
        var parameters = new TrinoParameterCollection();
        parameters.Add(1);

        Assert.Throws<TrinoParameterException>(() => ParameterBinder.Bind([null, null], parameters));
    }

    [Fact]
    public void Bind_NamedPlaceholderWithNoMatchingParameter_Throws()
    {
        var parameters = new TrinoParameterCollection();
        parameters.Add("other", 1);

        Assert.Throws<TrinoParameterException>(() => ParameterBinder.Bind(["id"], parameters));
    }

    [Fact]
    public void Bind_MixedPositionalAndNamedPlaceholders_Throws()
    {
        var parameters = new TrinoParameterCollection();
        parameters.Add("id", 1);
        parameters.Add(2);

        Assert.Throws<TrinoParameterException>(() => ParameterBinder.Bind(["id", null], parameters));
    }

    [Fact]
    public void Bind_NamedPlaceholders_ResolveCaseInsensitively()
    {
        var parameters = new TrinoParameterCollection();
        parameters.Add("Id", 1);

        var bound = ParameterBinder.Bind(["id"], parameters);

        Assert.Same(parameters[0], bound[0]);
    }

    [Fact]
    public void Bind_SameNamedParameterUsedTwice_BindsToBothPlaceholders()
    {
        var parameters = new TrinoParameterCollection();
        parameters.Add("id", 1);

        var bound = ParameterBinder.Bind(["id", "id"], parameters);

        Assert.Equal(2, bound.Count);
        Assert.Same(parameters[0], bound[0]);
        Assert.Same(parameters[0], bound[1]);
    }

    [Fact]
    public void Bind_UnusedNamedParameter_Throws()
    {
        var parameters = new TrinoParameterCollection();
        parameters.Add("id", 1);
        parameters.Add("unused", 2);

        Assert.Throws<TrinoParameterException>(() => ParameterBinder.Bind(["id"], parameters));
    }

    [Fact]
    public void Bind_NoPlaceholdersAndNoParameters_ReturnsEmpty()
    {
        Assert.Empty(ParameterBinder.Bind([], new TrinoParameterCollection()));
    }
}
