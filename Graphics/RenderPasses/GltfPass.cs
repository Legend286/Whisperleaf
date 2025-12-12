using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices; // For Marshal.SizeOf
using Veldrid;
using Veldrid.SPIRV;
using Whisperleaf.AssetPipeline; // For MaterialData
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics.Data; // For GpuStructs
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;
using IndirectDrawIndexedArguments = Whisperleaf.Graphics.Data.IndirectDrawIndexedArguments;
using MaterialUploader = Whisperleaf.AssetPipeline.MaterialUploader;

namespace Whisperleaf.Graphics.RenderPasses;

public sealed class GltfPass : IRenderPass, IDisposable
{
    private readonly GraphicsDevice _gd;
    private Pipeline _pipeline;
    private Pipeline _cullComputePipeline;
    private ResourceSet _cullComputeResourceSet;

    private readonly CameraUniformBuffer _cameraBuffer;
    private readonly LightUniformBuffer _lightBuffer;
    private readonly SceneGeometryBuffer _geometryBuffer;

    private DeviceBuffer _meshInfoBuffer; // StructuredBuffer for mesh data (AABB, offsets)
    private readonly List<MeshInfoGPU> _meshInfoData = new();
    private DeviceBuffer _instanceDataBuffer; // StructuredBuffer for instance data (world matrix, mesh index)
    private readonly List<InstanceDataGPU> _instanceData = new();
    private DeviceBuffer _indirectCommandBuffer; // IndirectBuffer for draw commands
    private DeviceBuffer _cullResultBuffer; // StructuredBuffer for compute output
    
    private const uint MaxInstances = 65536; // Max number of instances in the scene
    private const uint MaxMeshInfos = 32768; // Max number of unique meshes

    private Framebuffer _framebuffer;
    private Texture _colorTarget;
    private Texture _depthTarget;

    public Texture OutputTexture => _colorTarget;

    private readonly List<AssetPipeline.MaterialData> _materials = new();
    private readonly List<MeshInstance> _meshInstances = new();
    private readonly Dictionary<SceneNode, int> _nodeToInstance = new();
    private bool _loggedFirstInstance;

    private readonly struct MeshInstance
    {
        public readonly MeshRange Range;
        public readonly int InstanceDataIndex; // Index into _instanceDataBuffer
        public readonly SceneNode Node;

        public MeshInstance(MeshRange range, int instanceDataIndex, SceneNode node)
        {
            Range = range;
            InstanceDataIndex = instanceDataIndex;
            Node = node;
        }
    }
    
    // ResourceLayout for InstanceDataGPU to be used in the PBR vertex shader
    private ResourceLayout _instanceDataGPULayout;
    private ResourceSet _instanceDataGPULayoutResourceSet;

    public GltfPass(GraphicsDevice gd, uint width, uint height)
    {
        _gd = gd;
        _geometryBuffer = new SceneGeometryBuffer(gd);

        _cameraBuffer = new CameraUniformBuffer(gd);
        _lightBuffer = new LightUniformBuffer(gd);

        var factory = _gd.ResourceFactory;
        
        // Initialize layouts and resource sets for graphics pipeline
        _instanceDataGPULayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("InstanceDataStorage", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("MeshInfoStorage", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex)));

        // Initialize MeshInfoBuffer
        _meshInfoBuffer = factory.CreateBuffer(new BufferDescription(
            MaxMeshInfos * (uint)Marshal.SizeOf<MeshInfoGPU>(),
            BufferUsage.StructuredBufferReadOnly,
            (uint)Marshal.SizeOf<MeshInfoGPU>()));
        
        // Initialize InstanceDataBuffer
        _instanceDataBuffer = factory.CreateBuffer(new BufferDescription(
            MaxInstances * (uint)Marshal.SizeOf<InstanceDataGPU>(),
            BufferUsage.StructuredBufferReadWrite,
            (uint)Marshal.SizeOf<InstanceDataGPU>()));

