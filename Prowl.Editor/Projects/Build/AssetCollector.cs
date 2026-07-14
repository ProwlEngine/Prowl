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
        public Dictionary<string, Guid> ResourcesMap; // load path -> guid
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
        var resourceGuids = new List<Guid>();
        foreach (var entry in db.GetAllEntries())
        {
            if (IsResourcesAsset(entry.Path))
            {
                allAssets.Add(entry.Guid);
                resourceGuids.Add(entry.Guid);
                if (entry.SubAssets != null)
                    foreach (var sub in entry.SubAssets)
                        allAssets.Add(sub.Guid);

                // Build the load path: everything after the last "Resources/" segment, no extension
                string loadPath = GetResourceLoadPath(entry.Path);
                if (!string.IsNullOrEmpty(loadPath))
                {
                    if (resourcesMap.ContainsKey(loadPath))
                        Runtime.Debug.LogWarning($"[Build] Duplicate Resources load path '{loadPath}': '{entry.Path}' overrides another asset.");
                    resourcesMap[loadPath] = entry.Guid;
                }
            }
        }

        // Resources assets are build entry points just like scenes - in DependenciesOnly mode their
        // own transitive dependencies must be collected too, or they ship with broken references.
        if (dependenciesOnly && resourceGuids.Count > 0)
            allAssets.UnionWith(db.Dependencies.GetTransitiveDependencies(resourceGuids));

        // Ensure sub-assets of all collected parents are included, and walk what those sub-assets
        // themselves reference too - not just their GUID. Repeats until nothing new turns up, since
        // a newly pulled-in dependency can itself be a parent with its own sub-assets.
        int previousCount;
        do
        {
            previousCount = allAssets.Count;

            var newSubAssetGuids = new List<Guid>();
            foreach (var entry in db.GetAllEntries())
            {
                if (!allAssets.Contains(entry.Guid) || entry.SubAssets == null) continue;
                foreach (var sub in entry.SubAssets)
                    if (allAssets.Add(sub.Guid))
                        newSubAssetGuids.Add(sub.Guid);
            }

            if (newSubAssetGuids.Count > 0)
                allAssets.UnionWith(db.Dependencies.GetTransitiveDependencies(newSubAssetGuids));

        } while (allAssets.Count > previousCount);

        // Exclude editor-only files (importers flag non-shippable types; plus anything in an Editor/ folder).
        var editorOnlyImporters = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var editorOnly = new HashSet<Guid>();
        foreach (var entry in db.GetAllEntries())
        {
            if (!IsEditorOnly(entry, editorOnlyImporters)) continue;
            editorOnly.Add(entry.Guid);
            if (entry.SubAssets != null)
                foreach (var sub in entry.SubAssets)
                    editorOnly.Add(sub.Guid);
        }
        allAssets.ExceptWith(editorOnly);
        foreach (var kv in resourcesMap.Where(kv => editorOnly.Contains(kv.Value)).ToList())
            resourcesMap.Remove(kv.Key);

        return new CollectionResult { AllAssets = allAssets, ResourcesMap = resourcesMap };
    }

    /// <summary>
    /// An asset is editor-only (excluded from build packaging) when it lives under an Editor/ folder,
    /// or when its importer declares <see cref="Importers.AssetImporter.IsEditorOnlyAsset"/> (scripts,
    /// plugins, assembly definitions - things that aren't real runtime data assets).
    /// </summary>
    /// <param name="importerCache">Per-collection cache of importer-type-name -> editor-only.</param>
    public static bool IsEditorOnly(AssetEntry entry, Dictionary<string, bool> importerCache)
    {
        var segments = entry.Path.Split('/', '\\');
        if (segments.Any(s => s.Equals("Editor", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (string.IsNullOrEmpty(entry.ImporterType))
            return false;

        if (!importerCache.TryGetValue(entry.ImporterType, out bool editorOnly))
        {
            editorOnly = Importers.ImporterRegistry.CreateByTypeName(entry.ImporterType)?.IsEditorOnlyAsset ?? false;
            importerCache[entry.ImporterType] = editorOnly;
        }
        return editorOnly;
    }

    /// <summary>Check if an asset path is under a Resources/ folder.</summary>
    private static bool IsResourcesAsset(string relativePath)
    {
        var segments = relativePath.Split('/', '\\');
        return segments.Any(s => s.Equals("Resources", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get the load path for a Resources asset.
    /// "Art/Resources/Textures/Grass.png" -> "Textures/Grass"
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
