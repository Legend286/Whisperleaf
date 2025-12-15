using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Whisperleaf.Utilities.Serialization;

internal sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for Vector3");
        }

        float x = 0f, y = 0f, z = 0f;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name when reading Vector3");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            reader.Read();

            float value = reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetSingle(),
                JsonTokenType.Null => 0f,
                _ => throw new JsonException($"Unexpected token '{reader.TokenType}' when reading Vector3")
            };

            if (string.Equals(propertyName, "x", StringComparison.OrdinalIgnoreCase)) x = value;
            else if (string.Equals(propertyName, "y", StringComparison.OrdinalIgnoreCase)) y = value;
            else if (string.Equals(propertyName, "z", StringComparison.OrdinalIgnoreCase)) z = value;
            else reader.Skip();
        }

        return new Vector3(x, y, z);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("z", value.Z);
        writer.WriteEndObject();
    }
}
