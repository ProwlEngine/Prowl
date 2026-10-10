using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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

    /// <summary> Describes the mapping from window coordinates to the game render-target pixel space, published each frame by GameViewPanel. </summary>
    public readonly struct GameViewport
    {
        /// <summary> Top-left corner of the letterboxed display rectangle, in window coordinates. </summary>
        public readonly Float2 DisplayOrigin; // top-left of the letterboxed display rect, window coords
        /// <summary> Size of the display rectangle, in window coordinates. </summary>
        public readonly Float2 DisplaySize;   // size of the display rect, window coords
        /// <summary> Render-target pixel size, matching Camera.PixelWidth and Camera.PixelHeight. </summary>
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

    // Inverse of ToViewport, so warping the cursor takes coordinates in the same space reads report.
    private static Int2 FromViewport(Int2 viewportPos)
    {
        if (!TryGetViewport(out GameViewport vp)) return viewportPos;
        float x = viewportPos.X * (vp.DisplaySize.X / vp.RenderSize.X) + vp.DisplayOrigin.X;
        float y = viewportPos.Y * (vp.DisplaySize.Y / vp.RenderSize.Y) + vp.DisplayOrigin.Y;
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

    // Keyboard filtered only during gameplay execution outside game view. Simulated input reaches gameplay either way.
    public bool IsAnyKeyDown => (!ShouldFilter && _real.IsAnyKeyDown) || SimulatedInput.AnyKey;
    public char? GetPressedChar() => ShouldFilter ? null : _real.GetPressedChar();
    public string InputString => ShouldFilter ? string.Empty : _real.InputString;
    public bool GetKey(KeyCode key) => (!ShouldFilter && _real.GetKey(key)) || SimulatedInput.GetKey(key);
    public bool GetKeyDown(KeyCode key) => (!ShouldFilter && _real.GetKeyDown(key)) || SimulatedInput.GetKeyDown(key);
    public bool GetKeyUp(KeyCode key) => (!ShouldFilter && _real.GetKeyUp(key)) || SimulatedInput.GetKeyUp(key);

    public Int2 MapWindowPosition(Int2 windowPos) => ToViewport(windowPos);

    private static Int2? SimulatedPointer => Runtime.Application.IsGameplayExecuting ? SimulatedInput.Pointer : null;

    // Mouse filtered only during gameplay execution outside game view
    public Int2 PrevMousePosition => SimulatedPointer ?? ToViewport(_real.PrevMousePosition);
    public Int2 MousePosition
    {
        get => SimulatedPointer ?? ToViewport(_real.MousePosition);
        set => _real.MousePosition = FromViewport(value);
    }
    public Float2 MouseDelta
    {
        get
        {
            Float2 simulated = SimulatedInput.MouseDelta;
            if (ShouldFilter) return simulated;
            Float2 delta = _real.MouseDelta;
            if (TryGetViewport(out GameViewport vp))
                delta = new Float2(delta.X * (vp.RenderSize.X / vp.DisplaySize.X), delta.Y * (vp.RenderSize.Y / vp.DisplaySize.Y));
            return delta + simulated;
        }
    }
    public float MouseWheelDelta => ShouldFilter ? 0f : _real.MouseWheelDelta;
    public bool GetMouseButton(int button) => (!ShouldFilter && _real.GetMouseButton(button)) || SimulatedInput.GetMouseButton(button);
    public bool GetMouseButtonDown(int button) => (!ShouldFilter && _real.GetMouseButtonDown(button)) || SimulatedInput.GetMouseButtonDown(button);
    public bool GetMouseButtonUp(int button) => (!ShouldFilter && _real.GetMouseButtonUp(button)) || SimulatedInput.GetMouseButtonUp(button);

    public void ApplyCursorState(bool visible, CursorLockMode mode)
    {
        _real.ApplyCursorState(visible, mode);
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
    public int GetGamepadSlotCount() => _real.GetGamepadSlotCount();
    public bool IsGamepadConnected(int gamepadIndex) => _real.IsGamepadConnected(gamepadIndex);
    public bool GetGamepadButton(int gamepadIndex, GamepadButton button) => _real.GetGamepadButton(gamepadIndex, button);
    public bool GetGamepadButtonDown(int gamepadIndex, GamepadButton button) => _real.GetGamepadButtonDown(gamepadIndex, button);
    public bool GetGamepadButtonUp(int gamepadIndex, GamepadButton button) => _real.GetGamepadButtonUp(gamepadIndex, button);
    public Float2 GetGamepadAxis(int gamepadIndex, int axisIndex) => _real.GetGamepadAxis(gamepadIndex, axisIndex);
    public float GetGamepadTrigger(int gamepadIndex, int triggerIndex) => _real.GetGamepadTrigger(gamepadIndex, triggerIndex);
    public void SetGamepadVibration(int gamepadIndex, float leftMotor, float rightMotor) => _real.SetGamepadVibration(gamepadIndex, leftMotor, rightMotor);
}

/// <summary>
/// Input pressed on the player's behalf, such as by the CLI, merged into what gameplay reads in play mode. A key or
/// button is held for a time, reading down on the first frame gameplay sees it and up on the first frame after release.
/// </summary>
public static class SimulatedInput
{
    private sealed class Hold
    {
        public TimeSpan ReleaseAt;
        public long StartFrame;
        public long DownFrame = -1;
        public long UpFrame = -1;
        public bool Released;
    }

    private static readonly Dictionary<KeyCode, Hold> s_keys = new();
    private static readonly Dictionary<int, Hold> s_buttons = new();
    private static readonly Stopwatch s_clock = Stopwatch.StartNew();
    private static readonly object s_lock = new();
    private static Float2 s_look;
    private static TimeSpan s_lookUntil;
    private static Int2 s_pointer;
    private static TimeSpan s_pointerUntil;

    public static void Press(KeyCode key, double seconds) { lock (s_lock) s_keys[key] = new Hold { ReleaseAt = s_clock.Elapsed + TimeSpan.FromSeconds(seconds) }; }

    /// <summary>
    /// Presses a mouse button. With a position, in game render pixels, the pointer moves there first and the button
    /// goes down two frames later, so UI sees the hover before the press as it would from a real mouse.
    /// </summary>
    public static void PressButton(int button, double seconds, Int2? position = null)
    {
        lock (s_lock)
        {
            var hold = new Hold { ReleaseAt = s_clock.Elapsed + TimeSpan.FromSeconds(seconds) };
            if (position is { } at)
            {
                s_pointer = at;
                s_pointerUntil = s_clock.Elapsed + TimeSpan.FromSeconds(seconds + 0.5);
                hold.StartFrame = Time.FrameCount + 2;
            }
            s_buttons[button] = hold;
        }
    }

    /// <summary> Where a simulated click is, in game render pixels, while one is in progress. Only the game view reads it. </summary>
    public static Int2? Pointer
    {
        get
        {
            if (!Application.IsPlaying) return null;
            lock (s_lock) return s_clock.Elapsed < s_pointerUntil ? s_pointer : null;
        }
    }

    /// <summary> Adds a mouse movement every frame until the time is up, as turning a camera would. </summary>
    public static void Look(Float2 deltaPerFrame, double seconds)
    {
        lock (s_lock)
        {
            s_look = deltaPerFrame;
            s_lookUntil = s_clock.Elapsed + TimeSpan.FromSeconds(seconds);
        }
    }

    public static void Clear()
    {
        lock (s_lock)
        {
            s_keys.Clear();
            s_buttons.Clear();
            s_lookUntil = TimeSpan.Zero;
            s_pointerUntil = TimeSpan.Zero;
        }
    }

    /// <summary> Whether anything is still held or moving. </summary>
    public static bool Busy
    {
        get { lock (s_lock) return s_keys.Count > 0 || s_buttons.Count > 0 || s_clock.Elapsed < s_lookUntil; }
    }

    /// <summary> Releases holds whose time is up and forgets finished ones. Called once per editor frame. </summary>
    public static void Tick()
    {
        if (!Application.IsPlaying)
        {
            Clear();
            return;
        }

        lock (s_lock)
        {
            Tick(s_keys);
            Tick(s_buttons);
        }
    }

    private static void Tick<T>(Dictionary<T, Hold> holds) where T : notnull
    {
        long frame = Time.FrameCount;
        var done = new List<T>();
        foreach (var (key, hold) in holds)
        {
            if (!hold.Released && s_clock.Elapsed >= hold.ReleaseAt && hold.DownFrame >= 0 && frame > hold.DownFrame) hold.Released = true;
            bool upSeen = hold.UpFrame >= 0 && frame > hold.UpFrame;
            bool neverRead = hold.Released && s_clock.Elapsed > hold.ReleaseAt + TimeSpan.FromSeconds(5);
            if (upSeen || neverRead) done.Add(key);
        }
        foreach (var key in done) holds.Remove(key);
    }

    private static bool Gameplay => Application.IsPlaying && Application.IsGameplayExecuting;

    public static bool AnyKey
    {
        get
        {
            if (!Gameplay) return false;
            lock (s_lock) return s_keys.Values.Any(h => !h.Released);
        }
    }

    public static bool GetKey(KeyCode key) => Held(s_keys, key);
    public static bool GetKeyDown(KeyCode key) => Down(s_keys, key);
    public static bool GetKeyUp(KeyCode key) => Up(s_keys, key);
    public static bool GetMouseButton(int button) => Held(s_buttons, button);
    public static bool GetMouseButtonDown(int button) => Down(s_buttons, button);
    public static bool GetMouseButtonUp(int button) => Up(s_buttons, button);

    public static Float2 MouseDelta
    {
        get
        {
            if (!Gameplay) return Float2.Zero;
            lock (s_lock) return s_clock.Elapsed < s_lookUntil ? s_look : Float2.Zero;
        }
    }

    private static bool Held<T>(Dictionary<T, Hold> holds, T key) where T : notnull
    {
        if (!Gameplay) return false;
        lock (s_lock)
        {
            if (!holds.TryGetValue(key, out Hold? hold) || Time.FrameCount < hold.StartFrame) return false;
            if (hold.DownFrame < 0) hold.DownFrame = Time.FrameCount;
            if (hold.Released && hold.UpFrame < 0) hold.UpFrame = Time.FrameCount;
            return !hold.Released;
        }
    }

    private static bool Down<T>(Dictionary<T, Hold> holds, T key) where T : notnull
    {
        if (!Gameplay) return false;
        lock (s_lock)
        {
            if (!holds.TryGetValue(key, out Hold? hold) || Time.FrameCount < hold.StartFrame) return false;
            if (hold.DownFrame < 0) hold.DownFrame = Time.FrameCount;
            return hold.DownFrame == Time.FrameCount;
        }
    }

    private static bool Up<T>(Dictionary<T, Hold> holds, T key) where T : notnull
    {
        if (!Gameplay) return false;
        lock (s_lock)
        {
            if (!holds.TryGetValue(key, out Hold? hold) || !hold.Released) return false;
            if (hold.UpFrame < 0) hold.UpFrame = Time.FrameCount;
            return hold.UpFrame == Time.FrameCount;
        }
    }
}
