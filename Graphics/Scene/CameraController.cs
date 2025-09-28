using System.Numerics;
using Veldrid;
using Whisperleaf.Input;
using Whisperleaf.Platform;

namespace Whisperleaf.Graphics.Scene;

public class CameraController
{
    private readonly Camera _camera;
    private readonly InputManager _input;
    private readonly Window _window;

    private float _speed = 3.0f;
    private float _mouseSensitivity = 0.0025f;

    public CameraController(Camera camera, InputManager input, Window window)
    {
        _camera = camera;
        _input = input;
        _window = window;
    }

    public void Update(float deltaTime)
    {
        if (_input.IsButtonDown(MouseButton.Right))
        {
            _window.ShowCursor(false);

            HandleMovement(deltaTime);
            HandleMouse();
        }
        else
        {
            _window.ShowCursor(true);
        }
    }

    private void HandleMovement(float dt)
    {
        var forward = _camera.GetForward();
        var right = _camera.GetRight();
        var up = _camera.GetUp();

        if (_input.IsKeyDown(Key.W)) _camera.Position += forward * _speed * dt;
        if (_input.IsKeyDown(Key.S)) _camera.Position -= forward * _speed * dt;
        if (_input.IsKeyDown(Key.A)) _camera.Position -= right * _speed * dt;
        if (_input.IsKeyDown(Key.D)) _camera.Position += right * _speed * dt;
        if (_input.IsKeyDown(Key.Space)) _camera.Position += up * _speed * dt;
        if (_input.IsKeyDown(Key.ShiftLeft)) _camera.Position -= up * _speed * dt;
    }

    private void HandleMouse()
    {
        var (mouseX, mouseY) = _window.GetMousePosition;
        int centerX = _window.Width / 2;
        int centerY = _window.Height / 2;

        int dx = mouseX - centerX;
        int dy = mouseY - centerY;

        if (dx != 0 || dy != 0)
        {
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, dx * -_mouseSensitivity);
            var pitch = Quaternion.CreateFromAxisAngle(_camera.GetRight(), dy * -_mouseSensitivity);
            _camera.Orientation = Quaternion.Normalize(pitch * yaw * _camera.Orientation);

            // reset cursor back to center
            _window.SetMousePosition(centerX, centerY);
        }
    }
}
