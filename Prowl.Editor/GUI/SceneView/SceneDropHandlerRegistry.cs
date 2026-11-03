using System;

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
    public Scene Scene;
    public EditorCamera Camera;
    public Float2 MouseLocal;
    public Float2 PanelSize;
}

/// <summary> Interface for handling asset drops in the scene view. Implementations are discovered via SceneDropHandlerAttribute. </summary>
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
        AssetEntry? entry = EditorAssetBackend.Instance?.GetEntry(payload.AssetGuid);
        if (entry != null) EditorSceneManager.ConfirmSceneChange(() => EditorSceneManager.OpenScene(entry.Path));
    }
}

[SceneDropHandler(typeof(Material), Order = 10)]
internal class MaterialDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop on object to assign material";
    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        GameObject? hitGO = SceneViewPanel.PickObjectAt(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        if (hitGO.IsNotValid()) return;
        var mat = Runtime.AssetDatabase.Get(payload.AssetGuid) as Material;
        if (mat.IsNotValid()) return;
        MeshRenderer? meshRenderer = hitGO.GetComponent<MeshRenderer>();
        if (meshRenderer.IsValid()) { meshRenderer.Material = mat; EditorSceneManager.MarkDirty(); }
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
