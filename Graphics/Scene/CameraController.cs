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

    public CameraController(Camera camera, Window window)
    {
        _camera = camera;
        _window = window;
    }
    private bool skipNextDelta = false;

    private bool _isActive = false;
    private bool _wasRightDown = false;
    private bool initialCapture = true;

    public void Update(float deltaTime, bool allowStart)
    {
        bool rightDown = InputManager.IsButtonDown(MouseButton.Right);

        if (rightDown && !_wasRightDown)
        {
            if (allowStart)
            {
                _isActive = true;
                _window.ShowCursor(false);
                initialCapture = true;
            }
        }
        else if (!rightDown && _wasRightDown)
        {
            _isActive = false;
            _window.ShowCursor(true);
            initialCapture = true;
        }

        _wasRightDown = rightDown;

        if (_isActive)
        {
            int centerX = _window.Width / 2;
            int centerY = _window.Height / 2;

            if (initialCapture)
            {
                lastMouse = InputManager.MousePosition;
                initialCapture = false;
                skipNextDelta = true;
            }

            HandleMovement(deltaTime);
            HandleMouse();
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
    private (int X, int Y) lastMouse = (0, 0);
    private void HandleMouse()
    {
        var mouseDelta = InputManager.MouseDelta;

        if (skipNextDelta)
        { 
            // swallow the warp delta
            skipNextDelta = false;
            return;
        }
        
        currentMouse = InputManager.MousePosition;
        int centerX = _window.Width / 2;
        int centerY = _window.Height / 2;

        int dx = currentMouse.X - lastMouse.X;
        int dy = currentMouse.Y - lastMouse.Y;

        if (dx != 0 || dy != 0)
        {
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, dx * -_mouseSensitivity);
            var pitch = Quaternion.CreateFromAxisAngle(_camera.GetRight(), dy * -_mouseSensitivity);
            _camera.Orientation = Quaternion.Normalize(yaw * pitch * _camera.Orientation);

            // reset cursor back to center
            _window.SetMousePosition(lastMouse.X, lastMouse.Y);
        }
    }
}
