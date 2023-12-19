using Silk.NET.Input;
using Silk.NET.Maths;
using System.Collections.Generic;

namespace Prowl.Runtime;

public static class Input
{
    public static IInputContext Context { get; internal set; }

    public static IReadOnlyList<IKeyboard> Keyboards => Context.Keyboards;
    public static IReadOnlyList<IMouse> Mice => Context.Mice;
    public static IReadOnlyList<IJoystick> Joysticks => Context.Joysticks;

    internal static void Initialize()
    {
        Context = Window.InternalWindow.CreateInput();
        PreviousMousePosition = MousePosition;
    }

    internal static void Dispose()
    {
        Context.Dispose();
    }

    internal static void Update()
    {
        PreviousMousePosition = MousePosition;
    }

    public static bool IsKeyDown(Key key)
    {
        foreach (var keyboard in Keyboards)
            if (keyboard.IsKeyPressed(key))
                return true;
        return false;
    } 

    public static Vector2D<float> PreviousMousePosition { get; private set; }
    public static Vector2D<float> MouseDelta => MousePosition - PreviousMousePosition;
    public static Vector2D<float> MousePosition {
        get {
            return Mice[0].Position.ToGeneric();
        }
        set {
            Mice[0].Position = value.ToSystem();
        }
    }


    public static bool IsMouseDown(MouseButton button)
    {
        foreach (var mouse in Mice)
            if (mouse.IsButtonPressed(button))
                return true;
        return false;
    }
}