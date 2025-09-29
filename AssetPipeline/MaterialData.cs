using System.Numerics;
using Veldrid.ImageSharp;

namespace Whisperleaf.AssetPipeline;

public sealed class MaterialData
{
    public string Name = "Material";

    // Factors (used alone or multiplied with textures)
    public Vector4 BaseColorFactor = Vector4.One;
    public float MetallicFactor = 1f;
    public float RoughnessFactor = 1f;
    public Vector3 EmissiveFactor = Vector3.Zero;

    // Texture file paths (as discovered by Assimp)
    public string? BaseColorPath;
    public string? MetallicPath;
    public string? RoughnessPath;
    public string? NormalPath;
    public string? OcclusionPath;
    public string? EmissivePath;

    // (Optional) decoded images ready to upload later
    public ImageSharpTexture? BaseColorImage;
    public ImageSharpTexture? MetallicImage;
    public ImageSharpTexture? RoughnessImage;
    public ImageSharpTexture? NormalImage;
    public ImageSharpTexture? OcclusionImage;
    public ImageSharpTexture? EmissiveImage;
}