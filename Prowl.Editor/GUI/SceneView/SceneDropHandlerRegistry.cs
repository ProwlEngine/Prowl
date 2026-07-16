using System;

using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Theming;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class SceneDropHandlerAttribute : Attribute
{
    public Type TargetType { get; }
    public int Order { get; set; }
    public SceneDropHandlerAttribute(Type targetType) => TargetType = targetType;
}

public struct SceneDropContext
{
    public Scene Scene;
    public EditorCamera Camera;
    public Float2 MouseLocal;
    public Float2 PanelSize;
}

public interface ISceneDropHandler
{
    string DropHint { get; }
    void Handle(AssetDragPayload payload, SceneDropContext context);
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
        var entry = EditorAssetBackend.Instance?.GetEntry(payload.AssetGuid);
        if (entry != null) EditorSceneManager.OpenScene(entry.Path);
    }
}

[SceneDropHandler(typeof(Material), Order = 10)]
internal class MaterialDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop on object to assign material";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        var hitGO = SceneViewPanel.PickObjectAt(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        if (hitGO == null) return;
        var mat = Runtime.AssetDatabase.Get(payload.AssetGuid) as Material;
        if (mat == null) return;
        var meshRenderer = hitGO.GetComponent<MeshRenderer>();
        if (meshRenderer != null) { meshRenderer.Material = mat; EditorSceneManager.IsDirty = true; }
    }
}

[SceneDropHandler(typeof(Model), Order = 20)]
internal class ModelDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        Float3 dropPos = SceneViewPanel.GetDropPosition(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}

[SceneDropHandler(typeof(Mesh), Order = 21)]
internal class MeshDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        Float3 dropPos = SceneViewPanel.GetDropPosition(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}

[SceneDropHandler(typeof(PrefabAsset), Order = 22)]
internal class PrefabDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        Float3 dropPos = SceneViewPanel.GetDropPosition(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}

[SceneDropHandler(typeof(Sprite), Order = 23)]
internal class SpriteDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        Float3 dropPos = SceneViewPanel.GetDropPosition(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}
