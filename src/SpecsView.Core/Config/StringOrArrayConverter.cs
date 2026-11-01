using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpecsView.Core.Config;

/// <summary>Reads <c>"a"</c> or <c>["a", "b"]</c>; writes a single value as a plain string.</summary>
internal sealed class StringOrArrayConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return [reader.GetString()!];
            case JsonTokenType.StartArray:
                var values = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String)
                    {
                        throw new JsonException("Expected an array of metric ids.");
                    }

                    values.Add(reader.GetString()!);
                }

                return values;
            default:
                throw new JsonException("Expected a metric id or an array of metric ids.");
        }
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        if (value.Count == 1)
        {
            writer.WriteStringValue(value[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}
