using System.Collections.Concurrent;
using System.Globalization;

namespace TriQL.Client.Types;

/// <summary>A single field of a <c>row(...)</c> type signature. See FR-7.1.2.</summary>
/// <param name="Name">The field name, or <see langword="null"/> for an anonymous field.</param>
/// <param name="Type">The field's type signature.</param>
public sealed record TrinoRowField(string? Name, TrinoTypeSignature Type);

/// <summary>
/// A parsed Trino type signature tree, e.g. <c>map(varchar, array(row(a bigint, b timestamp(6)
/// with time zone)))</c>. Parsed signatures are cached per distinct type string. See FR-7.1.
/// </summary>
public sealed class TrinoTypeSignature
{
    private static readonly ConcurrentDictionary<string, TrinoTypeSignature> Cache = new(StringComparer.Ordinal);

    private TrinoTypeSignature(
        string rawSignature,
        string baseName,
        IReadOnlyList<int>? numericParameters,
        IReadOnlyList<TrinoRowField>? rowFields,
        IReadOnlyList<TrinoTypeSignature>? typeArguments,
        bool withTimeZone,
        string? intervalRange)
    {
        RawSignature = rawSignature;
        BaseName = baseName;
        NumericParameters = numericParameters ?? [];
        RowFields = rowFields ?? [];
        TypeArguments = typeArguments ?? [];
        WithTimeZone = withTimeZone;
        IntervalRange = intervalRange;
    }

    /// <summary>The original type string this signature was parsed from.</summary>
    public string RawSignature { get; }

    /// <summary>The lower-cased base type name, e.g. <c>varchar</c>, <c>array</c>, <c>row</c>, <c>timestamp</c>.</summary>
    public string BaseName { get; }

    /// <summary>Numeric parameters in declaration order, e.g. <c>[p, s]</c> for <c>decimal(p,s)</c>.</summary>
    public IReadOnlyList<int> NumericParameters { get; }

    /// <summary>Fields of a <c>row(...)</c> signature, in declaration order.</summary>
    public IReadOnlyList<TrinoRowField> RowFields { get; }

    /// <summary>Type arguments of <c>array(T)</c> (one element) or <c>map(K,V)</c> (two elements).</summary>
    public IReadOnlyList<TrinoTypeSignature> TypeArguments { get; }

    /// <summary>Whether a <c>time</c>/<c>timestamp</c> signature carries <c>with time zone</c>.</summary>
    public bool WithTimeZone { get; }

    /// <summary>For <c>interval</c> signatures, either <c>"year to month"</c> or <c>"day to second"</c>.</summary>
    public string? IntervalRange { get; }

    /// <summary>The declared length for <c>varchar(n)</c>/<c>char(n)</c>, or <see langword="null"/> if unbounded.</summary>
    public int? Length => BaseName is "varchar" or "char" && NumericParameters.Count > 0 ? NumericParameters[0] : null;

    /// <summary>The declared precision for <c>decimal(p,s)</c>, <c>time(p)</c>, or <c>timestamp(p)</c>.</summary>
    public int? Precision => BaseName switch
    {
        "decimal" or "time" or "timestamp" => NumericParameters.Count > 0 ? NumericParameters[0] : null,
        _ => null,
    };

    /// <summary>The declared scale for <c>decimal(p,s)</c>. Defaults to <c>0</c> when only precision is given.</summary>
    public int? Scale => BaseName == "decimal" ? (NumericParameters.Count > 1 ? NumericParameters[1] : 0) : null;

