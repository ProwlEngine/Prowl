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
    public static bool IsSpawnable(Asset? asset) => asset is Mesh or PrefabAsset or Sprite;

    /// <summary>Drop every asset in <paramref name="payload"/>.</summary>
    public static void Drop(AssetDragPayload payload, SceneDropContext context)
    {
        SceneDropContext ctx = context.Freeze();
        (List<AssetDragPayload> spawn, List<AssetDragPayload> other) = Split(payload);
        foreach (AssetDragPayload item in other)
            EditorRegistries.FindSceneDropHandler(item.AssetType)?.Handle(item, ctx);
        foreach ((AssetDragPayload item, Float3 pos) in Layout(spawn, ctx.DropPosition))
            EditorRegistries.FindSceneDropHandler(item.AssetType)?.Handle(item, ctx.At(pos));
    }

    /// <summary>The objects a drop of <paramref name="payload"/> at <paramref name="drop"/> spawns, each with its root position.</summary>
    public static List<(AssetDragPayload Item, Float3 Position)> Layout(AssetDragPayload payload, Float3 drop)
        => Layout(Split(payload).Spawn, drop);

    private static (List<AssetDragPayload> Spawn, List<AssetDragPayload> Other) Split(AssetDragPayload payload)
    {
        var spawn = new List<AssetDragPayload>();
        var other = new List<AssetDragPayload>();
        if (!payload.IsMulti)
        {
            (IsSpawnable(Runtime.AssetDatabase.Get(payload.AssetGuid)) ? spawn : other).Add(payload);
            return (spawn, other);
        }
        for (int i = 0; i < payload.AssetGuids.Length; i++)
        {
            Asset? asset = Runtime.AssetDatabase.Get(payload.AssetGuids[i]);
            string path = payload.AssetPaths[i];
            var item = new AssetDragPayload(payload.AssetGuids[i], System.IO.Path.GetFileNameWithoutExtension(path), path, asset.IsValid() ? asset.GetType() : null);
            (IsSpawnable(asset) ? spawn : other).Add(item);
        }
        return (spawn, other);
    }

    // One cell per object, sized by the largest footprint so neighbours never overlap; each footprint (not its
    // pivot) is centred in its cell. A single object goes at the drop point.
    private static List<(AssetDragPayload, Float3)> Layout(List<AssetDragPayload> items, Float3 drop)
    {
        var result = new List<(AssetDragPayload, Float3)>(items.Count);
        if (items.Count == 1) result.Add((items[0], drop));
        if (items.Count <= 1) return result;

        var centers = new Float3[items.Count];
        float cell = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            if (!TryGetLocalBounds(items[i].AssetGuid, out Float3 min, out Float3 max)) continue;
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
            result.Add((items[i], drop + new Float3(x, 0f, z)));
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
                return SceneViewPanel.TryGetWorldBounds(go, out min, out max);
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
        Float3 dropPos = context.DropPosition;
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}

[SceneDropHandler(typeof(PrefabAsset), Order = 22)]
internal class PrefabDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        Float3 dropPos = context.DropPosition;
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}

[SceneDropHandler(typeof(Sprite), Order = 23)]
internal class SpriteDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        Float3 dropPos = context.DropPosition;
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}
