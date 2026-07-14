using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Editor.Utils;

/// <summary>
/// Shared lookup table for attribute-driven editor registries.
/// Each registry holds one instance, delegates scan/get to it, and keeps its own static entry points.
/// </summary>
internal sealed class EditorTypeRegistry<TEditor> where TEditor : class
{
    private readonly Dictionary<Type, Type> _typeToEditor = new();
    private readonly Dictionary<Type, TEditor> _editorCache = new();
    private readonly string _logName;
    private readonly Func<Type, Type?> _resolveTarget;
    private readonly bool _checkInterfaces;
    private bool _initialized;

    public EditorTypeRegistry(string logName, Func<Type, Type?> resolveTarget, bool checkInterfaces = false)
    {
        _logName = logName;
        _resolveTarget = resolveTarget;
        _checkInterfaces = checkInterfaces;
    }

    public void Reinitialize() { _initialized = false; Initialize(); }

    public void ClearCache()
    {
        _initialized = false;
        _typeToEditor.Clear();
        _editorCache.Clear();
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _typeToEditor.Clear();
        _editorCache.Clear();

        foreach (var type in EditorUtils.GetAllTypes())
        {
            if (!typeof(TEditor).IsAssignableFrom(type) || type.IsAbstract) continue;
            var target = _resolveTarget(type);
            if (target == null) continue;
            _typeToEditor[target] = type;
        }

        Runtime.Debug.Log($"{_logName}: {_typeToEditor.Count} editors registered.");
    }

    public TEditor? GetEditor(Type targetType)
    {
        if (!_initialized) Initialize();

        if (_editorCache.TryGetValue(targetType, out var cached)) return cached;

        if (_typeToEditor.TryGetValue(targetType, out var editorType))
            return Cache(targetType, editorType);

        Type? baseType = targetType.BaseType;
        while (baseType != null)
        {
            if (_typeToEditor.TryGetValue(baseType, out editorType))
                return Cache(targetType, editorType);
            baseType = baseType.BaseType;
        }

        if (_checkInterfaces)
        {
            foreach (var iface in targetType.GetInterfaces())
            {
                if (_typeToEditor.TryGetValue(iface, out editorType))
                    return Cache(targetType, editorType);
            }
        }

        return null;
    }

    private TEditor Cache(Type targetType, Type editorType)
    {
        var editor = (TEditor)Activator.CreateInstance(editorType)!;
        _editorCache[targetType] = editor;
        return editor;
    }
}
