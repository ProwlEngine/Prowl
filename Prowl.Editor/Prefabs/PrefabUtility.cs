using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Prefabs;

/// <summary>
/// Editor-side prefab operations: create, instantiate, break, apply, revert.
/// </summary>
public static class PrefabUtility
{
    // ================================================================
    //  Creation
    // ================================================================

    /// <summary>
    /// Save a GameObject hierarchy as a new .prefab file and convert the source to a prefab instance.
    /// </summary>
    /// <param name="source">The GameObject to save as a prefab.</param>
    /// <param name="relativeSavePath">Path relative to the Assets folder (e.g., "Prefabs/Enemy.prefab").</param>
    /// <returns>True if successful.</returns>
    public static bool CreatePrefab(GameObject source, string relativeSavePath)
    {
        if (source == null || Project.Current == null) return false;

        // Clear any existing prefab data so we serialize a clean prefab source
        source.ClearPrefabDataRecursive();

        // Serialize the GO tree
        var savedId = source.AssetID;
        source.AssetID = Guid.Empty;
        var echo = Serializer.Serialize(typeof(object), source);
        source.AssetID = savedId;

        if (echo == null) return false;

        // Write the .prefab file
        string absolutePath = Path.Combine(Project.Current.AssetsPath, relativeSavePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, echo.WriteToString());

        // Ensure meta file exists so asset DB picks it up with a stable GUID
        var meta = MetaFile.EnsureMeta(absolutePath, typeof(Importers.PrefabImporter).FullName!);
        if (meta.Guid == Guid.Empty) return false;

        // Stamp the source GO as a prefab instance
        StampAsPrefabInstance(source, meta.Guid);

        EditorSceneManager.IsDirty = true;
        Runtime.Debug.Log($"[Prefab] Created prefab: {relativeSavePath}");

        return true;
    }

    // ================================================================
    //  Instantiation
    // ================================================================

    /// <summary>
    /// Instantiate a prefab from its asset GUID.
    /// Returns a GameObject ready to be added to a scene.
    /// </summary>
    public static GameObject? InstantiatePrefab(Guid prefabGuid)
    {
        var prefab = AssetDatabase.Get(prefabGuid) as PrefabAsset;
        if (prefab == null)
        {
            Runtime.Debug.LogWarning($"[Prefab] Failed to load prefab asset {prefabGuid}");
            return null;
        }
        return prefab.Instantiate();
    }

    // ================================================================
    //  Break
    // ================================================================

    /// <summary>
    /// Break a prefab instance removes all prefab tracking data.
    /// The GameObject becomes a plain non-prefab object.
    /// </summary>
    public static void BreakPrefabInstance(GameObject go)
    {
        // Capture prefab state for undo
        var prefabId = go.PrefabAssetId;
        var overrides = go.PrefabOverrides.ToList();
        var compCount = go.PrefabComponentCount;
        var childCount = go.PrefabChildCount;
        var goRef = go;

        Undo.RegisterAction("Break Prefab Instance",
            undo: () =>
            {
                // Re-stamp as prefab instance
                StampAsPrefabInstance(goRef, prefabId);
                goRef.PrefabOverrides = overrides;
                goRef.PrefabComponentCount = compCount;
                goRef.PrefabChildCount = childCount;
            },
            redo: () => goRef.ClearPrefabDataRecursive());

        go.ClearPrefabDataRecursive();
        EditorSceneManager.IsDirty = true;
    }

    // ================================================================
    //  Apply / Revert
    // ================================================================

