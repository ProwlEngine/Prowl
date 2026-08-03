using System;
using System.Collections.Generic;
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
    public static CollectionResult Collect(EditorAssetBackend db, List<Guid> sceneGuids, bool dependenciesOnly)
    {
        ArgumentNullException.ThrowIfNull(db);

        var allAssets = new HashSet<Guid>();
        var resourcesMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Enumerated once: the walk below revisits the set repeatedly, and re-reading the database each
        // pass is the difference between a linear collection and a quadratic one on a large project.
        var entries = db.GetAllEntries().ToList();

        if (dependenciesOnly)
        {
            allAssets = db.Dependencies.GetTransitiveDependencies(sceneGuids);
            foreach (var sg in sceneGuids)
                allAssets.Add(sg);
        }
        else
        {
            // All assets
            foreach (var entry in entries)
            {
                allAssets.Add(entry.Guid);
                foreach (var sub in entry.SubAssets)
                    allAssets.Add(sub.Guid);
            }
        }

        // Always include Resources/ folder assets regardless of dependency mode
        var resourceGuids = new List<Guid>();
        foreach (var entry in entries)
        {
            if (!IsResourcesAsset(entry.Path)) continue;

            allAssets.Add(entry.Guid);
            resourceGuids.Add(entry.Guid);
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
            foreach (var entry in entries)
            {
                if (!allAssets.Contains(entry.Guid)) continue;
                foreach (var sub in entry.SubAssets)
                    if (allAssets.Add(sub.Guid))
                        newSubAssetGuids.Add(sub.Guid);
            }

            if (newSubAssetGuids.Count > 0)
                allAssets.UnionWith(db.Dependencies.GetTransitiveDependencies(newSubAssetGuids));

        } while (allAssets.Count > previousCount);

        // Exclude editor-only assets (importers flag non-shippable types; plus anything under an
        // Editor/ folder) by default - UNLESS something that's actually shipping still depends on it,
        // in which case it ships anyway (a normal runtime asset can legitimately reference something
        // that happens to live under Editor/ tooling). Walked to a fixed point since the dependency
        // itself might also be editor-only (e.g. two files under Editor/ referencing each other, where
        // neither should ship unless something outside Editor/ needs the chain).
        var editorOnlyImporters = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var editorOnly = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (!IsEditorOnly(entry, editorOnlyImporters)) continue;
            editorOnly.Add(entry.Guid);
            foreach (var sub in entry.SubAssets)
                editorOnly.Add(sub.Guid);
        }

        var neededEditorOnly = new HashSet<Guid>();
        bool grew;
        do
        {
            grew = false;
            foreach (var guid in editorOnly)
            {
                if (!allAssets.Contains(guid) || neededEditorOnly.Contains(guid)) continue;
                foreach (var dependent in db.Dependencies.GetDependents(guid))
                {
                    if (!allAssets.Contains(dependent)) continue;
                    if (editorOnly.Contains(dependent) && !neededEditorOnly.Contains(dependent)) continue;
                    neededEditorOnly.Add(guid);
                    grew = true;
                    break;
                }
            }
        } while (grew);

        allAssets.ExceptWith(editorOnly.Where(g => !neededEditorOnly.Contains(g)));
        foreach (var kv in resourcesMap.Where(kv => editorOnly.Contains(kv.Value) && !neededEditorOnly.Contains(kv.Value)).ToList())
            resourcesMap.Remove(kv.Key);

        return new CollectionResult { AllAssets = allAssets, ResourcesMap = resourcesMap };
    }

    /// <summary>
    /// An asset is editor-only (excluded from build packaging by default - see <see cref="Collect"/>'s
    /// "still needed" check for the dependency override) when it lives under an Editor/ folder, or when
    /// its importer declares <see cref="Importers.AssetImporter.IsEditorOnlyAsset"/> (scripts, plugins,
    /// assembly definitions - things that aren't real runtime data assets).
    /// </summary>
    public static bool IsEditorOnly(AssetEntry entry, Dictionary<string, bool> importerCache)
    {
        var segments = entry.Path.Split('/', '\\');
        if (segments.Any(s => s.Equals("Editor", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (string.IsNullOrEmpty(entry.ImporterType))
            return false;

        // Constructing an importer is not free and the same type answers for every asset it handles.
        if (!importerCache.TryGetValue(entry.ImporterType, out bool editorOnly))
        {
            editorOnly = EditorRegistries.CreateImporterByName(entry.ImporterType)?.IsEditorOnlyAsset ?? false;
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
