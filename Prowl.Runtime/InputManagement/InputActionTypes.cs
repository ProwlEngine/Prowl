// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>
/// The type of input action determines how the action processes input.
/// </summary>
public enum InputActionType
{
    /// <summary>
    /// The action continuously provides a value based on the input.
    /// Examples: movement axes, mouse position, analog stick
    /// </summary>
    Value,

    /// <summary>
    /// The action acts like a button with discrete press and release events.
    /// Examples: jump, fire, interact buttons
    /// </summary>
    Button,

    /// <summary>
    /// The action passes through all input events without filtering.
    /// Useful for low-level input handling where you need every value change.
    /// </summary>
    PassThrough
}

/// <summary>
/// The phase of an input action indicates its current state.
/// </summary>
public enum InputActionPhase
{
    /// <summary>
    /// The action is disabled and not listening to input.
    /// </summary>
    Disabled,

    /// <summary>
    /// The action is enabled and waiting for input.
    /// </summary>
    Waiting,

    /// <summary>
    /// The action has been activated and is starting.
    /// </summary>
    Started,

    /// <summary>
    /// The action is performing (processing input).
    /// </summary>
    Performed,

    /// <summary>
    /// The action was canceled before completing.
    /// </summary>
    Canceled
}

/// <summary>
/// Defines how a control must be actuated to trigger an action.
/// Currently just a placeholder for future interaction features (hold, multi-tap, etc.)
/// </summary>
public enum InputInteractionType
{
    /// <summary>
    /// Default behavior - triggers when control is actuated.
    /// </summary>
    Default
}
