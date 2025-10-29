using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Whisperleaf.Utilities.Serialization;

internal sealed class Matrix4x4JsonConverter : JsonConverter<Matrix4x4>
{
    public override Matrix4x4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for Matrix4x4");
        }

        float m11 = 1f, m12 = 0f, m13 = 0f, m14 = 0f;
        float m21 = 0f, m22 = 1f, m23 = 0f, m24 = 0f;
        float m31 = 0f, m32 = 0f, m33 = 1f, m34 = 0f;
        float m41 = 0f, m42 = 0f, m43 = 0f, m44 = 1f;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name when reading Matrix4x4");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            reader.Read();

            float value = reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetSingle(),
                JsonTokenType.Null => 0f,
                _ => throw new JsonException($"Unexpected token '{reader.TokenType}' when reading Matrix4x4")
            };

            switch (propertyName)
            {
                case "m11": m11 = value; break;
                case "m12": m12 = value; break;
                case "m13": m13 = value; break;
                case "m14": m14 = value; break;
                case "m21": m21 = value; break;
                case "m22": m22 = value; break;
                case "m23": m23 = value; break;
                case "m24": m24 = value; break;
                case "m31": m31 = value; break;
                case "m32": m32 = value; break;
                case "m33": m33 = value; break;
                case "m34": m34 = value; break;
                case "m41": m41 = value; break;
                case "m42": m42 = value; break;
                case "m43": m43 = value; break;
                case "m44": m44 = value; break;
                default:
                    reader.Skip();
                    break;
            }
        }

        Matrix4x4 matrix = new Matrix4x4
        {
            M11 = m11, M12 = m12, M13 = m13, M14 = m14,
            M21 = m21, M22 = m22, M23 = m23, M24 = m24,
            M31 = m31, M32 = m32, M33 = m33, M34 = m34,
            M41 = m41, M42 = m42, M43 = m43, M44 = m44,
        };

        return matrix;
    }

    public override void Write(Utf8JsonWriter writer, Matrix4x4 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("m11", value.M11);
        writer.WriteNumber("m12", value.M12);
        writer.WriteNumber("m13", value.M13);
        writer.WriteNumber("m14", value.M14);
        writer.WriteNumber("m21", value.M21);
        writer.WriteNumber("m22", value.M22);
        writer.WriteNumber("m23", value.M23);
        writer.WriteNumber("m24", value.M24);
        writer.WriteNumber("m31", value.M31);
        writer.WriteNumber("m32", value.M32);
        writer.WriteNumber("m33", value.M33);
        writer.WriteNumber("m34", value.M34);
        writer.WriteNumber("m41", value.M41);
        writer.WriteNumber("m42", value.M42);
        writer.WriteNumber("m43", value.M43);
        writer.WriteNumber("m44", value.M44);
        writer.WriteEndObject();
    }
}
