// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Internal state tracker for interaction logic.
/// </summary>
internal class InteractionState
{
    public bool WasActuated { get; set; }
    public object? PressedValue { get; set; }
    public object? LastRaw { get; set; }
    public long ChangeOrder { get; set; }
    public float PressStartTime { get; set; }
    public float LastTapTime { get; set; }
    public int CurrentTapCount { get; set; }
    public bool HoldTriggered { get; set; }
    public bool TapCompleted { get; set; }
}

/// <summary>
/// Defines what type of input control a binding refers to.
/// </summary>
public enum InputBindingType
{
    Key,
    MouseButton,
    MouseAxis,
    GamepadButton,
    GamepadAxis,
    GamepadTrigger,
    Composite,
    /// <summary>A button on a headset controller, read through <see cref="XRInput"/>.</summary>
    XRButton,
    /// <summary>A headset controller's trigger or grip, from 0 to 1.</summary>
    XRAxis,
    /// <summary>A headset controller's thumbstick, or touchpad on controllers without one.</summary>
    XRStick,
    /// <summary>A finger resting on a headset controller's control, read through <see cref="XRInput.GetTouch"/>.</summary>
    XRTouch,
    /// <summary>Any keyboard key held.</summary>
    AnyKey,
    /// <summary>Any button held on the gamepad picked by <see cref="InputBinding.RequiredDeviceIndex"/>.</summary>
    AnyGamepadButton,
}

/// <summary>How a binding or composite must be actuated before it drives its action.</summary>
public interface IInputInteractionSettings
{
    InputInteractionType Interaction { get; set; }
    float HoldDuration { get; set; }
    int TapCount { get; set; }
    float TapWindow { get; set; }
    float MaxTapDuration { get; set; }

    /// <summary>
    /// How far an analog value must go, from 0 to 1, to count as pressed for Button actions and interactions.
    /// </summary>
    float PressPoint { get; set; }
}

/// <summary>
/// Represents a binding from an input control to an action.
/// A binding connects a physical input (like a key or button) to a logical action.
/// </summary>
public class InputBinding : IInputInteractionSettings
{
    /// <summary>
    /// The type of input control this binding refers to.
    /// </summary>
    public InputBindingType BindingType { get; set; }

    /// <summary>
    /// The specific key if this is a keyboard binding.
    /// </summary>
    public KeyCode? Key { get; set; }

    /// <summary>
    /// The specific mouse button if this is a mouse button binding.
    /// </summary>
    public MouseButton? MouseButton { get; set; }

    /// <summary>
    /// The specific gamepad button if this is a gamepad binding.
    /// </summary>
    public GamepadButton? GamepadButton { get; set; }

    /// <summary>
    /// The axis index if this is an axis binding.
    /// </summary>
    public int? AxisIndex { get; set; }

    /// <summary>The controller hand for an XR binding.</summary>
    public XRHand? XRHand { get; set; }

    /// <summary>The controller button for an <see cref="InputBindingType.XRButton"/> binding.</summary>
    public XRButton? XRButton { get; set; }

    /// <summary>The controller axis for an <see cref="InputBindingType.XRAxis"/> binding.</summary>
    public XRAxis? XRAxis { get; set; }

    /// <summary>The control a finger rests on for an <see cref="InputBindingType.XRTouch"/> binding.</summary>
    public XRTouch? XRTouch { get; set; }

    /// <summary>
    /// Optional interaction that determines how this binding triggers the action.
    /// </summary>
    public InputInteractionType Interaction { get; set; } = InputInteractionType.Default;

    /// <summary>
    /// Duration (in seconds) required for Hold interaction. Default: 0.4s
    /// </summary>
    public float HoldDuration { get; set; } = 0.4f;

    /// <summary>
    /// Number of taps required for MultiTap interaction. Default: 2
    /// </summary>
    public int TapCount { get; set; } = 2;

    /// <summary>
    /// Maximum time window (in seconds) between taps for MultiTap. Default: 0.3s
    /// </summary>
    public float TapWindow { get; set; } = 0.3f;

