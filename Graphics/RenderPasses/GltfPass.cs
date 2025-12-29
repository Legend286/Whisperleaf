using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Veldrid;
using Veldrid.SPIRV;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics.Assets;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.Immediate;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;
using Whisperleaf.Graphics.Shadows;
using TextureType = Veldrid.TextureType;

namespace Whisperleaf.Graphics.RenderPasses;

public sealed class GltfPass : IRenderPass, IDisposable {
    private readonly GraphicsDevice _gd;
    private readonly Pipeline _pipeline;
    private readonly CameraUniformBuffer _cameraBuffer;
    private readonly ModelUniformBuffer _modelBuffer; // Full set for MDI/Culling
    private readonly ModelUniformBuffer _compactModelBuffer; // Visible set for main pass
    private readonly LightUniformBuffer _lightBuffer;
    private readonly ShadowDataBuffer _shadowDataBuffer;
    private readonly GeometryBuffer _geometryBuffer;
    private readonly SceneBVH _staticBvh = new();
    private readonly SceneBVH _dynamicBvh = new();
    private readonly List<int> _staticIndices = new();
    private readonly List<int> _dynamicIndices = new();

    private readonly Dictionary<string, MeshGpu> _meshCache = new();
    private readonly Dictionary<string, MeshGpu> _customMeshCache = new();
    private readonly Dictionary<string, int> _materialCache = new();
    private readonly List<MaterialData> _materials = new();
    private readonly List<MeshInstance> _meshInstances = new();
    private readonly Dictionary<SceneNode, int> _nodeToInstance = new();
    private readonly Dictionary<SceneNode, SceneNode?> _nodeParents = new();
    private readonly Dictionary<SceneNode, Matrix4x4> _nodeWorldTransforms = new();
    private bool _loggedFirstInstance;
    private readonly List<ModelUniform> _visibleTransforms = new();
    private readonly List<SceneNode> _lightNodes = new();
    private readonly List<SceneNode> _visibleLights = new();
    private readonly List<LightUniform> _manualLights = new();
    private SceneNode? _selectedNode;
    private bool _structureChanged;

    // Forward+ Resources
    private Texture _lightGridTexture;
    private TextureView _lightGridView;
    private TextureView _lightGridSampledView;
    private DeviceBuffer _lightIndexListBuffer;
    private DeviceBuffer _lightIndexCounterBuffer;
    private Pipeline _lightCullPipeline;
    private ResourceSet _lightCullResourceSet;
    private ResourceLayout _lightCullLayout;
    private ResourceLayout _lightCullReadLayout;
    private ResourceSet _lightCullReadResourceSet;
    private Vector2 _lastScreenSize = Vector2.Zero;

    private TextureView? _hiZView;
    private Texture _dummyHiZ;
    private TextureView _dummyHiZView;

    public ShadowAtlas? ShadowAtlas { get; set; }

    public void SetHiZTexture(TextureView? view)
    {
        _hiZView = view;
        RecreateLightCullResourceSet();
    }

    // Statistics
    public int DrawCalls { get; private set; }

    public int RenderedInstances { get; private set; }

    public long RenderedTriangles { get; private set; }

    public long RenderedVertices { get; private set; }

    public SceneBVH.BVHStats CullingStats { get; private set; }

    public int UniqueMaterialCount => _materials.Count;

    public int TotalInstances => _meshInstances.Count;

    public int SourceMeshes => _meshCache.Count;

    public long SourceVertices { get; private set; }

    public long SourceIndices { get; private set; }

    public long TotalSceneTriangles { get; private set; }
    public int StructureVersion { get; private set; }

    public bool IsGizmoActive { get; set; }

    public ResourceSet? CsmResourceSet { get; set; }

    public DeviceBuffer LightDataBuffer => _lightBuffer.DataBuffer;
    public DeviceBuffer LightParamsBuffer => _lightBuffer.ParamBuffer;
    public TextureView LightGridSampledView => _lightGridSampledView;
    public DeviceBuffer LightIndexListBuffer => _lightIndexListBuffer;
    public DeviceBuffer ShadowDataBuffer => _shadowDataBuffer.Buffer;

    public IReadOnlyList<MeshInstance> MeshInstances => _meshInstances;
    
    public GeometryBuffer GeometryBuffer => _geometryBuffer;

    public ModelUniformBuffer ModelBuffer => _modelBuffer;
    
    public CameraUniformBuffer CameraBuffer => _cameraBuffer;

    public IReadOnlyList<SceneNode> LightNodes => _lightNodes;

    public IReadOnlyList<SceneNode> VisibleLights => _visibleLights;

    public struct MeshInstance {
        public readonly MeshGpu Mesh;
        public readonly int MaterialIndex;
        public readonly int TransformIndex; // Maps to SSBO index
        public readonly SceneNode Node;

        public MeshInstance(MeshGpu mesh, int materialIndex, int transformIndex, SceneNode node) {
            Mesh = mesh;
            MaterialIndex = materialIndex;
            TransformIndex = transformIndex;
            Node = node;
        }
    }

    public MaterialData? GetMaterial(int index) {
        if (index >= 0 && index < _materials.Count) {
            return _materials[index];
        }
        return null;
    }

