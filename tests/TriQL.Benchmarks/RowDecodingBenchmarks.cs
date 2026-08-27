using System.Globalization;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using TriQL.Client;
using TriQL.Client.Internal;
using TriQL.Client.Types;

namespace TriQL.Benchmarks;

/// <summary>
/// P3-T6 / NFR-PERF-3: compares the <see cref="Utf8RowDecoder"/> against the naive
/// <see cref="JsonDocument"/> + <see cref="RawJsonValueConverter"/> path it replaced.
/// <c>Allocated / RowCount</c> is the figure NFR-PERF-3 constrains.
/// </summary>
[MemoryDiagnoser]
public class RowDecodingBenchmarks
{
    private static readonly string[] ColumnTypes =
    [
        "bigint", "varchar", "double", "boolean",
        "decimal(10,2)", "date", "timestamp(3)", "uuid",
    ];

    private byte[] _dataUtf8 = [];
    private List<TrinoColumn> _columns = [];
    private TrinoTypeSignature[] _signatures = [];

    [Params(1_000, 10_000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _columns = [];
        for (var i = 0; i < ColumnTypes.Length; i++)
        {
            _columns.Add(new TrinoColumn($"c{i}", ColumnTypes[i]));
        }

        _signatures = [.. _columns.Select(c => c.TypeSignature)];
        _dataUtf8 = Encoding.UTF8.GetBytes(BuildData(RowCount));
    }

    private static string BuildData(int rowCount)
    {
        var builder = new StringBuilder("[");
        for (var i = 0; i < rowCount; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(CultureInfo.InvariantCulture, $$"""
                [{{i}},"customer#{{i:D9}}",{{i}}.5,true,"12.34","2001-08-22","2001-08-22 03:04:05.321","f7a2b8c0-1234-4567-8901-abcdefabcdef"]
                """);
        }

        return builder.Append(']').ToString();
    }

    /// <summary>
    /// The Phase 2 path: a <see cref="JsonDocument"/> tree, a boxed raw value per column (with an
    /// intermediate string for every text-shaped type), then a second pass to parse to the final type.
    /// Materializes every row so the comparison against <see cref="Utf8Decoder"/> is like-for-like.
    /// </summary>
    [Benchmark(Baseline = true)]
    public object?[][] Naive()
    {
        using var document = JsonDocument.Parse(_dataUtf8);
        var root = document.RootElement;
        var rows = new object?[root.GetArrayLength()][];
        var r = 0;
        foreach (var rowElement in root.EnumerateArray())
        {
            var raw = RawJsonValueConverter.ConvertRow(rowElement);
            var converted = new object?[raw.Length];
            for (var i = 0; i < raw.Length; i++)
            {
                converted[i] = TrinoValueConverter.Convert(raw[i], _signatures[i]);
            }

            rows[r++] = converted;
        }

        return rows;
    }

    /// <summary>The Phase 3 path: one pass over the response bytes straight to final CLR values.</summary>
    [Benchmark]
    public object?[][] Utf8Decoder() => Utf8RowDecoder.DecodeRows(_dataUtf8, _columns);
}
