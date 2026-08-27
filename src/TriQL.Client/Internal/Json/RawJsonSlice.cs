using System.Text.Json;
using System.Text.Json.Serialization;

namespace TriQL.Client.Internal.Json;

/// <summary>
/// Records the position, extent, and shape of a JSON member without materializing it, so the
/// <c>data</c> array can be decoded later straight from the response bytes (NFR-PERF-3).
/// </summary>
/// <param name="Start">Byte offset of the member's first token within the deserialized buffer.</param>
/// <param name="Length">Length in bytes of the member's complete token sequence.</param>
/// <param name="Kind">The member's JSON shape, used for direct-vs-spooled detection (FR-5.1.2).</param>
internal readonly record struct RawJsonSlice(int Start, int Length, JsonValueKind Kind);

/// <summary>
/// Captures a member as a <see cref="RawJsonSlice"/> by skipping it and recording the byte range the
/// reader traversed. Offsets are relative to the buffer the deserializer was given, so the caller
/// MUST deserialize from the same contiguous buffer it later slices.
/// </summary>
internal sealed class RawJsonSliceConverter : JsonConverter<RawJsonSlice>
{
    public override RawJsonSlice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var kind = reader.TokenType switch
        {
            JsonTokenType.StartArray => JsonValueKind.Array,
            JsonTokenType.StartObject => JsonValueKind.Object,
            JsonTokenType.Null => JsonValueKind.Null,
            JsonTokenType.String => JsonValueKind.String,
            JsonTokenType.Number => JsonValueKind.Number,
            JsonTokenType.True => JsonValueKind.True,
            JsonTokenType.False => JsonValueKind.False,
            _ => JsonValueKind.Undefined,
        };

        var start = reader.TokenStartIndex;
        reader.Skip();
        var end = reader.BytesConsumed;
        return new RawJsonSlice(checked((int)start), checked((int)(end - start)), kind);
    }

    public override void Write(Utf8JsonWriter writer, RawJsonSlice value, JsonSerializerOptions options) =>
        throw new NotSupportedException("RawJsonSlice is deserialize-only.");
}
