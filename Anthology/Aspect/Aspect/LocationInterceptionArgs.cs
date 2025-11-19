using System.Reflection;

namespace Aspect;

/// <summary>
/// Provides contextual information about property access to aspect methods.
/// </summary>
public class LocationInterceptionArgs
{
    private Action? _getValueAction;
    private Action? _setValueAction;

    /// <summary>
    /// Gets the property being accessed.
    /// </summary>
    public PropertyInfo Property { get; internal set; } = null!;

    /// <summary>
    /// Gets the instance on which the property is being accessed.
    /// Null for static properties.
    /// </summary>
    public object? Instance { get; internal set; }

    /// <summary>
    /// Gets or sets the property value.
    /// In OnGetValue: Set this to provide the value to return (after or instead of ProceedGetValue).
    /// In OnSetValue: This contains the value being set. Modify it before calling ProceedSetValue to change what gets set.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Gets the type of the property.
    /// </summary>
    public Type PropertyType => Property.PropertyType;

    /// <summary>
    /// Internal setter for the get value action.
    /// </summary>
    internal Action GetValueAction
    {
        set => _getValueAction = value;
    }

    /// <summary>
    /// Internal setter for the set value action.
    /// </summary>
    internal Action SetValueAction
    {
        set => _setValueAction = value;
    }

    /// <summary>
    /// Proceeds with getting the property value from the backing field/property.
    /// After calling this, the Value property will contain the retrieved value.
    /// </summary>
    public void ProceedGetValue()
    {
        _getValueAction?.Invoke();
    }

    /// <summary>
    /// Proceeds with setting the property value to the backing field/property.
    /// The Value property will be used as the value to set.
    /// </summary>
    public void ProceedSetValue()
    {
        _setValueAction?.Invoke();
    }
}
