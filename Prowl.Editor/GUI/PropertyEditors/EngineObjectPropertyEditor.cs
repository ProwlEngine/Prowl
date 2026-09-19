using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// PropertyEditor for all EngineObject-derived types (Mesh, Material, Shader, Texture2D, etc).
/// Shows an object reference field with name, icon, and asset selector modal.
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

        bool isAsset = eo != null && eo.AssetID != Guid.Empty;

        PropertyGridUtils.ObjectField(paper, id, label,
            eo != null ? EditorIcons.Cube : EditorIcons.Circle,
            eo != null ? EditorTheme.Purple400 : EditorTheme.Ink300,
            PropertyGridUtils.DescribeObjectRef(eo, fieldType),
            eo != null,
            EditorGUI.IsCompatibleDragTarget(fieldType),
            // Single click pings the asset, double click selects the instance or opens the selector.
            onClick: () => { if (isAsset) Selection.Ping(eo!.AssetID); },
            onDoubleClick: () =>
            {
                if (eo != null) Selection.Select(eo);
                else OpenAssetSelector(fieldType, onChange);
            },
            onPick: () => OpenAssetSelector(fieldType, onChange),
            acceptDrops: () => HandleDrops(paper, fieldType, onChange));
    }

    private static void HandleDrops(Paper paper, Type fieldType, Action<object?> onChange)
    {
        // This editor only handles plain (non-AssetRef) fields, so assigning an asset here serializes a
        // copy into the scene instead of a reference. Confirm with the user before committing that copy.
        var assetDrop = DragDrop.AcceptDrop<AssetDragPayload>(paper.IsParentHovered,
            dp => dp.AssetType != null && fieldType.IsAssignableFrom(dp.AssetType));
        if (assetDrop != null)
        {
            var droppedAsset = Runtime.AssetDatabase.Get(assetDrop.AssetGuid);
            if (droppedAsset != null)
                ConfirmAssetCopy(assetDrop.AssetName, droppedAsset, onChange);
        }

        if (!DragDrop.IsDragging && paper.IsParentHovered && DragDrop.Payload is GameObjectDragPayload goDrop)
        {
            var go = goDrop.GameObjects.Length > 0 ? goDrop.GameObjects[0] : null;
            if (go != null)
            {
                if (typeof(GameObject).IsAssignableFrom(fieldType))
                    onChange(go);
                else if (typeof(MonoBehaviour).IsAssignableFrom(fieldType) && go.GetComponent(fieldType) is { } comp)
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

    /// <summary>
    /// Warns that the target field is not an AssetRef, so assigning an asset stores a copy
    /// inside the scene rather than a reference. Only applies the value if the user confirms.
    /// </summary>
    private static void ConfirmAssetCopy(string assetName, object? asset, Action<object?> onChange)
    {
        var dialog = new DialogModal { Title = "Field Is Not an Asset Reference", Width = 440 };
        dialog.DrawContent = p =>
        {
            Origami.Label(p, "ac_l1", $"'{assetName}' is an asset, but this field is not an AssetRef.").Show();
            Origami.Label(p, "ac_l2", "Assigning it stores a copy of the asset inside the scene").Show();
            Origami.Label(p, "ac_l3", "rather than a reference to the original asset.").Show();
            Origami.Label(p, "ac_l4", "Are you sure you want to continue?").Show();
        };
        dialog.Button("Assign Copy", () => { onChange(asset); Modal.Pop(); }, OrigamiVariant.Warning);
        dialog.Button("Cancel", Modal.Pop);
        Modal.Push(dialog);
    }

    internal static void OpenAssetSelector(Type type, Action<object?> onChange)
    {
        // Scene types (GameObject, MonoBehaviour subclasses) -> Scene tab
        // Asset types (Mesh, Material, etc.) -> Assets tab
        bool isSceneType = typeof(GameObject).IsAssignableFrom(type) || typeof(MonoBehaviour).IsAssignableFrom(type);
        var tabs = isSceneType ? SelectorTabs.Scene : SelectorTabs.Assets;

        SelectorModal.Open($"Select {type.Name}", type, tabs, selected =>
        {
            // Like a drag, picking an asset into a non-AssetRef field stores a copy in the
            // scene rather than a reference. Scene selections and "None" pass through unchanged.
            if (selected is EngineObject eo && eo.AssetID != Guid.Empty)
                ConfirmAssetCopy(eo.Name, eo, onChange);
            else
                onChange(selected);
        });
    }
}
