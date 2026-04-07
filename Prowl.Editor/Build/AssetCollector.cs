using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prowl.Editor.Build;

/// <summary>
/// Collects all assets needed for a build based on scene dependencies and Resources/ folders.
/// </summary>
public static class AssetCollector
{
    public struct CollectionResult
    {
        public HashSet<Guid> AllAssets;
        public Dictionary<string, Guid> ResourcesMap; // load path → guid
    }

    /// <summary>
    /// Collect all assets needed for a set of scenes.
    /// Walks dependencies transitively and includes all Resources/ folder assets.
    /// </summary>
    public static CollectionResult Collect(List<Guid> sceneGuids, bool dependenciesOnly)
    {
        var db = EditorAssetDatabase.Instance;
        if (db == null)
            return new CollectionResult { AllAssets = new(), ResourcesMap = new() };

        var allAssets = new HashSet<Guid>();
        var resourcesMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (dependenciesOnly)
        {
            allAssets = db.Dependencies.GetTransitiveDependencies(sceneGuids);
            foreach (var sg in sceneGuids)
                allAssets.Add(sg);
        }
        else
        {
            // All assets
            foreach (var entry in db.GetAllEntries())
            {
                allAssets.Add(entry.Guid);
                if (entry.SubAssets != null)
                    foreach (var sub in entry.SubAssets)
                        allAssets.Add(sub.Guid);
            }
        }

        // Always include Resources/ folder assets regardless of dependency mode
        foreach (var entry in db.GetAllEntries())
        {
            if (IsResourcesAsset(entry.Path))
            {
                allAssets.Add(entry.Guid);
                if (entry.SubAssets != null)
                    foreach (var sub in entry.SubAssets)
                        allAssets.Add(sub.Guid);

                // Build the load path: everything after the last "Resources/" segment, no extension
                string loadPath = GetResourceLoadPath(entry.Path);
                if (!string.IsNullOrEmpty(loadPath))
                    resourcesMap[loadPath] = entry.Guid;
            }
        }

        // Ensure sub-assets of all collected parents are included
        foreach (var entry in db.GetAllEntries())
        {
            if (allAssets.Contains(entry.Guid) && entry.SubAssets != null)
            {
                foreach (var sub in entry.SubAssets)
                    allAssets.Add(sub.Guid);
            }
        }

        return new CollectionResult { AllAssets = allAssets, ResourcesMap = resourcesMap };
    }

    /// <summary>Check if an asset path is under a Resources/ folder.</summary>
    private static bool IsResourcesAsset(string relativePath)
    {
        var segments = relativePath.Split('/', '\\');
        return segments.Any(s => s.Equals("Resources", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get the load path for a Resources asset.
    /// "Art/Resources/Textures/Grass.png" → "Textures/Grass"
    /// </summary>
    private static string GetResourceLoadPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        int idx = normalized.LastIndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // Check if it starts with "Resources/"
            if (normalized.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
                idx = -1; // will add "/Resources/".Length below
            else
                return "";
        }

        string afterResources = normalized[(idx + "/Resources/".Length)..];
        // Remove extension
        int dotIdx = afterResources.LastIndexOf('.');
        if (dotIdx >= 0)
            afterResources = afterResources[..dotIdx];

        return afterResources;
    }
}
