using System.Collections.Generic;
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

        public DeviceBuffer? ParamsBuffer;
        public ResourceSet? ResourceSet;
        public ResourceSet? ParamsResourceSet;

        // Flag to detect packed RMA textures
        public bool UsePackedRMA;

        public void Dispose()
        {
            var disposedTextures = new HashSet<Texture?>();

            void DisposeTexture(Texture? tex)
            {
                if (tex != null && disposedTextures.Add(tex))
                {
                    tex.Dispose();
                }
            }

            DisposeTexture(BaseColorTex);
            DisposeTexture(MetallicTex);
            DisposeTexture(RoughnessTex);
            DisposeTexture(NormalTex);
            DisposeTexture(OcclusionTex);
            DisposeTexture(EmissiveTex);

            BaseColorView?.Dispose();
            MetallicView?.Dispose();
            RoughnessView?.Dispose();
            NormalView?.Dispose();
            OcclusionView?.Dispose();
            EmissiveView?.Dispose();
            ParamsBuffer?.Dispose();
            ResourceSet?.Dispose();
            ParamsResourceSet?.Dispose();
        }
    }
}
