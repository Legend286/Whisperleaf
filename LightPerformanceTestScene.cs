using System;
using System.Collections.Generic;
using System.Numerics;
using Whisperleaf.AssetPipeline;
using Whisperleaf.AssetPipeline.Scene;
using Whisperleaf.Graphics;

namespace Whisperleaf;

public class LightPerformanceTestScene
{
    private List<(SceneNode Node, Vector3 InitialPos, float Speed, float Radius, float Phase)> _movingLights = new();
    private float _time;

    public LightPerformanceTestScene(Renderer renderer)
    {
        Initialize(renderer);
    }
    
    public void Update(Renderer renderer)
    {
        _time += Whisperleaf.Platform.Time.DeltaTime;

        foreach (var (node, initialPos, speed, radius, phase) in _movingLights)
        {
            float t = _time * speed + phase;
            Vector3 offset = new Vector3(
                MathF.Cos(t) * radius,
                MathF.Sin(t * 0.5f) * 2.0f, // Gentle vertical bobbing
                MathF.Sin(t) * radius
            );

            var newPos = initialPos + offset;
            renderer.UpdateNodeTransform(node, Matrix4x4.CreateTranslation(newPos));
        }
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
        for(int i = 0; i < 500; i++)
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
        for (int i = 0; i < 100; i++)
        {
            float x = (float)(rng.NextDouble() * 120.0 - 60.0);
            float z = (float)(rng.NextDouble() * 120.0 - 60.0);
            float y = (float)(rng.NextDouble() * 5.0 + 2.0);
            
            var color = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
            var initialPos = new Vector3(x, y, z);
            
            var lightNode = new SceneNode 
            { 
                Name = $"Light_{i}", 
                Light = new SceneLight 
                { 
                    Type = 0, // Point
                    Intensity = 5.0f, 
                    Color = color,
                    Range = 20.0f,
                    CastShadows = i % 2 == 0,
                },
                LocalTransform = Matrix4x4.CreateTranslation(initialPos),
                IsStatic = false // Needs to be non-static to update transform easily in some systems
            };
            scene.RootNodes.Add(lightNode);

            // Setup movement params
            float speed = (float)(rng.NextDouble() * 2.0 + 0.5);
            float radius = (float)(rng.NextDouble() * 4.0 + 2.0);
            float phase = (float)(rng.NextDouble() * Math.PI * 2.0);
            _movingLights.Add((lightNode, initialPos, speed, radius, phase));
        }

        renderer.LoadScene(scene);
    }
}