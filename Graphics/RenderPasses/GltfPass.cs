using System;
using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Cache;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics.Assets;
using Whisperleaf.Graphics.Data;
using Whisperleaf.Graphics.Scene;
using Whisperleaf.Graphics.Scene.Data;

namespace Whisperleaf.Graphics.RenderPasses;

public sealed class GltfPass : IRenderPass, IDisposable {
    private readonly GraphicsDevice _gd;
    private readonly Pipeline _pipeline;
    private readonly CameraUniformBuffer _cameraBuffer;
    private readonly ModelUniformBuffer _modelBuffer;
    private readonly LightUniformBuffer _lightBuffer;
    private readonly GeometryBuffer _geometryBuffer;
    private readonly SceneBVH _bvh = new();

    private readonly Dictionary<string, MeshGpu> _meshCache = new();
    private readonly Dictionary<string, int> _materialCache = new();
    private readonly List<MaterialData> _materials = new();
    private readonly List<MeshInstance> _meshInstances = new();
    private readonly Dictionary<SceneNode, int> _nodeToInstance = new();
    private readonly Dictionary<SceneNode, SceneNode?> _nodeParents = new();
    private readonly Dictionary<SceneNode, Matrix4x4> _nodeWorldTransforms = new();
    private bool _loggedFirstInstance;
    private readonly List<ModelUniform> _visibleTransforms = new();
    private SceneNode? _selectedNode;
    
    // Statistics
    public int DrawCalls { get; private set; }
    public int RenderedInstances { get; private set; }
    public long RenderedTriangles { get; private set; }
    public long RenderedVertices { get; private set; }
    
    public int TotalInstances => _meshInstances.Count;
    public int SourceMeshes => _meshCache.Count;
    public long SourceVertices { get; private set; }
    public long SourceIndices { get; private set; }

    public void SetSelectedNode(SceneNode? node) {
        _selectedNode = node;
    }

    public void RebuildBVH() {
        if (_meshInstances.Count == 0) return;

        // Prepare indices for BVH
        var indices = new List<int>(_meshInstances.Count);
        for (int i = 0; i < _meshInstances.Count; i++) {
            indices.Add(i);
        }

        // Build BVH using current world transforms
        _bvh.Build(indices, (i) => {
            var inst = _meshInstances[i];
            var mesh = inst.Mesh;
            if (_nodeWorldTransforms.TryGetValue(inst.Node, out var world)) {
                // Transform AABB to world
                var center = (mesh.AABBMin + mesh.AABBMax) * 0.5f;
                var extents = (mesh.AABBMax - mesh.AABBMin) * 0.5f;
                
                var worldCenter = Vector3.Transform(center, world);
                
                var absM11 = Math.Abs(world.M11); var absM12 = Math.Abs(world.M12); var absM13 = Math.Abs(world.M13);
                var absM21 = Math.Abs(world.M21); var absM22 = Math.Abs(world.M22); var absM23 = Math.Abs(world.M23);
                var absM31 = Math.Abs(world.M31); var absM32 = Math.Abs(world.M32); var absM33 = Math.Abs(world.M33);
                
                float newEx = absM11 * extents.X + absM21 * extents.Y + absM31 * extents.Z;
                float newEy = absM12 * extents.X + absM22 * extents.Y + absM32 * extents.Z;
                float newEz = absM13 * extents.X + absM23 * extents.Y + absM33 * extents.Z;
                
                var worldMin = new Vector3(worldCenter.X - newEx, worldCenter.Y - newEy, worldCenter.Z - newEz);
                var worldMax = new Vector3(worldCenter.X + newEx, worldCenter.Y + newEy, worldCenter.Z + newEz);
                
                return (worldMin, worldMax);
            }
            return (new Vector3(-100000), new Vector3(100000));
        });
    }

    private struct MeshInstance {
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

    public GltfPass(GraphicsDevice gd) {
        _gd = gd;
        _geometryBuffer = new GeometryBuffer(gd);

        _cameraBuffer = new CameraUniformBuffer(gd);
        _modelBuffer = new ModelUniformBuffer(gd);
        _lightBuffer = new LightUniformBuffer(gd);

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
            gd.MainSwapchain.Framebuffer,
            enableDepth: true,
            enableBlend: false,
            extraLayouts: new[] {
                _cameraBuffer.Layout,
                _modelBuffer.Layout,
                PbrLayout.MaterialLayout,
                PbrLayout.MaterialParamsLayout,
                _lightBuffer.Layout,
                _lightBuffer.ParamLayout
            }
        );
    }

    public void AddLight(LightUniform light) {
        _lightBuffer.AddLight(light);
    }

                public void LoadScene(SceneAsset scene, bool additive = false)

