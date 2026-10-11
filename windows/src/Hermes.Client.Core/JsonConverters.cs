using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.Client.Core;

/// <summary>String-or-number to string (message ids arrive as either).</summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            // Number ids (the session DB uses integer message ids): keep raw text.
            JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.ValueSpan),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}

/// <summary>Epoch-second float (or null) to <see cref="DateTimeOffset"/>.</summary>
public sealed class EpochDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var seconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.Value.ToUnixTimeMilliseconds() / 1000.0);
        }
    }
}

/// <summary>
/// Message <c>content</c> arrives as a string, a parts array
/// (<c>[{"type":"text","text":"..."}]</c>), or null. Normalizes to text.
/// </summary>
public sealed class MessageContentConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? "";
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var sb = new System.Text.StringBuilder();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    using var doc = JsonDocument.ParseValue(ref reader);
                    if (doc.RootElement.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(text.GetString());
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            return sb.ToString();
        }

        reader.Skip();
        return "";
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
