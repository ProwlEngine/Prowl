using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Editor.Utils;

namespace Prowl.Editor.Importers;

/// <summary>
/// Discovers and maps AssetImporter subclasses to file extensions.
/// Uses the [ImporterFor] attribute to build the mapping at startup.
/// </summary>
public static class ImporterRegistry
{
    private static readonly Dictionary<string, Type> _extensionToImporter = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Type> _nameToImporter = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() { _initialized = false; Initialize(); }

    /// <summary>Drop cached importer type maps so the script AssemblyLoadContext can be collected.</summary>
    [Runtime.OnAssemblyUnload]
    public static void ClearCache()
    {
        _initialized = false;
        _extensionToImporter.Clear();
        _nameToImporter.Clear();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _extensionToImporter.Clear();
        _nameToImporter.Clear();

        foreach (var type in EditorUtils.GetAllTypes())
        {
            if (!typeof(AssetImporter).IsAssignableFrom(type) || type.IsAbstract) continue;

            var attr = type.GetCustomAttribute<ImporterForAttribute>();
            if (attr == null) continue;

            _nameToImporter[type.Name] = type;

            foreach (var ext in attr.Extensions)
                _extensionToImporter[NormalizeExt(ext)] = type;
        }

        Runtime.Debug.Log($"ImporterRegistry: Registered {_nameToImporter.Count} importers for {_extensionToImporter.Count} extensions. Extensions: {string.Join(", ", _extensionToImporter.Keys)}");
    }

    public static AssetImporter? GetForExtension(string extension)
    {
        if (_extensionToImporter.TryGetValue(NormalizeExt(extension), out var type))
            return (AssetImporter)Activator.CreateInstance(type)!;
        return null;
    }

    public static string GetImporterTypeName(string extension)
        => _extensionToImporter.TryGetValue(NormalizeExt(extension), out var type) ? type.Name : "DefaultImporter";

    public static AssetImporter? CreateByTypeName(string typeName)
    {
        if (_nameToImporter.TryGetValue(typeName, out var type))
            return (AssetImporter)Activator.CreateInstance(type)!;
        return null;
    }

    public static IEnumerable<string> RegisteredExtensions => _extensionToImporter.Keys;

    private static string NormalizeExt(string ext)
        => ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
}