    public void UpdateMaterial(int index, MaterialAsset asset)
    {
        if (index < 0 || index >= _materials.Count) return;
        var mat = _materials[index];
        
        bool texturesChanged = 
            !string.Equals(mat.BaseColorPath, asset.BaseColorTexture, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(mat.NormalPath, asset.NormalTexture, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(mat.EmissivePath, asset.EmissiveTexture, StringComparison.OrdinalIgnoreCase) ||
            (mat.UsePackedRMA && !string.Equals(mat.MetallicPath, asset.RMATexture, StringComparison.OrdinalIgnoreCase)) ||
            (!mat.UsePackedRMA && !string.IsNullOrEmpty(asset.RMATexture));

        bool alphaChanged = mat.AlphaMode != asset.AlphaMode;

        mat.Name = asset.Name;
        mat.BaseColorFactor = asset.BaseColorFactor;
        mat.EmissiveFactor = asset.EmissiveFactor;
        mat.MetallicFactor = asset.MetallicFactor;
        mat.RoughnessFactor = asset.RoughnessFactor;
        mat.AlphaMode = asset.AlphaMode;
        mat.AlphaCutoff = asset.AlphaCutoff;
        
        if (alphaChanged) _structureChanged = true;
        
        mat.BaseColorPath = asset.BaseColorTexture;
        mat.NormalPath = asset.NormalTexture;
        mat.EmissivePath = asset.EmissiveTexture;
        
        bool usePacked = !string.IsNullOrEmpty(asset.RMATexture);
        if (usePacked) {
            mat.MetallicPath = asset.RMATexture;
            mat.RoughnessPath = asset.RMATexture;
            mat.OcclusionPath = asset.RMATexture;
        } else {
            mat.MetallicPath = null;
            mat.RoughnessPath = null;
            mat.OcclusionPath = null;
        }
        mat.UsePackedRMA = usePacked;

        if (texturesChanged)
        {
            _gd.WaitForIdle();
            mat.Dispose(); // This disposes descriptors too, need to be careful if in use? 
            // Wait for idle ensures it's safe.
            MaterialUploader.Upload(_gd, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout, mat);
        }
        else
        {
            if (mat.ParamsBuffer != null)
            {
                var materialParams = new MaterialParams(
                    mat.BaseColorFactor,
                    mat.EmissiveFactor,
                    mat.MetallicFactor,
                    mat.RoughnessFactor,
                    mat.UsePackedRMA,
                    mat.AlphaCutoff,
                    (int)mat.AlphaMode
                );
                _gd.UpdateBuffer(mat.ParamsBuffer, 0, ref materialParams);
            }
        }
    }

    public void UpdateMaterial(string path, MaterialAsset asset)
    {
        string fullPath = Path.GetFullPath(path);
        for (int i = 0; i < _materials.Count; i++)
        {
            var mat = _materials[i];
            // Normalize existing path
            string? matPath = mat.AssetPath != null ? Path.GetFullPath(mat.AssetPath) : null;
            
            if (string.Equals(matPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateMaterial(i, asset);
            }
        }
    }

    public GltfPass(GraphicsDevice gd, ResourceLayout shadowAtlasLayout, ResourceLayout csmLayout, OutputDescription outputDescription, CameraUniformBuffer cameraBuffer) {
        _gd = gd;
        _geometryBuffer = new GeometryBuffer(gd);

        _cameraBuffer = cameraBuffer;
        _modelBuffer = new ModelUniformBuffer(gd);
        _compactModelBuffer = new ModelUniformBuffer(gd);
        _lightBuffer = new LightUniformBuffer(gd);
        _lightBuffer.BufferResized += () => {
             // Defer to next frame or force resize immediately?
             // Since this happens during PrepareRender (UpdateGPU), it's safe to resize immediately
             // provided we aren't using the old set yet.
             // PrepareRender calls CullLights immediately after UpdateGPU.
             // So we must update the set NOW.
             RecreateLightCullResourceSet();
        };
        _shadowDataBuffer = new ShadowDataBuffer(gd);

        InitializeLightCulling();

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
            new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4),
            new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
        );

        _pipeline = PipelineFactory.CreatePipeline(
            gd,
            "Graphics/Shaders/PBR.vert",
            "Graphics/Shaders/PBR.frag",
            vertexLayout,
            outputDescription,
            enableDepth: true,
            enableBlend: false,
            extraLayouts: new[] {
                _cameraBuffer.Layout,
                _modelBuffer.Layout,
                PbrLayout.MaterialLayout,
                PbrLayout.MaterialParamsLayout,
                _lightBuffer.Layout,
                _lightBuffer.ParamLayout,
                _shadowDataBuffer.Layout,
                shadowAtlasLayout,
                _lightCullReadLayout,
                csmLayout
            },
            depthWrite: false
        );
    }

    public void AddLight(LightUniform light) {
        _lightBuffer.AddLight(light);
    }

    public void SetSelectedNode(SceneNode? node) {
        _selectedNode = node;
    }

    private void InitializeLightCulling()
    {
        var factory = _gd.ResourceFactory;

        _lightCullLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute),
            new ResourceLayoutElementDescription("LightData", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("LightParams", ResourceKind.UniformBuffer, ShaderStages.Compute),
            new ResourceLayoutElementDescription("LightGrid", ResourceKind.TextureReadWrite, ShaderStages.Compute),
            new ResourceLayoutElementDescription("LightIndices", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
            new ResourceLayoutElementDescription("Counter", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
            new ResourceLayoutElementDescription("HiZMap", ResourceKind.TextureReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("HiZSampler", ResourceKind.Sampler, ShaderStages.Compute)
        ));

        _lightCullReadLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("LightGrid", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LightGridSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LightIndices", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment)
        ));

        _dummyHiZ = _gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));
        _dummyHiZView = _gd.ResourceFactory.CreateTextureView(_dummyHiZ);

        var computeShader = ShaderCache.GetShader(_gd, ShaderStages.Compute, "Graphics/Shaders/LightCulling.comp");
        var pd = new ComputePipelineDescription(computeShader, _lightCullLayout, 16, 16, 1);
        _lightCullPipeline = factory.CreateComputePipeline(pd);
        
