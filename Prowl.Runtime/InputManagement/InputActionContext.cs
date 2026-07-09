// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// Contains contextual information about an input action callback.
/// Passed to action callbacks to provide information about what triggered the action.
/// </summary>
public readonly struct InputActionContext
{
    /// <summary>
    /// The action that was triggered.
    /// </summary>
    public InputAction Action { get; }

    /// <summary>
    /// The current phase of the action.
    /// </summary>
    public InputActionPhase Phase { get; }

    /// <summary>
    /// The binding that triggered this action.
    /// </summary>
    public InputBinding? Binding { get; }

    /// <summary>
    /// The time when the action was triggered.
    /// </summary>
    public float Time { get; }

    /// <summary>
    /// The time since the action was started.
    /// </summary>
    public float Duration { get; }

    /// <summary>
    /// The raw value before processing.
    /// </summary>
    private readonly object _rawValue;

    /// <summary>
    /// The processed value.
    /// </summary>
    private readonly object _value;

    public InputActionContext(
        InputAction action,
        InputActionPhase phase,
        InputBinding? binding,
        object rawValue,
        object value,
        float time,
        float duration)
    {
        Action = action;
        Phase = phase;
        Binding = binding;
        _rawValue = rawValue;
        _value = value;
        Time = time;
        Duration = duration;
    }

    /// <summary>
    /// Reads the value as the specified type.
    /// </summary>
    public T ReadValue<T>() where T : struct
    {
        if (_value is T typedValue)
            return typedValue;

        // Attempt conversion
        return (T)Convert.ChangeType(_value, typeof(T));
    }

    /// <summary>
    /// Reads the raw, unprocessed value as the specified type.
    /// </summary>
    public T ReadRawValue<T>() where T : struct
    {
        if (_rawValue is T typedValue)
            return typedValue;

        return (T)Convert.ChangeType(_rawValue, typeof(T));
    }

    /// <summary>
    /// Gets the value as an object.
    /// </summary>
    public object ReadValueAsObject() => _value;

    /// <summary>
    /// Checks if the action is in the performed phase.
    /// </summary>
    public bool IsPerformed => Phase == InputActionPhase.Performed;

    /// <summary>
    /// Checks if the action is in the started phase.
    /// </summary>
    public bool IsStarted => Phase == InputActionPhase.Started;

    /// <summary>
    /// Checks if the action is in the canceled phase.
    /// </summary>
    public bool IsCanceled => Phase == InputActionPhase.Canceled;
}
