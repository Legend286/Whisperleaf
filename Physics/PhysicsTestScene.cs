using System.Numerics;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace Whisperleaf.Physics;

public class PhysicsTestScene
{
    private PhysicsManager _physics;
    private List<(RigidBody Body, SceneNode Node)> _bindings = new();
    
    private float _accumulator;
    private const float FixedTimeStep = 1.0f / 360.0f;
    
    public PhysicsTestScene(Renderer renderer)
    {
        _physics = new PhysicsManager();
        Initialize(renderer);
    }
    
    public void Update(Renderer renderer)
    {
        _accumulator += Whisperleaf.Platform.Time.DeltaTime;
        
        if (_accumulator > 0.2f) _accumulator = 0.2f;

        while (_accumulator >= FixedTimeStep)
        {
            _physics.Update(FixedTimeStep);
            _accumulator -= FixedTimeStep;
        }
        
        SyncTransforms(renderer);
    }
    
    private void SyncTransforms(Renderer renderer)
    {
         foreach (var (body, node) in _bindings)
        {
            // Sync Static State -> Kinematic/Dynamic
            var desiredType = node.IsStatic ? MotionType.Kinematic : MotionType.Dynamic;
            if (body.MotionType != desiredType)
            {
                body.MotionType = desiredType;
                if (desiredType == MotionType.Dynamic) body.SetActivationState(true);
            }

            bool isManipulating = renderer.IsManipulating && renderer.SelectedNode == node;

            if (isManipulating)
            {
                if (!body.IsStatic)
                {
                    if (body.AffectedByGravity) body.AffectedByGravity = false;
                    body.Velocity = JVector.Zero;
                    body.AngularVelocity = JVector.Zero;
                }
                
                var t = node.LocalTransform;
                body.Position = new JVector(t.Translation.X, t.Translation.Y, t.Translation.Z);
                
                if (Matrix4x4.Decompose(t, out _, out var rot, out _))
                {
                    body.Orientation = new JQuaternion(rot.X, rot.Y, rot.Z, rot.W);
                }
            }
            else
            {
                if (!body.IsStatic)
                {
                    if (!body.AffectedByGravity)
                    {
                        body.AffectedByGravity = true;
                        body.Velocity = JVector.Zero;
                        body.AngularVelocity = JVector.Zero;
                    }
                    
                    var pos = body.Position;
                    var jQuat = body.Orientation; 
                    var numQuat = new System.Numerics.Quaternion(jQuat.X, jQuat.Y, jQuat.Z, jQuat.W);
                    
                    var mat = Matrix4x4.CreateFromQuaternion(numQuat);
                    mat.Translation = new Vector3(pos.X, pos.Y, pos.Z);
                    
                    renderer.UpdateNodeTransform(node, mat);
                }
            }
        }
    }

    private void Initialize(Renderer renderer)
    {
        renderer.AddCustomMesh("cube", PrimitiveGenerator.CreateBox(1,1,1));
        renderer.AddCustomMesh("plane", PrimitiveGenerator.CreateBox(1000,0.01f,1000));
        renderer.AddCustomMesh("sphere", PrimitiveGenerator.CreateSphere(0.5f, 16));
        
        var scene = new SceneAsset { Name = "PhysicsTest" };
        
        scene.Materials.Add(new MaterialReference 
        { 
            Name = "Red", 
            BaseColorFactor = new Vector4(0.8f, 0.2f, 0.2f, 1.0f), 
            RoughnessFactor = 0.5f, 
            MetallicFactor = 0.1f 
        });
        
         scene.Materials.Add(new MaterialReference 
        { 
            Name = "Gray", 
            BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1.0f), 
            RoughnessFactor = 0.8f, 
            MetallicFactor = 0.1f 
        });

        // Floor
        var floorBody = _physics.CreatePlane(new Vector3(0, -1, 0), Vector3.UnitY); // Use CreatePlane for floor
        var floorNode = CreateNode("Floor", "plane", 1); // Still render a cube as floor visual
        floorNode.LocalTransform = Matrix4x4.CreateScale(100, 1, 100) * Matrix4x4.CreateTranslation(0, -1.0f, 0); // Visuals should match plane height
        scene.RootNodes.Add(floorNode);
        _bindings.Add((floorBody, floorNode));
        
        // Dynamic Stack
        for(int y = 0; y < 100; y++)
        {
             var pos = new Vector3(0, y * 1.05f + 5f, 0);
             var body = _physics.CreateBox(pos, Vector3.One); // CreateBox for cubes
             var node = CreateNode($"Box_{y}", "cube", 0);
             node.IsStatic = false;
             scene.RootNodes.Add(node);
             _bindings.Add((body, node));
        }
        
        // Spheres
         for(int i = 0; i < 20; i++)
        {
             var pos = new Vector3(i * 1.5f - 3f, 15f + i * 2f, 0.5f);
             var body = _physics.CreateSphere(pos, 0.5f); // CreateSphere for spheres
             var node = CreateNode($"Sphere_{i}", "sphere", 0);
             node.IsStatic = false;
             scene.RootNodes.Add(node);
             _bindings.Add((body, node));
        }
        
        // Light
        var lightNode = new SceneNode 
        { 
            Name = "Sun", 
            Light = new SceneLight 
            { 
                Type = 2, 
                Intensity = 3.0f, 
                Color = Vector3.One 
            },
            LocalTransform = Matrix4x4.CreateRotationX(-1.0f) * Matrix4x4.CreateTranslation(0, 10, 0)
        };
        scene.RootNodes.Add(lightNode);

        renderer.LoadScene(scene);
    }
    
    private SceneNode CreateNode(string name, string meshName, int materialIndex)
    {
        return new SceneNode
        {
            Name = name,
            Mesh = new MeshReference 
            { 
                MeshHash = meshName, 
                MaterialIndex = materialIndex,
            },
            LocalTransform = Matrix4x4.Identity
        };
    }
}