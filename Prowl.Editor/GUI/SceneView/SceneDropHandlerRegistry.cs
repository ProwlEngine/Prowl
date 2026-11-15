using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Theming;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary> Attribute that marks a class as a handler for dropping assets of a specific type into the scene view. </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class SceneDropHandlerAttribute : Attribute
{
    public Type TargetType { get; }
    public int Order { get; set; }
    public SceneDropHandlerAttribute(Type targetType) => TargetType = targetType;
}

public struct SceneDropContext
{
    public SceneInstance Scene;
    public EditorCamera Camera;
    public Float2 MouseLocal;
    public Float2 PanelSize;

    // Set by Freeze: the drop was resolved earlier and the camera may have moved since.
    private bool _frozen;
    private Float3 _frozenPosition;
    private GameObject? _frozenTarget;

    /// <summary>Where a spawned object goes: the surface under the cursor, grid-snapped.</summary>
    public readonly Float3 DropPosition => _frozen ? _frozenPosition : SceneViewPanel.GetDropPosition(Scene, Camera, MouseLocal, PanelSize);

    /// <summary>The object under the cursor, gizmos ignored.</summary>
    public readonly GameObject? PickTarget() => _frozen ? _frozenTarget : SceneViewPanel.PickObjectAt(Scene, Camera, MouseLocal, PanelSize, pickGizmos: false);

    /// <summary>Resolve the drop now, for a drop handled later (after an import), so later camera moves don't shift it.</summary>
    public readonly SceneDropContext Freeze()
    {
        SceneDropContext c = this;
        c._frozenPosition = DropPosition;
        c._frozenTarget = PickTarget();
        c._frozen = true;
        return c;
    }

    /// <summary>This drop, resolved, with the spawn point moved to <paramref name="position"/> (one cell of a multi-asset drop).</summary>
    public readonly SceneDropContext At(Float3 position)
    {
        SceneDropContext c = _frozen ? this : Freeze();
        c._frozenPosition = position;
        return c;
    }
}

/// <summary> Interface for handling asset drops in the scene view. Implementations are discovered via SceneDropHandlerAttribute. </summary>
public interface ISceneDropHandler
{
    string DropHint { get; }
    void Handle(AssetDragPayload payload, SceneDropContext context);
}

/// <summary>
/// What dropping assets on the scene view does, shared by drags from the Project panel, files dropped from the OS
/// and the drag preview. Assets that spawn an object (meshes, prefabs, sprites) are laid out in a square grid
/// centred on the drop point when there are several; everything else goes to its handler at the drop point.
/// </summary>
public static class SceneDrop
{
    public static bool IsSpawnable(Type? type)
        => type != null && (typeof(Mesh).IsAssignableFrom(type) || typeof(PrefabAsset).IsAssignableFrom(type) || typeof(Sprite).IsAssignableFrom(type));

    // The last payload's assets that have a handler, spawners first with their grid offset (null for the rest), so a
    // drag's preview and its drop measure the assets once.
    private static AssetDragPayload? s_planKey;
    private static List<(AssetDragPayload Item, ISceneDropHandler Handler, Float3? Offset)> s_plan = [];

    /// <summary>Drop every asset in <paramref name="payload"/>. False when no asset in it has a handler.</summary>
    public static bool Drop(AssetDragPayload payload, SceneDropContext context)
    {
        SceneDropContext ctx = context.Freeze();
        Float3 drop = ctx.DropPosition;
        // One non-spawning item per handler: each would hit the same target, so a second material would just replace the first.
        var used = new HashSet<ISceneDropHandler>();
        List<(AssetDragPayload Item, ISceneDropHandler Handler, Float3? Offset)> plan = Plan(payload);
        foreach ((AssetDragPayload item, ISceneDropHandler handler, Float3? offset) in plan)
        {
            if (offset is { } o) handler.Handle(item, ctx.At(drop + o));
            else if (used.Add(handler)) handler.Handle(item, ctx);
        }
        return plan.Count > 0;
    }

    /// <summary>The handler a drop of <paramref name="payload"/> uses, spawning first, for the drop hint; null when none applies.</summary>
    public static ISceneDropHandler? FindHandler(AssetDragPayload payload)
        => Plan(payload) is [var first, ..] ? first.Handler : null;

    /// <summary>The objects a drop of <paramref name="payload"/> at <paramref name="drop"/> spawns, each with its root position.</summary>
    public static List<(AssetDragPayload Item, Float3 Position)> Layout(AssetDragPayload payload, Float3 drop)
    {
        var result = new List<(AssetDragPayload, Float3)>();
        foreach ((AssetDragPayload item, _, Float3? offset) in Plan(payload))
            if (offset is { } o) result.Add((item, drop + o));
        return result;
    }

