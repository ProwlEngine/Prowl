// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Represents a logical input action (e.g., "Jump", "Move", "Fire").
/// Actions are independent of specific devices and can have multiple bindings.
/// </summary>
public class InputAction
{
    private object _currentValue = 0f;
    private object _previousValue = 0f;
    private InputActionPhase _phase = InputActionPhase.Disabled;
    private double _startTime;
    private List<InputCompositeBinding> _composites = new();

    /// <summary>
    /// The name of this action.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The type of action (Value, Button, or PassThrough).
    /// </summary>
    public InputActionType ActionType { get; set; } = InputActionType.Button;

    private Type _expectedValueType = typeof(float);

    /// <summary>
    /// The expected value type for this action (typeof(float), typeof(Float2), etc.).
    /// </summary>
    public Type ExpectedValueType
    {
        get => _expectedValueType;
        set
        {
            _expectedValueType = value;
            // Re-initialize values with correct type
            _currentValue = GetDefaultValue();
            _previousValue = GetDefaultValue();
        }
    }

    /// <summary>
    /// The list of bindings for this action.
    /// </summary>
    public List<InputBinding> Bindings { get; set; } = new();

    /// <summary>
    /// The current phase of this action.
    /// </summary>
    public InputActionPhase Phase => _phase;

    /// <summary>
    /// Whether this action is currently enabled.
    /// </summary>
    public bool Enabled => _phase != InputActionPhase.Disabled;

    /// <summary>
    /// The action map this action belongs to (if any).
    /// </summary>
    public InputActionMap? ActionMap { get; internal set; }

    /// <summary>
    /// Called when the action is started.
    /// </summary>
    public event Action<InputActionContext>? Started;

    /// <summary>
    /// Called when the action is performed.
    /// </summary>
    public event Action<InputActionContext>? Performed;

    /// <summary>
    /// Called when the action is canceled.
    /// </summary>
    public event Action<InputActionContext>? Canceled;

    /// <summary>
    /// Creates a new input action with the specified name and type.
    /// </summary>
    public InputAction(string name, InputActionType type = InputActionType.Button)
    {
        Name = name;
        ActionType = type;
        ExpectedValueType = type == InputActionType.Button ? typeof(float) : typeof(float);

        // Initialize with proper default value based on type
        _currentValue = GetDefaultValue();
        _previousValue = GetDefaultValue();
    }

    /// <summary>
    /// Enables this action to start listening for input.
    /// </summary>
    public void Enable()
    {
        if (_phase == InputActionPhase.Disabled)
        {
            _phase = InputActionPhase.Waiting;
        }
    }

    /// <summary>
    /// Disables this action to stop listening for input.
    /// </summary>
    public void Disable()
    {
        if (_phase != InputActionPhase.Disabled)
        {
            _phase = InputActionPhase.Disabled;
            _currentValue = GetDefaultValue();
            _previousValue = GetDefaultValue();
        }
    }

    /// <summary>
    /// Adds a binding to this action.
    /// </summary>
    public InputAction AddBinding(InputBinding binding)
    {
        Bindings.Add(binding);
        return this;
    }

    /// <summary>
    /// Adds a keyboard key binding.
    /// </summary>
    public InputAction AddBinding(KeyCode key, InputInteractionType interaction = InputInteractionType.Default)
    {
        return AddBinding(InputBinding.CreateKeyBinding(key, interaction));
    }

    /// <summary>
    /// Adds a mouse button binding.
    /// </summary>
    public InputAction AddBinding(MouseButton button, InputInteractionType interaction = InputInteractionType.Default)
    {
        return AddBinding(InputBinding.CreateMouseButtonBinding(button, interaction));
    }

    /// <summary>
    /// Adds a gamepad button binding.
    /// </summary>
    public InputAction AddBinding(GamepadButton button, int deviceIndex = 0, InputInteractionType interaction = InputInteractionType.Default)
    {
        return AddBinding(InputBinding.CreateGamepadButtonBinding(button, deviceIndex, interaction));
    }

    /// <summary>
    /// Adds a composite binding that combines multiple inputs into one value.
    /// Example: WASD keys → Vector2, or two triggers → float axis
    /// </summary>
    public InputAction AddBinding(InputCompositeBinding composite)
    {
        _composites.Add(composite);
        return this;
    }

    /// <summary>
    /// Reads the current value of this action.
    /// </summary>
    public T ReadValue<T>() where T : struct
    {
        if (!Enabled)
            return default;

        if (_currentValue is T typedValue)
            return typedValue;

        return (T)Convert.ChangeType(_currentValue, typeof(T));
    }

    /// <summary>
    /// Reads the current value as an object.
    /// </summary>
    public object ReadValueAsObject() => _currentValue;

    /// <summary>
    /// Checks if the action was pressed this frame (for button actions).
    /// </summary>
    public bool WasPressedThisFrame()
    {
        if (!Enabled || ActionType != InputActionType.Button)
            return false;

        return ReadValue<float>() > 0f && Convert.ToSingle(_previousValue) <= 0f;
    }