    /// <summary>Parses <paramref name="typeString"/>, caching the result per distinct string (FR-7.1.3).</summary>
    public static TrinoTypeSignature Parse(string typeString)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeString);
        return Cache.GetOrAdd(typeString, static s => new Parser(s).ParseSignature());
    }

    /// <inheritdoc/>
    public override string ToString() => RawSignature;

    private sealed class Parser(string text)
    {
        private int _pos;

        public TrinoTypeSignature ParseSignature()
        {
            var start = _pos;
            SkipWhitespace();
            var name = ReadIdentifier();
            if (name.Length == 0)
            {
                throw new FormatException($"Expected a type name at position {_pos} in '{text}'.");
            }

            List<int>? numericParams = null;
            List<TrinoRowField>? rowFields = null;
            List<TrinoTypeSignature>? typeArgs = null;

            SkipWhitespace();
            if (Peek() == '(')
            {
                Advance();
                switch (name)
                {
                    case "array":
                        typeArgs = [ParseSignature()];
                        break;
                    case "map":
                        var key = ParseSignature();
                        SkipWhitespace();
                        Expect(',');
                        var value = ParseSignature();
                        typeArgs = [key, value];
                        break;
                    case "row":
                        rowFields = ParseRowFields();
                        break;
                    default:
                        numericParams = ParseIntList();
                        break;
                }

                SkipWhitespace();
                Expect(')');
            }

            var withTimeZone = false;
            string? intervalRange = null;
            SkipWhitespace();
            if (MatchKeywordSequence("with", "time", "zone"))
            {
                withTimeZone = true;
            }
            else if (name == "interval")
            {
                SkipWhitespace();
                var fromUnit = ReadIdentifier();
                SkipWhitespace();
                if (!MatchKeywordSequence("to"))
                {
                    throw new FormatException($"Expected 'to' in interval type at position {_pos} in '{text}'.");
                }

                SkipWhitespace();
                var toUnit = ReadIdentifier();
                intervalRange = $"{fromUnit} to {toUnit}";
            }

            var raw = text[start.._pos].Trim();
            return new TrinoTypeSignature(raw, name, numericParams, rowFields, typeArgs, withTimeZone, intervalRange);
        }

        private List<TrinoRowField> ParseRowFields()
        {
            var fields = new List<TrinoRowField>();
            while (true)
            {
                SkipWhitespace();
                string? fieldName = null;

                if (Peek() == '"')
                {
                    fieldName = ReadQuotedIdentifier();
                }
                else
                {
                    var savedPos = _pos;
                    var candidate = ReadIdentifier();
                    SkipWhitespace();
                    var next = Peek();
                    if (next is '(' or ',' or ')' or '\0')
                    {
                        // `candidate` was itself the (anonymous) type name — rewind and parse it as a full signature.
                        _pos = savedPos;
                    }
                    else
                    {
                        fieldName = candidate;
                    }
                }

                var fieldType = ParseSignature();
                fields.Add(new TrinoRowField(fieldName, fieldType));
                SkipWhitespace();
                if (Peek() == ',')
                {
                    Advance();
                    continue;
                }

                break;
            }

            return fields;
        }

        private List<int> ParseIntList()
        {
            var list = new List<int>();
            while (true)
            {
                SkipWhitespace();
                list.Add(ReadInt());
                SkipWhitespace();
                if (Peek() == ',')
                {
                    Advance();
                    continue;
                }

                break;
            }

            return list;
        }

        private string ReadIdentifier()
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] == '_'))
            {
                _pos++;
            }

            return text[start.._pos].ToLowerInvariant();
        }

        private string ReadQuotedIdentifier()
        {
            Expect('"');
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                if (_pos >= text.Length)
                {
                    throw new FormatException($"Unterminated quoted identifier in '{text}'.");
                }

                var c = text[_pos++];
                if (c == '"')
                {
                    if (_pos < text.Length && text[_pos] == '"')
                    {
                        sb.Append('"');
                        _pos++;
                        continue;
                    }

                    break;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private int ReadInt()
        {
            var start = _pos;
            if (Peek() == '-')
            {
                _pos++;
            }

            while (_pos < text.Length && char.IsDigit(text[_pos]))
            {
                _pos++;
            }

            if (_pos == start)
            {
                throw new FormatException($"Expected a number at position {_pos} in '{text}'.");
            }

            return int.Parse(text[start.._pos], NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private bool MatchKeywordSequence(params string[] keywords)
        {
            var savedPos = _pos;
            foreach (var keyword in keywords)
            {
                SkipWhitespace();
                if (!MatchKeyword(keyword))
                {
                    _pos = savedPos;
                    return false;
                }
            }

            return true;
        }

        private bool MatchKeyword(string keyword)
        {
            var savedPos = _pos;
            var word = ReadIdentifier();
            if (string.Equals(word, keyword, StringComparison.Ordinal))
            {
                return true;
            }

            _pos = savedPos;
            return false;
        }

        private char Peek() => _pos < text.Length ? text[_pos] : '\0';

        private void Advance() => _pos++;

        private void Expect(char c)
        {
            if (Peek() != c)
            {
                throw new FormatException($"Expected '{c}' at position {_pos} in '{text}'.");
            }

            _pos++;
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
            {
                _pos++;
            }
        }
    }
}
