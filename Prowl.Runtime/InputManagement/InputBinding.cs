// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>
/// Internal state tracker for interaction logic.
/// </summary>
internal class InteractionState
{
    public bool WasActuated { get; set; }
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
    Composite
}

/// <summary>
/// Represents a binding from an input control to an action.
/// A binding connects a physical input (like a key or button) to a logical action.
/// </summary>
public class InputBinding
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
    /// Optional device requirements (e.g., only gamepad index 0).
    /// </summary>
    public int? RequiredDeviceIndex { get; set; }

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

    public override string ToString() => BindingType.ToString();
}
