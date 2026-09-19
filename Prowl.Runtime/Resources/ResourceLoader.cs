using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>
/// Load assets by path at runtime. Only assets inside a Resources folder, or a folder below one, can
/// be loaded this way, and they are always included in builds.
/// </summary>
/// <remarks>
/// A load path is the asset's path below its nearest Resources folder without the extension, with
/// "#Name" added for a sub asset. The path copied from the project panel works too, as long as it
/// points inside a Resources folder:
/// <code>
/// Assets/Art/Resources/Textures/Grass.png      GameResources.Load<Texture2D<("Textures/Grass")
/// Assets/Art/Resources/Models/Hero.fbx#Body    GameResources.Load<Mesh<("Models/Hero#Body")
///                                              GameResources.Load<Mesh<("Art/Resources/Models/Hero.fbx#Body")
/// </code>
/// </remarks>
public static class GameResources
{
    private const string ResourcesFolder = "Resources";
    private const char SubAssetSeparator = '#';

    private static Dictionary<string, Guid> _pathToGuid = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initialize the resources mapping. Called by the player at startup.</summary>
    public static void Initialize(Dictionary<string, Guid> pathToGuid)
    {
        _pathToGuid = pathToGuid ?? new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Load an asset or sub asset by its load path, or by its project path inside a Resources folder.
    /// Returns null if the path is not found, is outside every Resources folder, or can't be loaded.
    /// </summary>
    public static T? Load<T>(string path) where T : EngineObject
    {
        Guid guid = GetGuid(path);
        return guid == Guid.Empty ? null : AssetDatabase.Get(guid) as T;
    }

    /// <summary>Check if a resource path exists.</summary>
    public static bool Exists(string path) => GetGuid(path) != Guid.Empty;

    /// <summary>Get the GUID for a resource path, or Guid.Empty if not found.</summary>
    public static Guid GetGuid(string path)
    {
        string? key = ToLoadPath(path);
        return key != null && _pathToGuid.TryGetValue(key, out var guid) ? guid : Guid.Empty;
    }

    /// <summary>
    /// The load path of an asset at <paramref name="assetPath"/> (relative to Assets), or of its sub asset
    /// named <paramref name="subAssetName"/>. Null when the asset is not inside a Resources folder.
    /// </summary>
    public static string? GetLoadPath(string assetPath, string? subAssetName = null)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;

        string[] segments = assetPath.Replace('\\', '/').Trim('/').Split('/');

        // The nearest Resources folder, so nested Resources folders load relative to the innermost one.
        // The last segment is the file itself and never counts as the folder.
        int resources = -1;
        for (int i = segments.Length - 2; i >= 0 && resources < 0; i--)
        {
            if (segments[i].Equals(ResourcesFolder, StringComparison.OrdinalIgnoreCase))
                resources = i;
        }
        if (resources < 0) return null;

        string loadPath = string.Join("/", segments, resources + 1, segments.Length - resources - 1);
        loadPath = StripExtension(loadPath);
        if (loadPath.Length == 0) return null;

        return string.IsNullOrEmpty(subAssetName) ? loadPath : loadPath + SubAssetSeparator + subAssetName;
    }

    /// <summary>
    /// Turns what a caller passed into the key the map is stored under. A path inside a Resources folder is
    /// a project path and is cut down to its load path; anything else is taken as a load path already.
    /// </summary>
    private static string? ToLoadPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        path = path.Replace('\\', '/').Trim('/');

        string? subAsset = null;
        int separator = path.IndexOf(SubAssetSeparator);
        if (separator >= 0)
        {
            subAsset = path[(separator + 1)..];
            path = path[..separator];
        }

        string? projectLoadPath = GetLoadPath(path, subAsset);
        if (projectLoadPath != null) return projectLoadPath;

        path = StripExtension(path);
        if (path.Length == 0) return null;
        return string.IsNullOrEmpty(subAsset) ? path : path + SubAssetSeparator + subAsset;
    }

    // Only the file name carries an extension, so a dot in a folder name is left alone.
    private static string StripExtension(string path)
    {
        int slash = path.LastIndexOf('/');
        int dot = path.LastIndexOf('.');
        return dot > slash + 1 ? path[..dot] : path;
    }
}
