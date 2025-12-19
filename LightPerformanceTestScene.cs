using System;
using System.Numerics;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics;

namespace Whisperleaf;

public class LightPerformanceTestScene
{
    public LightPerformanceTestScene(Renderer renderer)
    {
        Initialize(renderer);
    }
    
    public void Update(Renderer renderer)
    {
        // Static scene
    }

    private void Initialize(Renderer renderer)
    {
        renderer.AddCustomMesh("cube", PrimitiveGenerator.CreateBox(1,1,1));
        renderer.AddCustomMesh("plane", PrimitiveGenerator.CreateBox(1000,0.1f,1000));
        
        var scene = new SceneAsset { Name = "LightPerformanceTest" };
        
        scene.Materials.Add(new MaterialReference 
        { 
            Name = "Gray", 
            BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1.0f), 
            RoughnessFactor = 0.8f, 
            MetallicFactor = 0.1f 
        });

        // Floor
        var floorNode = new SceneNode
        {
            Name = "Floor",
            Mesh = new MeshReference 
            { 
                MeshHash = "plane", 
                MaterialIndex = 0,
            },
            LocalTransform = Matrix4x4.CreateTranslation(0, -1.0f, 0),
            IsStatic = true
        };
        scene.RootNodes.Add(floorNode);
        
        // Cubes
        var rng = new Random(12345);
        for(int i = 0; i < 100; i++)
        {
             float x = (float)(rng.NextDouble() * 100.0 - 50.0);
             float z = (float)(rng.NextDouble() * 100.0 - 50.0);
             float y = (float)(rng.NextDouble() * 5.0 + 1.0);
             
             var node = new SceneNode
             {
                Name = $"Cube_{i}",
                Mesh = new MeshReference 
                { 
                    MeshHash = "cube", 
                    MaterialIndex = 0,
                },
                LocalTransform = Matrix4x4.CreateScale(new Vector3(1, y, 1)) * Matrix4x4.CreateTranslation(x, y/2, z),
                IsStatic = true
             };
             scene.RootNodes.Add(node);
        }
        
        // Lights
        for (int i = 0; i < 1000; i++)
        {
            float x = (float)(rng.NextDouble() * 120.0 - 60.0);
            float z = (float)(rng.NextDouble() * 120.0 - 60.0);
            float y = (float)(rng.NextDouble() * 5.0 + 2.0);
            
            var color = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
            
            var lightNode = new SceneNode 
            { 
                Name = $"Light_{i}", 
                Light = new SceneLight 
                { 
                    Type = 0, // Point
                    Intensity = 5.0f, 
                    Color = color,
                    Range = 8.0f,
                    CastShadows = false,
                },
                LocalTransform = Matrix4x4.CreateTranslation(x, y, z)
            };
            scene.RootNodes.Add(lightNode);
        }

        renderer.LoadScene(scene);
    }
}