        // Initialize CullResultBuffer (ReadWrite for compute output)
        _cullResultBuffer = factory.CreateBuffer(new BufferDescription(
            MaxInstances * (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>(),
            BufferUsage.StructuredBufferReadWrite,
            (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>()));
            
        // Initialize IndirectCommandBuffer (Indirect for draw call, TransferDst for copy)
        _indirectCommandBuffer = factory.CreateBuffer(new BufferDescription(
            MaxInstances * (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>(),
            BufferUsage.IndirectBuffer));
        
        // Create ResourceSet for graphics pipeline (InstanceDataGPU)
        _instanceDataGPULayoutResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            _instanceDataGPULayout, _instanceDataBuffer, _meshInfoBuffer));

        // Create the compute shader and pipeline
        CreateCullComputePipeline();

        Resize(width, height);
    }



    private void CreateCullComputePipeline()
    {
        var factory = _gd.ResourceFactory;
        
        // Define ResourceLayout for Compute Shader
        var computeLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            // Set 0: CameraUniformBuffer (for frustum culling)
            new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute),
            // Set 1: MeshInfoStorage (StructuredBufferReadOnly)
            new ResourceLayoutElementDescription("MeshInfoStorage", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            // Set 2: InstanceDataStorage (StructuredBufferReadOnly)
            new ResourceLayoutElementDescription("InstanceDataStorage", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            // Set 3: IndirectDrawCommands (RWStructuredBuffer)
            new ResourceLayoutElementDescription("IndirectDrawCommands", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute)));

        var computeShader = factory.CreateFromSpirv(new ShaderDescription(ShaderStages.Compute,
            System.Text.Encoding.UTF8.GetBytes(System.IO.File.ReadAllText("Graphics/Shaders/Cull.comp")), "main"));
        
        _cullComputePipeline = factory.CreateComputePipeline(new ComputePipelineDescription(
            computeShader,
            computeLayout,
            1, 1, 1)); // Local group size will be handled by shader source, but Veldrid needs explicit here.
        
