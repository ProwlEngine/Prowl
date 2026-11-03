using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// PropertyEditor for every EngineObject field: assets (Mesh, Material, Shader, Texture2D, ...), GameObjects and
/// components. A database asset is stored as a reference, so dropping or picking one assigns the asset itself.
/// </summary>
[CustomPropertyEditor(typeof(EngineObject))]
public class EngineObjectPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var eo = value as EngineObject;
        // Use the declared field type for the selector
        Type fieldType = _lastFieldType ?? typeof(EngineObject);
        _lastFieldType = null; // consume it

        var asset = eo as Asset;
        bool isAsset = asset is { IsFromDatabase: true };
        bool missing = asset is { IsMissing: true };

        PropertyGridUtils.ObjectField(paper, id, label,
            eo.IsValid() ? EditorIcons.Cube : EditorIcons.Circle,
            missing ? EditorTheme.Red400 : eo.IsValid() ? EditorTheme.Purple400 : EditorTheme.Ink300,
            PropertyGridUtils.DescribeObjectRef(eo, fieldType),
            eo.IsValid(),
            EditorGUI.IsCompatibleDragTarget(fieldType),
            // Click pings what is assigned or opens the selector when empty, double click selects what is assigned.
            onClick: () =>
            {
                if (eo.IsNotValid()) OpenAssetSelector(fieldType, onChange);
                else if (isAsset) Selection.Ping(asset!.AssetID);
                else if (PingTarget(eo) is { } go) Selection.Ping(go.Identifier);
            },
            onDoubleClick: () => { if (eo.IsValid()) Selection.Select(eo); },
            onPick: () => OpenAssetSelector(fieldType, onChange),
            acceptDrops: () => HandleDrops(paper, fieldType, onChange));
    }

    // A scene object field pings the GameObject in the Hierarchy.
    private static GameObject? PingTarget(EngineObject eo) => eo switch
    {
        GameObject go when go.IsValid() => go,
        Component c when c.GameObject.IsValid() => c.GameObject,
        _ => null,
    };

    private static void HandleDrops(Paper paper, Type fieldType, Action<object?> onChange)
    {
        AssetDragPayload? assetDrop = DragDrop.AcceptDrop<AssetDragPayload>(paper.IsParentHovered,
            dp => dp.AssetType != null && fieldType.IsAssignableFrom(dp.AssetType));
        if (assetDrop != null && AssetDatabase.Get(assetDrop.AssetGuid) is { } droppedAsset)
            onChange(droppedAsset);

        if (!DragDrop.IsDragging && paper.IsParentHovered && DragDrop.Payload is GameObjectDragPayload goDrop)
        {
            GameObject? go = goDrop.GameObjects.Length > 0 ? goDrop.GameObjects[0] : null;
            if (go.IsValid())
            {
                if (typeof(GameObject).IsAssignableFrom(fieldType))
                    onChange(go);
                else if (typeof(Component).IsAssignableFrom(fieldType) && go.GetComponent(fieldType) is { } comp)
                    onChange(comp);
            }
            DragDrop.EndDrag();
        }

        if (!DragDrop.IsDragging && paper.IsParentHovered && DragDrop.Payload is ComponentDragPayload compDrop)
        {
            if (fieldType.IsAssignableFrom(compDrop.Component.GetType()))
                onChange(compDrop.Component);
            DragDrop.EndDrag();
        }
    }

    /// <summary>
    /// Stores the declared field type so we search for the right asset type.
    /// Called by PropertyGrid before OnGUI.
    /// </summary>
    [ThreadStatic] private static Type? _lastFieldType;
    public static void SetFieldType(Type type) => _lastFieldType = type;

    internal static void OpenAssetSelector(Type type, Action<object?> onChange)
    {
        // Scene types (GameObject, Component subclasses) -> Scene tab
        // Asset types (Mesh, Material, etc.) -> Assets tab
        bool isSceneType = typeof(GameObject).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type);
        SelectorTabs tabs = isSceneType ? SelectorTabs.Scene : SelectorTabs.Assets;
        SelectorModal.Open($"Select {type.Name}", type, tabs, onChange);
    }
}
