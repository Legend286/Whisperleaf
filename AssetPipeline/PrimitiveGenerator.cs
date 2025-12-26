using System;
using System.Numerics;
using System.Collections.Generic;

namespace Whisperleaf.AssetPipeline;

public static class PrimitiveGenerator
{
    private const int FloatPerVertex = 12; // Pos(3) + Norm(3) + Tan(4) + UV(2)

    public static MeshData CreateBox(float width, float height, float depth)
    {
        float w2 = width * 0.5f;
        float h2 = height * 0.5f;
        float d2 = depth * 0.5f;

        // 24 vertices
        var vertices = new float[24 * FloatPerVertex];
        var indices = new uint[36];

        int vIndex = 0;
        int iIndex = 0;

        void AddFace(Vector3 normal, Vector3 tangent, Vector3 up, float w, float h, float d)
        {
            Vector3 side = Vector3.Cross(normal, up); // Actually tangent should be provided
            // Re-calculate basis for face
            // normal is Face Normal
            // tangent is Right
            // bitangent is Up
            
            // Assume tangent is passed correctly (1,0,0) etc.
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            // 4 corners
            // BL, BR, TR, TL
            Vector3[] corners = new Vector3[4];
            corners[0] = normal * d - tangent * w - bitangent * h;
            corners[1] = normal * d + tangent * w - bitangent * h;
            corners[2] = normal * d + tangent * w + bitangent * h;
            corners[3] = normal * d - tangent * w + bitangent * h;

            Vector2[] uvs = { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };

            uint baseIndex = (uint)(vIndex / FloatPerVertex);

            for (int i = 0; i < 4; i++)
            {
                // Pos
                vertices[vIndex++] = corners[i].X;
                vertices[vIndex++] = corners[i].Y;
                vertices[vIndex++] = corners[i].Z;
                // Norm
                vertices[vIndex++] = normal.X;
                vertices[vIndex++] = normal.Y;
                vertices[vIndex++] = normal.Z;
                // Tan
                vertices[vIndex++] = tangent.X;
                vertices[vIndex++] = tangent.Y;
                vertices[vIndex++] = tangent.Z;
                vertices[vIndex++] = 1.0f;
                // UV
                vertices[vIndex++] = uvs[i].X;
                vertices[vIndex++] = uvs[i].Y;
            }

            indices[iIndex++] = baseIndex + 0;
            indices[iIndex++] = baseIndex + 1;
            indices[iIndex++] = baseIndex + 2;
            indices[iIndex++] = baseIndex + 0;
            indices[iIndex++] = baseIndex + 2;
            indices[iIndex++] = baseIndex + 3;
        }

        // Front (+Z)
        AddFace(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, w2, h2, d2);
        // Back (-Z)
        AddFace(-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY, w2, h2, d2);
        // Right (+X)
        AddFace(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY, d2, h2, w2);
        // Left (-X)
        AddFace(-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, d2, h2, w2);
        // Top (+Y)
        AddFace(Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ, w2, d2, h2);
        // Bottom (-Y)
        AddFace(-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, w2, d2, h2);

        return new MeshData
        {
            Name = "Cube",
            Vertices = vertices,
            Indices = indices,
            AABBMin = new Vector3(-w2, -h2, -d2),
            AABBMax = new Vector3(w2, h2, d2),
            WorldMatrix = Matrix4x4.Identity
        };
    }

    public static MeshData CreateSphere(float radius, int tessellation)
    {
        int verticalSegments = tessellation;
        int horizontalSegments = tessellation * 2;

        var vertices = new List<float>();
        var indices = new List<uint>();

        for (int i = 0; i <= verticalSegments; i++)
        {
            float v = 1 - (float)i / verticalSegments;
            float latitude = (i * MathF.PI / verticalSegments) - MathF.PI / 2;
            float dy = MathF.Sin(latitude);
            float dxz = MathF.Cos(latitude);

            for (int j = 0; j <= horizontalSegments; j++)
            {
                float u = (float)j / horizontalSegments;
                float longitude = j * 2 * MathF.PI / horizontalSegments;
                float dx = MathF.Sin(longitude);
                float dz = MathF.Cos(longitude);

                dx *= dxz;
                dz *= dxz;

                var normal = new Vector3(dx, dy, dz);
                var pos = normal * radius;
                var tangent = new Vector3(MathF.Cos(longitude), 0, -MathF.Sin(longitude));

                // Pos
                vertices.Add(pos.X); vertices.Add(pos.Y); vertices.Add(pos.Z);
                // Norm
                vertices.Add(normal.X); vertices.Add(normal.Y); vertices.Add(normal.Z);
                // Tan
                vertices.Add(tangent.X); vertices.Add(tangent.Y); vertices.Add(tangent.Z); vertices.Add(1.0f);
                // UV
                vertices.Add(u); vertices.Add(v);
            }
        }

        uint stride = (uint)(horizontalSegments + 1);
        for (int i = 0; i < verticalSegments; i++)
        {
            for (int j = 0; j < horizontalSegments; j++)
            {
                uint nextI = (uint)(i + 1);
                uint nextJ = (uint)(j + 1);

                indices.Add((uint)(i * stride + j));
                indices.Add((uint)(nextI * stride + j));
                indices.Add((uint)(i * stride + nextJ));

                indices.Add((uint)(i * stride + nextJ));
                indices.Add((uint)(nextI * stride + j));
                indices.Add((uint)(nextI * stride + nextJ));
            }
        }

        return new MeshData
        {
            Name = "Sphere",
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            AABBMin = new Vector3(-radius),
            AABBMax = new Vector3(radius),
            WorldMatrix = Matrix4x4.Identity
        };
    }

    public static MeshData CreatePlane(float width, float depth)
    {
        float w2 = width * 0.5f;
        float d2 = depth * 0.5f;

        var vertices = new float[]
        {
            // Pos(3), Norm(3), Tan(4), UV(2)
            -w2, 0, -d2,  0, 1, 0,  1, 0, 0, 1,  0, 0,
             w2, 0, -d2,  0, 1, 0,  1, 0, 0, 1,  1, 0,
             w2, 0,  d2,  0, 1, 0,  1, 0, 0, 1,  1, 1,
            -w2, 0,  d2,  0, 1, 0,  1, 0, 0, 1,  0, 1
        };

        var indices = new uint[] { 0, 3, 2, 0, 2, 1 };

        return new MeshData
        {
            Name = "Plane",
            Vertices = vertices,
            Indices = indices,
            AABBMin = new Vector3(-w2, 0, -d2),
            AABBMax = new Vector3(w2, 0, d2),
            WorldMatrix = Matrix4x4.Identity
        };
    }
}
