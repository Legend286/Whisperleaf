using System.Numerics;
using Whisperleaf.Input;

namespace Whisperleaf.Graphics.Scene;

public class Camera
{
    public Vector3 Position { get; set; } = new(0, 1, 3);
    public Quaternion Orientation { get; set; } = Quaternion.Identity; 
    public float Fov { get; set; } = 90.0f;
    public float AspectRatio { get; set; }
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 10000.0f;
    public float ViewProjection => (GetViewMatrix() * GetProjectionMatrix()).M11;
    public Matrix4x4 ViewMatrix => GetViewMatrix();
    public Matrix4x4 ProjectionMatrix => GetProjectionMatrix();

    public Camera(float aspectRatio)
    {
        AspectRatio = aspectRatio;
    }
    public Matrix4x4 GetViewMatrix()
    {
        var forward = Vector3.Transform(-Vector3.UnitZ, Orientation);
        var up = Vector3.Transform(Vector3.UnitY, Orientation);
        
        return Matrix4x4.CreateLookAt(Position, Position + forward, up);
    }
    public Matrix4x4 GetProjectionMatrix() => Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI * Fov / 180.0f, AspectRatio, Near, Far);
    public Vector3 GetForward() => Vector3.Transform(-Vector3.UnitZ, Orientation);
    public Vector3 GetRight()   => Vector3.Transform(Vector3.UnitX, Orientation);
    public Vector3 GetUp()      => Vector3.Transform(Vector3.UnitY, Orientation);
}