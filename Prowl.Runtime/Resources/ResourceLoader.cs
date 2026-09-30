using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime;

/// <summary>An asset reachable through <see cref="GameResources"/>, with the type it loads as.</summary>
public readonly record struct ResourceEntry(string LoadPath, Guid Guid, string TypeName);

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
/// Several assets may share a load path, for example Enemy.png and Enemy.prefab, or the same path in two
/// Resources folders. The first one, by asset path, that is of the requested type is used.
/// </remarks>
public static class GameResources
{
    private const string ResourcesFolder = "Resources";
    private const char SubAssetSeparator = '#';

    private static ResourceEntry[] _all = [];
    private static Dictionary<string, List<ResourceEntry>> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialize the resources mapping. Called by the editor and the player at startup.
    /// Entries sharing a load path are tried in the order given.
    /// </summary>
    public static void Initialize(IEnumerable<ResourceEntry>? entries)
    {
        ResourceEntry[] all = entries?.ToArray() ?? [];
        var byPath = new Dictionary<string, List<ResourceEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in all)
        {
            if (!byPath.TryGetValue(entry.LoadPath, out var list))
                byPath[entry.LoadPath] = list = [];
            list.Add(entry);
        }
        _byPath = byPath;
        _all = all;
    }

    /// <summary>
    /// Load an asset or sub asset by its load path, or by its project path inside a Resources folder.
    /// Returns null if nothing of type <typeparamref name="T"/> is found at the path or it can't be loaded.
    /// </summary>
    public static T? Load<T>(string path) where T : EngineObject
    {
        foreach (var entry in Candidates<T>(path))
            if (AssetDatabase.Get(entry.Guid) is T asset)
                return asset;
        return null;
    }

    /// <summary>Load every Resources asset and sub asset of type <typeparamref name="T"/>.</summary>
    public static List<T> LoadAll<T>() where T : EngineObject => LoadMatching<T>(_all);

    /// <summary>
    /// Load every asset and sub asset of type <typeparamref name="T"/> under a folder, including its
    /// sub folders, or inside a single file when the path names one.
    /// </summary>
    public static List<T> LoadAll<T>(string path) where T : EngineObject
    {
        string? key = ToLoadPath(path);
        if (key == null) return LoadMatching<T>(_all);

        return LoadMatching<T>(_all.Where(e => IsAtOrBelow(e.LoadPath, key)));
    }

    /// <summary>Check if a resource path exists.</summary>
    public static bool Exists(string path) => GetGuid(path) != Guid.Empty;

    /// <summary>Check if a resource of type <typeparamref name="T"/> exists at the path.</summary>
    public static bool Exists<T>(string path) where T : EngineObject => GetGuid<T>(path) != Guid.Empty;

    /// <summary>Get the GUID of the first resource at the path, or Guid.Empty if not found.</summary>
    public static Guid GetGuid(string path) => GetGuid<EngineObject>(path);

    /// <summary>
    /// Get the GUID of the first resource of type <typeparamref name="T"/> at the path, or Guid.Empty
    /// if not found. Nothing is loaded.
    /// </summary>
    public static Guid GetGuid<T>(string path) where T : EngineObject
    {
        foreach (var entry in Candidates<T>(path))
            return entry.Guid;
        return Guid.Empty;
    }

    private static IEnumerable<ResourceEntry> Candidates<T>(string path) where T : EngineObject
    {
        string? key = ToLoadPath(path);
        if (key == null || !_byPath.TryGetValue(key, out var entries)) return [];
        return entries.Where(IsOfType<T>);
    }

    private static List<T> LoadMatching<T>(IEnumerable<ResourceEntry> entries) where T : EngineObject
    {
        var assets = new List<T>();
        foreach (var entry in entries.Where(IsOfType<T>))
            if (AssetDatabase.Get(entry.Guid) is T asset)
                assets.Add(asset);
        return assets;
    }

    // An entry whose type can't be resolved is still a candidate, and is checked once loaded.
    private static bool IsOfType<T>(ResourceEntry entry)
    {
        Type? type = RuntimeUtils.ResolveType(entry.TypeName);
        return type == null || typeof(T).IsAssignableFrom(type);
    }

    private static bool IsAtOrBelow(string loadPath, string folder)
    {
        if (!loadPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return false;
        return loadPath.Length == folder.Length || loadPath[folder.Length] is '/' or SubAssetSeparator;
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