    /// <summary>
    /// Apply all overrides from this instance back to its prefab asset.
    /// Only operates within the nesting boundary of this GO's PrefabAssetId.
    /// </summary>
    public static void ApplyOverrides(GameObject instanceRoot)
    {
        if (!instanceRoot.IsPrefabInstance) return;

        var db = EditorAssetDatabase.Instance;
        if (db == null || Project.Current == null) return;

        var entry = db.GetEntry(instanceRoot.PrefabAssetId);
        if (entry == null)
        {
            Runtime.Debug.LogWarning("[Prefab] Cannot apply prefab asset not found.");
            return;
        }

        // Capture old prefab file for undo
        string absolutePath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string? oldFileContent = File.Exists(absolutePath) ? File.ReadAllText(absolutePath) : null;
        var oldOverrides = instanceRoot.PrefabOverrides.ToList();
        var prefabGuid = instanceRoot.PrefabAssetId;
        var goRef = instanceRoot;

        // Serialize the instance tree with prefab data stripped
        var cleanCopy = CloneWithoutPrefabData(instanceRoot);
        if (cleanCopy == null) return;

        var echo = Serializer.Serialize(typeof(object), cleanCopy);
        if (echo == null) return;

        // Write to the .prefab file
        File.WriteAllText(absolutePath, echo.WriteToString());

        // Clear overrides on this instance
        ClearOverridesWithinBoundary(instanceRoot, instanceRoot.PrefabAssetId);

        // Reimport and refresh invalidate source cache first
        _sourceCache.Remove(instanceRoot.PrefabAssetId);
        db.Reimport(entry.Guid);
        RefreshAllInstances(instanceRoot.PrefabAssetId);

        Undo.RegisterAction("Apply Prefab Overrides",
            undo: () =>
            {
                // Restore old prefab file
                if (oldFileContent != null) File.WriteAllText(absolutePath, oldFileContent);
                _sourceCache.Remove(prefabGuid);
                db.Reimport(entry.Guid);
                // Restore overrides on instance
                goRef.PrefabOverrides = oldOverrides;
                RefreshAllInstances(prefabGuid);
            },
            redo: () => ApplyOverrides(goRef));

        EditorSceneManager.IsDirty = true;
        Runtime.Debug.Log($"[Prefab] Applied overrides to {entry.Path}");
    }

    /// <summary>
    /// Revert all overrides on this instance, restoring it to match the prefab source.
    /// </summary>
    public static void RevertOverrides(GameObject instanceRoot)
    {
        if (!instanceRoot.IsPrefabInstance) return;

        var prefab = AssetDatabase.Get(instanceRoot.PrefabAssetId) as PrefabAsset;
        if (prefab == null)
        {
            Runtime.Debug.LogWarning("[Prefab] Cannot revert prefab asset not found.");
            return;
        }

        // Capture old state for undo
        var oldSerialized = Serializer.Serialize(typeof(object), instanceRoot);
        var parentId = instanceRoot.Parent?.Identifier ?? Guid.Empty;
        var siblingIdx = instanceRoot.Parent != null ? instanceRoot.Parent.Children.IndexOf(instanceRoot) : -1;
        var prefabGuid = instanceRoot.PrefabAssetId;

        // Instantiate fresh from prefab
        var fresh = prefab.Instantiate();
        if (fresh == null) return;

        // Preserve identifiers so undo records stay valid
        CopyIdentifiers(instanceRoot, fresh);

        fresh.Transform.Position = instanceRoot.Transform.Position;
        fresh.Transform.Rotation = instanceRoot.Transform.Rotation;
        fresh.Transform.LocalScale = instanceRoot.Transform.LocalScale;
        fresh.Name = instanceRoot.Name;

        var scene = instanceRoot.Scene;
        var parent = instanceRoot.Parent;
        var rootIdx = parent == null && scene != null ? scene.GetRootIndex(instanceRoot) : -1;

        if (scene != null)
        {
            scene.Remove(instanceRoot);
            scene.Add(fresh);
            if (parent != null)
            {
                fresh.SetParent(parent);
                if (siblingIdx >= 0) fresh.SetSiblingIndex(siblingIdx);
            }
            else if (rootIdx >= 0)
            {
                scene.SetRootIndex(fresh, rootIdx);
            }
        }

        // Register undo that swaps fresh back to old
        var freshId = fresh.Identifier;
        Undo.RegisterAction("Revert Prefab Overrides",
            undo: () =>
            {
                var s = Scene.Current;
                if (s == null) return;
                var current = FindByIdentifier(s, freshId);
                if (current == null) return;

                var restored = Serializer.Deserialize<GameObject>(oldSerialized);
                if (restored == null) return;
                Undo.RestoreIdentifiers(restored, oldSerialized);

                var p = current.Parent;
                s.Remove(current);
                current.Dispose();
                s.Add(restored);
                if (p != null) restored.SetParent(p);
                Selection.Select(restored);
            },
            redo: () =>
            {
                var s = Scene.Current;
                if (s == null) return;
                // Re-revert: find by old identifier, replace with fresh prefab
                var pf = AssetDatabase.Get(prefabGuid) as PrefabAsset;
                if (pf == null) return;
                // Find the old-state GO by its identifier
                var oldGo = FindByIdentifier(s, oldSerialized.Get("Identifier")?.StringValue != null
                    && Guid.TryParse(oldSerialized.Get("Identifier")?.StringValue, out var oid) ? oid : Guid.Empty);
                if (oldGo == null) return;

                var f2 = pf.Instantiate();
                if (f2 == null) return;
                f2.Transform.Position = oldGo.Transform.Position;
                f2.Transform.Rotation = oldGo.Transform.Rotation;
                f2.Transform.LocalScale = oldGo.Transform.LocalScale;
                f2.Name = oldGo.Name;
                var p2 = oldGo.Parent;
                s.Remove(oldGo);
                oldGo.Dispose();
                s.Add(f2);
                if (p2 != null) f2.SetParent(p2);
                Selection.Select(f2);
            });

        Selection.Select(fresh);
        EditorSceneManager.IsDirty = true;
    }

