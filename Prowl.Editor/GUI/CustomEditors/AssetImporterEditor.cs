using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Attribute to register a custom asset editor for a specific EngineObject type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CustomAssetEditorAttribute : Attribute
{
    public Type TargetType { get; }
    public CustomAssetEditorAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>
/// Base class for custom asset editors shown in the inspector when an asset is selected.
/// </summary>
public abstract class AssetImporterEditor
{
    /// <summary>Draw the asset editor UI.</summary>
    public abstract void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset);
}

/// <summary>
/// Registry for AssetImporterEditor subclasses.
/// </summary>
public static class AssetImporterEditorRegistry
{
    private static readonly Dictionary<Type, Type> _typeToEditor = new();
    private static readonly Dictionary<Type, AssetImporterEditor> _editorCache = new();
    private static bool _initialized;

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() { _initialized = false; Initialize(); }

    /// <summary>Drop cached type maps and editor instances so the script AssemblyLoadContext can be collected.</summary>
    [Runtime.OnAssemblyUnload]
    public static void ClearCache()
    {
        _initialized = false;
        _typeToEditor.Clear();
        _editorCache.Clear();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _typeToEditor.Clear();
        _editorCache.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(AssetImporterEditor).IsAssignableFrom(type) || type.IsAbstract) continue;
                var attr = type.GetCustomAttribute<CustomAssetEditorAttribute>();
                if (attr == null) continue;
                _typeToEditor[attr.TargetType] = type;
            }
        }

        Runtime.Debug.Log($"AssetImporterEditorRegistry: {_typeToEditor.Count} asset editors registered.");
    }

    public static AssetImporterEditor? GetEditor(Type assetType)
    {
        if (_editorCache.TryGetValue(assetType, out var cached))
            return cached;

        // Exact match
        if (_typeToEditor.TryGetValue(assetType, out var editorType))
            return CacheAndReturn(assetType, editorType);

        // Walk base types
        Type? baseType = assetType.BaseType;
        while (baseType != null)
        {
            if (_typeToEditor.TryGetValue(baseType, out editorType))
                return CacheAndReturn(assetType, editorType);
            baseType = baseType.BaseType;
        }

        return null;
    }

    private static AssetImporterEditor CacheAndReturn(Type assetType, Type editorType)
    {
        var editor = (AssetImporterEditor)Activator.CreateInstance(editorType)!;
        _editorCache[assetType] = editor;
        return editor;
    }
}
