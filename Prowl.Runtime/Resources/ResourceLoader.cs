using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>
/// Load assets by path at runtime. Assets under any Resources/ folder in the project
/// are always included in builds and can be loaded by their relative path.
/// Example: Assets/Art/Resources/Textures/Grass.png → GameResources.Load&lt;Texture2D&gt;("Textures/Grass")
/// </summary>
public static class GameResources
{
    private static Dictionary<string, Guid> _pathToGuid = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initialize the resources mapping. Called by the player at startup.</summary>
    public static void Initialize(Dictionary<string, Guid> pathToGuid)
    {
        _pathToGuid = pathToGuid ?? new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Load an asset by its resource path (relative to the nearest Resources/ folder, no extension).
    /// Returns null if the path is not found or the asset can't be loaded.
    /// </summary>
    public static T? Load<T>(string path) where T : EngineObject
    {
        if (string.IsNullOrEmpty(path)) return null;
        path = path.Replace('\\', '/');

        if (_pathToGuid.TryGetValue(path, out var guid))
            return AssetDatabase.Get(guid) as T;

        return null;
    }

    /// <summary>Check if a resource path exists.</summary>
    public static bool Exists(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return _pathToGuid.ContainsKey(path.Replace('\\', '/'));
    }

    /// <summary>Get the GUID for a resource path, or Guid.Empty if not found.</summary>
    public static Guid GetGuid(string path)
    {
        if (string.IsNullOrEmpty(path)) return Guid.Empty;
        return _pathToGuid.TryGetValue(path.Replace('\\', '/'), out var guid) ? guid : Guid.Empty;
    }
}
