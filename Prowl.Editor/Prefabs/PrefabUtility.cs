using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Echo.Cloning;
using Prowl.Vector;
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
public static partial class PrefabUtility
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
    public static bool SaveAsPrefabAssetAndConnect(GameObject source, string relativeSavePath, bool overwrite = false)
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

        FlattenNestedPrefabs(cleanCopy);
        StabilizeSourceIdentifiers(cleanCopy);

        var writeContext = TreeValueContext(cleanCopy);
        var echo = Serializer.Serialize(typeof(object), cleanCopy, writeContext);
        if (echo == null) return false;

        ReportDroppedSceneReferences(writeContext, "Creating this prefab");

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        if (!TryWriteFile(absolutePath, echo.WriteToString())) return false;

        // Ensure meta file exists so asset DB picks it up with a stable GUID.
        var meta = MetaFile.EnsureMeta(absolutePath, nameof(Importers.PrefabImporter));
        if (meta.Guid == Guid.Empty) return false;

        RaisePrefabSaved(meta.Guid);

        // Stamp the source GO as an instance of the new prefab, with the identities it was written
        // out under. Undo restores the previous prefab links; the created asset is left on disk.
        var boundary = source.PrefabAssetId;
        var previous = CapturePrefabState(source, boundary);
        StampAsPrefabInstance(source, cleanCopy, meta.Guid, boundary);
        var stamped = CapturePrefabState(source, meta.Guid);

        Undo.RegisterAction("Create Prefab",
            undo: () => RestorePrefabState(previous),
            redo: () => RestorePrefabState(stamped));

        EditorSceneManager.MarkDirty();
        Runtime.Debug.Log($"[Prefab] Created prefab: {relativeSavePath}");

        return true;
    }

    /// <summary>
    /// Break the link on every prefab instance inside a tree that is about to become a prefab asset,
    /// so its objects become ordinary content of that asset.
    /// <para/>
    /// Prefabs do not nest. An asset is one self-contained tree, which is what lets a reference across
    /// what would have been a nesting boundary resolve, and what stops an asset going stale against
    /// another one it embeds. See Design/PrefabAudit.md for why this is a decision rather than a gap.
    /// </summary>
    private static void FlattenNestedPrefabs(GameObject root)
    {
        foreach (GameObject child in root.Children)
        {
            // Instance data within the boundary has already been cleared, so anything still carrying an
            // asset id is an instance of some other prefab.
            if (child.PrefabAssetId != Guid.Empty)
            {
                child.ClearPrefabDataRecursive();
                continue;
            }

            FlattenNestedPrefabs(child);
        }
    }

    /// <summary>
    /// Pin a tree's identifiers to the ones it will be written out with, so a prefab's objects keep
    /// the same identity every time the asset is saved. Overrides are matched to the source by these
    /// identifiers, so churning them on each save would orphan every override in every scene.
    /// <para/>
    /// An object that came from this prefab already knows its source identifier and adopts it; one
    /// added since is new content, and its current identifier becomes the stable one.
    /// </summary>
    internal static void StabilizeSourceIdentifiers(GameObject root)
    {
        var link = root.EnsurePrefabLink();

        if (link.SourceIdentifier == Guid.Empty)
            link.SourceIdentifier = root.Identifier;
        else
            root.SetIdentifier(link.SourceIdentifier);

        // Adopting a source identifier changes the key the map is stored under, so it is rebuilt
        // rather than patched. Afterwards each component's identifier is its source identifier.
        var previous = new Dictionary<Guid, Guid>(link.ComponentSources);
        link.ComponentSources.Clear();

        foreach (var component in root.GetComponents<MonoBehaviour>())
        {
            Guid sourceId = previous.TryGetValue(component.Identifier, out var known) ? known : component.Identifier;
            component.Identifier = sourceId;
            link.ComponentSources[sourceId] = sourceId;
        }

        foreach (var child in root.Children)
            StabilizeSourceIdentifiers(child);
    }

    /// <summary>
    /// Whether a prefab asset can be written back to. An authored prefab is the thing Apply exists
    /// for; a generated one (a model) is rebuilt on every import, so a write would either be
    /// discarded or land on the source file and destroy it. Instances of a generated prefab still
    /// track and revert overrides, they just cannot apply them.
    /// </summary>
    public static bool IsEditablePrefab(Guid prefabGuid)
        => AssetDatabase.Get(prefabGuid) is PrefabAsset { IsReadOnly: false };

    private static bool GuardEditablePrefab(Guid prefabGuid, string operation)
    {
        if (IsEditablePrefab(prefabGuid)) return true;

        string name = EditorAssetBackend.Instance?.GetEntry(prefabGuid)?.Path ?? prefabGuid.ToString();
        Runtime.Debug.LogWarning(AssetDatabase.Get(prefabGuid) is PrefabAsset
            ? $"[Prefab] Cannot {operation}: '{name}' is generated from its source file. " +
              "Change it there and reimport, or unpack the instance."
            : $"[Prefab] Cannot {operation}: prefab asset '{name}' could not be loaded.");
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

        var instance = GameObject.InstantiateDetached(prefab);
        if (instance != null) RaiseInstantiated(instance);
        return instance;
    }

    // ================================================================
    //  Break
    // ================================================================

    /// <summary>
    /// Break a prefab instance removes the link to its prefab asset.
    /// The GameObject becomes a plain non-prefab object, but nested prefab instances inside it keep
    /// their own links (breaking the outermost instance only).
    /// </summary>
    public static void UnpackPrefabInstance(GameObject go)
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
        _refreshingFromApply = true;
        try { ApplyOverridesCoreInner(instanceRoot, recordUndo); }
        finally { _refreshingFromApply = false; }
    }

    private static void ApplyOverridesCoreInner(GameObject instanceRoot, bool recordUndo)
    {
        // Pick up anything edited without the inspector noticing, so applying does not quietly drop it.
        ReconcileInstance(instanceRoot);

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

        // What the instance added is the instance's, not the prefab's. Writing it into the asset would
        // hand it to every other instance, and it was never listed as an override for the user to see.
        StripInstanceAdditions(cleanCopy, instanceRoot, GetCachedPrefabSource(instanceRoot.PrefabAssetId));

        FlattenNestedPrefabs(cleanCopy);

        // Name and root transform are per-instance, not prefab content, so keep the asset's own.
        PreserveSourceIdentity(cleanCopy, instanceRoot.PrefabAssetId);
        StabilizeSourceIdentifiers(cleanCopy);

        var writeContext = TreeValueContext(cleanCopy);
        var echo = Serializer.Serialize(typeof(object), cleanCopy, writeContext);
        if (echo == null) return;

        ReportDroppedSceneReferences(writeContext, "Applying these overrides");

        // Write to the .prefab file
        if (!TryWriteFile(absolutePath, echo.WriteToString())) return;

        RaisePrefabSaved(prefabGuid);

        // Clear overrides on this instance
        ClearOverridesWithinBoundary(instanceRoot, instanceRoot.PrefabAssetId);

        // Reimport and refresh invalidate source cache first
        InvalidateSource(prefabGuid);
        db.Reimport(entry.Guid);

        if (recordUndo)
        {
            Undo.RegisterAction("Apply Prefab Overrides",
                undo: () =>
                {
                    // Restore old prefab file
                    if (oldFileContent != null) TryWriteFile(absolutePath, oldFileContent);
                    InvalidateSource(prefabGuid);
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
        var rootId = instanceRoot.Identifier;

        var source = GameObject.InstantiateDetached(prefab);
        if (source == null) return;

        // Reverting brings the instance back into line with the prefab and drops what made it differ.
        // The objects themselves stay, so anything holding a reference to them still does.
        ReconcileToSource(instanceRoot, source, instanceRoot.PrefabAssetId);
        instanceRoot.PrefabOverrides.Clear();

        if (recordUndo)
        {
            Undo.RegisterAction("Revert Prefab Overrides",
                undo: () =>
                {
                    var live = Undo.FindGO(rootId);
                    if (live.IsNotValid()) return;

                    // Preserving identifiers: this replaces the instance, so it has to come back as
                    // the same object or every record addressing it stops resolving.
                    var restored = GameObject.DeserializePreservingIdentifiers(oldSerialized, InstanceValueContext());
                    if (restored == null) return;

                    SwapInPlace(live!, restored);
                    Selection.Select(restored);
                },
                redo: () =>
                {
                    var live = Undo.FindGO(rootId);
                    if (live.IsValid()) RevertOverridesCore(live!, recordUndo: false);
                });
        }

        Selection.Select(instanceRoot);
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
    /// Push one of an instance's overrides into the prefab, addressed the same way
    /// <see cref="RevertSingleOverride"/> addresses it.
    /// </summary>
    public static void ApplySingleOverride(GameObject instanceGO, string overridePath)
    {
        if (!instanceGO.IsPrefabInstance) return;
        if (!GuardNotPlaying("apply a prefab override")) return;

        if (!GuardEditablePrefab(instanceGO.PrefabAssetId, "apply a prefab override")) return;

        GameObject? instanceRoot = GetPrefabInstanceRoot(instanceGO);
        GameObject overrideHost = instanceRoot.IsValid() ? instanceRoot! : instanceGO;

        PropertyOverride? ov = overrideHost.PrefabOverrides.FirstOrDefault(o => o.Path == overridePath);

        if (ov == null)
        {
            Runtime.Debug.LogWarning($"[Prefab] No override '{overridePath}' on this instance to apply.");
            return;
        }

        ApplySingleOverrideCore(instanceGO, ov, recordUndo: true);
    }

    private static void ApplySingleOverrideCore(GameObject instanceGO, PropertyOverride ov, bool recordUndo)
    {
        _refreshingFromApply = true;
        try { ApplySingleOverrideCoreInner(instanceGO, ov, recordUndo); }
        finally { _refreshingFromApply = false; }
    }

    private static void ApplySingleOverrideCoreInner(GameObject instanceGO, PropertyOverride ov, bool recordUndo)
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

        // Built like an instance so its objects carry the source identifiers the override path is
        // written in terms of; the instance markings are taken back off before it is written out.
        var source = GameObject.InstantiateDetached(prefab);
        if (source == null) return;

        // Apply the override value to the source. A scene reference resolves here and is linked, not
        // copied, when the source is written back below.
        ParseOverridePath(source, ov.Path, out var target, out string fieldPath);
        if (target != null && !string.IsNullOrEmpty(fieldPath))
            ApplyFieldValue(target, fieldPath, ov.Value);

        // Save back to the .prefab file
        StripInstanceDataForEditing(source, prefabGuid);
        StabilizeSourceIdentifiers(source);
        var writeContext = TreeValueContext(source);
        var echo = Serializer.Serialize(typeof(object), source, writeContext);
        ReportDroppedSceneReferences(writeContext, "Applying this override");

        if (echo != null && TryWriteFile(absolutePath, echo.WriteToString()))
        {
            InvalidateSource(instanceGO.PrefabAssetId);
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
                    InvalidateSource(prefabGuid);
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

        RevertSingleOverrideCore(instanceGO, overridePath, recordUndo: true);
    }

    private static void RevertSingleOverrideCore(GameObject instanceGO, string overridePath, bool recordUndo)
    {
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

        if (recordUndo)
        {
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
                    // Through the core, or redoing would push a fresh undo entry every time.
                    var live = Undo.FindGO(rootId);
                    if (live.IsValid()) RevertSingleOverrideCore(live!, path, recordUndo: false);
                });
        }

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
    /// Context for serializing a whole component: it is listed as copied-by-value, so what it holds
    /// is written out and what it points at elsewhere in the scene is linked.
    /// <para/>
    /// Not <see cref="InstanceValueContext"/>, which lists nothing as copied and would write the
    /// component itself out as a link to itself - an echo that restores nothing at all.
    /// </summary>
    private static SerializationContext ComponentValueContext(MonoBehaviour component)
        => new() { ExternalReferences = new SceneReferenceResolver(component) };

    /// <summary>
    /// Context for serializing a whole GameObject tree: everything in the tree is listed as
    /// copied-by-value, so anything else it references is external and gets linked by identifier
    /// rather than deep-copied in. Used for writing .prefab files and for undo snapshots.
    /// <para/>
    /// Note this is not interchangeable with <see cref="InstanceValueContext"/>, which lists nothing
    /// as copied: passing that here would link the tree's own root instead of writing it out.
    /// </summary>
    internal static SerializationContext TreeValueContext(GameObject treeRoot)
        => new() { ExternalReferences = SceneReferenceResolver.ForTree(treeRoot) };

    /// <summary>
    /// Say what a write to a prefab asset just dropped. A prefab cannot hold a reference to a scene
    /// object, so one gets written as a link that reads back as null. That is the right thing to
    /// store and the wrong thing to do quietly: the author wired a reference up, pressed a button
    /// named Apply, and got a field that is empty in every instance from then on.
    /// </summary>
    internal static void ReportDroppedSceneReferences(SerializationContext context, string operation)
    {
        if (context.ExternalReferences is not SceneReferenceResolver resolver || resolver.LinkedOut.Count == 0)
            return;

        var names = resolver.LinkedOut
            .Select(o => o switch
            {
                GameObject go => go.Name,
                MonoBehaviour mb => $"{mb.GameObject.Name} > {mb.GetType().Name}",
                Transform t => t.GameObject.IsValid() ? $"{t.GameObject.Name} > Transform" : "Transform",
                _ => o.GetType().Name
            })
            .Distinct()
            .ToList();

        Runtime.Debug.LogWarning($"[Prefab] {operation} dropped {names.Count} reference(s) to objects outside the prefab, " +
            $"which a prefab cannot hold and which will read back as empty: {string.Join(", ", names)}");
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
    // ================================================================
    //  What the instance added
    // ================================================================

    /// <summary>
    /// Something an instance has that its prefab does not. Told apart by carrying no source identity,
    /// the same way a refresh tells them apart when it decides what to leave alone.
    /// </summary>
    public sealed class AdditionDescription
    {
        /// <summary>The object it is on, or the added object itself.</summary>
        public string ObjectName = "";
        /// <summary>The added component, or empty when a whole object was added.</summary>
        public string ComponentName = "";
        /// <summary>Identifier of the added component, or of the added object.</summary>
        public Guid Identifier;
        /// <summary>Identifier of the object the component was added to. Empty for an added object.</summary>
        public Guid OwnerIdentifier;

        public bool IsWholeObject => string.IsNullOrEmpty(ComponentName);
        public string Label => IsWholeObject ? ObjectName : $"{ObjectName} > {ComponentName}";
    }

    /// <summary>
    /// Everything this instance has that its prefab does not: components added to its objects, and
    /// objects added under them. A refresh keeps all of it and an apply leaves all of it behind, and
    /// until now nothing said it was there.
    /// </summary>
    public static List<AdditionDescription> DescribeAdditions(GameObject go)
    {
        GameObject? prefabRoot = GetPrefabInstanceRoot(go);
        if (prefabRoot.IsNotValid()) return [];

        GameObject root = prefabRoot!;
        var found = new List<AdditionDescription>();
        Walk(root);
        return found;

        void Walk(GameObject owner)
        {
            foreach (MonoBehaviour component in owner.GetComponents<MonoBehaviour>())
            {
                if (owner.GetComponentSourceIdentifier(component) != Guid.Empty) continue;

                found.Add(new AdditionDescription
                {
                    ObjectName = owner.Name,
                    ComponentName = component.GetType().Name,
                    Identifier = component.Identifier,
                    OwnerIdentifier = owner.Identifier
                });
            }

            foreach (GameObject child in owner.Children)
            {
                // Anything the prefab does not provide is the instance's, including an instance of
                // another prefab dropped in, which carries a source identity of its own.
                if (!IsProvidedByPrefab(child))
                {
                    found.Add(new AdditionDescription { ObjectName = child.Name, Identifier = child.Identifier });
                    continue;
                }

                Walk(child);
            }
        }
    }

    /// <summary>Whether this instance has anything its prefab does not.</summary>
    public static bool HasAnyAdditions(GameObject go) => DescribeAdditions(go).Count > 0;

    /// <summary>Take an addition back out of the instance. It was the instance's, so it simply goes.</summary>
    public static void RemoveAddition(GameObject instanceGO, AdditionDescription addition)
    {
        if (!instanceGO.IsPrefabInstance) return;
        if (!GuardNotPlaying("remove an addition")) return;

        GameObject? prefabRoot = GetPrefabInstanceRoot(instanceGO);
        GameObject root = prefabRoot.IsValid() ? prefabRoot! : instanceGO;

        if (addition.IsWholeObject)
        {
            GameObject? added = root.FindChildByIdentifier(addition.Identifier);
            if (added.IsNotValid()) return;

            Undo.RegisterDestroyObject(added!, "Remove Added Object");

            Scene? scene = added!.Scene;
            if (scene.IsValid()) scene!.Remove(added);
            added.Destroy();
        }
        else
        {
            GameObject? owner = root.Identifier == addition.OwnerIdentifier
                ? root
                : root.FindChildByIdentifier(addition.OwnerIdentifier);

            MonoBehaviour? component = owner.IsValid() ? owner!.GetComponentByIdentifier(addition.Identifier) : null;
            if (component.IsNotValid()) return;

            EchoObject? state = Serializer.Serialize(component!.GetType(), component, ComponentValueContext(component!));
            Type componentType = component!.GetType();
            Guid ownerId = owner!.Identifier;
            Guid componentId = component.Identifier;

            owner.RemoveComponent(component);

            Undo.RegisterAction("Remove Added Component",
                undo: () =>
                {
                    GameObject? live = Undo.FindGO(ownerId);
                    if (live.IsNotValid() || state == null) return;

                    if (Serializer.Deserialize(state, componentType, InstanceValueContext()) is not MonoBehaviour restored) return;
                    restored.Identifier = componentId;
                    live!.AddComponent(restored);
                },
                redo: () =>
                {
                    GameObject? live = Undo.FindGO(ownerId);
                    MonoBehaviour? target = live.IsValid() ? live!.GetComponentByIdentifier(componentId) : null;
                    if (target.IsValid()) live!.RemoveComponent(target!);
                });
        }

        EditorSceneManager.MarkDirty();
    }

    /// <summary>
    /// Put an addition into the prefab, so every instance gets it and this one stops carrying it as
    /// its own. The instance keeps the objects it already has and adopts the identities they were
    /// written under, rather than being handed a second copy by the refresh that follows.
    /// </summary>
    public static void ApplyAddition(GameObject instanceGO, AdditionDescription addition)
    {
        if (!instanceGO.IsPrefabInstance) return;
        if (!GuardNotPlaying("apply an addition")) return;
        if (!GuardEditablePrefab(instanceGO.PrefabAssetId, "apply an addition")) return;

        var db = EditorAssetBackend.Instance;
        if (db == null || Project.Current == null) return;

        Guid prefabGuid = instanceGO.PrefabAssetId;
        var entry = db.GetEntry(prefabGuid);
        if (entry == null) return;
        if (AssetDatabase.Get(prefabGuid) is not PrefabAsset prefab) return;

        GameObject? prefabRoot = GetPrefabInstanceRoot(instanceGO);
        GameObject root = prefabRoot.IsValid() ? prefabRoot! : instanceGO;

        string absolutePath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string? oldFileContent = File.Exists(absolutePath) ? File.ReadAllText(absolutePath) : null;

        GameObject? source = GameObject.InstantiateDetached(prefab);
        if (source == null) return;

        if (!WriteAdditionInto(source, root, addition, prefabGuid, out Action adopt))
            return;

        StripInstanceDataForEditing(source, prefabGuid);
        StabilizeSourceIdentifiers(source);

        var writeContext = TreeValueContext(source);
        var echo = Serializer.Serialize(typeof(object), source, writeContext);
        if (echo == null || !TryWriteFile(absolutePath, echo.WriteToString())) return;

        ReportDroppedSceneReferences(writeContext, "Applying this addition");

        // Stamped after the write, so the identities the instance takes on are the ones that ended up
        // in the file rather than the ones it had before stabilising.
        adopt();

        InvalidateSource(prefabGuid);
        db.Reimport(entry.Guid);

        Guid rootId = root.Identifier;
        Undo.RegisterAction("Apply Prefab Addition",
            undo: () =>
            {
                if (oldFileContent != null) TryWriteFile(absolutePath, oldFileContent);
                InvalidateSource(prefabGuid);
                db.Reimport(entry.Guid);

                // The instance goes back to owning it, which is what having no source identity means.
                GameObject? live = Undo.FindGO(rootId);
                if (live.IsValid()) ForgetSourceIdentity(live!, addition);
                RefreshAllInstances(prefabGuid);
            },
            redo: () =>
            {
                GameObject? live = Undo.FindGO(rootId);
                if (live.IsValid()) ApplyAddition(live!, addition);
            });

        RefreshAllInstances(prefabGuid);
        EditorSceneManager.MarkDirty();
    }

    /// <summary>
    /// Copy an addition into a tree about to become the prefab, and hand back what makes the live
    /// instance recognise it as the prefab's afterwards.
    /// </summary>
    private static bool WriteAdditionInto(GameObject source, GameObject root, AdditionDescription addition,
        Guid prefabGuid, out Action adopt)
    {
        adopt = () => { };

        if (addition.IsWholeObject)
        {
            GameObject? added = root.FindChildByIdentifier(addition.Identifier);
            GameObject? parent = added.IsValid() ? added!.Parent : null;
            if (added.IsNotValid() || parent.IsNotValid()) return false;

            GameObject? sourceParent = FindBySourceIdentifier(source, parent!.SourceIdentifier, source.PrefabAssetId);
            if (sourceParent == null) return false;

            GameObject? copy = Serializer.Deserialize<GameObject>(
                Serializer.Serialize(typeof(object), added, TreeValueContext(added!)), InstanceValueContext());
            if (copy == null) return false;

            copy.SetParent(sourceParent, worldPositionStays: false);
            copy.ClearPrefabDataRecursive();

            adopt = () => StampAsPrefabInstance(added!, copy, prefabGuid, added!.PrefabAssetId);
            return true;
        }

        GameObject? owner = root.Identifier == addition.OwnerIdentifier
            ? root
            : root.FindChildByIdentifier(addition.OwnerIdentifier);

        MonoBehaviour? component = owner.IsValid() ? owner!.GetComponentByIdentifier(addition.Identifier) : null;
        if (component.IsNotValid()) return false;

        GameObject? sourceOwner = FindBySourceIdentifier(source, owner!.SourceIdentifier, source.PrefabAssetId);
        if (sourceOwner == null) return false;

        MonoBehaviour copied = sourceOwner.AddComponent(component!.GetType());
        EchoObject? state = Serializer.Serialize(component!.GetType(), component, ComponentValueContext(component!));
        if (state != null)
            Serializer.DeserializeInto(state, copied, InstanceValueContext());

        adopt = () => owner!.EnsurePrefabLink().ComponentSources[component!.Identifier] = copied.Identifier;
        return true;
    }

    /// <summary>Undo of adopting: the instance owns it again, so it carries no source identity.</summary>
    private static void ForgetSourceIdentity(GameObject root, AdditionDescription addition)
    {
        if (addition.IsWholeObject)
        {
            GameObject? added = root.FindChildByIdentifier(addition.Identifier);
            if (added.IsValid()) added!.ClearPrefabDataRecursive();
            return;
        }

        GameObject? owner = root.Identifier == addition.OwnerIdentifier
            ? root
            : root.FindChildByIdentifier(addition.OwnerIdentifier);

        if (owner.IsValid()) owner!.EnsurePrefabLink().ComponentSources.Remove(addition.Identifier);
    }

    /// <summary>The override paths on one component of an instance, as they are stored on its root.</summary>
    private static List<string> PathsFor(GameObject instanceGO, MonoBehaviour component, out GameObject root)
    {
        GameObject? prefabRoot = GetPrefabInstanceRoot(instanceGO);
        root = prefabRoot.IsValid() ? prefabRoot! : instanceGO;

        string prefix = GetOverridePath(instanceGO, component, "");
        return root.PrefabOverrides
            .Where(o => o.Path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(o => o.Path)
            .ToList();
    }

    /// <summary>Whether this component of an instance has anything overridden on it.</summary>
    public static bool HasComponentOverrides(GameObject instanceGO, MonoBehaviour component)
        => instanceGO.IsPrefabInstance && PathsFor(instanceGO, component, out _).Count > 0;

    /// <summary>
    /// Put one component back to what the prefab says, in a single step. The whole instance can
    /// already be reverted from the prefab bar; this is the same thing at the scale the user is
    /// usually working at, which is the component they are looking at.
    /// </summary>
    public static void RevertComponentOverrides(GameObject instanceGO, MonoBehaviour component)
    {
        if (!instanceGO.IsPrefabInstance) return;
        if (!GuardNotPlaying("revert a component")) return;

        List<string> paths = PathsFor(instanceGO, component, out GameObject root);
        if (paths.Count == 0) return;

        // Captured either side and restored wholesale, so one undo step covers every member reverted
        // and the order the records run in does not matter.
        List<PropertyOverride> before = root.PrefabOverrides.ToList();
        EchoObject? beforeState = Serializer.Serialize(component.GetType(), component, ComponentValueContext(component));

        foreach (string path in paths)
            RevertSingleOverrideCore(instanceGO, path, recordUndo: false);

        List<PropertyOverride> after = root.PrefabOverrides.ToList();
        EchoObject? afterState = Serializer.Serialize(component.GetType(), component, ComponentValueContext(component));

        Guid rootId = root.Identifier;
        Guid componentId = component.Identifier;

        Undo.RegisterAction("Revert Component",
            undo: () => Restore(before, beforeState),
            redo: () => Restore(after, afterState));

        EditorSceneManager.MarkDirty();

        void Restore(List<PropertyOverride> overrides, EchoObject? state)
        {
            GameObject? live = Undo.FindGO(rootId);
            if (live.IsNotValid()) return;

            live!.PrefabOverrides = overrides.ToList();

            MonoBehaviour? target = live.GetComponentInChildrenByIdentifier(componentId);
            if (target.IsNotValid() || state == null) return;

            Serializer.DeserializeInto(state, target!, InstanceValueContext());
            target!.HierarchyStateChanged();
            target.OnValidate();
        }
    }

    /// <summary>Push everything overridden on one component into the prefab.</summary>
    public static void ApplyComponentOverrides(GameObject instanceGO, MonoBehaviour component)
    {
        if (!instanceGO.IsPrefabInstance) return;
        if (!GuardNotPlaying("apply a component")) return;
        if (!GuardEditablePrefab(instanceGO.PrefabAssetId, "apply a component")) return;

        List<string> paths = PathsFor(instanceGO, component, out GameObject root);
        foreach (string path in paths)
            ApplySingleOverride(instanceGO, path);
    }

    /// <summary>
    /// Put a component back to the values a new one of its type would have. What Reset means where
    /// there is no prefab to answer to; on an instance <see cref="RevertComponentOverrides"/> does.
    /// </summary>
    public static void ResetComponentToDefaults(GameObject go, MonoBehaviour component)
    {
        if (Application.IsPlaying)
        {
            Runtime.Debug.LogWarning("[Inspector] Cannot reset a component during play mode.");
            return;
        }

        object? fresh = Activator.CreateInstance(component.GetType(), nonPublic: true);
        if (fresh == null) return;

        EchoObject? defaults = Serializer.Serialize(component.GetType(), fresh, ComponentValueContext((MonoBehaviour)fresh));
        EchoObject? before = Serializer.Serialize(component.GetType(), component, ComponentValueContext(component));
        if (defaults == null || before == null) return;

        Guid goId = go.Identifier;
        Guid componentId = component.Identifier;

        Restore(defaults);

        Undo.RegisterAction("Reset Component",
            undo: () => Restore(before),
            redo: () => Restore(defaults));

        EditorSceneManager.MarkDirty();

        void Restore(EchoObject state)
        {
            GameObject? live = Undo.FindGO(goId);
            MonoBehaviour? target = live.IsValid() ? live!.GetComponentByIdentifier(componentId) : null;
            if (target.IsNotValid()) return;

            // Through the component's own Deserialize, and keeping its identity: it is the same
            // component with different values, not a new one.
            Guid identity = target!.Identifier;
            Serializer.DeserializeInto(state, target, InstanceValueContext());
            target.Identifier = identity;
            target.HierarchyStateChanged();
            target.OnValidate();
        }
    }

    /// <summary>
    /// One override, resolved against the objects it addresses so it can be read. The path itself is
    /// three source identifiers and a member name, which is the right thing to store and says nothing
    /// at all to the person looking at it.
    /// </summary>
    public sealed class OverrideDescription
    {
        public string Path = "";
        /// <summary>The object the override is on, named as the scene names it.</summary>
        public string ObjectName = "";
        /// <summary>The component it is on, or empty when the override is on the GameObject itself.</summary>
        public string ComponentName = "";
        public string MemberName = "";
        public string InstanceValue = "";
        public string SourceValue = "";
        /// <summary>False when the path no longer addresses anything, so it can only be removed.</summary>
        public bool Resolvable;

        /// <summary>What to group this under: one heading per object, or per component of one.</summary>
        public string Group => string.IsNullOrEmpty(ComponentName) ? ObjectName : $"{ObjectName} > {ComponentName}";
    }

    /// <summary>
    /// Everything this instance overrides, resolved for display and in a stable order: by the object
    /// it is on, then the component, then the member.
    /// </summary>
    public static List<OverrideDescription> DescribeOverrides(GameObject go)
    {
        GameObject? prefabRoot = GetPrefabInstanceRoot(go);
        if (prefabRoot.IsNotValid()) return [];

        GameObject root = prefabRoot!;
        GameObject? source = GetCachedPrefabSource(root.PrefabAssetId);

        var described = new List<OverrideDescription>();
        foreach (PropertyOverride ov in root.PrefabOverrides)
            described.Add(Describe(root, source, ov));

        return described
            .OrderBy(d => d.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.ComponentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.MemberName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static OverrideDescription Describe(GameObject root, GameObject? source, PropertyOverride ov)
    {
        var described = new OverrideDescription { Path = ov.Path };

        ParseOverridePath(root, ov.Path, out object? target, out string memberPath);
        described.MemberName = memberPath;

        switch (target)
        {
            case MonoBehaviour component:
                described.ObjectName = component.GameObject.IsValid() ? component.GameObject.Name : "";
                described.ComponentName = component.GetType().Name;
                break;
            case GameObject owner:
                described.ObjectName = owner.Name;
                break;
            default:
                // Nothing on the instance answers to it, so there is no name to give it beyond its own.
                described.ObjectName = ov.Path;
                described.MemberName = "";
                return described;
        }

        described.Resolvable = GetMemberByPath(target, memberPath).IsValid;
        if (!described.Resolvable) return described;

        described.InstanceValue = Describe(GetMemberValue(target, memberPath));

        if (source != null)
        {
            ParseOverridePath(source, ov.Path, out object? sourceTarget, out string sourceMemberPath);
            if (sourceTarget != null && GetMemberByPath(sourceTarget, sourceMemberPath).IsValid)
                described.SourceValue = Describe(GetMemberValue(sourceTarget, sourceMemberPath));
        }

        return described;

        static string Describe(object? value) => value switch
        {
            null => "none",
            EngineObject engineObject => engineObject.IsValid() ? engineObject.Name : "none",
            _ => value.ToString() ?? ""
        };
    }

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

    // Set while an apply is running, which reimports the asset itself. Without this the import
    // notification would refresh the instances a second time, replacing every object again.
    private static bool _refreshingFromApply;

    /// <summary>
    /// Bring open-scene instances up to date after their prefab asset was imported. Covers a prefab
    /// edited outside the editor, one arriving from source control, and a model whose import settings
    /// changed - a model imports to a prefab, so its instances update the same way.
    /// </summary>
    internal static void OnAssetsImported(string[] paths)
    {
        if (_refreshingFromApply || Application.IsPlaying) return;
        if (Scene.Current == null) return;

        var db = EditorAssetBackend.Instance;
        if (db == null) return;

        foreach (string path in paths)
        {
            var entry = db.GetEntry(path);
            if (entry == null || entry.MainAssetType != typeof(PrefabAsset)) continue;

            try
            {
                InvalidateSource(entry.Guid);
                RefreshAllInstances(entry.Guid);
            }
            catch (Exception ex)
            {
                // This runs inside the importer's notification. One instance that cannot be rebuilt
                // must not take the rest of the import down with it.
                Runtime.Debug.LogError($"[Prefab] Failed to refresh instances of '{entry.Path}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Bring the instances in a freshly loaded scene up to date with their prefabs.
    /// <para/>
    /// A prefab can change while a scene is closed - authored in prefab mode, pulled from source
    /// control, reimported because a model's settings changed - and the import notification above only
    /// reaches the scene that was open at the time. Without this the instances keep the contents the
    /// prefab used to have, and <see cref="ReconcileInstance"/>, which every scene save runs, then
    /// reads each of those differences as an edit the instance made and pins it as an override.
    /// </summary>
    internal static void OnSceneLoaded()
    {
        if (Application.IsPlaying) return;
        // The prefab editing scene holds the prefab itself, not instances of it.
        if (PrefabEditingMode.IsEditing) return;

        Scene? scene = Scene.Current;
        if (scene == null) return;

        // Collected before anything is refreshed, since a refresh adds and removes objects.
        var prefabs = new HashSet<Guid>();
        foreach (GameObject go in scene.AllObjects)
            if (go.IsPrefabInstance && IsInstanceRoot(go))
                prefabs.Add(go.PrefabAssetId);

        foreach (Guid prefabGuid in prefabs)
        {
            try
            {
                RefreshAllInstances(prefabGuid);
            }
            catch (Exception ex)
            {
                // One prefab that cannot be rebuilt must not stop the rest of the scene from updating.
                Runtime.Debug.LogError($"[Prefab] Failed to refresh instances of {prefabGuid} on scene load: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Bring every instance of a prefab in the current scene up to date with the asset.
    /// <para/>
    /// The instances are updated in place rather than rebuilt. A rebuild left every reference to the
    /// instance - from a script field, the selection, an undo record - pointing at an object that had
    /// been thrown away, and anything the instance had that the prefab did not know about went with
    /// it. Objects here keep their identity; only what the prefab provides is brought into line.
    /// </summary>
    public static void RefreshAllInstances(Guid prefabGuid)
    {
        List<GameObject> roots = FindInstancesOf(prefabGuid);
        if (roots.Count == 0) return;

        var prefab = AssetDatabase.Get(prefabGuid) as PrefabAsset;
        if (prefab == null) return;

        // One copy of the prefab's contents, read from by every instance. Never mutated.
        var source = GameObject.InstantiateDetached(prefab);
        if (source == null) return;

        foreach (var root in roots)
            RefreshInstance(root, source, prefabGuid);
    }

    /// <summary>
    /// Bring one instance back into line with a prefab, given a copy of the prefab's contents to read
    /// from. Split out so a caller that changed one instance can refresh that one rather than every
    /// instance of the prefab in the scene.
    /// </summary>
    private static void RefreshInstance(GameObject root, GameObject source, Guid prefabGuid)
    {
        var savedOverrides = root.PrefabOverrides.ToList();

        // Nested instances answer to their own prefab, so their overrides are re-applied after
        // this instance's structure has been brought back into line with its own source.
        var nested = CollectNestedInstances(root, prefabGuid);

        ReconcileToSource(root, source, prefabGuid);

        ApplyPropertyOverridesToInstance(root, savedOverrides);
        foreach (var nestedRoot in nested)
            if (nestedRoot.IsValid())
                ApplyPropertyOverridesToInstance(nestedRoot, nestedRoot.PrefabOverrides);

        RaiseInstanceUpdated(root);
    }

    /// <summary>
    /// Bring one instance back into line with its prefab, reading the prefab fresh. For a caller
    /// acting on a single instance; use <see cref="RefreshAllInstances"/> when the asset itself moved.
    /// </summary>
    internal static void RefreshOneInstance(GameObject instanceRoot)
    {
        if (AssetDatabase.Get(instanceRoot.PrefabAssetId) is not PrefabAsset prefab) return;

        var source = GameObject.InstantiateDetached(prefab);
        if (source == null) return;

        RefreshInstance(instanceRoot, source, instanceRoot.PrefabAssetId);
    }

    /// <summary>Instance roots of other prefabs sitting inside this one.</summary>
    private static List<GameObject> CollectNestedInstances(GameObject root, Guid boundaryPrefabId)
    {
        var nested = new List<GameObject>();
        Walk(root);
        return nested;

        void Walk(GameObject go)
        {
            foreach (var child in go.Children)
            {
                if (child.IsPrefabInstance && child.PrefabAssetId != boundaryPrefabId)
                {
                    nested.Add(child); // its own contents are its own business
                    continue;
                }
                Walk(child);
            }
        }
    }

    /// <summary>
    /// Make an instance match the prefab it came from, in place.
    /// <para/>
    /// Objects are paired by source identifier first, so the copy lands on the instance's own objects
    /// and every reference to them stays valid. Anything the instance added, which has no source
    /// identifier, is left alone. What belongs to the instance rather than the prefab is put back
    /// afterwards - see <see cref="PlacementState"/> for what that is.
    /// </summary>
    private static void ReconcileToSource(GameObject instance, GameObject source, Guid boundaryPrefabId)
    {
        var context = new CloneContext();
        var placement = new List<PlacementState>();

        PairToSource(instance, source, boundaryPrefabId, context, placement, isInstanceRoot: true);
        DropWhatTheSourceNoLongerHas(instance, source, boundaryPrefabId);

        Cloner.CopyTo(source, instance, context);

        foreach (PlacementState state in placement)
            state.Restore();

        AdoptClonedObjects(instance, source, boundaryPrefabId, context);
    }

    /// <summary>
    /// What the instance decides for one of its objects rather than the prefab, put back after the
    /// copy because nothing tracks it as an override.
    /// <para/>
    /// Where the instance root sits and what it is called is the placement of the instance itself, so
    /// it is never the prefab's to set. Below the root those are prefab content like any other value,
    /// tracked and reverted as overrides - a prefab that could not move or rename its own children
    /// could not change its layout at all. Hide flags are editor presentation at every depth.
    /// </summary>
    private readonly struct PlacementState(GameObject go, bool isInstanceRoot)
    {
        private readonly GameObject _go = go;
        private readonly bool _isInstanceRoot = isInstanceRoot;
        private readonly HideFlags _hideFlags = go.HideFlags;
        private readonly string _name = go.Name;
        private readonly Float3 _position = go.Transform.LocalPosition;
        private readonly Quaternion _rotation = go.Transform.LocalRotation;
        private readonly Float3 _scale = go.Transform.LocalScale;

        public void Restore()
        {
            if (_go.IsNotValid()) return;

            _go.HideFlags = _hideFlags;
            if (!_isInstanceRoot) return;

            _go.Name = _name;
            _go.Transform.LocalPosition = _position;
            _go.Transform.LocalRotation = _rotation;
            _go.Transform.LocalScale = _scale;
        }
    }

    /// <summary>
    /// Pairs every prefab object with the instance object standing for it, so the copy updates those
    /// rather than replacing them. A nested instance of another prefab is paired but sealed: where it
    /// sits is this prefab's business, what is inside it is not.
    /// </summary>
    private static void PairToSource(GameObject instance, GameObject source, Guid boundaryPrefabId,
        CloneContext context, List<PlacementState> placement, bool isInstanceRoot)
    {
        if (instance.IsPrefabInstance && instance.PrefabAssetId != boundaryPrefabId)
        {
            context.AddTarget(source, instance, walkContents: false);
            return;
        }

        context.AddTarget(source, instance);
        placement.Add(new PlacementState(instance, isInstanceRoot));

        // What ties the instance to its prefab, overrides and all, is the instance's own record. The
        // source carries a link too, and copying that one over would throw the overrides away.
        PrefabLink? sourceLink = source.PrefabLink;
        PrefabLink? instanceLink = instance.EnsurePrefabLink();
        if (sourceLink != null)
            context.AddTarget(sourceLink, instanceLink, walkContents: false);

        foreach (MonoBehaviour sourceComponent in source.GetComponents<MonoBehaviour>())
        {
            Guid sourceId = source.GetComponentSourceIdentifier(sourceComponent);
            if (sourceId == Guid.Empty) continue;

            MonoBehaviour? match = instance.GetComponents<MonoBehaviour>()
                .FirstOrDefault(c => instance.GetComponentSourceIdentifier(c) == sourceId);

            if (match.IsValid() && match!.GetType() == sourceComponent.GetType())
                context.AddTarget(sourceComponent, match!);
        }

        foreach (GameObject sourceChild in source.Children)
        {
            Guid sourceId = sourceChild.SourceIdentifier;
            if (sourceId == Guid.Empty) continue;

            GameObject? match = instance.Children.FirstOrDefault(c => c.SourceIdentifier == sourceId);
            if (match.IsValid())
                PairToSource(match!, sourceChild, boundaryPrefabId, context, placement, isInstanceRoot: false);
        }
    }

    /// <summary>
    /// Removes what the prefab used to provide and no longer does. Anything without a source
    /// identifier was added to the instance and is not the prefab's to take away.
    /// </summary>
    private static void DropWhatTheSourceNoLongerHas(GameObject instance, GameObject source, Guid boundaryPrefabId)
    {
        if (instance.IsPrefabInstance && instance.PrefabAssetId != boundaryPrefabId)
            return;

        PrefabLink link = instance.EnsurePrefabLink();
        var sourceComponentIds = source.GetComponents<MonoBehaviour>()
            .Select(c => source.GetComponentSourceIdentifier(c))
            .ToHashSet();

        foreach (MonoBehaviour component in instance.GetComponents<MonoBehaviour>().ToList())
        {
            if (component.IsNotValid()) continue;

            Guid sourceId = instance.GetComponentSourceIdentifier(component);
            if (sourceId == Guid.Empty || sourceComponentIds.Contains(sourceId)) continue;

            link.ComponentSources.Remove(component.Identifier);
            instance.RemoveComponentInternal(component);
        }

        foreach (GameObject child in instance.Children.ToList())
        {
            if (child.IsNotValid()) continue;

            // Anything the prefab does not provide belongs to the instance, including an instance of
            // some other prefab that was placed here, which carries a source identity of its own.
            if (!IsProvidedByPrefab(child)) continue;

            Guid sourceId = child.SourceIdentifier;
            GameObject? stillThere = source.Children.FirstOrDefault(c => c.SourceIdentifier == sourceId);
            if (stillThere.IsValid())
            {
                DropWhatTheSourceNoLongerHas(child, stillThere!, boundaryPrefabId);
                continue;
            }

            var childScene = child.Scene;
            if (childScene.IsValid()) childScene!.Remove(child);
            child.Destroy();
        }
    }

    /// <summary>
    /// Records where the objects the copy just created came from, puts new children into the scene,
    /// and brings sibling order back into line with the prefab.
    /// </summary>
    private static void AdoptClonedObjects(GameObject instance, GameObject source, Guid boundaryPrefabId, CloneContext context)
    {
        if (instance.IsPrefabInstance && instance.PrefabAssetId != boundaryPrefabId)
            return;

        PrefabLink link = instance.EnsurePrefabLink();
        link.AssetId = source.PrefabAssetId != Guid.Empty ? source.PrefabAssetId : link.AssetId;

        foreach (MonoBehaviour sourceComponent in source.GetComponents<MonoBehaviour>())
        {
            Guid sourceId = source.GetComponentSourceIdentifier(sourceComponent);
            if (sourceId == Guid.Empty) continue;
            if (!context.TryGetTarget(sourceComponent, out object? paired) || paired is not MonoBehaviour component)
                continue;

            component.OnValidate();
        }

        var scene = instance.Scene;
        int order = 0;

        foreach (GameObject sourceChild in source.Children)
        {
            if (!context.TryGetTarget(sourceChild, out object? paired) || paired is not GameObject child)
                continue;

            child.SourceIdentifier = sourceChild.SourceIdentifier;

            if (scene.IsValid() && child.Scene.IsNotValid())
                scene!.Add(child);

            child.SetSiblingIndex(order++);

            AdoptClonedObjects(child, sourceChild, boundaryPrefabId, context);
        }
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

    /// <summary>
    /// True when this child is part of what its parent's prefab provides, rather than something added
    /// to the instance afterwards. Both halves matter: an object with no source came from the
    /// instance, and one belonging to a different prefab is a nested instance that answers to that
    /// prefab, so neither is the parent prefab's structure.
    /// </summary>
    public static bool IsProvidedByPrefab(GameObject child)
    {
        var parent = child.Parent;
        if (!parent.IsValid() || !parent!.IsPrefabInstance) return false;

        return child.SourceIdentifier != Guid.Empty && child.PrefabAssetId == parent.PrefabAssetId;
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


    /// <summary>Parse an index-based override path into a target object and remaining field path.</summary>
    // An override path is "{sourceGameObjectId}/{sourceComponentId}/{field.path}", or
    // "{sourceGameObjectId}/$/{field}" for a field on the GameObject itself. The identifiers are the
    // ones the prefab is stored with, so a path keeps pointing at the same thing when components or
    // children are added, removed or reordered - which the old index-based paths could not.

    /// <summary>The override path for a field on a component of a prefab instance.</summary>
    public static string GetOverridePath(GameObject instanceGO, MonoBehaviour component, string fieldPath)
        => $"{instanceGO.SourceIdentifier}{PathSeparator}{instanceGO.GetComponentSourceIdentifier(component)}{PathSeparator}{fieldPath}";

    /// <summary>The override path for a field on the GameObject itself.</summary>
    public static string GetOverridePath(GameObject instanceGO, string fieldName)
        => $"{instanceGO.SourceIdentifier}{PathSeparator}{GameObjectMarker}{PathSeparator}{fieldName}";




    // ================================================================
    //  Automatic Override Detection (comparison-based)
    // ================================================================

    /// <summary>
    /// Compare a component's current state against its prefab source and update overrides.
    /// Uses index-based paths. Called after each component is drawn in the inspector.
    /// </summary>
    public static void RecordComponentOverrides(GameObject instanceGO, MonoBehaviour instanceComp)
    {
        if (!instanceGO.IsPrefabInstance) return;

        var source = GetCachedPrefabSource(instanceGO.PrefabAssetId);
        if (source == null) return;

        // The component this one came from, found by identity rather than by position, so adding or
        // reordering components on either side does not change which one it is compared against.
        string path = GetOverridePath(instanceGO, instanceComp, "");
        ParseOverridePath(source, path, out var sourceTarget, out _);
        if (sourceTarget is not MonoBehaviour sourceComp) return;

        if (sourceComp.GetType() != instanceComp.GetType())
        {
            // Runs once per drawn component per frame, so this must not log per occurrence.
            Runtime.Debug.LogWarningOnce($"prefab.mismatch.{path}",
                $"[Prefab] Component type mismatch: instance={instanceComp.GetType().Name}, source={sourceComp.GetType().Name}");
            return;
        }

        // Compare fields. Overrides are stored on the instance root (the paths are absolute within
        // the instance) so that apply/revert/refresh - which operate on the root - see them all.
        var prefabRoot = GetPrefabInstanceRoot(instanceGO);
        var root = prefabRoot.IsValid() ? prefabRoot : instanceGO;
        CompareFields(instanceComp, sourceComp, path, root.PrefabOverrides);
    }

    /// <summary>
    /// Bring an instance's override list in line with what its objects actually hold, by comparing
    /// every object and component against the prefab.
    /// <para/>
    /// Detection is otherwise driven by the inspector drawing something, which means an edit made by
    /// a scene tool, a script, or on an object nobody selected afterwards was never recorded - and
    /// then the next refresh, which only replays recorded overrides, threw it away.
    /// <para/>
    /// Only sound while the prefab is unchanged. Comparison cannot tell an edited instance from a
    /// changed prefab, so running this after a reimport would read every source change as an
    /// override and freeze the instance against the prefab from then on. Callers reconcile before
    /// they write, not after the asset moves.
    /// </summary>
    public static void ReconcileInstance(GameObject instanceGO)
    {
        if (!instanceGO.IsPrefabInstance) return;

        var prefabRoot = GetPrefabInstanceRoot(instanceGO);
        var root = prefabRoot.IsValid() ? prefabRoot! : instanceGO;

        Reconcile(root, root.PrefabAssetId);

        static void Reconcile(GameObject go, Guid boundaryPrefabId)
        {
            RecordGameObjectOverrides(go);
            foreach (var component in go.GetComponents<MonoBehaviour>())
                RecordComponentOverrides(go, component);

            foreach (var child in go.Children)
            {
                // A nested instance keeps its own overrides against its own prefab.
                if (child.IsPrefabInstance && child.PrefabAssetId != boundaryPrefabId) continue;
                Reconcile(child, boundaryPrefabId);
            }
        }
    }

    /// <summary>
    /// Reconcile every prefab instance in the open scene. Belongs before anything that writes the
    /// scene out or snapshots it to come back to later: an edit the inspector never saw is a raw
    /// value on the objects and nothing else, so a round trip through serialized data would keep the
    /// value but lose the fact that it differs from the prefab, and the next refresh would drop it.
    /// </summary>
    public static void ReconcileOpenScene()
    {
        Scene? scene = Scene.Current;
        if (scene == null) return;

        foreach (GameObject go in scene.AllObjects.ToList())
            if (IsInstanceRoot(go))
                ReconcileInstance(go);
    }

    /// <summary>
    /// Record any overrides an edit to <paramref name="target"/> just produced. Called from the
    /// editor's change hook so an edit is captured when it happens rather than whenever something
    /// gets around to drawing that object.
    /// </summary>
    public static void NotifyEdited(object? target)
    {
        switch (target)
        {
            case MonoBehaviour component when component.GameObject.IsValid():
                RecordComponentOverrides(component.GameObject, component);
                break;
            case GameObject go:
                RecordGameObjectOverrides(go);
                break;
        }
    }

    /// <summary>
    /// Detect the overrides that live on the GameObject itself rather than on one of its components.
    /// </summary>
    public static void RecordGameObjectOverrides(GameObject instanceGO)
    {
        if (!instanceGO.IsPrefabInstance) return;

        var source = GetCachedPrefabSource(instanceGO.PrefabAssetId);
        if (source == null) return;

        string pathPrefix = GetOverridePath(instanceGO, "");
        ParseOverridePath(source, pathPrefix, out var sourceTarget, out _);
        if (sourceTarget is not GameObject sourceGO) return;

        // Stored on the instance root so refresh/apply, which operate on the root, find child overrides.
        var prefabRoot = GetPrefabInstanceRoot(instanceGO);
        var overrides = (prefabRoot.IsValid() ? prefabRoot : instanceGO).PrefabOverrides;

        CompareField(pathPrefix, "TagIndex", instanceGO.TagIndex, sourceGO.TagIndex, overrides);
        CompareField(pathPrefix, "LayerIndex", instanceGO.LayerIndex, sourceGO.LayerIndex, overrides);
        CompareField(pathPrefix, "Enabled", instanceGO.Enabled, sourceGO.Enabled, overrides);
        CompareField(pathPrefix, "IsStatic", instanceGO.IsStatic, sourceGO.IsStatic, overrides);

        // Where the instance root sits and what it is called is the placement of the instance itself,
        // which the prefab has no say in. Below the root it is the prefab's layout, so it is compared
        // like anything else and the instance can move a child, see that it did, and put it back.
        if (IsInstanceRoot(instanceGO)) return;

        Transform instanceTransform = instanceGO.Transform;
        Transform sourceTransform = sourceGO.Transform;

        CompareField(pathPrefix, "Name", instanceGO.Name, sourceGO.Name, overrides);
        CompareField(pathPrefix, "Transform.LocalPosition", instanceTransform.LocalPosition, sourceTransform.LocalPosition, overrides);
        CompareField(pathPrefix, "Transform.LocalRotation", instanceTransform.LocalRotation, sourceTransform.LocalRotation, overrides);
        CompareField(pathPrefix, "Transform.LocalScale", instanceTransform.LocalScale, sourceTransform.LocalScale, overrides);
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

    /// <summary>
    /// Whether two field values are the same, for the kinds of value that can say so on their own:
    /// the very same object, or a value type or string, where equality means equal content. Anything
    /// else answers false and gets compared properly.
    /// </summary>
    private static bool IsSameByValue(Type fieldType, object? instanceVal, object? sourceVal)
    {
        if (ReferenceEquals(instanceVal, sourceVal)) return true;
        if (instanceVal == null || sourceVal == null) return false;

        return (fieldType.IsValueType || fieldType == typeof(string)) && instanceVal.Equals(sourceVal);
    }

    private static void CompareFields(object instance, object source, string pathPrefix, List<PropertyOverride> overrides)
    {
        foreach (var field in GetOverridableFields(instance))
        {
            var instanceVal = field.GetValue(instance);
            var sourceVal = field.GetValue(source);
            string path = pathPrefix + field.Name;

            // Looked up by hand: a lambda over `path` allocates a closure per field, and this runs for
            // every field of every drawn component every frame.
            PropertyOverride? existing = null;
            foreach (PropertyOverride candidate in overrides)
            {
                if (candidate.Path != path) continue;
                existing = candidate;
                break;
            }

            // Most fields are a number, a bool, an enum, a string or a small struct, and for those
            // this settles it without serializing anything.
            //
            // Only ever allowed to answer "the same": equality saying no proves nothing about
            // content, so that falls through to the real comparison. And only asked of things whose
            // equality is about their value by construction. A class is free to define equality as
            // loosely as it likes, and one that called two different states equal would turn a real
            // override into one nothing ever records.
            if (IsSameByValue(field.FieldType, instanceVal, sourceVal))
            {
                if (existing != null) overrides.Remove(existing);
                continue;
            }

            // A context per side, deliberately: Echo numbers object references as it goes, so sharing
            // one would give the second value different ids and make equal content compare unequal.
            var instanceEcho = Serializer.Serialize(field.FieldType, instanceVal, InstanceValueContext());
            var sourceEcho = Serializer.Serialize(field.FieldType, sourceVal, InstanceValueContext());

            // Compared as data rather than as text. Writing both out to strings first said the same
            // thing and built two of them per field, on every field of every drawn component, every
            // frame.
            bool areSame = instanceEcho == null
                ? sourceEcho == null
                : instanceEcho.Equals(sourceEcho);

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
        string path = pathPrefix + fieldName;
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

    // What each prefab holds, built once and kept until the prefab changes. This used to expire every
    // frame, which meant rebuilding a whole tree from Echo for every prefab an instance was being
    // compared against, on every frame the inspector drew one. Nothing about a prefab changes without
    // going through a write here or an import, and both drop what they invalidate, so the frame
    // counter was buying nothing the invalidation was not already buying.
    private static readonly Dictionary<Guid, GameObject> _sourceCache = new();

    private static GameObject? GetCachedPrefabSource(Guid prefabGuid)
    {
        if (_sourceCache.TryGetValue(prefabGuid, out GameObject? cached))
        {
            if (cached.IsValid()) return cached;
            _sourceCache.Remove(prefabGuid);
        }

        var prefab = Runtime.AssetDatabase.Get(prefabGuid) as PrefabAsset;
        if (prefab.IsNotValid() || prefab.GameObjectData == null) return null;

        // Built exactly as an instance is, so the comparison baseline matches what instantiating
        // would produce and, importantly, so its objects carry the same source identifiers that
        // override paths are written in terms of.
        var source = GameObject.InstantiateDetached(prefab);
        if (source != null)
            _sourceCache[prefabGuid] = source;

        return source;
    }

    /// <summary>
    /// Drop what was cached for a prefab. Every path that writes one, or hears that one was imported,
    /// calls this.
    /// <para/>
    /// Dropped rather than disposed. A source tree is never in a scene and never enabled, so it holds
    /// nothing that needs releasing, and <see cref="GetCorrespondingObjectFromSource"/> hands its
    /// objects out to callers: disposing here would turn a reference held across an import into a
    /// dead one, to buy nothing the collector was not already going to do.
    /// </summary>
    private static void InvalidateSource(Guid prefabGuid) => _sourceCache.Remove(prefabGuid);



    /// <summary>
    /// Convert every object that belonged to <paramref name="boundaryId"/> into an instance of the
    /// prefab just written, taking each one's identity from the object in <paramref name="written"/>
    /// it was written out as. Nested instances of other prefabs are left alone.
    /// <para/>
    /// The identities are the point. An override path names its object and component by the identity
    /// the prefab holds them under, so a tree marked as an instance without them addresses nothing
    /// and can never record an override against the asset it was just made from. The two trees are
    /// the same tree moments apart, so position is what pairs them.
    /// </summary>
    private static void StampAsPrefabInstance(GameObject go, GameObject written, Guid prefabGuid, Guid boundaryId)
    {
        if (go.PrefabAssetId != boundaryId) return;

        // StabilizeSourceIdentifiers left every identifier in the written tree equal to the source
        // identity it will be read back under, so the identifiers are what to take.
        PrefabLink link = go.EnsurePrefabLink();
        link.AssetId = prefabGuid;
        link.SourceIdentifier = written.Identifier;
        link.Overrides.Clear();
        link.ComponentSources.Clear();

        var components = go.GetComponents<MonoBehaviour>().ToList();
        var writtenComponents = written.GetComponents<MonoBehaviour>().ToList();
        for (int i = 0; i < Math.Min(components.Count, writtenComponents.Count); i++)
            link.ComponentSources[components[i].Identifier] = writtenComponents[i].Identifier;

        for (int i = 0; i < Math.Min(go.Children.Count, written.Children.Count); i++)
            StampAsPrefabInstance(go.Children[i], written.Children[i], prefabGuid, boundaryId);
    }

    /// <summary>
    /// Snapshot what ties every object within a boundary to its prefab, for undo. The whole link, not
    /// the asset id and overrides alone: an object restored without its source identities is linked
    /// to a prefab it can no longer address, which is indistinguishable from a broken instance.
    /// </summary>
    private static List<(GameObject go, PrefabLink? link)> CapturePrefabState(GameObject root, Guid boundaryId)
    {
        var captured = new List<(GameObject, PrefabLink?)>();
        Walk(root);
        return captured;

        void Walk(GameObject go)
        {
            if (go.PrefabAssetId != boundaryId) return;
            captured.Add((go, go.PrefabLink?.Clone()));
            foreach (var child in go.Children)
                Walk(child);
        }
    }

    private static void RestorePrefabState(List<(GameObject go, PrefabLink? link)> captured)
    {
        foreach (var (go, link) in captured)
        {
            if (go.IsNotValid()) continue;

            if (link == null)
                go.ClearPrefabData();
            else
                go.EnsurePrefabLink().CopyFrom(link);
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

    /// <summary>
    /// Write a prefab file, in one step as far as anything reading it is concerned.
    /// <para/>
    /// Every scene in the project can depend on one of these, and the copy of what it used to hold
    /// lives only in the undo stack. Writing in place leaves a truncated file if the process dies,
    /// the disk fills, or a scanner has the file open mid-write; writing beside it and moving it into
    /// place leaves either the old contents or the new ones.
    /// </summary>
    internal static bool TryWriteFile(string absolutePath, string contents)
    {
        string temporaryPath = absolutePath + ".tmp";
        try
        {
            // Same directory, so the move is a rename rather than a copy across volumes.
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, absolutePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[Prefab] Failed to write '{absolutePath}': {ex.Message}");
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            return false;
        }
    }

    /// <summary>
    /// Drop everything the instance added from a copy about to be written back as the prefab. The
    /// copy mirrors the instance one for one, so the live instance is walked alongside it to say
    /// which objects the prefab provided and which the instance introduced.
    /// </summary>
    private static void StripInstanceAdditions(GameObject copy, GameObject instance, GameObject? source)
    {
        // The prefab's own tree is the authority on what it provides. Asking the instance's objects
        // instead cannot tell content the prefab has always held from an instance of another prefab
        // dropped onto this one, since both carry a source identity.
        if (source == null) return;

        var instanceComponents = instance.GetComponents<MonoBehaviour>().ToList();
        var copyComponents = copy.GetComponents<MonoBehaviour>().ToList();
        var sourceComponents = source.GetComponents<MonoBehaviour>().ToList();

        for (int i = instanceComponents.Count - 1; i >= 0; i--)
        {
            if (i >= copyComponents.Count) continue;

            Guid sourceId = instance.GetComponentSourceIdentifier(instanceComponents[i]);
            bool provided = sourceId != Guid.Empty
                && sourceComponents.Any(c => source.GetComponentSourceIdentifier(c) == sourceId);

            if (!provided)
                copy.RemoveComponentInternal(copyComponents[i]);
        }

        for (int i = instance.Children.Count - 1; i >= 0; i--)
        {
            if (i >= copy.Children.Count) continue;

            Guid sourceId = instance.Children[i].SourceIdentifier;
            GameObject? sourceChild = sourceId == Guid.Empty
                ? null
                : source.Children.FirstOrDefault(c => c.SourceIdentifier == sourceId);

            if (sourceChild == null)
            {
                GameObject copyChild = copy.Children[i];
                copyChild.SetParent(null!);
                copyChild.Dispose();
                continue;
            }

            StripInstanceAdditions(copy.Children[i], instance.Children[i], sourceChild);
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
        StripInstanceDataForEditing(clone, source.PrefabAssetId);
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

    /// <summary>
    /// Like <see cref="StripPrefabDataWithinBoundary"/>, but keeps each object's record of which
    /// source object it came from. Used when a tree is about to become the prefab asset itself: it
    /// stops being an instance, but its identities have to stay the ones instances already match.
    /// </summary>
    internal static void StripInstanceDataForEditing(GameObject go, Guid boundaryPrefabId)
    {
        if (go.PrefabAssetId != boundaryPrefabId) return; // nested instance of another prefab

        go.PrefabLink?.ClearInstanceData();
        foreach (var child in go.Children)
            StripInstanceDataForEditing(child, boundaryPrefabId);
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
