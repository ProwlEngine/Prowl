// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Base class for composite bindings that combine multiple inputs into a single value.
/// </summary>
public abstract class InputCompositeBinding
{
    /// <summary>
    /// The component bindings that make up this composite.
    /// </summary>
    public Dictionary<string, InputBinding> Parts { get; set; } = new();

    /// <summary>
    /// Optional processors to apply to the final composite value.
    /// These are applied after combining the individual parts.
    /// </summary>
    public List<IInputProcessor> Processors { get; set; } = new();

    /// <summary>
    /// Reads the composite value based on the current state of its component parts.
    /// </summary>
    public abstract object ReadValue(IInputHandler inputHandler);
}

/// <summary>
/// Combines four buttons (up, down, left, right) into a Vector2.
/// Commonly used for WASD or arrow key movement.
/// </summary>
public class Vector2CompositeBinding : InputCompositeBinding
{
    public const string UP = "up";
    public const string DOWN = "down";
    public const string LEFT = "left";
    public const string RIGHT = "right";

    /// <summary>
    /// Whether to normalize the resulting vector to have a maximum magnitude of 1.
    /// Useful to prevent diagonal movement from being faster.
    /// </summary>
    public bool Normalize { get; set; } = true;

    public Vector2CompositeBinding(InputBinding up, InputBinding down, InputBinding left, InputBinding right, bool normalize = true)
    {
        Parts[UP] = up;
        Parts[DOWN] = down;
        Parts[LEFT] = left;
        Parts[RIGHT] = right;
        Normalize = normalize;

        // Mark these as composite parts
        up.CompositePartName = UP;
        down.CompositePartName = DOWN;
        left.CompositePartName = LEFT;
        right.CompositePartName = RIGHT;
    }

    public override object ReadValue(IInputHandler inputHandler)
    {
        Float2 value = Float2.Zero;

        // Check each direction
        if (IsPressed(inputHandler, Parts[UP]))
            value.Y += 1f;
        if (IsPressed(inputHandler, Parts[DOWN]))
            value.Y -= 1f;
        if (IsPressed(inputHandler, Parts[LEFT]))
            value.X -= 1f;
        if (IsPressed(inputHandler, Parts[RIGHT]))
            value.X += 1f;

        // Normalize if requested
        if (Normalize && !value.Equals(Float2.Zero))
        {
            float magnitude = (float)Math.Sqrt(value.X * value.X + value.Y * value.Y);
            if (magnitude > 1f)
                value /= magnitude;
        }

        // Apply processors to the final composite value
        foreach (var processor in Processors)
        {
            value = processor.Process(value);
        }

        return value;
    }

    private bool IsPressed(IInputHandler inputHandler, InputBinding binding)
    {
        return binding.BindingType switch
        {
            InputBindingType.Key => inputHandler.GetKey(binding.Key!.Value),
            InputBindingType.MouseButton => inputHandler.GetMouseButton((int)binding.MouseButton!.Value),
            InputBindingType.GamepadButton => inputHandler.GetGamepadButton(
                binding.RequiredDeviceIndex ?? 0,
                binding.GamepadButton!.Value),
            _ => false
        };
    }
}

/// <summary>
/// Combines two axes into a Vector2.
/// Commonly used for analog sticks or mouse delta.
/// </summary>
public class DualAxisCompositeBinding : InputCompositeBinding
{
    public const string X_AXIS = "x";
    public const string Y_AXIS = "y";

    public DualAxisCompositeBinding(InputBinding xAxis, InputBinding yAxis)
    {
        Parts[X_AXIS] = xAxis;
        Parts[Y_AXIS] = yAxis;

        xAxis.CompositePartName = X_AXIS;
        yAxis.CompositePartName = Y_AXIS;
    }

    public override object ReadValue(IInputHandler inputHandler)
    {
        float x = ReadSingleAxisValue(inputHandler, Parts[X_AXIS]);
        float y = ReadSingleAxisValue(inputHandler, Parts[Y_AXIS]);
        Float2 value = new Float2(x, y);

        // Apply processors to the final composite value
        foreach (var processor in Processors)
        {
            value = processor.Process(value);
        }

        return value;
    }

    private float ReadSingleAxisValue(IInputHandler inputHandler, InputBinding binding)
    {
        return binding.BindingType switch
        {
            // For triggers, read the trigger value directly
            InputBindingType.GamepadTrigger => inputHandler.GetGamepadTrigger(binding.RequiredDeviceIndex ?? 0, binding.AxisIndex ?? 0),

            // For mouse axis, read the specified axis
            InputBindingType.MouseAxis => binding.AxisIndex switch
            {
                0 => (float)inputHandler.MouseDelta.X,
                1 => (float)inputHandler.MouseDelta.Y,
                2 => inputHandler.MouseWheelDelta,
                _ => 0f
            },

            _ => 0f
        };
    }
}

/// <summary>
/// Combines positive and negative buttons into a single axis value.
/// Example: A/D keys for horizontal movement (-1 to 1).
/// </summary>
public class AxisCompositeBinding : InputCompositeBinding
{
    public const string POSITIVE = "positive";
    public const string NEGATIVE = "negative";

    public AxisCompositeBinding(InputBinding positive, InputBinding negative)
    {
        Parts[POSITIVE] = positive;
        Parts[NEGATIVE] = negative;

        positive.CompositePartName = POSITIVE;
        negative.CompositePartName = NEGATIVE;
    }

    public override object ReadValue(IInputHandler inputHandler)
    {
        float value = 0f;

        if (IsPressed(inputHandler, Parts[POSITIVE]))
            value += 1f;
        if (IsPressed(inputHandler, Parts[NEGATIVE]))
            value -= 1f;

        // Apply processors to the final composite value
        foreach (var processor in Processors)
        {
            value = processor.Process(value);
        }

        return value;
    }

    private bool IsPressed(IInputHandler inputHandler, InputBinding binding)
    {
        return binding.BindingType switch
        {
            InputBindingType.Key => inputHandler.GetKey(binding.Key!.Value),
            InputBindingType.MouseButton => inputHandler.GetMouseButton((int)binding.MouseButton!.Value),
            InputBindingType.GamepadButton => inputHandler.GetGamepadButton(
                binding.RequiredDeviceIndex ?? 0,
                binding.GamepadButton!.Value),
            _ => false
        };
    }
}
