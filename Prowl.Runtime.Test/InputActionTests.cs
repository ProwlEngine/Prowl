// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

using Xunit;

namespace Prowl.Runtime.Test;

public class InputActionTests
{
    private sealed class HeldKeys : NullInputHandler, IInputHandler
    {
        public readonly HashSet<KeyCode> Held = new();
        bool IInputHandler.GetKey(KeyCode key) => Held.Contains(key);
    }

    private sealed class PressedThisFrame : NullInputHandler, IInputHandler
    {
        public KeyCode? Key;
        public int? MouseButton;
        public bool HeldKey;
        bool IInputHandler.GetKeyDown(KeyCode key) => key == Key;
        bool IInputHandler.GetMouseButtonDown(int button) => button == MouseButton;
        bool IInputHandler.IsAnyKeyDown => HeldKey;
    }

    private static void WithInput(IInputHandler handler, Action test)
    {
        Input.PushHandler(handler);
        try { test(); }
        finally { Input.PopHandler(); }
    }

    [Fact]
    public void AnyKeyDown_TrueOnlyOnThePressFrame()
    {
        var input = new PressedThisFrame { HeldKey = true };
        WithInput(input, () =>
        {
            Assert.True(Input.AnyKey);
            Assert.False(Input.AnyKeyDown);

            input.Key = KeyCode.Q;
            Assert.True(Input.AnyKeyDown);
        });
    }

    [Fact]
    public void AnyButtonDown_CountsMouseButtons_AnyKeyDownDoesNot()
    {
        var input = new PressedThisFrame { MouseButton = 1 };
        WithInput(input, () =>
        {
            Assert.False(Input.AnyKeyDown);
            Assert.True(Input.AnyButtonDown);
        });
    }

    [Fact]
    public void Tap_StillFiresAfterAPressTooLongToBeATap()
    {
        var input = new HeldKeys();
        var action = new InputAction("Hop");
        action.AddBinding(KeyCode.Space, InputInteractionType.Tap);
        action.Enable();
        int performed = 0;
        action.Performed += _ => performed++;

        float time = 0f;
        void Frame(bool held, float seconds)
        {
            if (held) input.Held.Add(KeyCode.Space);
            else input.Held.Remove(KeyCode.Space);
            time += seconds;
            action.UpdateState(input, time);
        }

        // Held for a second, far past the tap window, so it is not a tap.
        Frame(true, 0.016f);
        Frame(true, 1f);
        Frame(false, 0.016f);
        Assert.Equal(0, performed);

        // A quick press afterwards is.
        Frame(true, 0.5f);
        Frame(true, 0.05f);
        Frame(false, 0.05f);
        Assert.Equal(1, performed);
    }

    private sealed class VibrationRecorder : NullInputHandler, IInputHandler
    {
        public readonly List<(int Pad, float Left, float Right)> Calls = new();
        void IInputHandler.SetGamepadVibration(int gamepadIndex, float leftMotor, float rightMotor) => Calls.Add((gamepadIndex, leftMotor, rightMotor));
    }

    [Fact]
    public void VibrateGamepad_StopsAfterItsDuration()
    {
        var input = new VibrationRecorder();
        WithInput(input, () =>
        {
            Input.VibrateGamepad(1f, 0.5f, 0.2f, gamepadIndex: 1);
            Input.UpdateActions(0.1f);
            Assert.Single(input.Calls);

            Input.UpdateActions(0.15f);
            Assert.Equal((1, 0f, 0f), input.Calls[^1]);
            Assert.Equal(2, input.Calls.Count);
        });
    }

    [Fact]
    public void SetGamepadVibration_CancelsAPendingTimeout()
    {
        var input = new VibrationRecorder();
        WithInput(input, () =>
        {
            Input.VibrateGamepad(1f, 1f, 0.1f);
            Input.SetGamepadVibration(0.3f, 0.3f);
            Input.UpdateActions(1f);

            Assert.Equal((0, 0.3f, 0.3f), input.Calls[^1]);
        });
    }
}
