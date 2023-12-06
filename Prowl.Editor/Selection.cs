using ImageMagick;
using Prowl.Runtime;

namespace Prowl.Editor;

public static class Selection 
{
    
    public static event Action<object>? OnSelectObject;
    public static event Action<object>? OnDeselectObject;

    private static readonly List<WeakReference> _currentRef = [];

    public static int Count => _currentRef.Count;
    public static Type? CurrentType { get; private set; } = null;

    public static object? Get(int index)
    {
        if (index < 0 || index >= _currentRef.Count)
            return null;
        var r = _currentRef[index];
        if (!r.IsAlive)
        {
            _currentRef.RemoveAt(index);
            return null;
        }
        return r.Target;
    }

    public static void Select<T>(T? obj, bool CanBeAdditive = false) where T : class 
    {
        if (obj is null) return;

        bool additive = Input.IsKeyDown(Raylib_cs.KeyboardKey.KEY_LEFT_CONTROL) || Input.IsKeyDown(Raylib_cs.KeyboardKey.KEY_RIGHT_CONTROL);

        if (!CanBeAdditive || !additive)
        {
            Clear();
            _currentRef.Add(new WeakReference(obj));
            CurrentType = typeof(T);
            OnSelectObject?.Invoke(obj);
        }
        else if(CanBeAdditive && additive)
        {
            AddSelect(obj);
        }

    }

    public static void AddSelect<T>(T? obj) where T : class
    {
        if (obj is null) return;

        // Is same object already selected then de-select it
        if (_currentRef.Any(x => x.Target == obj))
        {
            _currentRef.RemoveAll(x => x.Target == obj);
            if (Count == 0) CurrentType = null;
            OnDeselectObject?.Invoke(obj);
            return;
        }

        // No object selected then just select it
        if (CurrentType == null)
        {
            _currentRef.Add(new WeakReference(obj));
            CurrentType = typeof(T);
            OnSelectObject?.Invoke(obj);
        }
        else if (typeof(T).IsAssignableTo(CurrentType))
        {
            // Same type, add to selection
            _currentRef.Add(new WeakReference(obj));
            OnSelectObject?.Invoke(obj);
        }
        else
        {
            // New type, it overwrite all previous selected
            Clear();
            _currentRef.Add(new WeakReference(obj));
            CurrentType = typeof(T);
            OnSelectObject?.Invoke(obj);
        }
    }

    public static void Foreach<T>(Action<T> action) where T : class
    {
        var copy = _currentRef.ToList();
        foreach (var item in copy)
            if (item.IsAlive && item.Target.GetType().IsAssignableTo(typeof(T)))
                action((T)item.Target!);
    }

    public static bool IsSelected(object obj) => _currentRef.Any(x => ReferenceEquals(x.Target, obj));

    public static void Clear()
    {
        foreach (var item in _currentRef)
            if (item.IsAlive)
                OnDeselectObject?.Invoke(item.Target!);
        _currentRef.Clear();
        CurrentType = null;
    }
}
