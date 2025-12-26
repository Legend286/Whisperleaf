using System.Numerics;
using Veldrid;
using Whisperleaf.Input;
using Whisperleaf.Platform;

namespace Whisperleaf.Graphics.Scene;

public enum CameraControllerMode
{
    Free,
    Orbit
}

public class CameraController
{
    private readonly Camera _camera;
    private readonly Window _window;

    private float _speed = 20.0f;
    private float _mouseSensitivity = 0.0025f;

    public bool InputEnabled { get; set; } = true;
    public CameraControllerMode Mode { get; set; } = CameraControllerMode.Free;
    public Vector3 OrbitTarget { get; set; } = Vector3.Zero;
    
    private float _orbitDistance = 5.0f;
    private float _orbitYaw = 0.0f;
    private float _orbitPitch = 0.0f;

    public CameraController(Camera camera, Window window)
    {
        _camera = camera;
        _window = window;
        
        // Initialize orbit parameters from current camera position
        var offset = _camera.Position - OrbitTarget;
        _orbitDistance = offset.Length();
    }
    private bool skipNextDelta = false;

    private bool initialCapture = true;
    private (int X, int Y) _lockPosition;

    public void Update(float deltaTime)
    {
        if (!InputEnabled) return;

        if (InputManager.IsButtonDown(MouseButton.Right))
        {
            _window.ShowCursor(false);

            if (initialCapture)
            {
                _lockPosition = InputManager.MousePosition;
                initialCapture = false;
                skipNextDelta = true;
            }

            if (Mode == CameraControllerMode.Free)
            {
                HandleMovement(deltaTime);
                HandleMouse();
            }
            else
            {
                HandleOrbitMouse();
            }
        }
        else
        {
            _window.ShowCursor(true);
            initialCapture = true;
        }

        if (Mode == CameraControllerMode.Orbit)
        {
            UpdateOrbit(deltaTime);
        }
    }

    private void HandleMovement(float dt)
    {
        var forward = _camera.GetForward();
        var right = _camera.GetRight();
        var up = _camera.GetUp();

        if (InputManager.IsKeyDown(Key.W)) _camera.Position += forward * _speed * dt;
        if (InputManager.IsKeyDown(Key.S)) _camera.Position -= forward * _speed * dt;
        if (InputManager.IsKeyDown(Key.A)) _camera.Position -= right * _speed * dt;
        if (InputManager.IsKeyDown(Key.D)) _camera.Position += right * _speed * dt;
        if (InputManager.IsKeyDown(Key.Space)) _camera.Position += up * _speed * dt;
        if (InputManager.IsKeyDown(Key.ShiftLeft)) _camera.Position -= up * _speed * dt;
    }

    private (int X, int Y) currentMouse = (0, 0);
    private void HandleMouse()
    {
        if (skipNextDelta)
        { 
            skipNextDelta = false;
            return;
        }
        
        currentMouse = InputManager.MousePosition;
        
        int dx = currentMouse.X - _lockPosition.X;
        int dy = currentMouse.Y - _lockPosition.Y;

        if (dx != 0 || dy != 0)
        {
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, dx * -_mouseSensitivity);
            var pitch = Quaternion.CreateFromAxisAngle(_camera.GetRight(), dy * -_mouseSensitivity);
            _camera.Orientation = Quaternion.Normalize(yaw * pitch * _camera.Orientation);

            // reset cursor back to lock position
            _window.SetMousePosition(_lockPosition.X, _lockPosition.Y);
        }
    }

    private void HandleOrbitMouse()
    {
        if (skipNextDelta)
        {
            skipNextDelta = false;
            return;
        }

        currentMouse = InputManager.MousePosition;

        int dx = currentMouse.X - _lockPosition.X;
        int dy = currentMouse.Y - _lockPosition.Y;

        if (dx != 0 || dy != 0)
        {
            _orbitYaw += dx * _mouseSensitivity;
            _orbitPitch += dy * _mouseSensitivity;
            _orbitPitch = Math.Clamp(_orbitPitch, -MathF.PI / 2.0f + 0.01f, MathF.PI / 2.0f - 0.01f);

            _window.SetMousePosition(_lockPosition.X, _lockPosition.Y);
        }
    }

    private void UpdateOrbit(float dt)
    {
        _orbitDistance -= InputManager.WheelDelta * 0.5f;
        _orbitDistance = Math.Max(0.1f, _orbitDistance);

        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -_orbitYaw) *
                       Quaternion.CreateFromAxisAngle(Vector3.UnitX, -_orbitPitch);

        var offset = Vector3.Transform(new Vector3(0, 0, _orbitDistance), rotation);
        _camera.Position = OrbitTarget + offset;
        _camera.Orientation = rotation;
    }
}
