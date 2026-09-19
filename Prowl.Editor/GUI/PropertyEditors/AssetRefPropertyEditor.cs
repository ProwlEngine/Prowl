using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// PropertyEditor for AssetRef<T> fields (via the IAssetRef interface).
/// Supports asset references, runtime instances, drag-drop from project/hierarchy/inspector.
/// </summary>
[CustomPropertyEditor(typeof(IAssetRef))]
public class AssetRefPropertyEditor : PropertyEditor
{

    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        if (value is not IAssetRef assetRef) return;

        Type fieldType = assetRef.InstanceType;
        var instance = assetRef.GetInstance() as EngineObject;

        bool isAsset = instance != null && instance.AssetID != Guid.Empty;
        bool isInstance = instance != null && instance.AssetID == Guid.Empty;
        void Pick() => OpenAssetSelector(fieldType, asset => { assetRef.SetInstance(asset); onChange(assetRef); });

        PropertyGridUtils.ObjectField(paper, id, label,
            isAsset ? EditorIcons.Cube : isInstance ? EditorIcons.CircleDot : EditorIcons.Circle,
            isAsset ? EditorTheme.Purple400 : isInstance ? EditorTheme.Ink500 : EditorTheme.Ink300,
            PropertyGridUtils.DescribeObjectRef(instance, fieldType),
            instance != null,
            EditorGUI.IsCompatibleDragTarget(fieldType),
            onClick: () => { if (instance != null) Selection.Ping(instance.AssetID); },
            // Double click focuses an asset in the project, selects an instance, or opens the selector when empty.
            onDoubleClick: () =>
            {
                if (isAsset) Selection.Ping(instance!.AssetID);
                else if (isInstance) Selection.Select(instance!);
                else Pick();
            },
            onPick: Pick,
            acceptDrops: () => HandleDrops(paper, assetRef, fieldType, onChange));
    }

    private static void HandleDrops(Paper paper, IAssetRef assetRef, Type fieldType, Action<object?> onChange)
    {
        if (!paper.IsParentHovered || DragDrop.IsDragging) return;

        // Asset drop
        if (DragDrop.Payload is AssetDragPayload adp && adp.AssetType != null && fieldType.IsAssignableFrom(adp.AssetType))
        {
            var droppedAsset = Runtime.AssetDatabase.Get(adp.AssetGuid);
            if (droppedAsset != null)
            {
                assetRef.SetInstance(droppedAsset);
                onChange(assetRef);
            }
            DragDrop.EndDrag();
            return;
        }

        // GameObject drop
        if (DragDrop.Payload is GameObjectDragPayload goDrop && goDrop.GameObjects.Length > 0)
        {
            var go = goDrop.GameObjects[0];

            if (typeof(GameObject).IsAssignableFrom(fieldType))
            {
                // Direct GO reference
                assetRef.SetInstance(go);
                onChange(assetRef);
            }
            else if (typeof(MonoBehaviour).IsAssignableFrom(fieldType))
            {
                // Search GO for matching component
                var comp = go.GetComponent(fieldType);
                if (comp != null)
                {
                    assetRef.SetInstance(comp);
                    onChange(assetRef);
                }
            }
            DragDrop.EndDrag();
            return;
        }

        // Component drop
        if (DragDrop.Payload is ComponentDragPayload cdp && fieldType.IsAssignableFrom(cdp.Component.GetType()))
        {
            assetRef.SetInstance(cdp.Component);
            onChange(assetRef);
            DragDrop.EndDrag();
            return;
        }
    }

    private static void OpenAssetSelector(Type type, Action<object?> onChange)
    {
        SelectorModal.Open($"Select {type.Name}", type, SelectorTabs.Assets, onChange);
    }

}
