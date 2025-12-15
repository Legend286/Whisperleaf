using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Whisperleaf.Utilities.Serialization;

internal sealed class Vector4JsonConverter : JsonConverter<Vector4>
{
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for Vector4");
        }

        float x = 0f, y = 0f, z = 0f, w = 0f;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name when reading Vector4");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            reader.Read();

            float value = reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetSingle(),
                JsonTokenType.Null => 0f,
                _ => throw new JsonException($"Unexpected token '{reader.TokenType}' when reading Vector4")
            };

            if (string.Equals(propertyName, "x", StringComparison.OrdinalIgnoreCase)) x = value;
            else if (string.Equals(propertyName, "y", StringComparison.OrdinalIgnoreCase)) y = value;
            else if (string.Equals(propertyName, "z", StringComparison.OrdinalIgnoreCase)) z = value;
            else if (string.Equals(propertyName, "w", StringComparison.OrdinalIgnoreCase)) w = value;
            else reader.Skip();
        }

        return new Vector4(x, y, z, w);
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("z", value.Z);
        writer.WriteNumber("w", value.W);
        writer.WriteEndObject();
    }
}
