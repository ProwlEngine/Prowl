namespace Prowl.Editor;

public static class Selection {
    
    public static event Action<object, object>? OnSelectionChanged;

    public static object? Dragging;
    public static object? Current => _currentRef.Target;
    private static readonly WeakReference _currentRef = new(null);
    
    public static void Select<T>(T? obj, bool TriggerOnChanged = true) where T : class {
        var old = _currentRef.Target;
        _currentRef.Target = obj;
        if(TriggerOnChanged)
            OnSelectionChanged?.Invoke(old, obj);
    }
    
    public static void Clear(bool TriggerOnChanged = true)
    {
        var old = _currentRef.Target;
        _currentRef.Target = null;
        if (TriggerOnChanged)
            OnSelectionChanged?.Invoke(old, null);
    }

    internal static void SetDragging(object thing)
    {
        Dragging = thing;
    }
}
