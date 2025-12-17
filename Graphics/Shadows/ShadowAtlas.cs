using System;
using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Vortice.Direct3D11;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;
using SamplerDescription = Veldrid.SamplerDescription;

namespace Whisperleaf.Graphics.Shadows;

public class ShadowAtlas : IDisposable
{
    private readonly GraphicsDevice _gd;
    private Texture _textureArray;
    private TextureView _textureView;
    private ResourceSet _resourceSet;
    private ResourceLayout _layout;

    public ResourceLayout ResourceLayout => _layout;
    private Sampler _sampler;

    private const int TextureSize = 2048;
    private const int ArrayLayers = 3;

    // Page configurations (Grid Size N means N*N tiles)
    private readonly int[] _pageGrids = new[] { 2, 4, 8 }; 
    
    // Allocations per frame
    private readonly Dictionary<SceneNode, ShadowAllocation[]> _allocations = new();
    private readonly Framebuffer[] _layerFramebuffers;

    public struct ShadowAllocation
    {
        public int PageIndex;
        public int AtlasX; // Grid coords
        public int AtlasY;
        public int TileSize; // In pixels
        public int FaceIndex; // For point lights (0-5), 0 for spot
        public Matrix4x4 ViewProj;
    }

    public ShadowAtlas(GraphicsDevice gd)
    {
        _gd = gd;
        _layerFramebuffers = new Framebuffer[ArrayLayers];
        CreateResources();
        CreateFramebuffers();
    }

    private void CreateResources()
    {
        var factory = _gd.ResourceFactory;

        _textureArray = factory.CreateTexture(new TextureDescription(
            TextureSize, TextureSize, 1, 1, ArrayLayers,
            PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil | TextureUsage.Sampled, TextureType.Texture2D));
        
        _textureView = factory.CreateTextureView(_textureArray);

        _sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear, ComparisonKind.LessEqual, 
            0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ShadowMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));

        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _textureView, _sampler));
    }

    private void CreateFramebuffers()
    {
        var factory = _gd.ResourceFactory;
        for (int i = 0; i < ArrayLayers; i++)
        {
            var depthTargetDesc = new TextureViewDescription(_textureArray, 0, 1, (uint)i, 1);
            // We need a specific TextureView for the framebuffer attachment.
            // Note: Framebuffer does not take ownership of TextureView, so we must manage its lifetime.
            // However, typical Veldrid usage suggests keeping the TextureView alive while FB is alive.
            // But here we are creating it temporarily? 
            // Wait, CreateFramebuffer takes FramebufferAttachmentDescription which takes a Texture.
            // No, it takes a FramebufferAttachmentDescription.
            // FramebufferAttachmentDescription constructor takes (Texture target, uint arrayLayer).
            // So we don't need a TextureView for the framebuffer attachment description!
            
            var fbDesc = new FramebufferDescription(
                new FramebufferAttachmentDescription(_textureArray, (uint)i), // Depth
                Array.Empty<FramebufferAttachmentDescription>() // Color
            );
            _layerFramebuffers[i] = factory.CreateFramebuffer(fbDesc);
        }
    }
    
    public ResourceSet ResourceSet => _resourceSet;
    public ResourceLayout Layout => _layout;
    
    public Framebuffer GetFramebuffer(int layer) => _layerFramebuffers[layer];

    public void UpdateAllocations(List<SceneNode> lights, Camera camera)
    {
        _allocations.Clear();
        
        // Sort lights
        lights.Sort((a, b) =>
        {
            float scoreA = CalculateScore(a, camera);
            float scoreB = CalculateScore(b, camera);
            return scoreB.CompareTo(scoreA); 
        });

        // Track usage
        List<bool[]> pageUsage = new();
        for (int i = 0; i < ArrayLayers; i++)
        {
            int totalSlots = _pageGrids[i] * _pageGrids[i];
            pageUsage.Add(new bool[totalSlots]);
        }

        foreach (var light in lights)
        {
            if (light.Light == null) continue;

            int neededSlots = (light.Light.Type == 0) ? 6 : 1;
            int startPage = 0;
            
            // Heuristic: Point lights (6 slots) hard to fit in Page 0 (4 slots). Start at Page 1.
            if (neededSlots > 4) startPage = 1;

            for (int page = startPage; page < ArrayLayers; page++)
            {
                int gridSize = _pageGrids[page];
                int totalSlots = gridSize * gridSize;
                var usage = pageUsage[page];

                // Find free slots
                List<int> freeIndices = new();
                for (int i = 0; i < totalSlots; i++)
                {
                    if (!usage[i]) freeIndices.Add(i);
                    if (freeIndices.Count == neededSlots) break;
                }

                if (freeIndices.Count == neededSlots)
                {
                    var allocs = new ShadowAllocation[neededSlots];
                    for (int f = 0; f < neededSlots; f++)
                    {
                        int slotIdx = freeIndices[f];
                        usage[slotIdx] = true;
                        
                        int x = slotIdx % gridSize;
                        int y = slotIdx / gridSize;
                        int tileSize = TextureSize / gridSize;
                        
                        allocs[f] = new ShadowAllocation
                        {
                            PageIndex = page,
                            AtlasX = x,
                            AtlasY = y,
                            TileSize = tileSize,
                            FaceIndex = f,
                            ViewProj = Matrix4x4.Identity // Will be calculated during render prep
                        };
                    }
                    _allocations[light] = allocs;
                    break;
                }
            }
        }
    }
    
    public ShadowAllocation[]? GetAllocations(SceneNode node)
    {
        return _allocations.TryGetValue(node, out var allocs) ? allocs : null;
    }

    public void UpdateViewProj(SceneNode node, int faceIndex, Matrix4x4 viewProj)
    {
        if (_allocations.TryGetValue(node, out var allocs))
        {
            if (faceIndex >= 0 && faceIndex < allocs.Length)
            {
                allocs[faceIndex].ViewProj = viewProj;
            }
        }
    }
    
    public IEnumerable<SceneNode> GetAllocatedNodes() => _allocations.Keys;

    public int GetLayerCount() => ArrayLayers;

    private float CalculateScore(SceneNode node, Camera cam)
    {
        if (!_allocations.TryGetValue(node, out _)) // Avoid re-calculating if already processed? No, this is pre-alloc.
        {
             // ...
        }
        var dist = Vector3.Distance(node.ComputeWorldTransform(Matrix4x4.Identity).Translation, cam.Position);
        if (dist < 0.1f) dist = 0.1f;
        return node.Light!.Intensity / (dist * dist);
    }

    public void Dispose()
    {
        foreach (var fb in _layerFramebuffers) fb?.Dispose();
        _textureArray.Dispose();
        _textureView.Dispose();
        _sampler.Dispose();
        _layout.Dispose();
        _resourceSet.Dispose();
    }
}
