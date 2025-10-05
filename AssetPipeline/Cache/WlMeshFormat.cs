using System.Numerics;
using System.Runtime.InteropServices;

namespace Whisperleaf.AssetPipeline.Cache;

/// <summary>
/// Binary mesh format (.wlmesh) for cached mesh data
/// </summary>
public static class WlMeshFormat
{
    private const uint MAGIC = 0x534D4C57; // "WLMS" in little-endian
    private const uint VERSION = 1;

    private struct Header
    {
        public uint Magic;
        public uint Version;
        public uint VertexCount;
        public uint IndexCount;
        public uint VertexStride;
        public int MaterialIndex;
        public Vector3 AABBMin;
        public Vector3 AABBMax;
    }

    /// <summary>
    /// Write mesh data to binary format
    /// </summary>
    public static void Write(string path, MeshData mesh, string sourceHash)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // Write header fields directly
        bw.Write(MAGIC);
        bw.Write(VERSION);
        bw.Write((uint)(mesh.Vertices.Length / 12)); // VertexCount
        bw.Write((uint)mesh.Indices.Length); // IndexCount
        bw.Write(12 * sizeof(float)); // VertexStride
        bw.Write(mesh.MaterialIndex);
        bw.Write(mesh.AABBMin.X);
        bw.Write(mesh.AABBMin.Y);
        bw.Write(mesh.AABBMin.Z);
        bw.Write(mesh.AABBMax.X);
        bw.Write(mesh.AABBMax.Y);
        bw.Write(mesh.AABBMax.Z);

        // Write source hash (32 bytes)
        var hashBytes = Convert.FromHexString(sourceHash);
        bw.Write(hashBytes, 0, Math.Min(32, hashBytes.Length));
        if (hashBytes.Length < 32)
        {
            bw.Write(new byte[32 - hashBytes.Length]); // Pad to 32 bytes
        }

        // Write vertex data
        foreach (float v in mesh.Vertices)
            bw.Write(v);

        // Write index data
        foreach (uint i in mesh.Indices)
            bw.Write(i);
    }

    /// <summary>
    /// Read mesh data from binary format
    /// </summary>
    public static MeshData Read(string path, out string sourceHash)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);

        // Read header fields
        uint magic = br.ReadUInt32();
        uint version = br.ReadUInt32();
        uint vertexCount = br.ReadUInt32();
        uint indexCount = br.ReadUInt32();
        uint vertexStride = br.ReadUInt32();
        int materialIndex = br.ReadInt32();
        Vector3 aabbMin = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        Vector3 aabbMax = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

        // Validate
        if (magic != MAGIC)
            throw new InvalidDataException($"Invalid .wlmesh file: bad magic number");
        if (version != VERSION)
            throw new InvalidDataException($"Unsupported .wlmesh version: {version}");

        // Read source hash (32 bytes)
        byte[] hashBytes = br.ReadBytes(32);
        sourceHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Read vertex data
        float[] vertices = new float[vertexCount * 12];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = br.ReadSingle();

        // Read index data
        uint[] indices = new uint[indexCount];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = br.ReadUInt32();

        return new MeshData
        {
            Vertices = vertices,
            Indices = indices,
            MaterialIndex = materialIndex,
            AABBMin = aabbMin,
            AABBMax = aabbMax
        };
    }

    /// <summary>
    /// Compute hash for mesh data (for content-addressable storage)
    /// </summary>
    public static string ComputeHash(MeshData mesh)
    {
        // Hash: vertices + indices + material index
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Write vertex data
        foreach (float v in mesh.Vertices)
            bw.Write(v);

        // Write index data
        foreach (uint i in mesh.Indices)
            bw.Write(i);

        // Write material index
        bw.Write(mesh.MaterialIndex);

        return AssetCache.ComputeHash(ms.ToArray());
    }
}