        ResizeLightCullingResources(1280, 720);
    }

    public void ResizeLightCullingResources(uint width, uint height)
    {
        if (_lastScreenSize.X == width && _lastScreenSize.Y == height) return;
        _lastScreenSize = new Vector2(width, height);

        _gd.WaitForIdle(); // Critical: ensure resources not in use
        var factory = _gd.ResourceFactory;

        _lightGridTexture?.Dispose();
        _lightGridView?.Dispose();
        _lightGridSampledView?.Dispose();
        _lightIndexListBuffer?.Dispose();
        _lightIndexCounterBuffer?.Dispose();
        _lightCullReadResourceSet?.Dispose();

        uint tilesX = (width + 15) / 16;
        uint tilesY = (height + 15) / 16;

        _lightGridTexture = factory.CreateTexture(new TextureDescription(
            tilesX, tilesY, 1, 1, 1,
            PixelFormat.R32_G32_UInt, 
            TextureUsage.Storage | TextureUsage.Sampled, 
            TextureType.Texture2D));

        _lightGridView = factory.CreateTextureView(_lightGridTexture);
        _lightGridSampledView = factory.CreateTextureView(_lightGridTexture);

        uint maxLightsPerTile = 1024;
        uint totalIndices = tilesX * tilesY * maxLightsPerTile;
        _lightIndexListBuffer = factory.CreateBuffer(new BufferDescription(totalIndices * 4, BufferUsage.StructuredBufferReadWrite, 4));

        _lightIndexCounterBuffer = factory.CreateBuffer(new BufferDescription(4, BufferUsage.StructuredBufferReadWrite, 4));

        RecreateLightCullResourceSet();

        _lightCullReadResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            _lightCullReadLayout,
            _lightGridSampledView,
            _gd.PointSampler,
            _lightIndexListBuffer
        ));
    }

    private void RecreateLightCullResourceSet()
    {
        _lightCullResourceSet?.Dispose();
        _lightCullResourceSet = _gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _lightCullLayout,
            _cameraBuffer.Buffer,
            _lightBuffer.DataBuffer,
            _lightBuffer.ParamBuffer,
            _lightGridView,
            _lightIndexListBuffer,
            _lightIndexCounterBuffer,
            _hiZView ?? _dummyHiZView,
            _gd.PointSampler
        ));
    }

    private void CullLights(CommandList cl)
    {
        if (_lightIndexCounterBuffer == null)
        {
            Console.WriteLine("[GltfPass] CullLights skipped: CounterBuffer is null");
            return;
        }
        
        // Reset counter
        uint[] resetCount = { 0u };
        cl.UpdateBuffer(_lightIndexCounterBuffer, 0, resetCount);
        
        cl.SetPipeline(_lightCullPipeline);
        cl.SetComputeResourceSet(0, _lightCullResourceSet);
        
        uint tilesX = (uint)(_lastScreenSize.X + 15) / 16;
        uint tilesY = (uint)(_lastScreenSize.Y + 15) / 16;
        
        cl.Dispatch(tilesX, tilesY, 1);
    }

    public void RefreshStructure() => SortAndUploadInstances();

    public void RebuildBVH() {
        if (_meshInstances.Count == 0 && _lightNodes.Count == 0) return;

        // Rebuild static BVH only if explicitly requested (e.g. gizmo usage)
        BuildBVH(_staticBvh, _staticIndices);
    }

    private void RebuildDynamicBVH() {
        BuildBVH(_dynamicBvh, _dynamicIndices);
    }

    private void BuildBVH(SceneBVH bvh, List<int> indices) {
        if (indices.Count == 0) return;

        bvh.Build(indices, (i) =>
        {
            // Check if mesh or light
            if (i < _meshInstances.Count) {
                var inst = _meshInstances[i];

                if (!inst.Node.IsVisible) return (new Vector3(float.MaxValue), new Vector3(float.MinValue));

                var mesh = inst.Mesh;

                if (_nodeWorldTransforms.TryGetValue(inst.Node, out var world)) {
                    return GetWorldAABB(mesh, world);
                }
            }
            else {
                int lightIndex = i - _meshInstances.Count;

                if (lightIndex >= 0 && lightIndex < _lightNodes.Count) {
                    var node = _lightNodes[lightIndex];

                    if (!node.IsVisible) return (new Vector3(float.MaxValue), new Vector3(float.MinValue));

                    if (_nodeWorldTransforms.TryGetValue(node, out var world)) {
                        return GetLightAABB(node, world);
                    }
                }
            }


            return (new Vector3(-100000), new Vector3(100000));
        });
    }

    private (Vector3 Min, Vector3 Max) GetLightAABB(SceneNode node, Matrix4x4 world) {
        var light = node.Light;

        if (light == null) return (Vector3.Zero, Vector3.Zero);

        var pos = world.Translation;
        float range = light.Range;

        if (light.Type == 0) {
            // Point
            return (pos - new Vector3(range), pos + new Vector3(range));
        }
        else if (light.Type == 2) {
            // Spot
            // AABB of cone
            var dir = Vector3.TransformNormal(new Vector3(0, 0, -1), world);

            // Base circle center
            var C = pos + dir * range;

            // Base circle radius
            float radius = range * MathF.Tan(light.OuterCone);

            var nx = dir.X;
            var ny = dir.Y;
            var nz = dir.Z;

            // Extents of circle
            Vector3 extent = new Vector3(
                radius * MathF.Sqrt(1 - nx * nx),
                radius * MathF.Sqrt(1 - ny * ny),
                radius * MathF.Sqrt(1 - nz * nz)
            );

            Vector3 minC = C - extent;
            Vector3 maxC = C + extent;

            // AABB of cone is Union(Tip, BaseCircle)
            Vector3 min = Vector3.Min(pos, minC);
            Vector3 max = Vector3.Max(pos, maxC);

            return (min, max);
        }


        // Directional light (Type 1) is infinite, use huge bounds
        return (new Vector3(-100000), new Vector3(100000));
    }

    public void DrawDebug(ImmediateRenderer renderer, bool showBVH, bool showDynamicBVH, bool showSelection) {
        if (showBVH) {
            _staticBvh.DrawDebug(renderer, RgbaFloat.White);
        }


        if (showDynamicBVH) {
            _dynamicBvh.DrawDebug(renderer, RgbaFloat.Green);
        }


        if (showSelection && _selectedNode != null) {
            if (_nodeToInstance.TryGetValue(_selectedNode, out int index)) {
                var inst = _meshInstances[index];

                if (_nodeWorldTransforms.TryGetValue(_selectedNode, out var world)) {
                    var (min, max) = GetWorldAABB(inst.Mesh, world);
                    renderer.DrawAABB(min, max, RgbaFloat.Yellow);
                }
            }
            else if (_selectedNode.Light != null && _nodeWorldTransforms.TryGetValue(_selectedNode, out var world)) {
                // Draw light AABB
                var (min, max) = GetLightAABB(_selectedNode, world);
                renderer.DrawAABB(min, max, RgbaFloat.Yellow);
            }
        }


        foreach (var node in _visibleLights) {
            if (_nodeWorldTransforms.TryGetValue(node, out var world)) {
                var pos = world.Translation;
                var color = new RgbaFloat(node.Light!.Color.X, node.Light.Color.Y, node.Light.Color.Z, 1.0f);

                float s = 0.2f;
                renderer.DrawLine(pos + new Vector3(s, 0, 0), pos - new Vector3(s, 0, 0), color);
                renderer.DrawLine(pos + new Vector3(0, s, 0), pos - new Vector3(0, s, 0), color);
                renderer.DrawLine(pos + new Vector3(0, 0, s), pos - new Vector3(0, 0, s), color);

                if (node.Light.Type != 0) {
                    var dir = Vector3.TransformNormal(new Vector3(0, 0, -1), world);
                    renderer.DrawLine(pos, pos + dir * 2.0f, color);
                }
            }
        }
    }

    private (Vector3 Min, Vector3 Max) GetWorldAABB(MeshGpu mesh, Matrix4x4 world) {
        var center = (mesh.AABBMin + mesh.AABBMax) * 0.5f;
        var extents = (mesh.AABBMax - mesh.AABBMin) * 0.5f;

        var worldCenter = Vector3.Transform(center, world);

        var absM11 = Math.Abs(world.M11);
        var absM12 = Math.Abs(world.M12);
        var absM13 = Math.Abs(world.M13);
        var absM21 = Math.Abs(world.M21);
        var absM22 = Math.Abs(world.M22);
        var absM23 = Math.Abs(world.M23);
        var absM31 = Math.Abs(world.M31);
        var absM32 = Math.Abs(world.M32);
        var absM33 = Math.Abs(world.M33);

        float newEx = absM11 * extents.X + absM21 * extents.Y + absM31 * extents.Z;
        float newEy = absM12 * extents.X + absM22 * extents.Y + absM32 * extents.Z;
        float newEz = absM13 * extents.X + absM23 * extents.Y + absM33 * extents.Z;

        var worldMin = new Vector3(worldCenter.X - newEx, worldCenter.Y - newEy, worldCenter.Z - newEz);
        var worldMax = new Vector3(worldCenter.X + newEx, worldCenter.Y + newEy, worldCenter.Z + newEz);

        return (worldMin, worldMax);
    }

    public void AddCustomMesh(string name, MeshData data) {
        Console.WriteLine($"[GltfPass] Adding custom mesh '{name}' with {data.Vertices.Length} floats and {data.Indices.Length} indices.");

        if (_customMeshCache.ContainsKey(name)) {
            _customMeshCache[name].Dispose();
        }


        _customMeshCache[name] = new MeshGpu(_geometryBuffer, data);
    }

    private void PreloadImage(string? path, ref Image<Rgba32>? image)
    {
        if (string.IsNullOrEmpty(path)) return;
        
        // Skip if already on GPU
        if (CachedTextureUploader.IsCached(path)) return;
        
        try 
        {
            if (Path.GetExtension(path).Equals(".wltex", StringComparison.OrdinalIgnoreCase))
            {
                 image = CachedTextureUploader.LoadImage(path);
            }
            else if (File.Exists(path))
            {
                 image = Image.Load<Rgba32>(path);
            }
        }
        catch {}
    }

    private MaterialData?[] PrepareMaterials(SceneAsset scene) {
        var materials = new MaterialData?[scene.Materials.Count];
        
        for (int i = 0; i < scene.Materials.Count; i++) {
            var src = scene.Materials[i];
            string hash = (!string.IsNullOrEmpty(src.AssetPath)) ? "FILE:" + src.AssetPath : ComputeMaterialHash(src);
            
            bool isCached = false;
            if (_materialCache.ContainsKey(hash)) isCached = true;
            if (isCached) continue;

            MaterialData? material = null;
            if (!string.IsNullOrEmpty(src.AssetPath) && File.Exists(src.AssetPath))
            {
                 try 
                 {
                    var asset = MaterialAsset.Load(src.AssetPath);
                    material = new MaterialData
                    {
                        Name = asset.Name,
                        BaseColorFactor = asset.BaseColorFactor,
                        EmissiveFactor = asset.EmissiveFactor,
                        MetallicFactor = asset.MetallicFactor,
                        RoughnessFactor = asset.RoughnessFactor,
                        AlphaMode = asset.AlphaMode,
                        AlphaCutoff = asset.AlphaCutoff,
                        BaseColorPath = asset.BaseColorTexture,
                        NormalPath = asset.NormalTexture,
                        MetallicPath = asset.RMATexture, 
                        RoughnessPath = asset.RMATexture,
                        OcclusionPath = asset.RMATexture,
                        EmissivePath = asset.EmissiveTexture,
                        UsePackedRMA = !string.IsNullOrEmpty(asset.RMATexture),
                        AssetPath = src.AssetPath
                    };
                 }
                 catch {}
            }
            
            if (material == null)
            {
                material = new MaterialData {
                    Name = src.Name,
                    BaseColorFactor = src.BaseColorFactor,
                    EmissiveFactor = src.EmissiveFactor,
                    MetallicFactor = src.MetallicFactor,
                    RoughnessFactor = src.RoughnessFactor,
                    AlphaMode = src.AlphaMode,
                    AlphaCutoff = src.AlphaCutoff,
                    UsePackedRMA = !string.IsNullOrEmpty(src.RMAHash)
                };
                
                material.BaseColorPath = ResolveCachedTexture(src.BaseColorHash);
                material.NormalPath = ResolveCachedTexture(src.NormalHash);
                material.EmissivePath = ResolveCachedTexture(src.EmissiveHash);
                var rmaPath = ResolveCachedTexture(src.RMAHash);
                if (material.UsePackedRMA && rmaPath != null) {
                    material.MetallicPath = rmaPath;
                    material.RoughnessPath = rmaPath;
                    material.OcclusionPath = rmaPath;
                }
            }

            if (material != null)
            {
                PreloadImage(material.BaseColorPath, ref material.BaseColorImage);
                PreloadImage(material.NormalPath, ref material.NormalImage);
                PreloadImage(material.EmissivePath, ref material.EmissiveImage);
                if (material.UsePackedRMA)
                {
                     PreloadImage(material.MetallicPath, ref material.MetallicImage);
                }
                else
                {
                     PreloadImage(material.MetallicPath, ref material.MetallicImage);
                     PreloadImage(material.RoughnessPath, ref material.RoughnessImage);
                     PreloadImage(material.OcclusionPath, ref material.OcclusionImage);
                }
                
                materials[i] = material;
            }
        }
        
        return materials;
    }

    private int[] UploadMaterials(SceneAsset scene, MaterialData?[] preparedMaterials) {
        int[] map = new int[scene.Materials.Count];
        
        for (int i = 0; i < scene.Materials.Count; i++) {
            var src = scene.Materials[i];
            
            // 1. Check Cache
            string hash = (!string.IsNullOrEmpty(src.AssetPath)) ? "FILE:" + src.AssetPath : ComputeMaterialHash(src);
            if (_materialCache.TryGetValue(hash, out int cachedIndex)) {
                map[i] = cachedIndex;
                
                // If we prepared data but found it cached (race condition or re-check), dispose the prepared images
                preparedMaterials[i]?.Dispose();
                continue;
            }

            // 2. Use Prepared
            var material = preparedMaterials[i];
            if (material != null)
            {
                int newIndex = _materials.Count;
                map[i] = newIndex;
                _materialCache[hash] = newIndex;

                // This is fast now (just GPU copy)
                MaterialUploader.Upload(_gd, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout, material);
                
                // Clean up images immediately after upload
                material.BaseColorImage?.Dispose(); material.BaseColorImage = null;
                material.NormalImage?.Dispose(); material.NormalImage = null;
                material.EmissiveImage?.Dispose(); material.EmissiveImage = null;
                material.MetallicImage?.Dispose(); material.MetallicImage = null;
                material.RoughnessImage?.Dispose(); material.RoughnessImage = null;
                material.OcclusionImage?.Dispose(); material.OcclusionImage = null;

                _materials.Add(material);
            }
        }
        return map;
    }

    public void LoadScene(string scenePath) {
        LoadScene(scenePath, false);
    }

    public void LoadScene(string scenePath, bool additive)
    {
        try 
        {
            var sceneAsset = SceneAsset.Load(scenePath);
            LoadScene(sceneAsset, additive);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GltfPass] Load failed: {ex.Message}");
        }
    }

    public void LoadScene(SceneAsset sceneAsset, bool additive = false)
    {
        _gd.WaitForIdle();
        if (!additive) ClearResources();

        // 1. Prepare Materials (Synchronous)
        var materials = PrepareMaterials(sceneAsset);
        
        // 2. Upload Materials
        int[] materialMap = UploadMaterials(sceneAsset, materials);
        
        // 3. Load Meshes
        LoadMeshes(sceneAsset, materialMap);
        
        _structureChanged = true;
    }

    private void LoadMeshes(SceneAsset scene, int[] materialMap)
    {
        if (scene.RootNodes.Count == 0) return;

        foreach (var node in scene.RootNodes)
        {
            TraverseAndLoad(node, Matrix4x4.Identity, null, materialMap);
        }
    }

    private void TraverseAndLoad(SceneNode node, Matrix4x4 parentWorld, SceneNode? parent, int[] materialMap)
    {
        var worldTransform = node.LocalTransform * parentWorld;

        _nodeParents[node] = parent;
        _nodeWorldTransforms[node] = worldTransform;
        if (node.Light != null)
        {
            _lightNodes.Add(node);
            _structureChanged = true;
        }

        if (node.Mesh != null)
        {
            string hash = node.Mesh.MeshHash;
            MeshGpu? meshGpu = null;
            MeshData? data = null;

            if (!_meshCache.ContainsKey(hash) && !_customMeshCache.ContainsKey(hash))
            {
                if (AssetCache.HasMesh(hash, out var meshPath))
                {
                    try 
                    {
                        data = WlMeshFormat.Read(meshPath, out _);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GltfPass] Failed to read mesh: {ex.Message}");
                    }
                }
            }

            AddMeshInstance(node, worldTransform, data, node.Mesh.MaterialIndex, materialMap);
        }

        foreach (var child in node.Children)
        {
            TraverseAndLoad(child, worldTransform, node, materialMap);
        }
    }

    private void AddMeshInstance(SceneNode node, Matrix4x4 world, MeshData? data, int matIdx, int[] materialMap)
    {
        MeshGpu? meshGpu = null;
        string hash = node.Mesh!.MeshHash;

        if (_meshCache.TryGetValue(hash, out meshGpu))
        {
            // Already exists
        }
        else if (_customMeshCache.TryGetValue(hash, out meshGpu))
        {
            // Custom mesh (e.g. primitive)
        }
        else if (data != null)
        {
            // Create new
            meshGpu = new MeshGpu(_geometryBuffer, data);
            _meshCache[hash] = meshGpu;
            SourceVertices += data.Vertices.Length / 12;
            SourceIndices += data.Indices.Length;
        }

        if (meshGpu != null)
        {
            int globalMaterialIndex = 0;
            if (matIdx >= 0 && matIdx < materialMap.Length)
            {
                globalMaterialIndex = materialMap[matIdx];
            }

            int transformIndex = _modelBuffer.Allocate(world);
            int instanceIndex = _meshInstances.Count;
            _meshInstances.Add(new MeshInstance(meshGpu, globalMaterialIndex, transformIndex, node));
            _nodeToInstance[node] = instanceIndex;
        }
    }

    public int InstanceCount => _meshInstances.Count;

    public void UpdateInstanceTransform(int instanceIndex, Matrix4x4 transform) {
        if ((uint)instanceIndex >= _meshInstances.Count)
            throw new ArgumentOutOfRangeException(nameof(instanceIndex));
    }

    public bool TryGetWorldTransform(SceneNode node, out Matrix4x4 transform) {
        if (_nodeWorldTransforms.TryGetValue(node, out transform)) {
            if (IsDegenerateMatrix(transform)) {
                transform = Matrix4x4.Identity;
                _nodeWorldTransforms[node] = transform;
            }


            return true;
        }


        return false;
    }

    public bool ApplyWorldTransform(SceneNode node, Matrix4x4 worldTransform) {
        if (!_nodeWorldTransforms.ContainsKey(node)) {
            return false;
        }


        Matrix4x4 parentWorld = Matrix4x4.Identity;

        if (_nodeParents.TryGetValue(node, out var parent) && parent != null &&
            _nodeWorldTransforms.TryGetValue(parent, out var cachedParentWorld)) {
            parentWorld = cachedParentWorld;
        }


        if (!Matrix4x4.Invert(parentWorld, out var parentInverse)) {
            parentInverse = Matrix4x4.Identity;
        }


        node.LocalTransform = worldTransform * parentInverse;

        if (IsDegenerateMatrix(node.LocalTransform)) {
            node.LocalTransform = Matrix4x4.Identity;
        }


        UpdateWorldRecursive(node, parentWorld);

        return true;
    }

    public (List<int> Results, SceneBVH.BVHStats Stats) Query(Frustum frustum) {
        var (staticIndices, staticStats) = _staticBvh.Query(frustum);
        var (dynamicIndices, dynamicStats) = _dynamicBvh.Query(frustum);

        var results = new List<int>(staticIndices.Count + dynamicIndices.Count);
        results.AddRange(staticIndices);
        results.AddRange(dynamicIndices);

        var stats = staticStats;
        stats.NodesVisited += dynamicStats.NodesVisited;
        stats.NodesCulled += dynamicStats.NodesCulled;
        stats.LeafsTested += dynamicStats.LeafsTested;

        return (results, stats);
    }

    private List<int> _cachedVisibleIndices = new();

    public void Update(Camera camera) {
        if (_structureChanged)
        {
            SortAndUploadInstances();
            _structureChanged = false;
        }

        // Rebuild dynamic BVH every frame
        RebuildDynamicBVH();

        _cachedVisibleIndices.Clear();
        _visibleTransforms.Clear();
        _visibleLights.Clear();

        var viewProj = camera.ViewMatrix * camera.ProjectionMatrix;
        var frustum = new Frustum(viewProj);

        // Query both BVHs
        var (visibleIndices, stats) = Query(frustum);
        CullingStats = stats;

        // Force include selected node and its subtree (bypass culling)
        if (_selectedNode != null && IsGizmoActive) {
            ForceVisibleRecursive(_selectedNode, visibleIndices);
        }


        visibleIndices.Sort();
        _cachedVisibleIndices = visibleIndices;

        // Separate Meshes and Lights
        // Also populate _visibleTransforms for meshes
        foreach (var idx in visibleIndices) {
            if (idx < _meshInstances.Count) {
                // Is Mesh
                // Note: We don't populate _visibleTransforms here because we need batching logic from Render
                // But we can identify it's a mesh
            }
            else {
                // Is Light
                int lightIndex = idx - _meshInstances.Count;

                if (lightIndex >= 0 && lightIndex < _lightNodes.Count) {
                    _visibleLights.Add(_lightNodes[lightIndex]);
                }
            }
        }
    }

    public void UpdateModelBuffer() {
        if (_meshInstances.Count == 0) return;
        
        var uniforms = new ModelUniform[_meshInstances.Count];
        
        for (int i = 0; i < _meshInstances.Count; i++) {
            var inst = _meshInstances[i];
            if (_nodeWorldTransforms.TryGetValue(inst.Node, out var world)) {
                uniforms[i] = new ModelUniform(world);
            } else {
                uniforms[i] = new ModelUniform(Matrix4x4.Identity);
            }
        }
        
        _modelBuffer.UpdateAll(uniforms);
    }

    public void PrepareResources(GraphicsDevice gd, Camera? camera, Vector2 screenSize, int debugMode)
    {
        if (camera == null) return;
        
        ResizeLightCullingResources((uint)screenSize.X, (uint)screenSize.Y);
        
        // _cameraBuffer.Update(gd, camera, screenSize, debugMode); // Updated by Renderer now
        
        CollectLights();
        _lightBuffer.UpdateGPU();
    }

    public void RecordCulling(CommandList cl)
    {
        CullLights(cl);
    }

    public void Render(GraphicsDevice gd, CommandList cl, Camera? camera, Vector2 screenSize, int debugMode) {
        if (camera == null)
            return;

        // Reset frame stats
        DrawCalls = 0;
        RenderedInstances = 0;
        RenderedTriangles = 0;
        RenderedVertices = 0;

        if (_meshInstances.Count == 0)
            return;

        // Use cached visible indices from Update()
        var visibleIndices = _cachedVisibleIndices;

        var drawRanges = new List<(MeshGpu Mesh, int MaterialIndex, int InstanceCount, int StartIndex)>();

        if (visibleIndices.Count > 0) {
            int listIndex = 0;
            int currentVisibleStart = 0;

            while (listIndex < visibleIndices.Count) {
                int instanceIdx = visibleIndices[listIndex];

                // Skip lights in mesh processing
                if (instanceIdx >= _meshInstances.Count) {
                    listIndex++;

                    continue;
                }


                var batchStartInstance = _meshInstances[instanceIdx];
                var batchMesh = batchStartInstance.Mesh;
                int batchMaterialIndex = batchStartInstance.MaterialIndex;
                int batchVisibleCount = 0;

                while (listIndex < visibleIndices.Count) {
                    int nextIdx = visibleIndices[listIndex];

                    if (nextIdx == instanceIdx && listIndex > 0 && visibleIndices[listIndex - 1] == instanceIdx) {
                        listIndex++;

                        continue;
                    }


                    instanceIdx = nextIdx;

                    // Stop if we hit a light
                    if (nextIdx >= _meshInstances.Count)
                        break;

                    var next = _meshInstances[nextIdx];

                    if (next.Mesh != batchMesh || next.MaterialIndex != batchMaterialIndex)
                        break;

                    if (_nodeWorldTransforms.TryGetValue(next.Node, out var world)) {
                        _visibleTransforms.Add(new ModelUniform(world));
                        batchVisibleCount++;
                    }


                    listIndex++;
                }


                if (batchVisibleCount > 0) {
                    drawRanges.Add((batchMesh, batchMaterialIndex, batchVisibleCount, currentVisibleStart));
                    currentVisibleStart += batchVisibleCount;
                }
            }
        }


        // 2. Upload Buffer
        _compactModelBuffer.UpdateAll(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_visibleTransforms));

        // 3. Draw Pass
        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(1, _compactModelBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(4, _lightBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(5, _lightBuffer.ParamResourceSet);
        cl.SetGraphicsResourceSet(6, _shadowDataBuffer.ResourceSet);

        if (ShadowAtlas != null) {
            cl.SetGraphicsResourceSet(7, ShadowAtlas.ResourceSet);
        }

        cl.SetGraphicsResourceSet(8, _lightCullReadResourceSet);

        if (CsmResourceSet != null) {
            cl.SetGraphicsResourceSet(9, CsmResourceSet);
        }

        cl.SetVertexBuffer(0, _geometryBuffer.VertexBuffer);
        cl.SetIndexBuffer(_geometryBuffer.IndexBuffer, IndexFormat.UInt32);

        foreach (var batch in drawRanges) {
            // Bind Material
            var material = ResolveMaterial(batch.MaterialIndex);

            if (material == null || material.ResourceSet == null || material.ParamsResourceSet == null)
            {
                // Skip invalid material draws to avoid GPU crash
                continue;
            }

            cl.SetGraphicsResourceSet(2, material.ResourceSet);
            cl.SetGraphicsResourceSet(3, material.ParamsResourceSet);

            cl.DrawIndexed(
                batch.Mesh.Range.IndexCount,
                (uint)batch.InstanceCount,
                batch.Mesh.Range.IndexStart,
                batch.Mesh.Range.VertexOffset,
                (uint)batch.StartIndex);

            // Stats
            DrawCalls++;
            RenderedInstances += batch.InstanceCount;
            RenderedTriangles += (long)(batch.Mesh.IndexCount / 3) * batch.InstanceCount;
            RenderedVertices += (long)batch.Mesh.Range.VertexCount * batch.InstanceCount;
        }
    }

    private void ForceVisibleRecursive(SceneNode node, List<int> visibleIndices) {
        if (_nodeToInstance.TryGetValue(node, out int idx)) {
            visibleIndices.Add(idx);
        }
        // Force visible lights too?
        // Lights are not in _nodeToInstance map which maps to MeshInstances only.
        // We need to map light node to light index.
        // But since we sort everything, we can just find it.
        // Optimization: Create _nodeToLightIndex map?


        foreach (var child in node.Children) {
            ForceVisibleRecursive(child, visibleIndices);
        }
    }

    private void SortAndUploadInstances() {
        if (_meshInstances.Count == 0 && _lightNodes.Count == 0) return;

        // Sort meshes: Opaque first, then AlphaMask, then Blend
        _meshInstances.Sort((a, b) =>
        {
            var matA = GetMaterial(a.MaterialIndex);
            var matB = GetMaterial(b.MaterialIndex);
            
            int modeA = matA != null ? (int)matA.AlphaMode : 0;
            int modeB = matB != null ? (int)matB.AlphaMode : 0;
            
            int cmpMode = modeA.CompareTo(modeB);
            if (cmpMode != 0) return cmpMode;

            int cmpMat = a.MaterialIndex.CompareTo(b.MaterialIndex);
            if (cmpMat != 0) return cmpMat;

            return a.Mesh.GetHashCode().CompareTo(b.Mesh.GetHashCode());
        });

        var uniforms = new ModelUniform[_meshInstances.Count];
        _nodeToInstance.Clear();
        TotalSceneTriangles = 0;

        _staticIndices.Clear();
        _dynamicIndices.Clear();

        // Meshes
        for (int i = 0; i < _meshInstances.Count; i++) {
            var inst = _meshInstances[i];

            if (inst.Node.IsVisible) {
                if (inst.Node.IsStatic) _staticIndices.Add(i);
                else _dynamicIndices.Add(i);
            }

            TotalSceneTriangles += inst.Mesh.IndexCount / 3;

            if (!_nodeWorldTransforms.TryGetValue(inst.Node, out var world))
                world = Matrix4x4.Identity;

            uniforms[i] = new ModelUniform(world);

            _meshInstances[i] = new MeshInstance(inst.Mesh, inst.MaterialIndex, i, inst.Node);
            _nodeToInstance[inst.Node] = i;
        }

        // Lights
        int meshCount = _meshInstances.Count;
        for (int i = 0; i < _lightNodes.Count; i++) {
            var node = _lightNodes[i];
            if (!node.IsVisible) continue;

            int globalIndex = meshCount + i;
            if (node.IsStatic) _staticIndices.Add(globalIndex);
            else _dynamicIndices.Add(globalIndex);
        }

        BuildBVH(_staticBvh, _staticIndices);
        RebuildDynamicBVH(); 

        _modelBuffer.EnsureCapacity(_meshInstances.Count);        
        _modelBuffer.UpdateAll(uniforms);

        StructureVersion++;
    }

    private MaterialData? ResolveMaterial(int index) {
        if (index >= 0 && index < _materials.Count) {
            return _materials[index];
        }


        return null;
    }

    private string ComputeMaterialHash(MaterialReference mat) {
        return $"{mat.BaseColorFactor}_{mat.EmissiveFactor}_{mat.MetallicFactor}_{mat.RoughnessFactor}_{mat.AlphaMode}_{mat.AlphaCutoff}_{mat.BaseColorHash}_{mat.NormalHash}_{mat.RMAHash}_{mat.EmissiveHash}";
    }

    private static string? ResolveCachedTexture(string? hash) {
        if (string.IsNullOrEmpty(hash))
            return null;

        return AssetCache.HasTexture(hash, out var path) ? path : null;
    }

    private void ClearResources() {
        _gd.WaitForIdle(); // Ensure no pending frames are using resources
        Console.WriteLine("[GltfPass] ClearResources called");

        foreach (var mesh in _meshCache.Values) {
            mesh.Dispose();
        }


        _meshCache.Clear();
        // Do NOT clear custom mesh cache

        SourceVertices = 0;
        SourceIndices = 0;

        foreach (var mat in _materials) {
            mat.Dispose();
        }


        _materials.Clear();
        _materialCache.Clear();

        _meshInstances.Clear();
        _modelBuffer.Clear();
        _nodeToInstance.Clear();
        _nodeParents.Clear();
        _nodeWorldTransforms.Clear();
        _loggedFirstInstance = false;

        _lightNodes.Clear();

        _staticIndices.Clear();
        _dynamicIndices.Clear();
    }

    private void UpdateWorldRecursive(SceneNode node, Matrix4x4 parentWorld) {
        var worldTransform = node.LocalTransform * parentWorld;

        if (IsDegenerateMatrix(worldTransform)) {
            worldTransform = Matrix4x4.Identity;
        }


        _nodeWorldTransforms[node] = worldTransform;

        foreach (var child in node.Children) {
            UpdateWorldRecursive(child, worldTransform);
        }
    }

    private static bool IsDegenerateMatrix(in Matrix4x4 matrix) {
        const float epsilon = 1e-6f;

        bool allZero = matrix.M11 == 0f && matrix.M12 == 0f && matrix.M13 == 0f && matrix.M14 == 0f &&
                       matrix.M21 == 0f && matrix.M22 == 0f && matrix.M23 == 0f && matrix.M24 == 0f &&
                       matrix.M31 == 0f && matrix.M32 == 0f && matrix.M33 == 0f && matrix.M34 == 0f &&
                       matrix.M41 == 0f && matrix.M42 == 0f && matrix.M43 == 0f && matrix.M44 == 0f;

        if (allZero) return true;
        if (!Matrix4x4.Decompose(matrix, out var scale, out _, out _)) return true;
        if (Math.Abs(scale.X) < epsilon || Math.Abs(scale.Y) < epsilon || Math.Abs(scale.Z) < epsilon) return true;

        return false;
    }

    private static void LogInstanceDebug(SceneNode node, Matrix4x4 local, Matrix4x4 world) {
    }

    private void CollectLights() {
        _lightBuffer.Clear();
        var shadowData = new List<ShadowData>();

        // 1. Manual lights
        foreach (var l in _manualLights) {
            _lightBuffer.AddLight(l);
        }


        // 2. Scene lights (use _visibleLights populated by Update)
        foreach (var node in _visibleLights) {
            if (node.Light == null) continue;

            if (_nodeWorldTransforms.TryGetValue(node, out var world)) {
                var pos = world.Translation;
                var dir = Vector3.TransformNormal(new Vector3(0, 0, -1), world);

                float innerDeg = (float)(node.Light.InnerCone * 180.0 / Math.PI);
                float outerDeg = (float)(node.Light.OuterCone * 180.0 / Math.PI);

                int shadowIndex = -1;

                if (ShadowAtlas != null) {
                    var allocs = ShadowAtlas.GetAllocations(node);

                    if (allocs != null && allocs.Length > 0) {
                        shadowIndex = shadowData.Count;

                        foreach (var alloc in allocs) {
                            float scale = (float)alloc.TileSize / 2048.0f;
                            float x = (float)alloc.AtlasX * alloc.TileSize / 2048.0f;
                            float y = (float)alloc.AtlasY * alloc.TileSize / 2048.0f;

                            shadowData.Add(new ShadowData(
                                alloc.ViewProj,
                                new Vector4(x, y, scale, (float)alloc.PageIndex)
                            ));
                        }
                    }
                }


                var light = new LightUniform(
                    pos,
                    node.Light.Range,
                    node.Light.Color,
                    node.Light.Intensity,
                    (LightType)node.Light.Type,
                    dir,
                    innerDeg,
                    outerDeg,
                    shadowIndex,
                    node.Light.VolumetricIntensity
                );

                _lightBuffer.AddLight(light);
            }
        }
        _shadowDataBuffer.Update(shadowData.ToArray());
    }

    public void Dispose() {
        _dummyHiZ.Dispose();
        _dummyHiZView.Dispose();
        ClearResources();
        // CameraBuffer is shared/owned by Renderer
        _modelBuffer.Dispose();
        _compactModelBuffer.Dispose();
        _lightBuffer.Dispose();
        _shadowDataBuffer.Dispose();
        _pipeline.Dispose();
        _geometryBuffer.Dispose();
    }
}