    private static GameObject? FindByIdentifier(Scene scene, Guid id)
    {
        foreach (var root in scene.RootObjects)
        {
            var found = root.FindChildByIdentifier(id);
            if (found != null) return found;
        }
        return null;
    }

    // ================================================================
    //  Override Detection
    // ================================================================

    /// <summary>
    /// Apply a single override from an instance to the prefab source.
    /// </summary>
    public static void ApplySingleOverride(GameObject instanceGO, PropertyOverride ov)
    {
        if (!instanceGO.IsPrefabInstance) return;

        var db = EditorAssetDatabase.Instance;
        if (db == null || Project.Current == null) return;

        var entry = db.GetEntry(instanceGO.PrefabAssetId);
        if (entry == null) return;

        // Load the prefab source, apply the single field, save back
        var prefab = Runtime.AssetDatabase.Get(instanceGO.PrefabAssetId) as PrefabAsset;
        if (prefab?.GameObjectData == null) return;

        // Capture old prefab file content for undo
        string absolutePath = System.IO.Path.Combine(Project.Current.AssetsPath, entry.Path);
        string? oldFileContent = System.IO.File.Exists(absolutePath) ? System.IO.File.ReadAllText(absolutePath) : null;
        var ovPath = ov.Path;
        var ovValue = ov.Value;
        var prefabGuid = instanceGO.PrefabAssetId;
        var goRef = instanceGO;

        var source = Serializer.Deserialize<GameObject>(prefab.GameObjectData);
        if (source == null) return;

        // Apply the override value to the source
        ParseOverridePath(source, ov.Path, out var target, out string fieldPath);
        if (target != null && !string.IsNullOrEmpty(fieldPath))
            ApplyFieldValue(target, fieldPath, ov.Value);

        // Save back to the .prefab file
        var echo = Serializer.Serialize(typeof(object), source);
        if (echo != null)
        {
            System.IO.File.WriteAllText(absolutePath, echo.WriteToString());
            _sourceCache.Remove(instanceGO.PrefabAssetId);
            db.Reimport(entry.Guid);
        }

        // Remove this override from the instance
        instanceGO.PrefabOverrides.Remove(ov);

        Undo.RegisterAction("Apply Single Override",
            undo: () =>
            {
                // Restore old prefab file
                if (oldFileContent != null) System.IO.File.WriteAllText(absolutePath, oldFileContent);
                _sourceCache.Remove(prefabGuid);
                db.Reimport(entry.Guid);
                // Re-add the override to the instance
                goRef.PrefabOverrides.Add(new PropertyOverride { Path = ovPath, Value = ovValue });
                RefreshAllInstances(prefabGuid);
            },
            redo: () =>
            {
                // Re-apply
                ApplySingleOverride(goRef, new PropertyOverride { Path = ovPath, Value = ovValue });
            });

        // Refresh other instances to pick up the change
        RefreshAllInstances(instanceGO.PrefabAssetId);

        EditorSceneManager.IsDirty = true;
    }