                {

                    _gd.WaitForIdle();

                    

                    if (!additive)

                    {

                        ClearResources();

                    }

            

                    int[] materialMap = LoadMaterials(scene);

                    LoadMeshes(scene, materialMap);

                    

                    SortAndUploadInstances();

                }

    public void LoadScene(string scenePath) {
        var sceneAsset = SceneAsset.Load(scenePath);
        LoadScene(sceneAsset, false);
    }

    public int InstanceCount => _meshInstances.Count;

    public void UpdateInstanceTransform(int instanceIndex, Matrix4x4 transform) {
        if ((uint)instanceIndex >= _meshInstances.Count)
            throw new ArgumentOutOfRangeException(nameof(instanceIndex));

        // Only update the source data. The GPU buffer is rebuilt every frame in Render().
        // var instance = _meshInstances[instanceIndex]; // Unused
        // _modelBuffer.UpdateTransform(instance.TransformIndex, transform); 
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

    public void Render(GraphicsDevice gd, CommandList cl, Camera? camera = null) {
        if (camera == null || _meshInstances.Count == 0)
            return;

        // Reset frame stats
        DrawCalls = 0;
        RenderedInstances = 0;
        RenderedTriangles = 0;
        RenderedVertices = 0;

        _cameraBuffer.Update(gd, camera);
        _lightBuffer.UpdateGPU();

        // 1. Culling Pass
        _visibleTransforms.Clear();
        var viewProj = camera.ViewMatrix * camera.ProjectionMatrix;
        var frustum = new Frustum(viewProj);

        // Query BVH
        var visibleIndices = _bvh.Query(frustum);
        
        // Force include selected node and its subtree (bypass culling)
        if (_selectedNode != null) {
            ForceVisibleRecursive(_selectedNode, visibleIndices);
        }
        
        visibleIndices.Sort(); // Batching relies on sorted order

        var drawRanges = new List<(MeshGpu Mesh, int MaterialIndex, int InstanceCount, int StartIndex)>();

        if (visibleIndices.Count > 0)
        {
            int listIndex = 0;
            int currentVisibleStart = 0;

            while (listIndex < visibleIndices.Count)
            {
                int instanceIdx = visibleIndices[listIndex];
                var batchStartInstance = _meshInstances[instanceIdx];
                var batchMesh = batchStartInstance.Mesh;
                int batchMaterialIndex = batchStartInstance.MaterialIndex;
                int batchVisibleCount = 0;

                // Process all visible instances that belong to this batch
                while (listIndex < visibleIndices.Count)
                {
                    int nextIdx = visibleIndices[listIndex];
                    
                    // Skip duplicates if ForceVisible added already visible indices
                    if (nextIdx == instanceIdx && listIndex > 0 && visibleIndices[listIndex-1] == instanceIdx)
                    {
                        listIndex++;
                        continue;
                    }
                    instanceIdx = nextIdx; // Update current tracker
                    
                    var next = _meshInstances[nextIdx];
                    
                    // Check if we hit a new batch boundary
                    if (next.Mesh != batchMesh || next.MaterialIndex != batchMaterialIndex)
                        break;

                    if (_nodeWorldTransforms.TryGetValue(next.Node, out var world))
                    {
                        _visibleTransforms.Add(new ModelUniform(world));
                        batchVisibleCount++;
                    }
                    
                    listIndex++;
                }

                if (batchVisibleCount > 0)
                {
                    drawRanges.Add((batchMesh, batchMaterialIndex, batchVisibleCount, currentVisibleStart));
                    currentVisibleStart += batchVisibleCount;
                }
            }
        }
        
        // 2. Upload Buffer
        _modelBuffer.UpdateAll(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_visibleTransforms));

        // 3. Draw Pass
        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _cameraBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(1, _modelBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(4, _lightBuffer.ResourceSet);
        cl.SetGraphicsResourceSet(5, _lightBuffer.ParamResourceSet);

        cl.SetVertexBuffer(0, _geometryBuffer.VertexBuffer);
        cl.SetIndexBuffer(_geometryBuffer.IndexBuffer, IndexFormat.UInt32);

        foreach (var batch in drawRanges)
        {
            // Bind Material
            var material = ResolveMaterial(batch.MaterialIndex);
            if (material != null) {
                if (material.ResourceSet != null) cl.SetGraphicsResourceSet(2, material.ResourceSet);
                if (material.ParamsResourceSet != null) cl.SetGraphicsResourceSet(3, material.ParamsResourceSet);
            }

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
        foreach (var child in node.Children) {
            ForceVisibleRecursive(child, visibleIndices);
        }
    }

    private void SortAndUploadInstances() {
        if (_meshInstances.Count == 0) return;

        // Sort: Material -> Mesh
        _meshInstances.Sort((a, b) =>
        {
            int cmpMat = a.MaterialIndex.CompareTo(b.MaterialIndex);

            if (cmpMat != 0) return cmpMat;

            return a.Mesh.GetHashCode().CompareTo(b.Mesh.GetHashCode());
        });

        // Rebuild SSBO and update map
        var uniforms = new ModelUniform[_meshInstances.Count];
        _nodeToInstance.Clear();

        // Prepare indices for BVH
        var indices = new List<int>(_meshInstances.Count);

        for (int i = 0; i < _meshInstances.Count; i++) {
            var inst = _meshInstances[i];
            indices.Add(i);

            // Look up world transform
            if (!_nodeWorldTransforms.TryGetValue(inst.Node, out var world))
                world = Matrix4x4.Identity;

            uniforms[i] = new ModelUniform(world);

            // Update instance with new SSBO index
            _meshInstances[i] = new MeshInstance(inst.Mesh, inst.MaterialIndex, i, inst.Node);
            _nodeToInstance[inst.Node] = i;
        }

        // Build BVH
        _bvh.Build(indices, (i) => {
            var inst = _meshInstances[i];
            var mesh = inst.Mesh;
            if (_nodeWorldTransforms.TryGetValue(inst.Node, out var world)) {
                // Transform AABB to world
                var center = (mesh.AABBMin + mesh.AABBMax) * 0.5f;
                var extents = (mesh.AABBMax - mesh.AABBMin) * 0.5f;
                
                var worldCenter = Vector3.Transform(center, world);
                
                var absM11 = Math.Abs(world.M11); var absM12 = Math.Abs(world.M12); var absM13 = Math.Abs(world.M13);
                var absM21 = Math.Abs(world.M21); var absM22 = Math.Abs(world.M22); var absM23 = Math.Abs(world.M23);
                var absM31 = Math.Abs(world.M31); var absM32 = Math.Abs(world.M32); var absM33 = Math.Abs(world.M33);
                
                float newEx = absM11 * extents.X + absM21 * extents.Y + absM31 * extents.Z;
                float newEy = absM12 * extents.X + absM22 * extents.Y + absM32 * extents.Z;
                float newEz = absM13 * extents.X + absM23 * extents.Y + absM33 * extents.Z;
                
                var worldMin = new Vector3(worldCenter.X - newEx, worldCenter.Y - newEy, worldCenter.Z - newEz);
                var worldMax = new Vector3(worldCenter.X + newEx, worldCenter.Y + newEy, worldCenter.Z + newEz);
                
                return (worldMin, worldMax);
            }
            return (new Vector3(-100000), new Vector3(100000)); // Should not happen if logic is correct
        });

        _modelBuffer.EnsureCapacity(_meshInstances.Count);
        _modelBuffer.UpdateAll(uniforms);
    }

    private int[] LoadMaterials(SceneAsset scene) {
        int[] map = new int[scene.Materials.Count];

        for (int i = 0; i < scene.Materials.Count; i++) {
            var src = scene.Materials[i];
            string hash = ComputeMaterialHash(src);

            if (_materialCache.TryGetValue(hash, out int globalIndex)) {
                map[i] = globalIndex;

                continue;
            }


            // New material
            globalIndex = _materials.Count;
            map[i] = globalIndex;
            _materialCache[hash] = globalIndex;

            var material = new MaterialData {
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

            // Fix: If we have an emissive texture but the factor is zero (black),
            // force the factor to white so the texture is visible.
            // This handles models that rely on texture alone and default factor to 0.
            if (material.EmissivePath != null && material.EmissiveFactor == Vector3.Zero) {
                material.EmissiveFactor = Vector3.One;
            }


            var rmaPath = ResolveCachedTexture(src.RMAHash);

            if (material.UsePackedRMA && rmaPath != null) {
                material.MetallicPath = rmaPath;
                material.RoughnessPath = rmaPath;
                material.OcclusionPath = rmaPath;
            }


            MaterialUploader.Upload(_gd, PbrLayout.MaterialLayout, PbrLayout.MaterialParamsLayout, material);
            _materials.Add(material);
        }


        return map;
    }

    private void LoadMeshes(SceneAsset scene, int[] materialMap) {
        if (scene.RootNodes.Count == 0)
            return;

        _loggedFirstInstance = false;

        foreach (var node in scene.RootNodes) {
            LoadMeshRecursive(node, Matrix4x4.Identity, null, materialMap);
        }
    }

    private void LoadMeshRecursive(SceneNode node, Matrix4x4 parentWorld, SceneNode? parent, int[] materialMap) {
        _nodeParents[node] = parent;
        var worldTransform = node.LocalTransform * parentWorld;
        _nodeWorldTransforms[node] = worldTransform;

        if (node.Mesh != null) {
            var meshRef = node.Mesh;
            if (TryLoadMesh(meshRef, materialMap, out MeshGpu? meshGpu, out int globalMaterialIndex)) {
                var mesh = meshGpu!;
                // Note: We don't add to _meshes list anymore, as we use cache.
                // But we still need to track instances.
                
                int transformIndex = _modelBuffer.Allocate(worldTransform);
                int instanceIndex = _meshInstances.Count;
                _meshInstances.Add(new MeshInstance(mesh, globalMaterialIndex, transformIndex, node));
                _nodeToInstance[node] = instanceIndex;

                if (!_loggedFirstInstance) {
                    _loggedFirstInstance = true;
                    LogInstanceDebug(node, node.LocalTransform, worldTransform);
                    Console.WriteLine("[GltfPass] GPU buffer contains:");
                    //      PrintMatrix(_modelBuffer.GetLastUploadedMatrix());
                }
            }
        }


        foreach (var child in node.Children) {
            LoadMeshRecursive(child, worldTransform, node, materialMap);
        }
    }

    private bool TryLoadMesh(MeshReference meshRef, int[] materialMap, out MeshGpu? meshGpu, out int globalMaterialIndex) {
        meshGpu = null;
        globalMaterialIndex = 0;

        // Check cache first
        if (_meshCache.TryGetValue(meshRef.MeshHash, out meshGpu)) {
            // Resolve material index for this specific instance using the map
            if (meshRef.MaterialIndex >= 0 && meshRef.MaterialIndex < materialMap.Length) {
                globalMaterialIndex = materialMap[meshRef.MaterialIndex];
            }


            return true;
        }


        if (!AssetCache.HasMesh(meshRef.MeshHash, out var meshPath)) {
            Console.WriteLine($"[ScenePass] Missing cached mesh for hash {meshRef.MeshHash}");

            return false;
        }


        try {
            var meshData = WlMeshFormat.Read(meshPath, out _);

            // Create new MeshGpu (geometry only)
            // Note: We ignore meshData.MaterialIndex here as it's local to the file.
            // We set it anyway for consistency but it's not used by GltfPass for sorting anymore.
            meshGpu = new MeshGpu(_geometryBuffer, meshData);

            // Cache it
            _meshCache[meshRef.MeshHash] = meshGpu;
            
            // Update source stats
            SourceVertices += meshData.Vertices.Length / 12;
            SourceIndices += meshData.Indices.Length;

            // Resolve material index
            if (meshRef.MaterialIndex >= 0 && meshRef.MaterialIndex < materialMap.Length) {
                globalMaterialIndex = materialMap[meshRef.MaterialIndex];
            }


            return true;
        }
        catch (Exception ex) {
            Console.WriteLine($"[ScenePass] Failed to load mesh '{meshRef.MeshHash}': {ex.Message}");

            return false;
        }
    }

    private MaterialData? ResolveMaterial(int index) {
        if (index >= 0 && index < _materials.Count) {
            return _materials[index];
        }


        return null;
    }

    private string ComputeMaterialHash(MaterialReference mat) {
        // Concatenate properties to form a unique key for the material content
        return $"{mat.BaseColorFactor}_{mat.EmissiveFactor}_{mat.MetallicFactor}_{mat.RoughnessFactor}_{mat.BaseColorHash}_{mat.NormalHash}_{mat.RMAHash}_{mat.EmissiveHash}";
    }

    private static string? ResolveCachedTexture(string? hash) {
        if (string.IsNullOrEmpty(hash))
            return null;

        return AssetCache.HasTexture(hash, out var path) ? path : null;
    }

    private void ClearResources() {
        foreach (var mesh in _meshCache.Values) {
            mesh.Dispose();
        }


        _meshCache.Clear();
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
    }

    private void UpdateWorldRecursive(SceneNode node, Matrix4x4 parentWorld) {
        var worldTransform = node.LocalTransform * parentWorld;

        if (IsDegenerateMatrix(worldTransform)) {
            worldTransform = Matrix4x4.Identity;
        }


        _nodeWorldTransforms[node] = worldTransform;

        // Note: We don't update _modelBuffer here anymore because it's rebuilt every frame in Render().
        // If we need optimization for static objects later, we'll need a different strategy.

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
        // Debug logging... (kept minimal)
    }

    public void Dispose() {
        ClearResources();
        _cameraBuffer.Dispose();
        _modelBuffer.Dispose();
        _lightBuffer.Dispose();
        _pipeline.Dispose();
        _geometryBuffer.Dispose();
    }

}