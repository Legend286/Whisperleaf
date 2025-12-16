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

        // Texture Hashes (for cache lookup)
        public string? BaseColorHash;
        public string? NormalHash;
        public string? EmissiveHash;
        public string? RMAHash;

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
            // 1. Dispose ResourceSets (descriptors) first, as they reference the views.
            ResourceSet?.Dispose();
            ParamsResourceSet?.Dispose();

            // 2. Dispose TextureViews (VkImageView) before the Textures (VkImage).
            //    Vulkan requirement: ImageView must be destroyed before the Image it points to.
            BaseColorView?.Dispose();
            MetallicView?.Dispose();
            RoughnessView?.Dispose();
            NormalView?.Dispose();
            OcclusionView?.Dispose();
            EmissiveView?.Dispose();

            // 3. Dispose other buffers
            ParamsBuffer?.Dispose();

            // 4. Finally dispose Textures (VkImage).
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
        }
    }
}
