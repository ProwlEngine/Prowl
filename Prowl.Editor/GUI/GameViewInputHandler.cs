using System;

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI;

/// <summary>
/// Input handler for play mode that only forwards keyboard/mouse input
/// when the Game View panel is hovered AND gameplay code is executing.
/// Editor code (camera, gizmos, UI) always gets full input.
/// Gamepads always pass through.
/// Wraps the real DefaultInputHandler underneath.
/// </summary>
public class GameViewInputHandler : IInputHandler
{
    private readonly IInputHandler _real;

    /// <summary>
    /// Set to true each frame by GameViewPanel when it's hovered.
    /// Reset to false at the start of each frame.
    /// </summary>
    public static bool IsGameViewFocused { get; set; }

    /// <summary>
    /// The Game View's mapping from window space into the game render-target's pixel space, published each
    /// frame by GameViewPanel. While gameplay is executing in the editor, MousePosition/PrevMousePosition are
    /// reported in this render-target space (which matches Camera.PixelWidth/PixelHeight) instead of raw window
    /// space, so gameplay picking (Camera.ScreenPointToRay etc.) behaves like a standalone build. Null when
    /// there is no valid game viewport.
    /// </summary>
    public static GameViewport? Viewport { get; set; }

    public readonly struct GameViewport
    {
        public readonly Float2 DisplayOrigin; // top-left of the letterboxed display rect, window coords
        public readonly Float2 DisplaySize;   // size of the display rect, window coords
        public readonly Int2 RenderSize;      // render-target pixel size (== Camera.PixelWidth/PixelHeight)

        public GameViewport(Float2 displayOrigin, Float2 displaySize, Int2 renderSize)
        {
            DisplayOrigin = displayOrigin;
            DisplaySize = displaySize;
            RenderSize = renderSize;
        }
    }

    private bool ShouldFilter => Runtime.Application.IsGameplayExecuting && !IsGameViewFocused;

    private static bool TryGetViewport(out GameViewport vp)
    {
        if (Runtime.Application.IsGameplayExecuting && Viewport is { } v && v.DisplaySize.X > 0 && v.DisplaySize.Y > 0)
        {
            vp = v;
            return true;
        }
        vp = default;
        return false;
    }

    // Remap a window-space cursor position into the game render-target's pixel space while gameplay runs, so
    // scripts reading Input.MousePosition get coordinates consistent with Camera.PixelWidth/Height. Outside
    // gameplay execution (editor camera/gizmos/panels) the raw window position passes through unchanged.
    private static Int2 ToViewport(Int2 windowPos)
    {
        if (!TryGetViewport(out GameViewport vp)) return windowPos;
        float x = (windowPos.X - vp.DisplayOrigin.X) * (vp.RenderSize.X / vp.DisplaySize.X);
        float y = (windowPos.Y - vp.DisplayOrigin.Y) * (vp.RenderSize.Y / vp.DisplaySize.Y);
        return new Int2((int)MathF.Round(x), (int)MathF.Round(y));
    }

    public GameViewInputHandler(IInputHandler realHandler)
    {
        _real = realHandler;
    }

    // Clipboard always works
    public string Clipboard
    {
        get => _real.Clipboard;
        set => _real.Clipboard = value;
    }

    // Keyboard filtered only during gameplay execution outside game view
    public bool IsAnyKeyDown => ShouldFilter ? false : _real.IsAnyKeyDown;
    public char? GetPressedChar() => ShouldFilter ? null : _real.GetPressedChar();
    public string InputString => ShouldFilter ? string.Empty : _real.InputString;
    public bool GetKey(KeyCode key) => ShouldFilter ? false : _real.GetKey(key);
    public bool GetKeyDown(KeyCode key) => ShouldFilter ? false : _real.GetKeyDown(key);
    public bool GetKeyUp(KeyCode key) => ShouldFilter ? false : _real.GetKeyUp(key);

    // Mouse filtered only during gameplay execution outside game view
    public Int2 PrevMousePosition => ToViewport(_real.PrevMousePosition);
    public Int2 MousePosition
    {
        get => ToViewport(_real.MousePosition);
        set => _real.MousePosition = value;
    }
    public Float2 MouseDelta
    {
        get
        {
            if (ShouldFilter) return Float2.Zero;
            Float2 delta = _real.MouseDelta;
            if (TryGetViewport(out GameViewport vp))
                delta = new Float2(delta.X * (vp.RenderSize.X / vp.DisplaySize.X), delta.Y * (vp.RenderSize.Y / vp.DisplaySize.Y));
            return delta;
        }
    }
    public float MouseWheelDelta => ShouldFilter ? 0f : _real.MouseWheelDelta;
    public bool GetMouseButton(int button) => ShouldFilter ? false : _real.GetMouseButton(button);
    public bool GetMouseButtonDown(int button) => ShouldFilter ? false : _real.GetMouseButtonDown(button);
    public bool GetMouseButtonUp(int button) => ShouldFilter ? false : _real.GetMouseButtonUp(button);
    public void SetCursorVisible(bool visible, int miceIndex = 0)
    {
        _real.SetCursorVisible(visible, miceIndex);
    }

    public void SetCursorShape(PaperCursor shape, int miceIndex = 0)
    {
        _real.SetCursorShape(shape, miceIndex);
    }

    // Events always forward (editor needs these for its own input processing)
    public event Action<KeyCode, bool> OnKeyEvent
    {
        add => _real.OnKeyEvent += value;
        remove => _real.OnKeyEvent -= value;
    }
    public event Action<MouseButton, float, float, bool, bool> OnMouseEvent
    {
        add => _real.OnMouseEvent += value;
        remove => _real.OnMouseEvent -= value;
    }

    // Gamepads always pass through (physical controllers work regardless of focus)
    public int GetGamepadCount() => _real.GetGamepadCount();
    public bool IsGamepadConnected(int gamepadIndex) => _real.IsGamepadConnected(gamepadIndex);
    public bool GetGamepadButton(int gamepadIndex, GamepadButton button) => _real.GetGamepadButton(gamepadIndex, button);
    public bool GetGamepadButtonDown(int gamepadIndex, GamepadButton button) => _real.GetGamepadButtonDown(gamepadIndex, button);
    public bool GetGamepadButtonUp(int gamepadIndex, GamepadButton button) => _real.GetGamepadButtonUp(gamepadIndex, button);
    public Float2 GetGamepadAxis(int gamepadIndex, int axisIndex) => _real.GetGamepadAxis(gamepadIndex, axisIndex);
    public float GetGamepadTrigger(int gamepadIndex, int triggerIndex) => _real.GetGamepadTrigger(gamepadIndex, triggerIndex);
    public void SetGamepadVibration(int gamepadIndex, float leftMotor, float rightMotor) => _real.SetGamepadVibration(gamepadIndex, leftMotor, rightMotor);
}
