using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Projects;
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
    /// Nested prefab instances within the hierarchy keep their own links, in the saved asset and in the scene.
    /// </summary>
    /// <param name="source">The GameObject to save as a prefab.</param>
    /// <param name="relativeSavePath">Path relative to the Assets folder (e.g., "Prefabs/Enemy.prefab").</param>
    /// <param name="overwrite">Allow replacing an existing file. Off by default replacing a prefab
    /// keeps its GUID, so every instance in every scene would silently adopt the new contents.</param>
    /// <returns>True if successful.</returns>
    public static bool CreatePrefab(GameObject source, string relativeSavePath, bool overwrite = false)
    {
        if (source == null || Project.Current == null) return false;
        if (!GuardNotPlaying("create a prefab")) return false;

        if (!relativeSavePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            Runtime.Debug.LogError($"[Prefab] '{relativeSavePath}' is not a .prefab path.");
            return false;
        }

        string absolutePath = Path.Combine(Project.Current.AssetsPath, relativeSavePath);
        if (!overwrite && File.Exists(absolutePath))
        {
            Runtime.Debug.LogWarning($"[Prefab] '{relativeSavePath}' already exists; pass overwrite to replace it.");
            return false;
        }

        // Serialize a copy with this object's own prefab data stripped. Nested instances keep theirs,
        // so saving a hierarchy that contains prefabs preserves those links.
        var cleanCopy = CloneWithoutPrefabData(source);
        if (cleanCopy == null) return false;

        var echo = Serializer.Serialize(typeof(object), cleanCopy);
        if (echo == null) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        if (!TryWriteFile(absolutePath, echo.WriteToString())) return false;

        // Ensure meta file exists so asset DB picks it up with a stable GUID.
        var meta = MetaFile.EnsureMeta(absolutePath, nameof(Importers.PrefabImporter));
        if (meta.Guid == Guid.Empty) return false;

        // Stamp the source GO as an instance of the new prefab. Undo restores the previous prefab
        // links; the created asset itself is left on disk.
        var boundary = source.PrefabAssetId;
        var previous = CapturePrefabState(source, boundary);
        StampAsPrefabInstance(source, meta.Guid, boundary);

        Undo.RegisterAction("Create Prefab",
            undo: () => RestorePrefabState(previous),
            redo: () => StampAsPrefabInstance(source, meta.Guid, boundary));

        EditorSceneManager.MarkDirty();
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
    /// Break a prefab instance removes the link to its prefab asset.
    /// The GameObject becomes a plain non-prefab object, but nested prefab instances inside it keep
    /// their own links (breaking the outermost instance only).
    /// </summary>
    public static void BreakPrefabInstance(GameObject go)
    {
        if (!go.IsPrefabInstance) return;
        if (!GuardNotPlaying("break a prefab instance")) return;

        // Unpacking is an instance-level operation. Breaking a child would leave a hole inside an
        // instance that the next refresh silently fills back in.
        var unpackRoot = GetPrefabInstanceRoot(go);
        if (unpackRoot.IsValid()) go = unpackRoot!;

        var boundary = go.PrefabAssetId;
        var previous = CapturePrefabState(go, boundary);
        var goRef = go;

        Undo.RegisterAction("Break Prefab Instance",
            undo: () => RestorePrefabState(previous),
            redo: () => StripPrefabDataWithinBoundary(goRef, boundary));

        StripPrefabDataWithinBoundary(go, boundary);
        EditorSceneManager.MarkDirty();
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
        if (!GuardNotPlaying("apply prefab overrides")) return;

        // This serializes the whole instance tree over the asset, so it has to run on the instance
        // root. Handed a child, it would replace the prefab with just that subtree.
        var applyRoot = GetPrefabInstanceRoot(instanceRoot);
        if (applyRoot.IsValid()) instanceRoot = applyRoot!;

        var db = EditorAssetBackend.Instance;
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

        // Name and root transform are per-instance, not prefab content, so keep the asset's own.
        PreserveSourceIdentity(cleanCopy, instanceRoot.PrefabAssetId);

        var echo = Serializer.Serialize(typeof(object), cleanCopy);
        if (echo == null) return;

        // Write to the .prefab file
        if (!TryWriteFile(absolutePath, echo.WriteToString())) return;

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
                if (oldFileContent != null) TryWriteFile(absolutePath, oldFileContent);
                _sourceCache.Remove(prefabGuid);
                db.Reimport(entry.Guid);
                // Restore overrides on instance
                goRef.PrefabOverrides = oldOverrides;
                RefreshAllInstances(prefabGuid);
            },
            redo: () => ApplyOverrides(goRef));

        EditorSceneManager.MarkDirty();
        Runtime.Debug.Log($"[Prefab] Applied overrides to {entry.Path}");
    }

    /// <summary>
    /// Revert all overrides on this instance, restoring it to match the prefab source.
    /// </summary>
    public static void RevertOverrides(GameObject instanceRoot)
    {
        if (!instanceRoot.IsPrefabInstance) return;
        if (!GuardNotPlaying("revert prefab overrides")) return;

        var revertRoot = GetPrefabInstanceRoot(instanceRoot);
        if (revertRoot.IsValid()) instanceRoot = revertRoot!;

        var prefab = AssetDatabase.Get(instanceRoot.PrefabAssetId) as PrefabAsset;
        if (prefab == null)
        {
            Runtime.Debug.LogWarning("[Prefab] Cannot revert prefab asset not found.");
            return;
        }

        // Capture old state for undo
        var oldSerialized = Serializer.Serialize(typeof(object), instanceRoot);
        var parentId = instanceRoot.Parent.IsValid() ? instanceRoot.Parent.Identifier : Guid.Empty;
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
            instanceRoot.Destroy(); // TODO should this be Destroy (deferred) or Dispose?
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
                current.Destroy(); // TODO should this be Destroy (deferred) or Dispose?
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
                oldGo.Destroy(); // TODO should this be Destroy (deferred) or Dispose?
                s.Add(f2);
                if (p2 != null) f2.SetParent(p2);
                Selection.Select(f2);
            });

        Selection.Select(fresh);
        EditorSceneManager.MarkDirty();
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
        if (!GuardNotPlaying("apply a prefab override")) return;

        var db = EditorAssetBackend.Instance;
        if (db == null || Project.Current == null) return;

        var entry = db.GetEntry(instanceGO.PrefabAssetId);
        if (entry == null) return;

        // Load the prefab source, apply the single field, save back
        var prefab = Runtime.AssetDatabase.Get(instanceGO.PrefabAssetId) as PrefabAsset;
        if (prefab.IsNotValid() || prefab.GameObjectData == null) return;

        // Capture old prefab file content for undo
        string absolutePath = System.IO.Path.Combine(Project.Current.AssetsPath, entry.Path);
        string? oldFileContent = System.IO.File.Exists(absolutePath) ? System.IO.File.ReadAllText(absolutePath) : null;
        var ovPath = ov.Path;
        var ovValue = ov.Value;
        var prefabGuid = instanceGO.PrefabAssetId;
        // Overrides live on the prefab instance root.
        var instanceRoot = GetPrefabInstanceRoot(instanceGO);
        var goRef = instanceRoot.IsValid() ? instanceRoot : instanceGO;

        var source = Serializer.Deserialize<GameObject>(prefab.GameObjectData);
        if (source == null) return;

        // Apply the override value to the source
        ParseOverridePath(source, ov.Path, out var target, out string fieldPath);
        if (target != null && !string.IsNullOrEmpty(fieldPath))
            ApplyFieldValue(target, fieldPath, ov.Value);

        // Save back to the .prefab file
        var echo = Serializer.Serialize(typeof(object), source);
        if (echo != null && TryWriteFile(absolutePath, echo.WriteToString()))
        {
            _sourceCache.Remove(instanceGO.PrefabAssetId);
            db.Reimport(entry.Guid);
        }

        // Remove this override from the instance (stored on the root)
        goRef.PrefabOverrides.Remove(ov);

        Undo.RegisterAction("Apply Single Override",
            undo: () =>
            {
                // Restore old prefab file
                if (oldFileContent != null) TryWriteFile(absolutePath, oldFileContent);
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

        EditorSceneManager.MarkDirty();
    }

    /// <summary>
    /// Revert a single override load the source value and write it back to the instance field.
    /// </summary>
    public static void RevertSingleOverride(GameObject instanceGO, string overridePath)
    {
        if (!instanceGO.IsPrefabInstance) return;
        if (!GuardNotPlaying("revert a prefab override")) return;

        var source = GetCachedPrefabSource(instanceGO.PrefabAssetId);
        if (source == null) return;

        // Find the source value via the path
        ParseOverridePath(source, overridePath, out var sourceTarget, out string sourceFieldPath);
        if (sourceTarget == null || string.IsNullOrEmpty(sourceFieldPath)) return;

        // Read the source value
        var sourceMember = GetMemberByPath(sourceTarget, sourceFieldPath);
        if (!sourceMember.IsValid) return;

        // Overrides live on the prefab instance root, with root-relative paths, so resolve and
        // mutate against the root rather than whichever GO the inspector happened to pass in.
        var prefabRoot = GetPrefabInstanceRoot(instanceGO);
        var root = prefabRoot.IsValid() ? prefabRoot : instanceGO;

        // Find the instance target
        ParseOverridePath(root, overridePath, out var instanceTarget, out string instanceFieldPath);
        if (instanceTarget == null) return;

        // Capture old instance value for undo
        var oldInstanceValue = GetMemberValue(instanceTarget, instanceFieldPath);
        var oldInstanceEcho = Serializer.Serialize(sourceMember.MemberType, oldInstanceValue);
        var removedOverrides = root.PrefabOverrides.Where(o => o.Path == overridePath).ToList();
        var goRef = root;
        var path = overridePath;

        // Copy source value to instance
        var sourceValue = GetMemberValue(sourceTarget, sourceFieldPath);
        SetMemberValue(instanceTarget, instanceFieldPath, sourceValue);
        if (instanceTarget is MonoBehaviour reverted)
            reverted.HierarchyStateChanged();

        // Remove the override entry
        root.PrefabOverrides.RemoveAll(o => o.Path == overridePath);

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

        EditorSceneManager.MarkDirty();
    }

    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// A field or property addressed by an override path. GameObject-level overrides name properties
    /// (Enabled) while component overrides name fields, and writing through a property setter is what
    /// keeps side effects like enable/disable propagation working.
    /// </summary>
    private readonly struct Member
    {
        private readonly FieldInfo? _field;
        private readonly PropertyInfo? _property;

        private Member(FieldInfo field) { _field = field; _property = null; }
        private Member(PropertyInfo property) { _field = null; _property = property; }

        public bool IsValid => _field != null || _property != null;
        public Type MemberType => _field?.FieldType ?? _property!.PropertyType;
        public object? GetValue(object target) => _field != null ? _field.GetValue(target) : _property!.GetValue(target);

        public void SetValue(object target, object? value)
        {
            if (_field != null) _field.SetValue(target, value);
            else if (_property!.CanWrite) _property.SetValue(target, value);
        }

        public static Member Find(object target, string name)
        {
            var field = target.GetType().GetField(name, InstanceMembers);
            if (field != null) return new Member(field);

            var property = target.GetType().GetProperty(name, InstanceMembers);
            return property != null ? new Member(property) : default;
        }
    }

    private static bool TraverseToParent(object target, string[] parts, out object parent)
    {
        parent = target;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var member = Member.Find(parent, parts[i]);
            if (!member.IsValid) return false;
            var next = member.GetValue(parent);
            if (next == null) return false;
            parent = next;
        }
        return true;
    }

    private static Member GetMemberByPath(object target, string memberPath)
    {
        string[] parts = memberPath.Split('.');
        if (!TraverseToParent(target, parts, out var parent)) return default;
        return Member.Find(parent, parts[^1]);
    }

    private static object? GetMemberValue(object target, string memberPath)
    {
        string[] parts = memberPath.Split('.');
        if (!TraverseToParent(target, parts, out var parent)) return null;
        var member = Member.Find(parent, parts[^1]);
        return member.IsValid ? member.GetValue(parent) : null;
    }

    private static void SetMemberValue(object target, string memberPath, object? value)
    {
        string[] parts = memberPath.Split('.');
        if (!TraverseToParent(target, parts, out var parent)) return;
        Member.Find(parent, parts[^1]).SetValue(parent, value);
    }

    /// <summary>Check if a specific property path is overridden on a GameObject.</summary>
    public static bool IsPropertyOverridden(GameObject go, string path)
    {
        if (!go.IsPrefabInstance) return false;
        // Overrides are stored on the instance root with root-relative paths.
        var root = GetPrefabInstanceRoot(go);
        return (root.IsValid() ? root : go).PrefabOverrides.Any(o => o.Path == path);
    }

    /// <summary>Check if a prefab instance has any overrides at all.</summary>
    public static bool HasAnyOverrides(GameObject go)
    {
        if (!go.IsPrefabInstance) return false;
        var root = GetPrefabInstanceRoot(go);
        return (root.IsValid() ? root : go).PrefabOverrides.Count > 0;
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
            root.Destroy(); // TODO should this be Destroy (deferred) or Dispose?
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
                if (targetObj == null || string.IsNullOrEmpty(fieldPath))
                {
                    // The structure shifted (component/child added, removed or reordered) so this
                    // index-based path no longer resolves. Skip rather than mis-applying the value.
                    // Once per path: every instance of the prefab reports the same broken path.
                    Runtime.Debug.LogWarningOnce($"prefab.path.{ov.Path}",
                        $"[Prefab] Override path '{ov.Path}' no longer resolves on the instance; skipping.");
                    continue;
                }

                // Validate the member still exists on the resolved target before writing, so a path
                // that now points at a different component type doesn't silently land on the wrong field.
                if (!GetMemberByPath(targetObj, fieldPath).IsValid)
                {
                    Runtime.Debug.LogWarningOnce($"prefab.member.{ov.Path}",
                        $"[Prefab] Override '{ov.Path}' has no matching field on the current instance; skipping.");
                    continue;
                }

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
        if (!TraverseToParent(target, parts, out var parent)) return;

        var member = Member.Find(parent, parts[^1]);
        if (!member.IsValid) return;

        var deserialized = Serializer.Deserialize(value, member.MemberType);

        // Null is a valid override for reference fields (e.g. clearing an object reference).
        // Only skip for non-nullable value types where null means deserialization failed.
        bool allowsNull = !member.MemberType.IsValueType || Nullable.GetUnderlyingType(member.MemberType) != null;
        if (deserialized == null && !allowsNull) return;

        member.SetValue(parent, deserialized);

        // Component enabled state is a serialized field, so an override writes it directly and skips
        // the Enabled setter. Re-derive so dispatch registration matches what was just written.
        if (parent is MonoBehaviour behaviour)
            behaviour.HierarchyStateChanged();
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
            // Runs once per drawn component per frame, so this must not log per occurrence.
            Runtime.Debug.LogWarningOnce($"prefab.mismatch.{instanceGO.PrefabAssetId}.{goPath}.{compIndex}",
                $"[Prefab] Component type mismatch at index {compIndex}: instance={instanceComp.GetType().Name}, source={sourceComp.GetType().Name}");
            return;
        }

        // Build path prefix
        string pathPrefix = string.IsNullOrEmpty(goPath)
            ? $"c{compIndex}"
            : $"{goPath}.c{compIndex}";

        // Compare fields. Overrides are stored on the instance root (paths are root-relative) so
        // that apply/revert/refresh - which operate on the root - can see overrides on any child.
        var prefabRoot = GetPrefabInstanceRoot(instanceGO);
        var root = prefabRoot.IsValid() ? prefabRoot : instanceGO;
        CompareFields(instanceComp, sourceComp, pathPrefix, root.PrefabOverrides);
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
        // Stored on the instance root (root-relative paths) so refresh/apply can find child overrides.
        var prefabRoot = GetPrefabInstanceRoot(instanceGO);
        var overrides = (prefabRoot.IsValid() ? prefabRoot : instanceGO).PrefabOverrides;

        // Compare GO-level fields (excluding Name and Transform those are per-instance)
        CompareField(pathPrefix, "TagIndex", instanceGO.TagIndex, sourceGO.TagIndex, overrides);
        CompareField(pathPrefix, "LayerIndex", instanceGO.LayerIndex, sourceGO.LayerIndex, overrides);
        CompareField(pathPrefix, "Enabled", instanceGO.Enabled, sourceGO.Enabled, overrides);
        // Name and Transform (Position/Rotation/Scale) are intentionally NOT tracked -
        // they are per-instance values that don't constitute overrides.
    }

    // Serialized state that must never become a per-instance override.
    // This list is load-bearing: the field set below is Echo's, so anything Echo persists and that is
    // not named here is comparable. _identifier especially - identifiers are regenerated on every
    // deserialization, so instance and source always differ and every component would record one.
    private static readonly HashSet<string> _skipFields = new()
    {
        "_identifier",          // regenerated per load; never per-instance state
        "_enabledInHierarchy",  // derived from _enabled and the parent chain
        "_go",                  // back-reference to the owning GameObject
        "_hasStarted", "_hasBeenEnabled", "_executeAlwaysCached", // runtime lifecycle bookkeeping
        "HideFlags",            // editor presentation, not content
        "AssetID", "AssetPath"  // asset identity, meaningless on a scene component
    };

    // Keyed by concrete type; the field set never changes for a type within a session.
    private static readonly Dictionary<Type, FieldInfo[]> _overridableFields = new();

    /// <summary>
    /// The fields an override may address: exactly what Echo persists, minus engine bookkeeping.
    /// </summary>
    private static FieldInfo[] GetOverridableFields(object instance)
    {
        Type type = instance.GetType();
        if (_overridableFields.TryGetValue(type, out var cached)) return cached;

        var fields = instance.GetSerializableFields()
            .Where(f => !_skipFields.Contains(f.Name))
            .ToArray();

        _overridableFields[type] = fields;
        return fields;
    }

    private static void CompareFields(object instance, object source, string pathPrefix, List<PropertyOverride> overrides)
    {
        foreach (var field in GetOverridableFields(instance))
        {
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
        if (prefab.IsNotValid() || prefab.GameObjectData == null) return null;

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

    /// <summary>
    /// Convert every object that belonged to <paramref name="boundaryId"/> into an instance of
    /// <paramref name="prefabGuid"/>. Nested instances of other prefabs are left alone.
    /// </summary>
    private static void StampAsPrefabInstance(GameObject go, Guid prefabGuid, Guid boundaryId)
    {
        if (go.PrefabAssetId != boundaryId) return;

        go.PrefabAssetId = prefabGuid;
        go.PrefabOverrides.Clear();
        go.PrefabComponentCount = go.GetComponents<MonoBehaviour>().Count();
        go.PrefabChildCount = go.Children.Count;

        foreach (var child in go.Children)
            StampAsPrefabInstance(child, prefabGuid, boundaryId);
    }

    /// <summary>Snapshot the prefab tracking data of every object within a boundary, for undo.</summary>
    private static List<(GameObject go, Guid assetId, List<PropertyOverride> overrides, int compCount, int childCount)>
        CapturePrefabState(GameObject root, Guid boundaryId)
    {
        var captured = new List<(GameObject, Guid, List<PropertyOverride>, int, int)>();
        Walk(root);
        return captured;

        void Walk(GameObject go)
        {
            if (go.PrefabAssetId != boundaryId) return;
            captured.Add((go, go.PrefabAssetId, go.PrefabOverrides.ToList(), go.PrefabComponentCount, go.PrefabChildCount));
            foreach (var child in go.Children)
                Walk(child);
        }
    }

    private static void RestorePrefabState(
        List<(GameObject go, Guid assetId, List<PropertyOverride> overrides, int compCount, int childCount)> captured)
    {
        foreach (var (go, assetId, overrides, compCount, childCount) in captured)
        {
            if (go.IsNotValid()) continue;
            go.PrefabAssetId = assetId;
            go.PrefabOverrides = overrides.ToList();
            go.PrefabComponentCount = compCount;
            go.PrefabChildCount = childCount;
        }
    }

    /// <summary>Copy the prefab source's name and root transform onto a tree about to overwrite it.</summary>
    private static void PreserveSourceIdentity(GameObject cleanCopy, Guid prefabGuid)
    {
        var source = GetCachedPrefabSource(prefabGuid);
        if (source == null) return;

        cleanCopy.Name = source.Name;
        cleanCopy.Transform.LocalPosition = source.Transform.LocalPosition;
        cleanCopy.Transform.LocalRotation = source.Transform.LocalRotation;
        cleanCopy.Transform.LocalScale = source.Transform.LocalScale;
    }

    private static bool GuardNotPlaying(string operation)
    {
        if (!Application.IsPlaying) return true;
        Runtime.Debug.LogWarning($"[Prefab] Cannot {operation} during play mode.");
        return false;
    }

    private static bool TryWriteFile(string absolutePath, string contents)
    {
        try
        {
            File.WriteAllText(absolutePath, contents);
            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[Prefab] Failed to write '{absolutePath}': {ex.Message}");
            return false;
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

    /// <summary>
    /// Clear prefab tracking data on every object belonging to <paramref name="boundaryPrefabId"/>,
    /// stopping at nested instances of other prefabs so their links survive.
    /// </summary>
    internal static void StripPrefabDataWithinBoundary(GameObject go, Guid boundaryPrefabId)
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