        _cullComputeResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            computeLayout,
            _cameraBuffer.Buffer, // CameraBuffer
            _meshInfoBuffer, // MeshInfoBuffer
            _instanceDataBuffer, // InstanceDataBuffer
            _cullResultBuffer)); // CullResultBuffer (RWStructured)
            
        // Clean up shader module after pipeline creation
        computeShader.Dispose();
    }

    public void Resize(uint width, uint height)
    {
        if (_colorTarget != null) _gd.DisposeWhenIdle(_colorTarget);
        if (_depthTarget != null) _gd.DisposeWhenIdle(_depthTarget);
        if (_framebuffer != null) _gd.DisposeWhenIdle(_framebuffer);
        if (_pipeline != null) _gd.DisposeWhenIdle(_pipeline);

        var factory = _gd.ResourceFactory;

        _colorTarget = factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        
        _depthTarget = factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil));

        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depthTarget, _colorTarget));

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("v_Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("v_Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
            new VertexElementDescription("v_Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float4),
            new VertexElementDescription("v_TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
        );

        _pipeline = PipelineFactory.CreatePipeline(
            _gd,
            "Graphics/Shaders/PBR.vert",
            "Graphics/Shaders/PBR.frag",
            vertexLayout,
            _framebuffer,
            enableDepth: true,
            enableBlend: false,
            extraLayouts: new[] { 
                _cameraBuffer.Layout, 
                _instanceDataGPULayout, // Set 1: InstanceDataGPU (for graphics pipeline)
                PbrLayout.MaterialLayout, 
                PbrLayout.MaterialParamsLayout,
                _lightBuffer.Layout,
                _lightBuffer.ParamLayout
            }
        );
    }
    
    public int AllocateInstance(Matrix4x4 worldMatrix, uint meshInfoIndex)
    {
        var instance = new InstanceDataGPU { WorldMatrix = worldMatrix, MeshInfoIndex = meshInfoIndex };
        int index = _instanceData.Count;
        _instanceData.Add(instance);

        if (_instanceData.Count > MaxInstances)
        {
            Console.WriteLine("Warning: Exceeded max instance count. Instances will not be rendered.");
            return -1;
        }

        _gd.UpdateBuffer(_instanceDataBuffer, (uint)index * (uint)Marshal.SizeOf<InstanceDataGPU>(), ref instance);
        return index;
    }

    private void UpdateInstanceTransform(int instanceDataIndex, Matrix4x4 transform)
    {
        if ((uint)instanceDataIndex >= _instanceData.Count)
            throw new ArgumentOutOfRangeException(nameof(instanceDataIndex));

        var instance = _instanceData[instanceDataIndex];
        instance.WorldMatrix = transform;
        _instanceData[instanceDataIndex] = instance; // Update CPU mirror
        
        _gd.UpdateBuffer(_instanceDataBuffer, (uint)instanceDataIndex * (uint)Marshal.SizeOf<InstanceDataGPU>(), ref instance);
    }
    
    public int AllocateMeshInfo(MeshRange range)
    {
        var meshInfo = new MeshInfoGPU
        {
            VertexOffset = range.VertexOffset,
            IndexOffset = range.IndexOffset,
            IndexCount = range.IndexCount,
            MaterialIndex = range.MaterialIndex,
            AABBMin = range.AABBMin,
            AABBMax = range.AABBMax
        };
        int index = _meshInfoData.Count;
        _meshInfoData.Add(meshInfo);

        if (_meshInfoData.Count > MaxMeshInfos)
        {
            Console.WriteLine("Warning: Exceeded max mesh info count. Meshes might not be rendered correctly.");
            return -1;
        }

        _gd.UpdateBuffer(_meshInfoBuffer, (uint)index * (uint)Marshal.SizeOf<MeshInfoGPU>(), ref meshInfo);
        return index;
    }

    public void AddLight(LightUniform light)
    {
        _lightBuffer.AddLight(light);
    }

    public void LoadScene(SceneAsset scene)
    {
        ClearResources();

        LoadMaterials(scene);
        LoadMeshes(scene);
    }

    public void LoadScene(string scenePath)
    {
        var sceneAsset = SceneAsset.Load(scenePath);
        LoadScene(sceneAsset);
    }

    public int InstanceCount => _meshInstances.Count;

    public bool TryGetWorldTransform(SceneNode node, out Matrix4x4 transform)
    {
        transform = node.WorldMatrix;
        return true;
    }

    public bool ApplyWorldTransform(SceneNode node, Matrix4x4 worldTransform)
    {
        var parentWorld = node.Parent?.WorldMatrix ?? Matrix4x4.Identity;

        if (!Matrix4x4.Invert(parentWorld, out var parentInverse))
        {
            parentInverse = Matrix4x4.Identity;
        }

        node.LocalTransform = worldTransform * parentInverse;
        
        node.UpdateWorld(parentWorld);
        UpdateGpuBuffers(node);
        return true;
    }
    
    private void UpdateGpuBuffers(SceneNode node)
    {
        if (_nodeToInstance.TryGetValue(node, out var instanceIndex))
        {
            var instance = _meshInstances[instanceIndex];
            UpdateInstanceTransform(instance.InstanceDataIndex, node.WorldMatrix);
        }

        foreach (var child in node.Children)
        {
            UpdateGpuBuffers(child);
        }
    }

    public void Render(GraphicsDevice gd, CommandList cl, Camera? camera = null) {
        if (camera == null || _instanceData.Count == 0) // Check _instanceData.Count for rendering
            return;

        _cameraBuffer.Update(gd, camera);
        _lightBuffer.UpdateGPU();

        // Dispatch Compute Shader for culling
        cl.SetPipeline(_cullComputePipeline);
        cl.SetComputeResourceSet(0, _cullComputeResourceSet);
        // Assuming local_size_x = 64 in shader. Dispatch in groups of 64.
        uint dispatchCount = (uint)Math.Ceiling((double)_instanceData.Count / 64);
        cl.Dispatch(dispatchCount, 1, 1);
        
        // Copy cull results to indirect buffer
        cl.CopyBuffer(_cullResultBuffer, 0, _indirectCommandBuffer, 0, (uint)_instanceData.Count * (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>());

        // Render pass
        cl.SetFramebuffer(_framebuffer);
        cl.SetFullViewports();
        cl.ClearColorTarget(0, RgbaFloat.Black);
        cl.ClearDepthStencil(1.0f);

        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet); // Camera uniforms
        // Set 1: InstanceDataBuffer (StructuredBuffer)
        cl.SetGraphicsResourceSet(1, _instanceDataGPULayoutResourceSet); // For PBR.vert

        // Temporary: Bind the first material to ensure textures are visible
        // In a real indirect renderer, we'd use bindless textures or texture arrays indexed by MaterialIndex
        if (_materials.Count > 0)
        {
            cl.SetGraphicsResourceSet(2, _materials[0].ResourceSet);
            cl.SetGraphicsResourceSet(3, _materials[0].ParamsResourceSet);
        }

        cl.SetGraphicsResourceSet(4, _lightBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(5, _lightBuffer.ParamResourceSet);

        // Bind global buffers once
        cl.SetVertexBuffer(0, _geometryBuffer.VertexBuffer);
        cl.SetIndexBuffer(_geometryBuffer.IndexBuffer, IndexFormat.UInt32);
        
        // Draw all instances using indirect commands
        // The drawCount here is the total number of potential draws (_instanceData.Count),
        // and the compute shader sets instanceCount to 0 for culled objects.
        cl.DrawIndexedIndirect(_indirectCommandBuffer, 0, (uint)_instanceData.Count, (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>());
    }

    private void LoadMaterials(SceneAsset scene)
    {
        for (int i = 0; i < scene.Materials.Count; i++)
        {
            var src = scene.Materials[i];

            var material = new AssetPipeline.MaterialData
            {
                Name = src.Name,
                BaseColorFactor = src.BaseColorFactor,
                EmissiveFactor = src.EmissiveFactor,
                MetallicFactor = src.MetallicFactor,
                RoughnessFactor = src.RoughnessFactor,
                UsePackedRMA = !string.IsNullOrEmpty(src.RMAHash)
            };

            material.BaseColorPath = ResolveCachedTexture(src.BaseColorHash);
            material.NormalPath = ResolveCachedTexture(src.NormalHash);
            material.EmissivePath = ResolveCachedTexture(src.EmissiveHash);

            var rmaPath = ResolveCachedTexture(src.RMAHash);
            if (material.UsePackedRMA && rmaPath != null)
            {
                material.MetallicPath = rmaPath;
                material.RoughnessPath = rmaPath;
                material.OcclusionPath = rmaPath;
            }

            MaterialUploader.Upload(_gd, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout, material);
            _materials.Add(material);
        }
    }

    private void LoadMeshes(SceneAsset scene)
    {
        if (scene.RootNodes.Count == 0)
            return;

        _loggedFirstInstance = false;
        foreach (var node in scene.RootNodes)
        {
            LoadMeshRecursive(node, Matrix4x4.Identity, null);
        }
    }

    private void LoadMeshRecursive(SceneNode node, Matrix4x4 parentWorld, SceneNode? parent)
    {
        node.UpdateWorld(parentWorld);
        var worldTransform = node.WorldMatrix;

        if (node.Mesh != null)
        {
            var meshRef = node.Mesh;
            if (AssetCache.HasMesh(meshRef.MeshHash, out var meshPath))
            {
                try
                {
                    var meshData = WlMeshFormat.Read(meshPath, out _);

                    if (_materials.Count > 0)
                    {
                        meshData.MaterialIndex = Math.Clamp(meshRef.MaterialIndex, 0, _materials.Count - 1);
                    }
                    else
                    {
                        meshData.MaterialIndex = meshRef.MaterialIndex;
                    }
                    
                    var range = _geometryBuffer.AddMesh(meshData);
                    int meshInfoIndex = AllocateMeshInfo(range);

                    int instanceDataIndex = AllocateInstance(worldTransform, (uint)meshInfoIndex);
                    int instanceIndex = _meshInstances.Count;
                    _meshInstances.Add(new MeshInstance(range, instanceDataIndex, node));
                    _nodeToInstance[node] = instanceIndex;

                    if (!_loggedFirstInstance)
                    {
                        _loggedFirstInstance = true;
                        LogInstanceDebug(node, node.LocalTransform, worldTransform);
                        Console.WriteLine("[GltfPass] GPU buffer contains:");
                        PrintMatrix(worldTransform); 
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScenePass] Failed to load mesh '{meshRef.MeshHash}': {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[ScenePass] Missing cached mesh for hash {meshRef.MeshHash}");
            }
        }

        foreach (var child in node.Children)
        {
            LoadMeshRecursive(child, worldTransform, node);
        }
    }

    private AssetPipeline.MaterialData? ResolveMaterial(int index)
    {
        if (index >= 0 && index < _materials.Count)
        {
            return _materials[index];
        }

        return null;
    }

    private static string? ResolveCachedTexture(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
            return null;

        return AssetCache.HasTexture(hash, out var path) ? path : null;
    }

    private void ClearResources()
    {
        _geometryBuffer.Clear();

        foreach (var mat in _materials)
        {
            mat.Dispose();
        }
        _materials.Clear();

        _meshInstances.Clear();
        _nodeToInstance.Clear();
        _loggedFirstInstance = false;
        
        _meshInfoData.Clear();
        _instanceData.Clear();
    }

    private static void LogInstanceDebug(SceneNode node, Matrix4x4 local, Matrix4x4 world)
    {
        Console.WriteLine($"[GltfPass] First mesh instance: '{node.Name}'");

        if (Matrix4x4.Decompose(local, out var localScale, out var localRotation, out var localTranslation))
        {
            Console.WriteLine($"  Local  -> Position: {localTranslation}, Scale: {localScale}");
        }
        else
        {
            Console.WriteLine("  Local  -> Decompose failed");
        }

        if (Matrix4x4.Decompose(world, out var worldScale, out var worldRotation, out var worldTranslation))
        {
            Console.WriteLine($"  World  -> Position: {worldTranslation}, Scale: {worldScale}");
        }
        else
        {
            Console.WriteLine("  World  -> Decompose failed");
        }

        Console.WriteLine("  Local matrix:");
        PrintMatrix(local);
        Console.WriteLine("  World matrix:");
        PrintMatrix(world);
    }

    private static void PrintMatrix(Matrix4x4 matrix)
    {
        Console.WriteLine(
            $"    [{matrix.M11,8:F4} {matrix.M12,8:F4} {matrix.M13,8:F4} {matrix.M14,8:F4}]");
        Console.WriteLine(
            $"    [{matrix.M21,8:F4} {matrix.M22,8:F4} {matrix.M23,8:F4} {matrix.M24,8:F4}]");
        Console.WriteLine(
            $"    [{matrix.M31,8:F4} {matrix.M32,8:F4} {matrix.M33,8:F4} {matrix.M34,8:F4}]");
        Console.WriteLine(
            $"    [{matrix.M41,8:F4} {matrix.M42,8:F4} {matrix.M43,8:F4} {matrix.M44,8:F4}]");
    }

    public void Dispose()
    {
        ClearResources();
        _cameraBuffer.Dispose();
        _lightBuffer.Dispose();
        _pipeline.Dispose();
        _colorTarget.Dispose();
        _depthTarget.Dispose();
        _framebuffer.Dispose();
        _geometryBuffer.Dispose();
        _meshInfoBuffer.Dispose();
        _instanceDataBuffer.Dispose();
        _indirectCommandBuffer.Dispose();
        _cullResultBuffer.Dispose();
        _cullComputePipeline.Dispose();
        _cullComputeResourceSet.Dispose();
        _instanceDataGPULayout.Dispose();
        _instanceDataGPULayoutResourceSet.Dispose();
    }
}