    /// <summary>
    /// Revert a single override load the source value and write it back to the instance field.
    /// </summary>
    public static void RevertSingleOverride(GameObject instanceGO, string overridePath)
    {
        if (!instanceGO.IsPrefabInstance) return;

        var source = GetCachedPrefabSource(instanceGO.PrefabAssetId);
        if (source == null) return;

        // Find the source value via the path
        ParseOverridePath(source, overridePath, out var sourceTarget, out string sourceFieldPath);
        if (sourceTarget == null || string.IsNullOrEmpty(sourceFieldPath)) return;

        // Read the source value
        var sourceField = GetFieldByPath(sourceTarget, sourceFieldPath);
        if (sourceField == null) return;

        // Find the instance target
        ParseOverridePath(instanceGO, overridePath, out var instanceTarget, out string instanceFieldPath);
        if (instanceTarget == null) return;

        // Capture old instance value for undo
        var oldInstanceValue = GetFieldValue(instanceTarget, instanceFieldPath);
        var oldInstanceEcho = Serializer.Serialize(sourceField.FieldType, oldInstanceValue);
        var removedOverrides = instanceGO.PrefabOverrides.Where(o => o.Path == overridePath).ToList();
        var goRef = instanceGO;
        var path = overridePath;

        // Copy source value to instance
        var sourceValue = GetFieldValue(sourceTarget, sourceFieldPath);
        SetFieldValue(instanceTarget, instanceFieldPath, sourceValue);

        // Remove the override entry
        instanceGO.PrefabOverrides.RemoveAll(o => o.Path == overridePath);

        Undo.RegisterAction("Revert Single Override",
            undo: () =>
            {
                // Restore old instance value
                ParseOverridePath(goRef, path, out var undoTarget, out string undoFieldPath);
                if (undoTarget != null && oldInstanceEcho != null)
                    ApplyFieldValue(undoTarget, undoFieldPath, oldInstanceEcho);
                // Re-add removed overrides
                goRef.PrefabOverrides.AddRange(removedOverrides);
            },
            redo: () =>
            {
                RevertSingleOverride(goRef, path);
            });

        EditorSceneManager.IsDirty = true;
    }

