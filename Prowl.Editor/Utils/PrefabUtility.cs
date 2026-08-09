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

        var echo = Serializer.Serialize(typeof(object), cleanCopy, TreeValueContext(cleanCopy));
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

    /// <summary>
    /// Whether a prefab asset can be written back to. A .prefab file is authored, so applying to it
    /// is the whole point; a prefab imported from another format (a model) is generated from its
    /// source file, and writing a serialized hierarchy over that .fbx or .obj would destroy it.
    /// Instances of an imported prefab still track and revert overrides, they just cannot apply.
    /// </summary>
    public static bool IsEditablePrefab(Guid prefabGuid)
    {
        var entry = EditorAssetBackend.Instance?.GetEntry(prefabGuid);
        return entry != null && entry.Path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    private static bool GuardEditablePrefab(Guid prefabGuid, string operation)
    {
        if (IsEditablePrefab(prefabGuid)) return true;

        var entry = EditorAssetBackend.Instance?.GetEntry(prefabGuid);
        Runtime.Debug.LogWarning($"[Prefab] Cannot {operation}: '{entry?.Path ?? prefabGuid.ToString()}' is " +
            "imported from its source file. Reimport it to change it, or unpack the instance.");
        return false;
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

        if (!GuardEditablePrefab(instanceRoot.PrefabAssetId, "apply prefab overrides")) return;

        ApplyOverridesCore(instanceRoot, recordUndo: true);
    }

    private static void ApplyOverridesCore(GameObject instanceRoot, bool recordUndo)
    {
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
        // Keyed by identifier, not by reference: the refresh below replaces this very object, so a
        // captured reference would be dead by the time undo or redo runs.
        var rootId = instanceRoot.Identifier;

        // Serialize the instance tree with prefab data stripped
        var cleanCopy = CloneWithoutPrefabData(instanceRoot);
        if (cleanCopy == null) return;

        // Name and root transform are per-instance, not prefab content, so keep the asset's own.
        PreserveSourceIdentity(cleanCopy, instanceRoot.PrefabAssetId);

        var echo = Serializer.Serialize(typeof(object), cleanCopy, TreeValueContext(cleanCopy));
        if (echo == null) return;

        // Write to the .prefab file
        if (!TryWriteFile(absolutePath, echo.WriteToString())) return;

        // Clear overrides on this instance
        ClearOverridesWithinBoundary(instanceRoot, instanceRoot.PrefabAssetId);

        // Reimport and refresh invalidate source cache first
        _sourceCache.Remove(prefabGuid);
        db.Reimport(entry.Guid);

        if (recordUndo)
        {
            Undo.RegisterAction("Apply Prefab Overrides",
                undo: () =>
                {
                    // Restore old prefab file
                    if (oldFileContent != null) TryWriteFile(absolutePath, oldFileContent);
                    _sourceCache.Remove(prefabGuid);
                    db.Reimport(entry.Guid);
                    // Put the overrides back on the live instance before refreshing, since the
                    // refresh is what re-applies them to the rebuilt objects.
                    var live = Undo.FindGO(rootId);
                    if (live.IsValid()) live!.PrefabOverrides = oldOverrides.ToList();
                    RefreshAllInstances(prefabGuid);
                },
                redo: () =>
                {
                    var live = Undo.FindGO(rootId);
                    if (live.IsValid()) ApplyOverridesCore(live!, recordUndo: false);
                });
        }

        RefreshAllInstances(prefabGuid);

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

        RevertOverridesCore(instanceRoot, recordUndo: true);
    }

    private static void RevertOverridesCore(GameObject instanceRoot, bool recordUndo)
    {
        var prefab = AssetDatabase.Get(instanceRoot.PrefabAssetId) as PrefabAsset;
        if (prefab == null)
        {
            Runtime.Debug.LogWarning("[Prefab] Cannot revert prefab asset not found.");
            return;
        }

        // Nothing to swap into, so leave the instance alone rather than reporting success and
        // selecting a replacement that is in no scene.
        if (instanceRoot.Scene == null)
        {
            Runtime.Debug.LogWarning("[Prefab] Cannot revert an instance that is not in a scene.");
            return;
        }

        // Capture old state for undo. The tree itself is written by value; references out of it are
        // linked, not cloned into the snapshot.
        var oldSerialized = Serializer.Serialize(typeof(object), instanceRoot, TreeValueContext(instanceRoot));
        var siblingIdx = instanceRoot.Parent != null ? instanceRoot.Parent.Children.IndexOf(instanceRoot) : -1;
        var prefabGuid = instanceRoot.PrefabAssetId;

        // Instantiate fresh from prefab
        var fresh = prefab.Instantiate();
        if (fresh == null) return;

        // Preserve identifiers so undo records stay valid
        CopyInstanceState(instanceRoot, fresh);

        fresh.Transform.Position = instanceRoot.Transform.Position;
        fresh.Transform.Rotation = instanceRoot.Transform.Rotation;
        fresh.Transform.LocalScale = instanceRoot.Transform.LocalScale;
        fresh.Name = instanceRoot.Name;

        var scene = instanceRoot.Scene!;
        var parent = instanceRoot.Parent;
        var rootIdx = parent == null ? scene.GetRootIndex(instanceRoot) : -1;

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

        // CopyInstanceState gave the replacement the old object's identifier, so one key addresses the
        // instance across every swap either direction.
        var rootId = fresh.Identifier;

        if (recordUndo)
        {
            Undo.RegisterAction("Revert Prefab Overrides",
                undo: () =>
                {
                    var live = Undo.FindGO(rootId);
                    if (live.IsNotValid()) return;

                    var restored = Serializer.Deserialize<GameObject>(oldSerialized, InstanceValueContext());
                    if (restored == null) return;
                    Undo.RestoreIdentifiers(restored, oldSerialized);

                    SwapInPlace(live!, restored);
                    Selection.Select(restored);
                },
                redo: () =>
                {
                    var live = Undo.FindGO(rootId);
                    if (live.IsValid()) RevertOverridesCore(live!, recordUndo: false);
                });
        }

        Selection.Select(fresh);
        EditorSceneManager.MarkDirty();
    }

    /// <summary>
    /// Put <paramref name="replacement"/> where <paramref name="existing"/> is, keeping its parent,
    /// sibling order and root order, and destroy the object it replaces.
    /// </summary>
    private static void SwapInPlace(GameObject existing, GameObject replacement)
    {
        var scene = existing.Scene;
        if (scene == null) return;

        var parent = existing.Parent;
        int siblingIdx = existing.GetSiblingIndex() ?? -1;
        int rootIdx = parent == null ? scene.GetRootIndex(existing) : -1;

        scene.Remove(existing);
        existing.Destroy();
        scene.Add(replacement);

        if (parent != null)
        {
            replacement.SetParent(parent);
            if (siblingIdx >= 0) replacement.SetSiblingIndex(siblingIdx);
        }
        else if (rootIdx >= 0)
        {
            scene.SetRootIndex(replacement, rootIdx);
        }
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

        if (!GuardEditablePrefab(instanceGO.PrefabAssetId, "apply a prefab override")) return;

        ApplySingleOverrideCore(instanceGO, ov, recordUndo: true);
    }

    private static void ApplySingleOverrideCore(GameObject instanceGO, PropertyOverride ov, bool recordUndo)
    {
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
        // Overrides live on the prefab instance root, and the refresh below replaces that object, so
        // undo and redo address it by identifier rather than holding on to it.
        var instanceRoot = GetPrefabInstanceRoot(instanceGO);
        var goRef = instanceRoot.IsValid() ? instanceRoot! : instanceGO;
        var rootId = goRef.Identifier;

        var source = Serializer.Deserialize<GameObject>(prefab.GameObjectData, InstanceValueContext());
        if (source == null) return;

        // Apply the override value to the source. A scene reference resolves here and is linked, not
        // copied, when the source is written back below.
        ParseOverridePath(source, ov.Path, out var target, out string fieldPath);
        if (target != null && !string.IsNullOrEmpty(fieldPath))
            ApplyFieldValue(target, fieldPath, ov.Value);

        // Save back to the .prefab file
        var echo = Serializer.Serialize(typeof(object), source, TreeValueContext(source));
        if (echo != null && TryWriteFile(absolutePath, echo.WriteToString()))
        {
            _sourceCache.Remove(instanceGO.PrefabAssetId);
            db.Reimport(entry.Guid);
        }

        // Remove this override from the instance (stored on the root)
        goRef.PrefabOverrides.Remove(ov);

        if (recordUndo)
        {
            Undo.RegisterAction("Apply Single Override",
                undo: () =>
                {
                    // Restore old prefab file
                    if (oldFileContent != null) TryWriteFile(absolutePath, oldFileContent);
                    _sourceCache.Remove(prefabGuid);
                    db.Reimport(entry.Guid);
                    // Re-add the override to the instance
                    var live = Undo.FindGO(rootId);
                    if (live.IsValid()) live!.PrefabOverrides.Add(new PropertyOverride { Path = ovPath, Value = ovValue });
                    RefreshAllInstances(prefabGuid);
                },
                redo: () =>
                {
                    var live = Undo.FindGO(rootId);
                    if (live.IsValid())
                        ApplySingleOverrideCore(live!, new PropertyOverride { Path = ovPath, Value = ovValue }, recordUndo: false);
                });
        }

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
        var oldInstanceEcho = Serializer.Serialize(sourceMember.MemberType, oldInstanceValue, InstanceValueContext());
        var removedOverrides = root.PrefabOverrides.Where(o => o.Path == overridePath).ToList();
        // A later refresh replaces the instance, so address it by identifier rather than holding it.
        var rootId = root!.Identifier;
        var path = overridePath;

        // Copy source value to instance
        var sourceValue = GetMemberValue(sourceTarget, sourceFieldPath);
        SetMemberValue(instanceTarget, instanceFieldPath, sourceValue);
        if (instanceTarget is MonoBehaviour reverted)
        {
            reverted.HierarchyStateChanged();
            reverted.OnValidate();
        }

        // Remove the override entry
        root.PrefabOverrides.RemoveAll(o => o.Path == overridePath);

        Undo.RegisterAction("Revert Single Override",
            undo: () =>
            {
                var live = Undo.FindGO(rootId);
                if (live.IsNotValid()) return;

                // Restore old instance value
                ParseOverridePath(live!, path, out var undoTarget, out string undoFieldPath);
                if (undoTarget != null && oldInstanceEcho != null)
                    ApplyFieldValue(undoTarget, undoFieldPath, oldInstanceEcho);
                // Re-add removed overrides
                live!.PrefabOverrides.AddRange(removedOverrides);
            },
            redo: () =>
            {
                var live = Undo.FindGO(rootId);
                if (live.IsValid()) RevertSingleOverride(live!, path);
            });

        EditorSceneManager.MarkDirty();
    }

    // ================================================================
    //  Scene reference linking
    // ================================================================

    // An override value is serialized on its own, detached from any scene graph, so Echo cannot tell
    // that a GameObject/component field points at another scene object rather than at content to
    // copy. Left alone it deep-copies the target into the override blob, and applying that later
    // produces an orphan clone that is in no scene. Keying the reference by identifier instead lets
    // it re-link to the live object, the same way the component clipboard does.
    private static SerializationContext InstanceValueContext()
        => new() { ExternalReferences = new SceneReferenceResolver() };

    /// <summary>
    /// Context for serializing a whole GameObject tree: everything in the tree is listed as
    /// copied-by-value, so anything else it references is external and gets linked by identifier
    /// rather than deep-copied in. Used for writing .prefab files and for undo snapshots.
    /// <para/>
    /// Note this is not interchangeable with <see cref="InstanceValueContext"/>, which lists nothing
    /// as copied: passing that here would link the tree's own root instead of writing it out.
    /// </summary>
    internal static SerializationContext TreeValueContext(GameObject treeRoot)
        => new() { ExternalReferences = new SceneReferenceResolver(CollectTreeObjects(treeRoot)) };

    /// <summary>Every object a GameObject tree is made of: the objects, their transforms and their
    /// components. All of them must count as copied, or they would serialize as links.</summary>
    private static object[] CollectTreeObjects(GameObject root)
    {
        var objects = new List<object>();
        Collect(root);
        return objects.ToArray();

        void Collect(GameObject go)
        {
            objects.Add(go);
            objects.Add(go.Transform);
            foreach (var comp in go.GetComponents<MonoBehaviour>())
                objects.Add(comp);
            foreach (var child in go.Children)
                Collect(child);
        }
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

    /// <summary>
    /// Whether an override path still addresses something on this instance. Index-based paths stop
    /// resolving when the structure drifts (a component or child added, removed or reordered), and
    /// such an entry can no longer be applied or reverted - only removed.
    /// </summary>
    public static bool IsOverrideResolvable(GameObject instanceGO, string overridePath)
    {
        if (!instanceGO.IsPrefabInstance) return false;

        var prefabRoot = GetPrefabInstanceRoot(instanceGO);
        var root = prefabRoot.IsValid() ? prefabRoot! : instanceGO;

        ParseOverridePath(root, overridePath, out var target, out string fieldPath);
        if (target == null || string.IsNullOrEmpty(fieldPath)) return false;

        return GetMemberByPath(target, fieldPath).IsValid;
    }

    /// <summary>
    /// Drop an override entry without touching the instance or the asset. For entries that no longer
    /// resolve, this is the only way to get rid of them: revert and apply both bail on the same
    /// unresolvable path, so the instance would otherwise read as permanently modified.
    /// </summary>
    public static void RemoveOverride(GameObject instanceGO, string overridePath)
    {
        if (!instanceGO.IsPrefabInstance) return;
        if (!GuardNotPlaying("remove a prefab override")) return;

        var prefabRoot = GetPrefabInstanceRoot(instanceGO);
        var root = prefabRoot.IsValid() ? prefabRoot! : instanceGO;

        var removed = root.PrefabOverrides.Where(o => o.Path == overridePath).ToList();
        if (removed.Count == 0) return;

        var rootId = root.Identifier;
        Undo.RegisterAction("Remove Prefab Override",
            undo: () =>
            {
                var live = Undo.FindGO(rootId);
                if (live.IsValid()) live!.PrefabOverrides.AddRange(removed);
            },
            redo: () =>
            {
                var live = Undo.FindGO(rootId);
                if (live.IsValid()) live!.PrefabOverrides.RemoveAll(o => o.Path == overridePath);
            });

        root.PrefabOverrides.RemoveAll(o => o.Path == overridePath);
        EditorSceneManager.MarkDirty();
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

        // The whole selection is remapped, not just the first entry: it can hold several instances,
        // and children of them as well as the roots. GameObjects are noted by identifier because the
        // objects themselves are about to be replaced; anything else is kept as-is.
        var selectionSnapshot = Selection.Selected
            .Select(o => o is GameObject go ? new SelectedGameObject(go.Identifier) : o)
            .ToList();
        bool remapSelection = Selection.Selected.Any(o => o is GameObject);

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
            CopyInstanceState(root, fresh);

            fresh.PrefabOverrides = savedOverrides;
            fresh.Name = savedName;
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

            // Applied last, once the old instance is out of the scene and the fresh one is in it with
            // the same identifiers: overrides holding scene references resolve by identifier, so they
            // would otherwise bind to the instance being replaced.
            ApplyPropertyOverridesToInstance(fresh, savedOverrides);
        }

        if (roots.Count > 0 && remapSelection)
        {
            Selection.Clear();
            foreach (var entry in selectionSnapshot)
            {
                if (entry is SelectedGameObject selected)
                {
                    var live = Undo.FindGO(selected.Identifier);
                    if (live.IsValid()) Selection.AddToSelection(live!);
                }
                else
                {
                    Selection.AddToSelection(entry);
                }
            }
        }
    }

    /// <summary>A GameObject held in the selection, noted by identifier so it survives a refresh.
    /// A distinct type rather than a bare Guid, so it cannot be confused with a selected asset.</summary>
    private readonly record struct SelectedGameObject(Guid Identifier);

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
        // Collected rather than validated per field, so a component with several overridden fields
        // rebuilds its derived state once, after all of them have been written.
        var touched = new HashSet<MonoBehaviour>();

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
                if (targetObj is MonoBehaviour behaviour)
                    touched.Add(behaviour);
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"[Prefab] Failed to apply override '{ov.Path}': {ex.Message}");
            }
        }

        // An override writes fields directly, so components that derive state from them (colliders
        // rebuilding their shapes, renderers rebuilding caches) would otherwise keep whatever the
        // prefab source had until something else happened to touch them.
        foreach (var behaviour in touched)
        {
            try
            {
                behaviour.OnValidate();
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"[Prefab] OnValidate threw on {behaviour.GetType().Name}: {ex.Message}");
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

        var deserialized = Serializer.Deserialize(value, member.MemberType, InstanceValueContext());

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
        CompareField(pathPrefix, "IsStatic", instanceGO.IsStatic, sourceGO.IsStatic, overrides);
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

            // A context per side, deliberately: Echo numbers object references as it goes, so sharing
            // one would give the second value different ids and make equal content compare unequal.
            var instanceEcho = Serializer.Serialize(field.FieldType, instanceVal, InstanceValueContext());
            var sourceEcho = Serializer.Serialize(field.FieldType, sourceVal, InstanceValueContext());

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
            var serialized = Serializer.Serialize(typeof(T), instanceVal, InstanceValueContext());
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

    // Cache the deserialized prefab source for comparison (per prefab GUID), for the current frame
    // only, so an edited or reimported prefab is picked up on the next one.
    private static readonly Dictionary<Guid, (GameObject go, long frame)> _sourceCache = new();

    private static GameObject? GetCachedPrefabSource(Guid prefabGuid)
    {
        long frame = Runtime.Time.FrameCount;

        if (_sourceCache.TryGetValue(prefabGuid, out var cached) && cached.frame == frame)
            return cached.go;

        var prefab = Runtime.AssetDatabase.Get(prefabGuid) as PrefabAsset;
        if (prefab.IsNotValid() || prefab.GameObjectData == null) return null;

        // Deserialized the same way an instance is, so the comparison baseline matches what
        // Instantiate would actually produce.
        var source = Serializer.Deserialize<GameObject>(prefab.GameObjectData,
            new SerializationContext { ExternalReferences = SceneReferenceResolver.None });
        if (source != null)
            _sourceCache[prefabGuid] = (source, frame);

        return source;
    }

    /// <summary>
    /// Carry per-instance state from an old GO tree onto a fresh one (matched by structure index).
    /// Identifiers keep undo records and selection valid; hide flags are editor bookkeeping that the
    /// override system deliberately never tracks, so a refresh would otherwise reset them.
    /// </summary>
    private static void CopyInstanceState(GameObject oldGO, GameObject freshGO)
    {
        freshGO.SetIdentifier(oldGO.Identifier);
        freshGO.HideFlags = oldGO.HideFlags;

        var oldComps = oldGO.GetComponents().ToArray();
        var freshComps = freshGO.GetComponents().ToArray();
        for (int i = 0; i < Math.Min(oldComps.Length, freshComps.Length); i++)
        {
            freshComps[i].Identifier = oldComps[i].Identifier;
            freshComps[i].HideFlags = oldComps[i].HideFlags;
        }

        for (int i = 0; i < Math.Min(oldGO.Children.Count, freshGO.Children.Count); i++)
            CopyInstanceState(oldGO.Children[i], freshGO.Children[i]);
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
        // Serialize the source. Objects outside this tree are scene references, so they are linked
        // rather than copied into the clone (and from there into the asset).
        var savedId = source.AssetID;
        source.AssetID = Guid.Empty;
        var echo = Serializer.Serialize(typeof(object), source, TreeValueContext(source));
        source.AssetID = savedId;
        if (echo == null) return null;

        // Deserialize a clean copy, re-linking those references to the live scene objects.
        var clone = Serializer.Deserialize<GameObject>(echo, InstanceValueContext());
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
