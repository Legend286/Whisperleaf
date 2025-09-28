using System.Collections.Generic;
using Veldrid;
using Whisperleaf.Platform;

namespace Whisperleaf.Input;

public class InputManager
{
    private readonly HashSet<Key> _keysDown = new();
    private readonly HashSet<Key> _keysPressedThisFrame = new();
    private readonly HashSet<Key> _keysReleasedThisFrame = new();

    private readonly HashSet<MouseButton> _buttonsDown = new();
    private readonly HashSet<MouseButton> _buttonsPressedThisFrame = new();
    private readonly HashSet<MouseButton> _buttonsReleasedThisFrame = new();

    private int _mouseX, _mouseY;
    private int _lastMouseX, _lastMouseY;

    public (int X, int Y) MousePosition => (_mouseX, _mouseY);
    public (int dX, int dY) MouseDelta => (_mouseX - _lastMouseX, _mouseY - _lastMouseY);
    public float WheelDelta { get; private set; }

    public void Update(Window window)
    {
        _keysPressedThisFrame.Clear();
        _keysReleasedThisFrame.Clear();
        _buttonsPressedThisFrame.Clear();
        _buttonsReleasedThisFrame.Clear();
        WheelDelta = 0;

        _lastMouseX = _mouseX;
        _lastMouseY = _mouseY;

        InputSnapshot snapshot = window.PumpEvents();

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
    public bool IsKeyDown(Key key) => _keysDown.Contains(key);
    public bool WasKeyPressed(Key key) => _keysPressedThisFrame.Contains(key);
    public bool WasKeyReleased(Key key) => _keysReleasedThisFrame.Contains(key);

    // Mouse queries
    public bool IsButtonDown(MouseButton btn) => _buttonsDown.Contains(btn);
    public bool WasButtonPressed(MouseButton btn) => _buttonsPressedThisFrame.Contains(btn);
    public bool WasButtonReleased(MouseButton btn) => _buttonsReleasedThisFrame.Contains(btn);
}
