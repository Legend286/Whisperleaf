using System.Collections.Generic;
using Veldrid;

namespace Whisperleaf.Input;

public static class InputManager
{
    private static readonly HashSet<Key> _keysDown = new();
    private static readonly HashSet<Key> _keysPressedThisFrame = new();
    private static readonly HashSet<Key> _keysReleasedThisFrame = new();

    private static readonly HashSet<MouseButton> _buttonsDown = new();
    private static readonly HashSet<MouseButton> _buttonsPressedThisFrame = new();
    private static readonly HashSet<MouseButton> _buttonsReleasedThisFrame = new();

    private static int _mouseX, _mouseY;
    private static int _lastMouseX, _lastMouseY;

    public static (int X, int Y) MousePosition => (_mouseX, _mouseY);
    public static (int dX, int dY) MouseDelta => (_mouseX - _lastMouseX, _mouseY - _lastMouseY);
    public static float WheelDelta { get; private set; }

    public static void Update(InputSnapshot snapshot)
    {
        _keysPressedThisFrame.Clear();
        _keysReleasedThisFrame.Clear();
        _buttonsPressedThisFrame.Clear();
        _buttonsReleasedThisFrame.Clear();
        WheelDelta = 0;

        _lastMouseX = _mouseX;
        _lastMouseY = _mouseY;

        _mouseX = (int)snapshot.MousePosition.X;
        _mouseY = (int)snapshot.MousePosition.Y;
        WheelDelta = snapshot.WheelDelta;

        foreach (var keyEvent in snapshot.KeyEvents)
        {
            if (keyEvent.Down)
            {
                if (_keysDown.Add(keyEvent.Key))
                    _keysPressedThisFrame.Add(keyEvent.Key);
            }
            else
            {
                if (_keysDown.Remove(keyEvent.Key))
                    _keysReleasedThisFrame.Add(keyEvent.Key);
            }
        }

        foreach (var mouseEvent in snapshot.MouseEvents)
        {
            if (mouseEvent.Down)
            {
                if (_buttonsDown.Add(mouseEvent.MouseButton))
                    _buttonsPressedThisFrame.Add(mouseEvent.MouseButton);
            }
            else
            {
                if (_buttonsDown.Remove(mouseEvent.MouseButton))
                    _buttonsReleasedThisFrame.Add(mouseEvent.MouseButton);
            }
        }
    }

    // Keyboard queries
    public static bool IsKeyDown(Key key) => _keysDown.Contains(key);
    public static bool WasKeyPressed(Key key) => _keysPressedThisFrame.Contains(key);
    public static bool WasKeyReleased(Key key) => _keysReleasedThisFrame.Contains(key);

    // Mouse queries
    public static bool IsButtonDown(MouseButton btn) => _buttonsDown.Contains(btn);
    public static bool WasButtonPressed(MouseButton btn) => _buttonsPressedThisFrame.Contains(btn);
    public static bool WasButtonReleased(MouseButton btn) => _buttonsReleasedThisFrame.Contains(btn);
}