    private static System.Reflection.FieldInfo? GetFieldByPath(object target, string fieldPath)
    {
        string[] parts = fieldPath.Split('.');
        object current = target;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var field = current.GetType().GetField(parts[i],
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return null;
            current = field.GetValue(current);
            if (current == null) return null;
        }
        return current.GetType().GetField(parts[^1],
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    }

    private static object? GetFieldValue(object target, string fieldPath)
    {
        string[] parts = fieldPath.Split('.');
        object current = target;
        for (int i = 0; i < parts.Length; i++)
        {
            var field = current.GetType().GetField(parts[i],
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return null;
            current = field.GetValue(current);
            if (current == null && i < parts.Length - 1) return null;
        }
        return current;
    }

    private static void SetFieldValue(object target, string fieldPath, object? value)
    {
        string[] parts = fieldPath.Split('.');
        object current = target;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var field = current.GetType().GetField(parts[i],
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return;
            current = field.GetValue(current);
            if (current == null) return;
        }
        var finalField = current.GetType().GetField(parts[^1],
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        finalField?.SetValue(current, value);
    }

    /// <summary>Check if a specific property path is overridden on a GameObject.</summary>
    public static bool IsPropertyOverridden(GameObject go, string path)
    {
        if (!go.IsPrefabInstance) return false;
        return go.PrefabOverrides.Any(o => o.Path == path);
    }

    /// <summary>Check if a GameObject has any overrides at all.</summary>
    public static bool HasAnyOverrides(GameObject go)
    {
        if (!go.IsPrefabInstance) return false;
        return go.PrefabOverrides.Count > 0;
    }

    // ================================================================
    //  Instance Refresh
    // ================================================================

    /// <summary>
    /// Refresh all instances of a prefab in the current scene after the prefab asset changes.
    /// Re-instantiates from the updated source and re-applies each instance's overrides.
    /// </summary>
    public static void RefreshAllInstances(Guid prefabGuid)
    {
        var scene = Scene.Current;
        if (scene == null) return;

        // Find all instance roots for this prefab
        var roots = scene.AllObjects
            .Where(go => go.PrefabAssetId == prefabGuid && IsInstanceRoot(go))
            .ToList();

        var prefab = AssetDatabase.Get(prefabGuid) as PrefabAsset;
        if (prefab == null) return;

        var selectedGO = Selection.GetSelected<GameObject>().FirstOrDefault();
        GameObject? newSelection = null;

        foreach (var root in roots)
        {
            var savedOverrides = root.PrefabOverrides.ToList();
            var savedName = root.Name;
            var pos = root.Transform.Position;
            var rot = root.Transform.Rotation;
            var scale = root.Transform.LocalScale;
            var parent = root.Parent;
            var siblingIdx = root.GetSiblingIndex() ?? -1;
            var rootIdx = parent == null ? scene.GetRootIndex(root) : -1;

            var fresh = prefab.Instantiate();
            if (fresh == null) continue;

            // Preserve identifiers from the old instance so undo records stay valid
            CopyIdentifiers(root, fresh);

            fresh.PrefabOverrides = savedOverrides;
            fresh.Name = savedName;
            ApplyPropertyOverridesToInstance(fresh, savedOverrides);
            fresh.Transform.Position = pos;
            fresh.Transform.Rotation = rot;
            fresh.Transform.LocalScale = scale;

            scene.Remove(root);
            scene.Add(fresh);
            if (parent != null)
            {
                fresh.SetParent(parent);
                if (siblingIdx >= 0) fresh.SetSiblingIndex(siblingIdx);
            }
            else if (rootIdx >= 0)
            {
                scene.SetRootIndex(fresh, rootIdx);
            }

            if (selectedGO == root)
                newSelection = fresh;
        }

        if (newSelection != null)
            Selection.Select(newSelection);
    }

    // ================================================================
    //  Nesting Helpers
    // ================================================================

    /// <summary>
    /// Find the prefab instance root by walking up the parent chain.
    /// The root is the highest ancestor with the same PrefabAssetId.
    /// </summary>
    public static GameObject? GetPrefabInstanceRoot(GameObject go)
    {
        if (!go.IsPrefabInstance) return null;

        Guid prefabId = go.PrefabAssetId;
        GameObject root = go;

        while (root.Parent != null && root.Parent.IsValid() && root.Parent.PrefabAssetId == prefabId)
            root = root.Parent;

        return root;
    }

    /// <summary>True if this GO is a prefab instance root (not just a child within a prefab).</summary>
    public static bool IsInstanceRoot(GameObject go)
    {
        if (!go.IsPrefabInstance) return false;
        // Root if parent is null, or parent has a different PrefabAssetId
        return go.Parent == null || !go.Parent.IsValid() || go.Parent.PrefabAssetId != go.PrefabAssetId;
    }

    /// <summary>True if this GO is a nested prefab root (different PrefabAssetId from parent).</summary>
    public static bool IsNestedPrefabRoot(GameObject go)
    {
        if (!go.IsPrefabInstance) return false;
        return go.Parent != null && go.Parent.IsValid() && go.Parent.IsPrefabInstance
            && go.Parent.PrefabAssetId != go.PrefabAssetId;
    }

    // ================================================================
    //  Internal Helpers
    // ================================================================

    /// <summary>
    /// Re-applies stored property overrides to a freshly instantiated GO tree.
    /// Parses index-based paths to find target GO/component/field.
    /// </summary>
    private static void ApplyPropertyOverridesToInstance(GameObject root, List<PropertyOverride> overrides)
    {
        foreach (var ov in overrides)
        {
            try
            {
                // Parse the path to find the target
                ParseOverridePath(root, ov.Path, out var targetObj, out string fieldPath);
                if (targetObj == null || string.IsNullOrEmpty(fieldPath)) continue;

                ApplyFieldValue(targetObj, fieldPath, ov.Value);
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"[Prefab] Failed to apply override '{ov.Path}': {ex.Message}");
            }
        }
    }

    /// <summary>Parse an index-based override path into a target object and remaining field path.</summary>
    private static void ParseOverridePath(GameObject root, string path, out object? target, out string fieldPath)
    {
        target = null;
        fieldPath = "";

        var parts = path.Split('.');
        GameObject currentGO = root;
        int i = 0;

        // Walk GO path (g0, g1, etc.)
        while (i < parts.Length && parts[i].StartsWith('g'))
        {
            if (!int.TryParse(parts[i].AsSpan(1), out int childIdx) || childIdx < 0 || childIdx >= currentGO.Children.Count)
                return;
            currentGO = currentGO.Children[childIdx];
            i++;
        }

        if (i >= parts.Length) return;

        if (parts[i] == "$")
        {
            // GO-level field
            target = currentGO;
            fieldPath = string.Join(".", parts.Skip(i + 1));
        }
        else if (parts[i].StartsWith('c'))
        {
            // Component field
            if (!int.TryParse(parts[i].AsSpan(1), out int compIdx)) return;
            var comps = currentGO.GetComponents<MonoBehaviour>().ToList();
            if (compIdx >= comps.Count) return;
            target = comps[compIdx];
            fieldPath = string.Join(".", parts.Skip(i + 1));
        }
    }

    private static void ApplyFieldValue(object target, string fieldPath, EchoObject value)
    {
        string[] parts = fieldPath.Split('.');
        object current = target;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var field = current.GetType().GetField(parts[i],
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return;
            current = field.GetValue(current);
            if (current == null) return;
        }

        string finalField = parts[^1];
        var finalFieldInfo = current.GetType().GetField(finalField,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (finalFieldInfo == null) return;

        var deserialized = Serializer.Deserialize(value, finalFieldInfo.FieldType);
        if (deserialized != null)
            finalFieldInfo.SetValue(current, deserialized);
    }

    // ================================================================
    //  Automatic Override Detection (comparison-based)
    // ================================================================

    /// <summary>
    /// Compare a component's current state against its prefab source and update overrides.
    /// Uses index-based paths. Called after each component is drawn in the inspector.
    /// </summary>
    public static void DetectComponentOverrides(GameObject instanceGO, MonoBehaviour instanceComp)
    {
        if (!instanceGO.IsPrefabInstance) return;

        var source = GetCachedPrefabSource(instanceGO.PrefabAssetId);
        if (source == null) return;

        // Build the GO path from instance root
        string goPath = BuildGOPath(instanceGO);

        // Find the matching source GO by index path
        var sourceGO = ResolveGOPath(source, goPath);
        if (sourceGO == null) return;

        // Find matching component by index (all components, not just same type)
        var instanceComps = instanceGO.GetComponents<MonoBehaviour>().ToList();
        int compIndex = instanceComps.IndexOf(instanceComp);
        if (compIndex < 0) return;

        var sourceComps = sourceGO.GetComponents<MonoBehaviour>().ToList();
        if (compIndex >= sourceComps.Count) return;

        var sourceComp = sourceComps[compIndex];
        if (sourceComp.GetType() != instanceComp.GetType())
        {
            Runtime.Debug.LogWarning($"[Prefab] Component type mismatch at index {compIndex}: instance={instanceComp.GetType().Name}, source={sourceComp.GetType().Name}");
            return;
        }

        // Build path prefix
        string pathPrefix = string.IsNullOrEmpty(goPath)
            ? $"c{compIndex}"
            : $"{goPath}.c{compIndex}";

        // Compare fields
        CompareFields(instanceComp, sourceComp, pathPrefix, instanceGO.PrefabOverrides);
    }

    /// <summary>
    /// Detect GO-level overrides (Name, Tag, Layer, Enabled, Transform).
    /// </summary>
    public static void DetectGOOverrides(GameObject instanceGO)
    {
        if (!instanceGO.IsPrefabInstance) return;

        var source = GetCachedPrefabSource(instanceGO.PrefabAssetId);
        if (source == null) return;

        string goPath = BuildGOPath(instanceGO);
        var sourceGO = ResolveGOPath(source, goPath);
        if (sourceGO == null) return;

        string pathPrefix = string.IsNullOrEmpty(goPath) ? "$" : $"{goPath}.$";
        var overrides = instanceGO.PrefabOverrides;

        // Compare GO-level fields (excluding Name and Transform those are per-instance)
        CompareField(pathPrefix, "TagIndex", instanceGO.TagIndex, sourceGO.TagIndex, overrides);
        CompareField(pathPrefix, "LayerIndex", instanceGO.LayerIndex, sourceGO.LayerIndex, overrides);
        CompareField(pathPrefix, "Enabled", instanceGO.Enabled, sourceGO.Enabled, overrides);
        // Name and Transform (Position/Rotation/Scale) are intentionally NOT tracked —
        // they are per-instance values that don't constitute overrides.
    }

    // Fields that should never be compared for prefab overrides
    private static readonly HashSet<string> _skipFields = new()
    {
        "_identifier", "_enabledInHierarchy", "_go",
        "_hasStarted", "_hasBeenEnabled", "_executeAlwaysCached",
        "HideFlags"
    };

    private static void CompareFields(object instance, object source, string pathPrefix, List<PropertyOverride> overrides)
    {
        var fields = PropertyGrid.GetSerializableFields(instance.GetType());
        foreach (var field in fields)
        {
            if (_skipFields.Contains(field.Name)) continue;

            var instanceVal = field.GetValue(instance);
            var sourceVal = field.GetValue(source);
            string path = $"{pathPrefix}.{field.Name}";

            var instanceEcho = Serializer.Serialize(field.FieldType, instanceVal);
            var sourceEcho = Serializer.Serialize(field.FieldType, sourceVal);

            bool areSame = (instanceEcho?.WriteToString() ?? "") == (sourceEcho?.WriteToString() ?? "");

            var existing = overrides.FirstOrDefault(o => o.Path == path);
            if (!areSame)
            {
                if (existing != null)
                    existing.Value = instanceEcho!;
                else if (instanceEcho != null)
                    overrides.Add(new PropertyOverride { Path = path, Value = instanceEcho });
            }
            else if (existing != null)
            {
                overrides.Remove(existing);
            }
        }
    }

    private static void CompareField<T>(string pathPrefix, string fieldName, T instanceVal, T sourceVal, List<PropertyOverride> overrides)
    {
        string path = $"{pathPrefix}.{fieldName}";
        bool areSame = EqualityComparer<T>.Default.Equals(instanceVal, sourceVal);

        var existing = overrides.FirstOrDefault(o => o.Path == path);
        if (!areSame)
        {
            var serialized = Serializer.Serialize(typeof(T), instanceVal);
            if (existing != null)
                existing.Value = serialized!;
            else if (serialized != null)
                overrides.Add(new PropertyOverride { Path = path, Value = serialized });
        }
        else if (existing != null)
        {
            overrides.Remove(existing);
        }
    }

    // ================================================================
    //  GO Path Helpers
    // ================================================================

    /// <summary>Build index path from prefab instance root to this GO. Empty string = root.</summary>
    public static string BuildGOPath(GameObject go)
    {
        var root = GetPrefabInstanceRoot(go);
        if (root == null || root == go) return "";

        var parts = new List<string>();
        var current = go;
        while (current != root && current.Parent != null)
        {
            int idx = current.Parent.Children.IndexOf(current);
            parts.Insert(0, $"g{idx}");
            current = current.Parent;
        }
        return string.Join(".", parts);
    }

    /// <summary>Resolve an index path like "g0.g2" to a GO in the source tree.</summary>
    public static GameObject? ResolveGOPath(GameObject root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        var current = root;
        foreach (var part in path.Split('.'))
        {
            if (!part.StartsWith('g') || !int.TryParse(part.AsSpan(1), out int idx))
                return null;
            if (idx < 0 || idx >= current.Children.Count) return null;
            current = current.Children[idx];
        }
        return current;
    }

    // Cache the deserialized prefab source for comparison (per prefab GUID)
    private static readonly Dictionary<Guid, (GameObject go, long frame)> _sourceCache = new();

    private static GameObject? GetCachedPrefabSource(Guid prefabGuid)
    {
        long frame = Runtime.Time.FrameCount;

        if (_sourceCache.TryGetValue(prefabGuid, out var cached) && cached.frame == frame)
            return cached.go;

        var prefab = Runtime.AssetDatabase.Get(prefabGuid) as PrefabAsset;
        if (prefab?.GameObjectData == null) return null;

        var source = Serializer.Deserialize<GameObject>(prefab.GameObjectData);
        if (source != null)
            _sourceCache[prefabGuid] = (source, frame);

        return source;
    }

    /// <summary>
    /// Copy identifiers from an old GO tree to a fresh one (matched by structure index).
    /// Preserves GO and component identifiers so undo records, selection, etc. stay valid.
    /// </summary>
    private static void CopyIdentifiers(GameObject oldGO, GameObject freshGO)
    {
        freshGO.SetIdentifier(oldGO.Identifier);

        var oldComps = oldGO.GetComponents().ToArray();
        var freshComps = freshGO.GetComponents().ToArray();
        for (int i = 0; i < Math.Min(oldComps.Length, freshComps.Length); i++)
            freshComps[i].Identifier = oldComps[i].Identifier;

        for (int i = 0; i < Math.Min(oldGO.Children.Count, freshGO.Children.Count); i++)
            CopyIdentifiers(oldGO.Children[i], freshGO.Children[i]);
    }

    private static void StampAsPrefabInstance(GameObject go, Guid prefabGuid)
    {
        go.PrefabAssetId = prefabGuid;
        foreach (var child in go.Children)
        {
            // Don't overwrite nested prefab instances
            if (child.IsPrefabInstance && child.PrefabAssetId != prefabGuid)
                continue;
            StampAsPrefabInstance(child, prefabGuid);
        }
    }

    private static GameObject? CloneWithoutPrefabData(GameObject source)
    {
        // Serialize the source
        var savedId = source.AssetID;
        source.AssetID = Guid.Empty;
        var echo = Serializer.Serialize(typeof(object), source);
        source.AssetID = savedId;
        if (echo == null) return null;

        // Deserialize a clean copy
        var clone = Serializer.Deserialize<GameObject>(echo);
        if (clone == null) return null;

        // Strip prefab data from the clone
        StripPrefabDataWithinBoundary(clone, source.PrefabAssetId);
        return clone;
    }

    private static void StripPrefabDataWithinBoundary(GameObject go, Guid boundaryPrefabId)
    {
        if (go.PrefabAssetId == boundaryPrefabId)
        {
            go.ClearPrefabData();
            foreach (var child in go.Children)
                StripPrefabDataWithinBoundary(child, boundaryPrefabId);
        }
        // Nested prefab children keep their own prefab data
    }

    private static void ClearOverridesWithinBoundary(GameObject go, Guid boundaryPrefabId)
    {
        if (go.PrefabAssetId == boundaryPrefabId)
        {
            go.PrefabOverrides.Clear();
            foreach (var child in go.Children)
                ClearOverridesWithinBoundary(child, boundaryPrefabId);
        }
    }
}