    private static List<(AssetDragPayload Item, ISceneDropHandler Handler, Float3? Offset)> Plan(AssetDragPayload payload)
    {
        if (ReferenceEquals(payload, s_planKey)) return s_plan;
        var spawn = new List<(AssetDragPayload, ISceneDropHandler)>();
        var other = new List<(AssetDragPayload, ISceneDropHandler, Float3?)>();
        for (int i = 0; i < payload.AssetGuids.Length; i++)
        {
            Guid guid = payload.AssetGuids[i];
            string path = payload.AssetPaths[i];
            // Typed from the asset index rather than by loading each asset.
            Type? type = i == 0 ? payload.AssetType : EditorAssetBackend.Instance?.GetEntry(guid)?.MainAssetType;
            if (EditorRegistries.FindSceneDropHandler(type) is not { } handler) continue;
            AssetDragPayload item = payload.IsMulti ? new AssetDragPayload(guid, System.IO.Path.GetFileNameWithoutExtension(path), path, type) : payload;
            if (IsSpawnable(type)) spawn.Add((item, handler));
            else other.Add((item, handler, null));
        }
        s_planKey = payload;
        s_plan = [.. Offsets(spawn), .. other];
        return s_plan;
    }

    // One cell per object, sized by the largest footprint so neighbours never overlap; each footprint (not its
    // pivot) is centred in its cell. A single object goes at the drop point.
    private static List<(AssetDragPayload, ISceneDropHandler, Float3?)> Offsets(List<(AssetDragPayload Item, ISceneDropHandler Handler)> items)
    {
        var result = new List<(AssetDragPayload, ISceneDropHandler, Float3?)>(items.Count);
        if (items.Count == 1) result.Add((items[0].Item, items[0].Handler, Float3.Zero));
        if (items.Count <= 1) return result;

        var centers = new Float3[items.Count];
        float cell = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            if (!TryGetLocalBounds(items[i].Item.AssetGuid, out Float3 min, out Float3 max)) continue;
            centers[i] = (min + max) * 0.5f;
            cell = MathF.Max(cell, MathF.Max(max.X - min.X, max.Z - min.Z));
        }
        float pitch = (cell > 0f ? cell : 1f) * 1.1f;
        int cols = (int)MathF.Ceiling(MathF.Sqrt(items.Count));
        int rows = (items.Count + cols - 1) / cols;

        for (int i = 0; i < items.Count; i++)
        {
            float x = (i % cols - (cols - 1) * 0.5f) * pitch - centers[i].X;
            float z = (i / cols - (rows - 1) * 0.5f) * pitch - centers[i].Z;
            result.Add((items[i].Item, items[i].Handler, new Float3(x, 0f, z)));
        }
        return result;
    }

    /// <summary>An asset's bounds as spawned with its root at the origin, or false when it draws nothing measurable.</summary>
    private static bool TryGetLocalBounds(Guid guid, out Float3 min, out Float3 max)
    {
        min = max = Float3.Zero;
        switch (Runtime.AssetDatabase.Get(guid))
        {
            case Mesh mesh when mesh.IsValid():
                (min, max) = (mesh.bounds.Min, mesh.bounds.Max);
                return true;
            case PrefabAsset prefab when prefab.IsValid():
                GameObject? go = GameObject.InstantiateDetached(prefab);
                if (go.IsNotValid()) return false;
                go.Transform.Position = Float3.Zero;
                bool ok = SceneViewPanel.TryGetWorldBounds(go, out min, out max);
                go.Destroy();
                return ok;
            default:
                return false;
        }
    }
}

// ================================================================
//  Built-in scene drop handlers
// ================================================================

[SceneDropHandler(typeof(Scene), Order = 0)]
internal class SceneAssetDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to open scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        AssetEntry? entry = EditorAssetBackend.Instance?.GetEntry(payload.AssetGuid);
        if (entry != null) EditorSceneManager.OpenWithPrompt(entry.Path);
    }
}

[SceneDropHandler(typeof(Material), Order = 10)]
internal class MaterialDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop on object to assign material";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        GameObject? hitGO = context.PickTarget();
        if (hitGO.IsNotValid()) return;
        var mat = Runtime.AssetDatabase.Get(payload.AssetGuid) as Material;
        if (mat.IsNotValid()) return;
        MeshRenderer? meshRenderer = hitGO.GetComponent<MeshRenderer>();
        if (meshRenderer.IsNotValid()) return;
        Undo.RecordObject(meshRenderer, "Assign Material");
        meshRenderer.Material = mat;
    }
}

[SceneDropHandler(typeof(Mesh), Order = 21)]
internal class MeshDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        HierarchyPanel.SpawnAssetInScene(payload, null, context.DropPosition);
    }
}

[SceneDropHandler(typeof(PrefabAsset), Order = 22)]
internal class PrefabDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        HierarchyPanel.SpawnAssetInScene(payload, null, context.DropPosition);
    }
}

[SceneDropHandler(typeof(Sprite), Order = 23)]
internal class SpriteDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        HierarchyPanel.SpawnAssetInScene(payload, null, context.DropPosition);
    }
}
