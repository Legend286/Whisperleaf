using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using System.Numerics;

namespace Whisperleaf.Physics;

public class PhysicsManager
{
    public World World { get; private set; }
    private readonly List<(RigidBody Body, object? UserData)> _bodies = new();

    public PhysicsManager()
    {
        var capacity = new World.Capacity 
        { 
            BodyCount = 4000, 
            ConstraintCount = 2000, 
            ContactCount = 8000, 
            SmallConstraintCount = 1000 
        };
        World = new World(capacity);
        World.Gravity = new JVector(0, -9.81f, 0);
    }

    public void Update(float dt)
    {
        if (dt > 1.0f / 10.0f) dt = 1.0f / 10.0f; 
        World.Step(dt, true);
    }
    
    public RigidBody CreateBox(Vector3 position, Vector3 size, bool isStatic = false)
    {
        var shape = new BoxShape(size.X, size.Y, size.Z);
        var body = World.CreateRigidBody();
        body.AddShape(shape);
        body.Position = new JVector(position.X, position.Y, position.Z);
        if (isStatic) body.MotionType = MotionType.Static;
        _bodies.Add((body, null));
        return body;
    }
    
    public RigidBody CreateSphere(Vector3 position, float radius, bool isStatic = false)
    {
        var shape = new SphereShape(radius);
        var body = World.CreateRigidBody();
        body.AddShape(shape);
        body.Position = new JVector(position.X, position.Y, position.Z);
        if (isStatic) body.MotionType = MotionType.Static;
        _bodies.Add((body, null));
        return body;
    }

    public RigidBody CreatePlane(Vector3 position, Vector3 normal)
    {
        // Jitter2 doesn't have a PlaneShape. We use a large thin box instead.
        // A box of size (1000, 0.1, 1000) acts as a sufficient ground plane.
        var shape = new BoxShape(1000, 0.1f, 1000); 
        var body = World.CreateRigidBody();
        body.AddShape(shape);
        
        // Offset Y position by half height so top surface is at position.Y
        body.Position = new JVector(position.X, position.Y - 0.05f, position.Z);
        body.MotionType = MotionType.Static;
        _bodies.Add((body, null));
        return body;
    }
    
    public IEnumerable<(RigidBody Body, object? UserData)> GetBodies() => _bodies;
}