using System.Numerics;

namespace Whisperleaf.Graphics.Scene;

public class Camera
{
    public Vector3 Position { get; set; } = new(0, 1, -3);
    public Vector3 Target { get; set; } = Vector3.Zero;
    public Vector3 Up { get; set; } = Vector3.UnitY;

    public float Fov { get; set; } = 90.0f;
    public float AspectRatio { get; set; }
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 100.0f;

    public Camera(float aspectRatio)
    {
        AspectRatio = aspectRatio;
    }

    public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookAt(Position, Target, Up);
    
    public Matrix4x4 GetProjectionMatrix() => Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI * Fov / 180.0f, AspectRatio, Near, Far);
}