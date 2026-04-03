using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _extensionToImporter.Clear();
        _nameToImporter.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(AssetImporter).IsAssignableFrom(type) || type.IsAbstract) continue;

                var attr = type.GetCustomAttribute<ImporterForAttribute>();
                if (attr == null) continue;

                _nameToImporter[type.Name] = type;

                foreach (var ext in attr.Extensions)
                {
                    string normalized = ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
                    _extensionToImporter[normalized] = type;
                }
            }
        }

        Runtime.Debug.Log($"ImporterRegistry: Registered {_nameToImporter.Count} importers for {_extensionToImporter.Count} extensions.");
    }

    public static AssetImporter? GetForExtension(string extension)
    {
        string ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        if (_extensionToImporter.TryGetValue(ext, out var type))
            return (AssetImporter)Activator.CreateInstance(type)!;
        return null;
    }

    public static string GetImporterTypeName(string extension)
    {
        string ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        return _extensionToImporter.TryGetValue(ext, out var type) ? type.Name : "DefaultImporter";
    }

    public static AssetImporter? CreateByTypeName(string typeName)
    {
        if (_nameToImporter.TryGetValue(typeName, out var type))
            return (AssetImporter)Activator.CreateInstance(type)!;
        return null;
    }

    public static IEnumerable<string> RegisteredExtensions => _extensionToImporter.Keys;
}