    /// <summary>
    /// Checks if the action was released this frame (for button actions).
    /// </summary>
    public bool WasReleasedThisFrame()
    {
        if (!Enabled || ActionType != InputActionType.Button)
            return false;

        return ReadValue<float>() <= 0f && Convert.ToSingle(_previousValue) > 0f;
    }

    /// <summary>
    /// Checks if the action is currently pressed (for button actions).
    /// </summary>
    public bool IsPressed()
    {
        if (!Enabled || ActionType != InputActionType.Button)
            return false;

        return ReadValue<float>() > 0f;
    }

    /// <summary>
    /// Internal method to update the action state based on input.
    /// Called by the input system each frame.
    /// </summary>
    internal void UpdateState(IInputHandler inputHandler, double currentTime)
    {
        if (!Enabled)
            return;

        _previousValue = _currentValue;

        // Read the current value from bindings
        object newValue = ReadValueFromBindings(inputHandler);

        // Apply processors
        newValue = ApplyProcessors(newValue);

        bool valueChanged = !newValue.Equals(_currentValue);
        _currentValue = newValue;

        // Determine if the action should trigger based on its type
        bool shouldTrigger = ActionType switch
        {
            InputActionType.Value => valueChanged,
            InputActionType.Button => IsValueActuated(newValue),
            InputActionType.PassThrough => valueChanged,
            _ => false
        };

        // Update phase and invoke callbacks
        if (shouldTrigger)
        {
            if (_phase == InputActionPhase.Waiting || _phase == InputActionPhase.Canceled)
            {
                // Action is starting
                _phase = InputActionPhase.Started;
                _startTime = currentTime;
                InvokeCallback(Started, InputActionPhase.Started, currentTime, 0);
            }

            // Action is performing
            _phase = InputActionPhase.Performed;
            double duration = currentTime - _startTime;
            InvokeCallback(Performed, InputActionPhase.Performed, currentTime, duration);
        }
        else if (_phase == InputActionPhase.Performed || _phase == InputActionPhase.Started)
        {
            // Action was canceled
            _phase = InputActionPhase.Canceled;
            double duration = currentTime - _startTime;
            InvokeCallback(Canceled, InputActionPhase.Canceled, currentTime, duration);
            _phase = InputActionPhase.Waiting;
        }
    }

    private object ReadValueFromBindings(IInputHandler inputHandler)
    {
        // Check composites first - return the first actuated composite
        foreach (var composite in _composites)
        {
            object compositeValue = composite.ReadValue(inputHandler);
            if (IsValueActuated(compositeValue))
                return compositeValue;
        }

        // Then check regular bindings and return the first actuated value
        foreach (var binding in Bindings)
        {
            object value = ReadBinding(binding, inputHandler);
            if (IsValueActuated(value))
                return value;
        }

        return GetDefaultValue();
    }

    private object ReadBinding(InputBinding binding, IInputHandler inputHandler)
    {
        return binding.BindingType switch
        {
            InputBindingType.Key => binding.Key.HasValue && inputHandler.GetKey(binding.Key.Value) ? 1f : 0f,
            InputBindingType.MouseButton => binding.MouseButton.HasValue && inputHandler.GetMouseButton((int)binding.MouseButton.Value) ? 1f : 0f,
            InputBindingType.GamepadButton => binding.GamepadButton.HasValue && inputHandler.GetGamepadButton(binding.RequiredDeviceIndex ?? 0, binding.GamepadButton.Value) ? 1f : 0f,
            InputBindingType.GamepadAxis => inputHandler.GetGamepadAxis(binding.RequiredDeviceIndex ?? 0, binding.AxisIndex ?? 0),
            InputBindingType.GamepadTrigger => inputHandler.GetGamepadTrigger(binding.RequiredDeviceIndex ?? 0, binding.AxisIndex ?? 0),
            InputBindingType.MouseAxis => binding.AxisIndex switch
            {
                0 => (float)inputHandler.MouseDelta.X,
                1 => (float)inputHandler.MouseDelta.Y,
                2 => inputHandler.MouseWheelDelta,
                _ => 0f
            },
            _ => GetDefaultValue()
        };
    }

    private object ApplyProcessors(object value)
    {
        foreach (var binding in Bindings)
        {
            foreach (var processor in binding.Processors)
            {
                if (value is float floatValue)
                    value = processor.Process(floatValue);
                else if (value is Float2 vectorValue)
                    value = processor.Process(vectorValue);
            }
        }

        return value;
    }

    private bool IsValueActuated(object value)
    {
        if (value is float floatValue)
            return Math.Abs(floatValue) > 0.0001f;
        if (value is Float2 vectorValue)
            return Math.Abs(vectorValue.X) > 0.0001f || Math.Abs(vectorValue.Y) > 0.0001f;
        return false;
    }

    private object GetDefaultValue()
    {
        if (ExpectedValueType == typeof(Float2))
            return Float2.Zero;
        return 0f;
    }

    private void InvokeCallback(Action<InputActionContext>? callback, InputActionPhase phase, double time, double duration)
    {
        if (callback == null)
            return;

        var context = new InputActionContext(
            this,
            phase,
            null, // Could track which binding triggered this
            _currentValue,
            _currentValue,
            time,
            duration
        );

        callback.Invoke(context);
    }

    public override string ToString() => $"{Name} ({ActionType})";
}
