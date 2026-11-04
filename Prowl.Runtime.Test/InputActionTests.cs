// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class InputActionTests
{
    private sealed class HeldKeys : NullInputHandler, IInputHandler
    {
        public readonly HashSet<KeyCode> Held = [];
        public readonly HashSet<KeyCode> PhysicalHeld = [];
        bool IInputHandler.GetKey(KeyCode key, bool physical) => physical ? PhysicalHeld.Contains(key) : Held.Contains(key);
        
    }

    private sealed class PressedThisFrame : NullInputHandler, IInputHandler
    {
        public KeyCode? Key;
        public KeyCode? PhysicalKey;
        public int? MouseButton;
        public bool HeldKey;
        public readonly HashSet<KeyCode> Held = [];
        public readonly HashSet<KeyCode> PhysicalHeld = [];
        bool IInputHandler.GetKey(KeyCode key, bool physical) => physical ? PhysicalHeld.Contains(key) : Held.Contains(key);
        bool IInputHandler.GetKeyDown(KeyCode key, bool physical) => physical ? key == PhysicalKey : key == Key;
        bool IInputHandler.GetMouseButtonDown(int button) => button == MouseButton;
        bool IInputHandler.IsAnyKeyDown => HeldKey;
    }

    private sealed class PhysicalEventDispatcher : NullInputHandler, IInputHandler
    {
        public event Action<KeyCode, bool>? PhysicalKeyEvent;
        event Action<KeyCode, bool> IInputHandler.OnPhysicalKeyEvent
        {
            add => PhysicalKeyEvent += value;
            remove => PhysicalKeyEvent -= value;
        }
        public void Fire(KeyCode key, bool down) => PhysicalKeyEvent?.Invoke(key, down);
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

    private sealed class Gamepads : NullInputHandler, IInputHandler
    {
        public int Slots = 2;
        public readonly HashSet<(int Pad, GamepadButton Button)> Held = [];
        public float RightTrigger;
        int IInputHandler.GetGamepadSlotCount() => Slots;
        bool IInputHandler.IsGamepadConnected(int gamepadIndex) => gamepadIndex < Slots;
        bool IInputHandler.GetGamepadButton(int gamepadIndex, GamepadButton button) => Held.Contains((gamepadIndex, button));
        float IInputHandler.GetGamepadTrigger(int gamepadIndex, int triggerIndex) => triggerIndex == 1 ? RightTrigger : 0f;
    }

    [Fact]
    public void RenameAction_KeepsTheActionEnabledAndInPlace()
    {
        var map = new InputActionMap("Player");
        map.AddAction("Jump");
        InputAction fire = map.AddAction("Fire");
        map.AddAction("Pause");
        map.Enable();

        Assert.True(map.RenameAction("Fire", "Shoot"));

        Assert.Same(fire, map.GetAction("Shoot"));
        Assert.True(fire.Enabled);
        Assert.Equal(["Jump", "Shoot", "Pause"], map.Actions.Select(a => a.Name));
        Assert.False(map.RenameAction("Shoot", "Jump"));
    }

    [Fact]
    public void Actions_KeepTheirOrderThroughSaveAndLoad()
    {
        var map = new InputActionMap("Player");
        map.AddAction("Move");
        map.AddAction("Jump");
        map.AddAction("Fire");
        map.MoveAction(2, 0);

        EchoObject saved = EchoObject.NewCompound();
        map.Serialize(ref saved, new SerializationContext());
        var loaded = new InputActionMap("Loaded");
        loaded.Deserialize(saved, new SerializationContext());

        Assert.Equal(["Fire", "Move", "Jump"], loaded.Actions.Select(a => a.Name));
    }

    [Fact]
    public void MultiTapOnAComposite_PerformsWithTheTappedDirection()
    {
        var input = new HeldKeys();
        var dash = new InputAction("Dash", InputActionType.Value) { ExpectedValueType = typeof(Float2) };
        dash.AddBinding(new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.W), InputBinding.CreateKeyBinding(KeyCode.S),
            InputBinding.CreateKeyBinding(KeyCode.A), InputBinding.CreateKeyBinding(KeyCode.D))
        { Interaction = InputInteractionType.MultiTap });
        dash.Enable();
        Float2? performedWith = null;
        dash.Performed += ctx => performedWith = (Float2)ctx.ReadValueAsObject();

        float time = 0f;
        void Frame(bool held)
        {
            if (held) input.Held.Add(KeyCode.A);
            else input.Held.Remove(KeyCode.A);
            time += 0.05f;
            dash.UpdateState(input, time);
        }

        Frame(true);
        Frame(false);
        Assert.Null(performedWith);

        Frame(true);
        Assert.Equal(new Float2(-1f, 0f), performedWith);
    }

    private sealed class Sticks : NullInputHandler, IInputHandler
    {
        public Float2 Left, Right;
        int IInputHandler.GetGamepadSlotCount() => 1;
        bool IInputHandler.IsGamepadConnected(int gamepadIndex) => gamepadIndex == 0;
        Float2 IInputHandler.GetGamepadAxis(int gamepadIndex, int axisIndex) => axisIndex == 0 ? Left : Right;
    }

    private static InputAction TwoSticks(InputActionType type)
    {
        var action = new InputAction("Aim", type) { ExpectedValueType = typeof(Float2) };
        action.AddBinding(InputBinding.CreateGamepadAxisBinding(0));
        action.AddBinding(InputBinding.CreateGamepadAxisBinding(1));
        action.Enable();
        return action;
    }

    [Fact]
    public void Value_TakesTheControlPushedFurthest()
    {
        var input = new Sticks { Left = new Float2(0.2f, 0f), Right = new Float2(0f, 0.9f) };
        InputAction aim = TwoSticks(InputActionType.Value);

        aim.UpdateState(input, 0f);

        Assert.Equal(new Float2(0f, 0.9f), aim.ReadValue<Float2>());
    }

    [Fact]
    public void PassThrough_TakesTheControlThatMovedLast()
    {
        var input = new Sticks { Right = new Float2(0f, 0.9f) };
        InputAction aim = TwoSticks(InputActionType.PassThrough);
        aim.UpdateState(input, 0f);

        input.Left = new Float2(0.2f, 0f);
        aim.UpdateState(input, 0.1f);

        Assert.Equal(new Float2(0.2f, 0f), aim.ReadValue<Float2>());
    }

    [Fact]
    public void Value_HeldStill_StaysPerformed()
    {
        var input = new Sticks { Left = new Float2(0.5f, 0f) };
        var move = new InputAction("Move", InputActionType.Value) { ExpectedValueType = typeof(Float2) };
        move.AddBinding(InputBinding.CreateGamepadAxisBinding(0));
        move.Enable();
        int canceled = 0;
        move.Canceled += _ => canceled++;

        move.UpdateState(input, 0f);
        move.UpdateState(input, 0.1f);
        Assert.Equal(0, canceled);
        Assert.Equal(InputActionPhase.Performed, move.Phase);

        input.Left = Float2.Zero;
        move.UpdateState(input, 0.2f);
        Assert.Equal(1, canceled);
    }

    [Fact]
    public void DoubleTapDash_PerformsOnceWithTheDirection()
    {
        var input = new HeldKeys();
        var dash = new InputAction("Dash", InputActionType.Value) { ExpectedValueType = typeof(Float2) };
        dash.AddBinding(new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.W), InputBinding.CreateKeyBinding(KeyCode.S),
            InputBinding.CreateKeyBinding(KeyCode.A), InputBinding.CreateKeyBinding(KeyCode.D))
        { Interaction = InputInteractionType.MultiTap });
        dash.Enable();
        int performed = 0;
        dash.Performed += _ => performed++;

        float time = 0f;
        bool Frame(bool held)
        {
            if (held) input.Held.Add(KeyCode.D);
            else input.Held.Remove(KeyCode.D);
            time += 0.05f;
            dash.UpdateState(input, time);
            return dash.WasPerformedThisFrame();
        }

        Assert.False(Frame(true));
        Assert.False(Frame(false));
        Assert.True(Frame(true));
        Assert.Equal(new Float2(1f, 0f), dash.ReadValue<Float2>());
        Assert.False(Frame(true));
        Assert.False(Frame(false));
        Assert.Equal(1, performed);
    }

    [Fact]
    public void ButtonOnATrigger_WaitsForThePressPoint()
    {
        var input = new Gamepads();
        var fire = new InputAction("Fire");
        fire.AddBinding(new InputBinding { BindingType = InputBindingType.GamepadTrigger, AxisIndex = 1, PressPoint = 0.5f });
        fire.Enable();

        input.RightTrigger = 0.3f;
        fire.UpdateState(input, 0f);
        Assert.False(fire.IsPressed());

        input.RightTrigger = 0.6f;
        fire.UpdateState(input, 0.1f);
        Assert.True(fire.IsPressed());
    }

    [Fact]
    public void GamepadButton_WithNoDevice_ReadsEveryPad()
    {
        var input = new Gamepads();
        input.Held.Add((1, GamepadButton.A));

        var anyPad = new InputAction("Jump");
        anyPad.AddBinding(new InputBinding { BindingType = InputBindingType.GamepadButton, GamepadButton = GamepadButton.A });
        var firstPad = new InputAction("JumpP1");
        firstPad.AddBinding(GamepadButton.A, deviceIndex: 0);
        anyPad.Enable();
        firstPad.Enable();

        anyPad.UpdateState(input, 0f);
        firstPad.UpdateState(input, 0f);

        Assert.True(anyPad.IsPressed());
        Assert.False(firstPad.IsPressed());
    }

    [Fact]
    public void AnyKeyAndAnyButton_PressOnAnything()
    {
        var keys = new PressedThisFrame { HeldKey = true };
        var anyKey = new InputAction("Skip");
        anyKey.AddBinding(new InputBinding { BindingType = InputBindingType.AnyKey });
        anyKey.Enable();
        anyKey.UpdateState(keys, 0f);
        Assert.True(anyKey.IsPressed());

        var pads = new Gamepads();
        pads.Held.Add((0, GamepadButton.DPadLeft));
        var anyButton = new InputAction("Skip");
        anyButton.AddBinding(new InputBinding { BindingType = InputBindingType.AnyGamepadButton });
        anyButton.Enable();
        anyButton.UpdateState(pads, 0f);
        Assert.True(anyButton.IsPressed());
    }

    [Fact]
    public void PressPointAndCompositeInteraction_SurviveSaveAndLoad()
    {
        var map = new InputActionMap("Player");
        InputAction fire = map.AddAction("Fire");
        fire.AddBinding(new InputBinding { BindingType = InputBindingType.GamepadTrigger, AxisIndex = 1, PressPoint = 0.8f });
        InputAction lean = map.AddAction("Lean", InputActionType.Value);
        lean.AddBinding(new AxisCompositeBinding(InputBinding.CreateKeyBinding(KeyCode.E), InputBinding.CreateKeyBinding(KeyCode.Q))
        {
            Interaction = InputInteractionType.Hold, HoldDuration = 0.25f, PressPoint = 0.7f
        });

        EchoObject saved = EchoObject.NewCompound();
        map.Serialize(ref saved, new SerializationContext());
        var loaded = new InputActionMap("Loaded");
        loaded.Deserialize(saved, new SerializationContext());

        Assert.Equal(0.8f, loaded.GetAction("Fire").Bindings[0].PressPoint);
        InputCompositeBinding composite = loaded.GetAction("Lean").CompositeBindings[0];
        Assert.Equal(InputInteractionType.Hold, composite.Interaction);
        Assert.Equal(0.25f, composite.HoldDuration);
        Assert.Equal(0.7f, composite.PressPoint);
    }

    private sealed class VibrationRecorder : NullInputHandler, IInputHandler
    {
        public readonly List<(int Pad, float Left, float Right)> Calls = [];
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

        [Fact]
    public void PhysicalKey_BindsAndActuatesAction()
    {
        var input = new HeldKeys();
        var action = new InputAction("MoveForward");
        action.AddBinding(KeyCode.W, physical: true);
        action.Enable();

        action.UpdateState(input, 0f);
        Assert.False(action.IsPressed());

        input.PhysicalHeld.Add(KeyCode.W);
        action.UpdateState(input, 0.016f);
        Assert.True(action.IsPressed());
        Assert.Equal(1.0f, action.ReadValue<float>());

        input.PhysicalHeld.Remove(KeyCode.W);
        action.UpdateState(input, 0.032f);
        Assert.False(action.IsPressed());
    }

    [Fact]
    public void PhysicalKey_SurvivesSaveAndLoad()
    {
        var map = new InputActionMap("Player");
        InputAction jump = map.AddAction("Jump");
        jump.AddBinding(KeyCode.Space, physical: true);

        EchoObject saved = EchoObject.NewCompound();
        map.Serialize(ref saved, new SerializationContext());
        var loaded = new InputActionMap("Loaded");
        loaded.Deserialize(saved, new SerializationContext());

        var loadedAction = loaded.GetAction("Jump");
        Assert.NotNull(loadedAction);
        Assert.Single(loadedAction.Bindings);
        Assert.Equal(InputBindingType.Key, loadedAction.Bindings[0].BindingType);
        Assert.True(loadedAction.Bindings[0].UsePhysicalKey);
        Assert.Equal(KeyCode.Space, loadedAction.Bindings[0].Key);
    }

    [Fact]
    public void PhysicalKey_DirectQuery()
    {
        var input = new HeldKeys();
        WithInput(input, () =>
        {
            input.PhysicalHeld.Add(KeyCode.ControlLeft);
            input.PhysicalHeld.Add(KeyCode.S);

            Assert.True(Input.GetKey(KeyCode.ControlLeft, physical: true));
            Assert.True(Input.GetKey(KeyCode.S, physical: true));
            Assert.False(Input.GetKey(KeyCode.A, physical: true));

            // Default physical = false
            Assert.False(Input.GetKey(KeyCode.ControlLeft));
            Assert.False(Input.GetKey(KeyCode.S));
        });
    }

    [Fact]
    public void PhysicalKey_KeyCombo()
    {
        var input = new PressedThisFrame { Key = KeyCode.S, PhysicalKey = KeyCode.S };
        input.PhysicalHeld.Add(KeyCode.ControlLeft);
        WithInput(input, () =>
        {
            Assert.True(Input.GetKeyCombo(KeyCode.ControlLeft, KeyCode.S, physical: true));
            Assert.False(Input.GetKeyCombo(KeyCode.ControlLeft, KeyCode.S, physical: false));
        });
    }

    [Fact]
    public void PhysicalKeyEvent_FiredByHandler()
    {
        var dispatcher = new PhysicalEventDispatcher();
        WithInput(dispatcher, () =>
        {
            KeyCode receivedKey = KeyCode.Unknown;
            bool receivedDown = false;
            int count = 0;

            Action<KeyCode, bool> handler = (k, down) =>
            {
                receivedKey = k;
                receivedDown = down;
                count++;
            };

            Input.OnPhysicalKeyEvent += handler;
            try
            {
                dispatcher.Fire(KeyCode.Space, true);
                Assert.Equal(1, count);
                Assert.Equal(KeyCode.Space, receivedKey);
                Assert.True(receivedDown);

                dispatcher.Fire(KeyCode.Space, false);
                Assert.Equal(2, count);
                Assert.Equal(KeyCode.Space, receivedKey);
                Assert.False(receivedDown);
            }
            finally
            {
                Input.OnPhysicalKeyEvent -= handler;
            }
        });
    }

    [Fact]
    public void PhysicalKey_PressedThisFrame()
    {
        var input = new PressedThisFrame { PhysicalKey = KeyCode.Space };
        WithInput(input, () =>
        {
            Assert.True(Input.GetKeyDown(KeyCode.Space, physical: true));
            Assert.False(Input.GetKeyDown(KeyCode.Space, physical: false));
            Assert.False(Input.GetKeyDown(KeyCode.Enter, physical: true));
        });
    }
}