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
    private object _currentValue = 0.0f;
    private object _previousValue = 0.0f;
    private InputActionPhase _phase = InputActionPhase.Disabled;
    private float _startTime;
    private List<InputCompositeBinding> _composites = [];

    /// <summary>The composite bindings for this action (read-only).</summary>
    public IReadOnlyList<InputCompositeBinding> CompositeBindings => _composites;
    private Dictionary<InputBinding, InteractionState> _interactionStates = [];

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
    /// The expected value type for this action (typeof(float), typeof(Float2)).
    /// </summary>
    public Type ExpectedValueType
    {
        get => _expectedValueType;
        set
        {
            // Type must be either Double or Float2
            if (value != typeof(float) && value != typeof(Float2))
                throw new ArgumentException("ExpectedValueType must be either typeof(float) or typeof(Float2).");

            _expectedValueType = value;
            // Re-initialize values with correct type
            _currentValue = GetDefaultValue();
            _previousValue = GetDefaultValue();
        }
    }

    /// <summary>
    /// The list of bindings for this action.
    /// </summary>
    public List<InputBinding> Bindings { get; set; } = [];

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

    /// <summary>Removes a composite binding by index.</summary>
    public void RemoveCompositeAt(int index) => _composites.RemoveAt(index);

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

        bool currentActuated = IsValueActuated(_currentValue);
        bool previousActuated = IsValueActuated(_previousValue);
        return currentActuated && !previousActuated;
    }

    /// <summary>
    /// Checks if the action was released this frame (for button actions).
    /// </summary>
    public bool WasReleasedThisFrame()
    {
        if (!Enabled || ActionType != InputActionType.Button)
            return false;

        bool currentActuated = IsValueActuated(_currentValue);
        bool previousActuated = IsValueActuated(_previousValue);
        return !currentActuated && previousActuated;
    }

    /// <summary>
    /// Checks if the action is currently pressed (for button actions).
    /// </summary>
    public bool IsPressed()
    {
        if (!Enabled || ActionType != InputActionType.Button)
            return false;

        return IsValueActuated(_currentValue);
    }

    /// <summary>
    /// Internal method to update the action state based on input.
    /// Called by the input system each frame.
    /// </summary>
    internal void UpdateState(IInputHandler inputHandler, float currentTime)
    {
        if (!Enabled)
            return;

        _previousValue = _currentValue;

        // Read the current value from bindings with interaction logic
        object newValue = ReadValueFromBindings(inputHandler, currentTime);

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
            float duration = currentTime - _startTime;
            InvokeCallback(Performed, InputActionPhase.Performed, currentTime, duration);
        }
        else if (_phase == InputActionPhase.Performed || _phase == InputActionPhase.Started)
        {
            // Action was canceled
            _phase = InputActionPhase.Canceled;
            float duration = currentTime - _startTime;
            InvokeCallback(Canceled, InputActionPhase.Canceled, currentTime, duration);
            _phase = InputActionPhase.Waiting;
        }
    }

    private object ReadValueFromBindings(IInputHandler inputHandler, float currentTime)
    {
        // Check composites first - return the first actuated composite
        foreach (InputCompositeBinding composite in _composites)
        {
            object compositeValue = composite.ReadValue(inputHandler);
            if (IsValueActuated(compositeValue))
            {
                // Apply processors from the composite
                foreach (IInputProcessor processor in composite.Processors)
                {
                    if (compositeValue is float floatValue)
                        compositeValue = processor.Process(floatValue);
                    else if (compositeValue is Float2 vectorValue)
                        compositeValue = processor.Process(vectorValue);
                }
                return compositeValue;
            }
        }

        // Then check regular bindings with interaction logic
        foreach (InputBinding binding in Bindings)
        {
            // Ensure interaction state exists for this binding
            if (!_interactionStates.ContainsKey(binding))
                _interactionStates[binding] = new InteractionState();

            object rawValue = ReadBinding(binding, inputHandler);

            // Apply processors from THIS binding only
            foreach (IInputProcessor processor in binding.Processors)
            {
                if (rawValue is float floatValue)
                    rawValue = processor.Process(floatValue);
                else if (rawValue is Float2 vectorValue)
                    rawValue = processor.Process(vectorValue);
            }

            bool isActuated = IsValueActuated(rawValue);

            // Evaluate interaction and get the result
            if (EvaluateInteraction(binding, rawValue, isActuated, currentTime, out object interactionValue))
            {
                return interactionValue;
            }
        }

        return GetDefaultValue();
    }

    private bool EvaluateInteraction(InputBinding binding, object rawValue, bool isActuated, float currentTime, out object value)
    {
        InteractionState state = _interactionStates[binding];
        value = GetDefaultValue();

        switch (binding.Interaction)
        {
            case InputInteractionType.Default:
                // Standard behavior - actuated = trigger
                if (isActuated)
                {
                    value = rawValue;
                    return true;
                }
                break;

            case InputInteractionType.Press:
                // Only trigger on initial press (down)
                if (isActuated && !state.WasActuated)
                {
                    value = GetActuatedValue();
                    state.WasActuated = true;
                    return true;
                }
                else if (!isActuated)
                {
                    state.WasActuated = false;
                }
                break;

            case InputInteractionType.Release:
                // Only trigger on release (up)
                if (!isActuated && state.WasActuated)
                {
                    value = GetActuatedValue();
                    state.WasActuated = false;
                    return true;
                }
                else if (isActuated)
                {
                    state.WasActuated = true;
                }
                break;

            case InputInteractionType.Hold:
                // Trigger after being held for specified duration
                if (isActuated)
                {
                    if (!state.WasActuated)
                    {
                        // Just pressed
                        state.PressStartTime = currentTime;
                        state.HoldTriggered = false;
                        state.WasActuated = true;
                    }
                    else if (!state.HoldTriggered)
                    {
                        // Check if hold duration met
                        float heldDuration = currentTime - state.PressStartTime;
                        if (heldDuration >= binding.HoldDuration)
                        {
                            value = GetActuatedValue();
                            state.HoldTriggered = true;
                            return true;
                        }
                    }
                }
                else
                {
                    state.WasActuated = false;
                    state.HoldTriggered = false;
                }
                break;

            case InputInteractionType.Tap:
                // Quick press and release
                if (isActuated)
                {
                    if (!state.WasActuated)
                    {
                        state.PressStartTime = currentTime;
                        state.WasActuated = true;
                        state.TapCompleted = false;
                    }
                    else
                    {
                        // Check if held too long
                        float heldDuration = currentTime - state.PressStartTime;
                        if (heldDuration > binding.MaxTapDuration)
                        {
                            state.TapCompleted = true; // Cancel tap
                        }
                    }
                }
                else if (state.WasActuated && !state.TapCompleted)
                {
                    // Released quickly enough
                    float heldDuration = currentTime - state.PressStartTime;
                    if (heldDuration <= binding.MaxTapDuration)
                    {
                        value = GetActuatedValue();
                        state.WasActuated = false;
                        return true;
                    }
                    state.WasActuated = false;
                }
                break;

            case InputInteractionType.MultiTap:
                // Multiple rapid taps
                if (isActuated && !state.WasActuated)
                {
                    // New tap
                    float timeSinceLastTap = currentTime - state.LastTapTime;

                    if (timeSinceLastTap <= binding.TapWindow)
                    {
                        state.CurrentTapCount++;
                    }
                    else
                    {
                        // Reset if too much time passed
                        state.CurrentTapCount = 1;
                    }

                    state.LastTapTime = currentTime;
                    state.WasActuated = true;

                    // Check if we reached the required tap count
                    if (state.CurrentTapCount >= binding.TapCount)
                    {
                        value = GetActuatedValue();
                        state.CurrentTapCount = 0; // Reset
                        return true;
                    }
                }
                else if (!isActuated)
                {
                    state.WasActuated = false;
                }
                break;

            case InputInteractionType.PressAndRelease:
                // Trigger on both press and release
                if (isActuated && !state.WasActuated)
                {
                    value = GetActuatedValue();
                    state.WasActuated = true;
                    return true;
                }
                else if (!isActuated && state.WasActuated)
                {
                    value = GetActuatedValue();
                    state.WasActuated = false;
                    return true;
                }
                break;
        }

        return false;
    }

    private object ReadBinding(InputBinding binding, IInputHandler inputHandler)
    {
        return binding.BindingType switch
        {
            InputBindingType.Key => binding.Key.HasValue && inputHandler.GetKey(binding.Key.Value) ? 1.0f : 0.0f,
            InputBindingType.MouseButton => binding.MouseButton.HasValue && inputHandler.GetMouseButton((int)binding.MouseButton.Value) ? 1.0f : 0.0f,
            InputBindingType.GamepadButton => binding.GamepadButton.HasValue && inputHandler.GetGamepadButton(binding.RequiredDeviceIndex ?? 0, binding.GamepadButton.Value) ? 1.0f : 0.0f,
            InputBindingType.GamepadAxis => inputHandler.GetGamepadAxis(binding.RequiredDeviceIndex ?? 0, binding.AxisIndex ?? 0),
            InputBindingType.GamepadTrigger => inputHandler.GetGamepadTrigger(binding.RequiredDeviceIndex ?? 0, binding.AxisIndex ?? 0),
            InputBindingType.MouseAxis => binding.AxisIndex switch
            {
                0 => inputHandler.MouseDelta.X,
                1 => inputHandler.MouseDelta.Y,
                2 => inputHandler.MouseWheelDelta,
                _ => 0.0f
            },
            _ => GetDefaultValue()
        };
    }

    private bool IsValueActuated(object value)
    {
        if (value is float floatValue)
            return Maths.Abs(floatValue) > 0.0001;
        if (value is Float2 vectorValue)
            return Maths.Abs(vectorValue.X) > 0.0001f || Maths.Abs(vectorValue.Y) > 0.0001;
        return false;
    }

    private object GetDefaultValue()
    {
        if (ExpectedValueType == typeof(Float2))
            return Float2.Zero;
        return 0.0f;
    }

    private object GetActuatedValue()
    {
        if (ExpectedValueType == typeof(Float2))
            return new Float2(1.0f, 1.0f);
        return 1.0f;
    }

    private void InvokeCallback(Action<InputActionContext>? callback, InputActionPhase phase, float time, float duration)
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
