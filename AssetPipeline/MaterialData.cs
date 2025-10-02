using System.Numerics;
using Veldrid;
using Veldrid.ImageSharp;

namespace Whisperleaf.AssetPipeline
{
    public sealed class MaterialData : IDisposable
    {
        public string Name = "Material";

        // Factors
        public Vector4 BaseColorFactor = Vector4.One;
        public float MetallicFactor = 1f;
        public float RoughnessFactor = 1f;
        public Vector3 EmissiveFactor = Vector3.Zero;

        // CPU-side file paths
        public string? BaseColorPath;
        public string? MetallicPath;
        public string? RoughnessPath;
        public string? NormalPath;
        public string? OcclusionPath;
        public string? EmissivePath;

        // GPU resources
        public Texture? BaseColorTex;
        public TextureView? BaseColorView;
        public Texture? MetallicTex;
        public TextureView? MetallicView;
        public Texture? RoughnessTex;
        public TextureView? RoughnessView;
        public Texture? NormalTex;
        public TextureView? NormalView;
        public Texture? OcclusionTex;
        public TextureView? OcclusionView;
        public Texture? EmissiveTex;
        public TextureView? EmissiveView;

        public ResourceSet? ResourceSet;

        public void Dispose()
        {
            BaseColorTex?.Dispose();
            BaseColorView?.Dispose();
            MetallicTex?.Dispose();
            MetallicView?.Dispose();
            RoughnessTex?.Dispose();
            RoughnessView?.Dispose();
            NormalTex?.Dispose();
            NormalView?.Dispose();
            OcclusionTex?.Dispose();
            OcclusionView?.Dispose();
            EmissiveTex?.Dispose();
            EmissiveView?.Dispose();
            ResourceSet?.Dispose();
        }
    }
}