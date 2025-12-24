using System;
using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Vortice.Direct3D11;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics.RenderPasses;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;
using SamplerDescription = Veldrid.SamplerDescription;

namespace Whisperleaf.Graphics.Shadows;

public class ShadowAtlas : IDisposable {
    private readonly GraphicsDevice _gd;
    private Texture _textureArray;
    private TextureView _textureView;
    private ResourceSet _resourceSet;
    private ResourceLayout _layout;

    public ResourceLayout ResourceLayout => _layout;

    private Sampler _sampler;

    private const int TextureSize = 2048;
    private const int ArrayLayers = 8;

    // Page configurations (Grid Size N means N*N tiles)
    private readonly int[] _pageGrids = new[] { 2, 2, 4, 8, 8, 16, 16, 32 };

    // Allocations per frame
    private readonly Dictionary<SceneNode, ShadowAllocation[]> _allocations = new();
    private readonly Framebuffer[] _layerFramebuffers;

    public struct ShadowAllocation {
        public int PageIndex;
        public int AtlasX; // Grid coords
        public int AtlasY;
        public int TileSize; // In pixels
        public int FaceIndex; // For point lights (0-5), 0 for spot
        public Matrix4x4 ViewProj;
    }

    public ShadowAtlas(GraphicsDevice gd) {
        _gd = gd;
        _layerFramebuffers = new Framebuffer[ArrayLayers];
        CreateResources();
        CreateFramebuffers();
    }

