using System.Numerics;
using Veldrid;
using Whisperleaf.Input;
using Whisperleaf.Platform;

namespace Whisperleaf.Graphics.Scene;

public class CameraController
{
    private readonly Camera _camera;
    private readonly Window _window;

    private float _speed = 20.0f;
    private float _mouseSensitivity = 0.0025f;

    public bool InputEnabled { get; set; } = true;

    public CameraController(Camera camera, Window window)
    {
        _camera = camera;
        _window = window;
    }
    private bool skipNextDelta = false;

    private bool initialCapture = true;
    private (int X, int Y) _lockPosition;

    public void Update(float deltaTime)
    {
        if (InputEnabled && InputManager.IsButtonDown(MouseButton.Right))
        {
            _window.ShowCursor(false);

            if (initialCapture)
            {
                _lockPosition = InputManager.MousePosition;
                initialCapture = false;
                skipNextDelta = true;
            }

            HandleMovement(deltaTime);
            HandleMouse();
        }
        else
        {
            _window.ShowCursor(true);
            initialCapture = true;
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
}