    /// <summary>
    /// Maximum duration (in seconds) for a Tap to be valid. Default: 0.2s
    /// </summary>
    public float MaxTapDuration { get; set; } = 0.2f;

    public float PressPoint { get; set; } = 0.5f;

    /// <summary>
    /// Optional processors to apply to the input value (e.g., normalize, clamp, invert).
    /// </summary>
    public List<IInputProcessor> Processors { get; set; } = [];

    /// <summary>
    /// For composite bindings, references to the component bindings.
    /// </summary>
    public Dictionary<string, InputBinding> CompositeParts { get; set; } = [];

    /// <summary>
    /// Indicates if this binding is part of a composite.
    /// </summary>
    public bool IsComposite => BindingType == InputBindingType.Composite;

    /// <summary>
    /// Indicates if this binding is a part within a composite.
    /// </summary>
    public string? CompositePartName { get; set; }

    /// <summary>
    /// The gamepad this binding reads. Null reads every connected gamepad.
    /// </summary>
    public int? RequiredDeviceIndex { get; set; }

    /// <summary>Clears every control field, so switching <see cref="BindingType"/> leaves nothing stale behind.</summary>
    public void ClearControl()
    {
        Key = null;
        MouseButton = null;
        GamepadButton = null;
        AxisIndex = null;
        XRHand = null;
        XRButton = null;
        XRAxis = null;
        XRTouch = null;
    }