    private void CreateResources() {
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

    private void CreateFramebuffers() {
        var factory = _gd.ResourceFactory;

        for (int i = 0; i < ArrayLayers; i++) {
            var depthTargetDesc = new TextureViewDescription(_textureArray, 0, 1, (uint)i, 1);
            
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

        public void UpdateAllocations(List<SceneNode> lights, Camera camera, GltfPass scene, bool globalEnabled)

        {

            _allocations.Clear();

            

            if (!globalEnabled) return;

            

            // Sort lights
        lights.Sort((a, b) =>
        {
            float scoreA = CalculateScore(a, camera);
            float scoreB = CalculateScore(b, camera);

            return scoreB.CompareTo(scoreA);
        });

        // Track usage
        List<bool[]> pageUsage = new();

        for (int i = 0; i < ArrayLayers; i++) {
            int totalSlots = _pageGrids[i] * _pageGrids[i];
            pageUsage.Add(new bool[totalSlots]);
        }


                foreach (var light in lights)


                {


                    if (light.Light == null) continue;


                    if (!light.Light.CastShadows) continue;


                    if (!scene.TryGetWorldTransform(light, out var lightWorld)) continue;

            var lightPos = lightWorld.Translation;
            var lightDir = Vector3.TransformNormal(new Vector3(0, 0, -1), lightWorld);

            int neededSlots = (light.Light.Type == 0) ? 6 : 1;
            int startPage = 0;

            // Heuristic: Point lights (6 slots) hard to fit in Page 0 (4 slots). Start at Page 1.
            if (neededSlots > 4) startPage = 1;

            for (int page = startPage; page < ArrayLayers; page++) {
                int gridSize = _pageGrids[page];
                int totalSlots = gridSize * gridSize;
                var usage = pageUsage[page];

                // Find free slots
                List<int> freeIndices = new();

                for (int i = 0; i < totalSlots; i++) {
                    if (!usage[i]) freeIndices.Add(i);

                    if (freeIndices.Count == neededSlots) break;
                }


                if (freeIndices.Count == neededSlots) {
                    var allocs = new ShadowAllocation[neededSlots];

                    for (int f = 0; f < neededSlots; f++) {
                        int slotIdx = freeIndices[f];
                        usage[slotIdx] = true;

                        int x = slotIdx % gridSize;
                        int y = slotIdx / gridSize;
                        int tileSize = TextureSize / gridSize;

                        // Calculate ViewProj immediately
                        Matrix4x4 view, proj;
                        float near = 0.05f;
                        float far = Math.Max(light.Light.Range, near + 0.05f);
                        
                        if (light.Light.Type == 0) {
                            view = GetPointLightView(lightPos, f);
                            proj = Matrix4x4.CreatePerspectiveFieldOfView((MathF.PI / 2.0f) + 0.05f, 1.0f, near, far);
                        } else {
                            view = Matrix4x4.CreateLookAt(lightPos, lightPos + lightDir, Vector3.UnitY);
                            float fov = Math.Clamp(light.Light.OuterCone * 2.0f, 0.01f, MathF.PI - 0.1f);
                            proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, 1.0f, near, far);
                        }

                        // Apply Vulkan clip space correction (Z 0-1, Y-flip handled by Render/Sample consistency?)
                        // If PBR.frag expects ViewProj to yield clip coords.
                        // And ShadowPass uses standard viewport.
                        // If we use Veldrid, we usually stick to standard.
                        // But let's assume correction is needed for consistency if ShadowPass was using it.
                        // Actually, let's keep it standard here. ShadowPass sets ViewProjBuffer.
                        // Wait, ShadowPass.RenderLight computed ViewProj locally. Now we do it here.
                        
                        // We must ensure this matrix matches what ShadowPass would have used.
                        // ShadowPass used `CreatePerspective` helper I added (with fix)?
                        // No, I reverted the fix logic in ShadowPass because "it is not the bug".
                        // So standard matrix.

                        allocs[f] = new ShadowAllocation {
                            PageIndex = page,
                            AtlasX = x,
                            AtlasY = y,
                            TileSize = tileSize,
                            FaceIndex = f,
                            ViewProj = view * proj
                        };
                    }


                    _allocations[light] = allocs;

                    break;
                }
            }
        }
    }

    public ShadowAllocation[]? GetAllocations(SceneNode node) {
        return _allocations.TryGetValue(node, out var allocs) ? allocs : null;
    }

    public void UpdateViewProj(SceneNode node, int faceIndex, Matrix4x4 viewProj) {
        if (_allocations.TryGetValue(node, out var allocs)) {
            if (faceIndex >= 0 && faceIndex < allocs.Length) {
                allocs[faceIndex].ViewProj = viewProj;
            }
        }
    }

    private Matrix4x4 GetPointLightView(Vector3 pos, int face)
    {
        return face switch
        {
            0 => Matrix4x4.CreateLookAt(pos, pos + Vector3.UnitX, Vector3.UnitY),
            1 => Matrix4x4.CreateLookAt(pos, pos - Vector3.UnitX, Vector3.UnitY),
            2 => Matrix4x4.CreateLookAt(pos, pos + Vector3.UnitY, -Vector3.UnitZ),
            3 => Matrix4x4.CreateLookAt(pos, pos - Vector3.UnitY, Vector3.UnitZ),
            4 => Matrix4x4.CreateLookAt(pos, pos + Vector3.UnitZ, Vector3.UnitY),
            5 => Matrix4x4.CreateLookAt(pos, pos - Vector3.UnitZ, Vector3.UnitY),
            _ => Matrix4x4.Identity
        };
    }

    public IEnumerable<SceneNode> GetAllocatedNodes() => _allocations.Keys;

    public int GetLayerCount() => ArrayLayers;

    private float CalculateScore(SceneNode node, Camera cam) {
        if (!_allocations.TryGetValue(node, out _)) // Avoid re-calculating if already processed? No, this is pre-alloc.
        {

        }


        var dist = Vector3.Distance(node.ComputeWorldTransform(Matrix4x4.Identity).Translation, cam.Position);
        if (dist < 0.1f) dist = 0.1f;

        return node.Light!.Intensity / (dist * dist);
    }

    public void Dispose() {
        foreach (var fb in _layerFramebuffers) fb?.Dispose();
        _textureArray.Dispose();
        _textureView.Dispose();
        _sampler.Dispose();
        _layout.Dispose();
        _resourceSet.Dispose();
    }
}
