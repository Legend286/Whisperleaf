using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Jitter2.Dynamics;
using Whisperleaf.Graphics.Scene;

namespace Whisperleaf.Physics;

public class PhysicsThread : IDisposable
{
    private readonly PhysicsManager _manager;
    private volatile bool _running;
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action<PhysicsManager>> _commandQueue = new();
    
    // Mapping between SceneNodes and Physics Bodies
    // Accessed by Main Thread during Sync and Physics Thread during Create
    // We need to be careful. 
    // Best approach: Main thread creates SceneNode. Calls Physics.AddBody(node, ...).
    // Physics thread creates Body, maps it to Node (weakly? or ID?).
    // Sync: Physics thread pushes State to a ConcurrentQueue or DoubleBuffer.
    // Main thread reads State and applies to Node.
    
    // For simplicity:
    // We will let Main Thread hold the Map.
    // Physics Thread returns a Handle (RigidBody).
    // Main Thread maps Node -> RigidBody.
    // Sync: Main thread iterates its map, reads Body.Position (assuming atomic read/thread safe enough for visual)
    // Jitter2 body.Position returns JVector struct, which is atomic copy.
    
    public PhysicsManager Manager => _manager;

    public PhysicsThread()
    {
        _manager = new PhysicsManager();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Physics Thread" };
        _thread.Start();
    }

    private void Loop()
    {
        // Fixed timestep logic
        const float dt = 1.0f / 60.0f;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        double accumulator = 0.0;
        long lastTicks = 0;

        while (_running)
        {
            long currentTicks = stopwatch.ElapsedTicks;
            double frameTime = (double)(currentTicks - lastTicks) / System.Diagnostics.Stopwatch.Frequency;
            lastTicks = currentTicks;
            
            // Cap frame time to avoid spiral of death
            if (frameTime > 0.25) frameTime = 0.25;

            accumulator += frameTime;

            while (accumulator >= dt)
            {
                // Process queue before step
                while (_commandQueue.TryDequeue(out var cmd))
                {
                    cmd(_manager);
                }

                _manager.Update(dt);
                accumulator -= dt;
            }

            // Sleep a bit to not burn 100% CPU if ahead
            Thread.Sleep(1);
        }
    }

    public void Enqueue(Action<PhysicsManager> command)
    {
        _commandQueue.Enqueue(command);
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(1000);
        }
    }
}