    /// <summary>
    /// Creates a keyboard key binding.
    /// </summary>
    public static InputBinding CreateKeyBinding(KeyCode key, InputInteractionType interaction = InputInteractionType.Default)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.Key,
            Key = key,
            Interaction = interaction
        };
    }

    /// <summary>
    /// Creates a mouse button binding.
    /// </summary>
    public static InputBinding CreateMouseButtonBinding(MouseButton button, InputInteractionType interaction = InputInteractionType.Default)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.MouseButton,
            MouseButton = button,
            Interaction = interaction
        };
    }

    /// <summary>
    /// Creates a gamepad button binding.
    /// </summary>
    public static InputBinding CreateGamepadButtonBinding(GamepadButton button, int deviceIndex = 0, InputInteractionType interaction = InputInteractionType.Default)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.GamepadButton,
            GamepadButton = button,
            RequiredDeviceIndex = deviceIndex,
            Interaction = interaction
        };
    }

    /// <summary>
    /// Creates a gamepad axis binding (for thumbsticks).
    /// </summary>
    public static InputBinding CreateGamepadAxisBinding(int axisIndex, int deviceIndex = 0)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.GamepadAxis,
            AxisIndex = axisIndex,
            RequiredDeviceIndex = deviceIndex
        };
    }

    /// <summary>
    /// Creates a gamepad trigger binding.
    /// </summary>
    public static InputBinding CreateGamepadTriggerBinding(int triggerIndex, int deviceIndex = 0)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.GamepadTrigger,
            AxisIndex = triggerIndex,
            RequiredDeviceIndex = deviceIndex
        };
    }

    /// <summary>
    /// Creates a mouse axis binding (0 = X delta, 1 = Y delta, 2 = wheel).
    /// </summary>
    public static InputBinding CreateMouseAxisBinding(int axisIndex)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.MouseAxis,
            AxisIndex = axisIndex
        };
    }

    public static InputBinding CreateXRButtonBinding(XRHand hand, XRButton button, InputInteractionType interaction = InputInteractionType.Default)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.XRButton,
            XRHand = hand,
            XRButton = button,
            Interaction = interaction
        };
    }

    public static InputBinding CreateXRAxisBinding(XRHand hand, XRAxis axis)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.XRAxis,
            XRHand = hand,
            XRAxis = axis
        };
    }

    public static InputBinding CreateXRTouchBinding(XRHand hand, XRTouch touch, InputInteractionType interaction = InputInteractionType.Default)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.XRTouch,
            XRHand = hand,
            XRTouch = touch,
            Interaction = interaction
        };
    }

    public static InputBinding CreateXRStickBinding(XRHand hand)
    {
        return new InputBinding
        {
            BindingType = InputBindingType.XRStick,
            XRHand = hand
        };
    }

    /// <summary>Whether this button style binding is held. Triggers and XR axes count once past <see cref="PressPoint"/>.</summary>
    internal bool IsPressed(IInputHandler inputHandler) => BindingType switch
    {
        InputBindingType.Key => Key.HasValue && inputHandler.GetKey(Key.Value),
        InputBindingType.AnyKey => inputHandler.IsAnyKeyDown,
        InputBindingType.MouseButton => MouseButton.HasValue && inputHandler.GetMouseButton((int)MouseButton.Value),
        InputBindingType.GamepadButton => GamepadButton.HasValue && AnyGamepad(inputHandler, pad => inputHandler.GetGamepadButton(pad, GamepadButton.Value)),
        InputBindingType.AnyGamepadButton => AnyGamepad(inputHandler, pad => AnyButtonHeld(inputHandler, pad)),
        InputBindingType.GamepadTrigger => ReadGamepadTrigger(inputHandler) >= PressPoint,
        InputBindingType.XRButton => XRButton.HasValue && XRInput.GetButton(XRHand ?? Runtime.XRHand.Right, XRButton.Value),
        InputBindingType.XRAxis => ReadXRAxis() >= PressPoint,
        InputBindingType.XRTouch => XRTouch.HasValue && XRInput.GetTouch(XRHand ?? Runtime.XRHand.Right, XRTouch.Value),
        _ => false
    };

    internal float ReadXRAxis() => XRInput.GetAxis(XRHand ?? Runtime.XRHand.Right, XRAxis ?? Runtime.XRAxis.Trigger);

    /// <summary>The trigger value, the highest across gamepads when no device is required.</summary>
    internal float ReadGamepadTrigger(IInputHandler inputHandler)
    {
        if (RequiredDeviceIndex.HasValue)
            return inputHandler.GetGamepadTrigger(RequiredDeviceIndex.Value, AxisIndex ?? 0);

        float best = 0f;
        for (int pad = 0; pad < inputHandler.GetGamepadSlotCount(); pad++)
            if (inputHandler.IsGamepadConnected(pad))
                best = Maths.Max(best, inputHandler.GetGamepadTrigger(pad, AxisIndex ?? 0));
        return best;
    }

    /// <summary>The stick value, the one pushed furthest across gamepads when no device is required.</summary>
    internal Float2 ReadGamepadAxis(IInputHandler inputHandler)
    {
        if (RequiredDeviceIndex.HasValue)
            return inputHandler.GetGamepadAxis(RequiredDeviceIndex.Value, AxisIndex ?? 0);

        Float2 best = Float2.Zero;
        float bestLength = 0f;
        for (int pad = 0; pad < inputHandler.GetGamepadSlotCount(); pad++)
        {
            if (!inputHandler.IsGamepadConnected(pad)) continue;
            Float2 value = inputHandler.GetGamepadAxis(pad, AxisIndex ?? 0);
            float length = value.X * value.X + value.Y * value.Y;
            if (length > bestLength)
            {
                best = value;
                bestLength = length;
            }
        }
        return best;
    }

    private bool AnyGamepad(IInputHandler inputHandler, Func<int, bool> test)
    {
        if (RequiredDeviceIndex.HasValue)
            return test(RequiredDeviceIndex.Value);

        for (int pad = 0; pad < inputHandler.GetGamepadSlotCount(); pad++)
            if (inputHandler.IsGamepadConnected(pad) && test(pad))
                return true;
        return false;
    }

    private static readonly GamepadButton[] s_gamepadButtons = [.. Enum.GetValues<GamepadButton>().Where(b => b != Runtime.GamepadButton.Unknown)];

    private static bool AnyButtonHeld(IInputHandler inputHandler, int pad)
    {
        foreach (GamepadButton button in s_gamepadButtons)
            if (inputHandler.GetGamepadButton(pad, button))
                return true;
        return false;
    }

    public override string ToString() => BindingType.ToString();
}
