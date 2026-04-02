using System;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Returned by widgets to allow chaining callbacks.
/// The widget has already been drawn — this just holds the callback registration.
/// </summary>
public struct WidgetResult<T>
{
    private readonly Action<Action<T>> _registerCallback;

    internal WidgetResult(Action<Action<T>> registerCallback)
    {
        _registerCallback = registerCallback;
    }

    /// <summary>
    /// Register a callback that fires when the widget's value changes.
    /// This is called from Paper's end-of-frame event processing.
    /// </summary>
    public void OnValueChanged(Action<T> callback)
    {
        _registerCallback?.Invoke(callback);
    }
}
