using System;
using Prowl.Editor.GUI.Popups;
using Prowl.PaperUI;
using Prowl.OrigamiUI;
using Prowl.Runtime;

using Color = System.Drawing.Color;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// PropertyEditor for AssetRef&lt;T&gt; fields (via the IAssetRef interface).
/// Supports asset references, runtime instances, drag-drop from project/hierarchy/inspector.
/// </summary>
[CustomPropertyEditor(typeof(IAssetRef))]
public class AssetRefPropertyEditor : PropertyEditor
{

    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var assetRef = value as IAssetRef;
        if (assetRef == null) return;

        Type fieldType = assetRef.InstanceType;
        var instance = assetRef.GetInstance() as EngineObject;

        bool isAsset = instance != null && instance.AssetID != Guid.Empty;
        bool isInstance = instance != null && instance.AssetID == Guid.Empty;
        string suffix = isAsset ? instance!.GetType().Name : isInstance ? "Instance" : fieldType.Name;
        string displayName = instance != null ? $"{instance.Name} ({suffix})" : $"None ({fieldType.Name})";
        string icon = isAsset ? EditorIcons.Cube : isInstance ? EditorIcons.CircleDot : EditorIcons.Circle;
        var iconColor = isAsset ? EditorTheme.Purple400 : isInstance ? EditorTheme.Ink500 : EditorTheme.Ink300;

        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(4).Enter())
        {
            // Label
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            // Check for compatible drags
            bool isDragTarget = false;
            if (DragDrop.IsDragging)
            {
                if (DragDrop.Payload is AssetDragPayload adp && adp.AssetType != null && fieldType.IsAssignableFrom(adp.AssetType))
                    isDragTarget = true;
                else if (DragDrop.Payload is GameObjectDragPayload && typeof(GameObject).IsAssignableFrom(fieldType))
                    isDragTarget = true;
                else if (DragDrop.Payload is GameObjectDragPayload && typeof(MonoBehaviour).IsAssignableFrom(fieldType))
                    isDragTarget = true; // Will search GO for matching component
                else if (DragDrop.Payload is ComponentDragPayload cdp && fieldType.IsAssignableFrom(cdp.Component.GetType()))
                    isDragTarget = true;
            }

            var fieldEl = paper.Row($"{id}_field")
                .Height(EditorTheme.RowHeight)
                .BackgroundColor(isDragTarget ? Color.FromArgb(60, EditorTheme.Purple400) : EditorTheme.Neutral300)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(3).ChildLeft(4).ChildRight(2).RowBetween(2)
                .BorderColor(isDragTarget ? EditorTheme.Purple400 : EditorTheme.Ink200).BorderWidth(1)
                .OnClick((fieldType, assetRef, onChange, instance, isAsset, isInstance), (cap, e) =>
                {
                    if (cap.instance != null)
                        Selection.Ping(cap.instance!.AssetID);
                })
                .OnDoubleClick((fieldType, assetRef, onChange, instance, isAsset, isInstance), (cap, _) =>
                {
                    if (cap.isAsset)
                    {
                        // Asset → focus in project
                        Selection.Ping(cap.instance!.AssetID);
                    }
                    else if (cap.isInstance)
                    {
                        // Instance → select it in inspector
                        Selection.Select(cap.instance!);
                    }
                    else
                    {
                        // None → open selector
                        OpenAssetSelector(cap.fieldType, asset => { cap.assetRef.SetInstance(asset); cap.onChange(cap.assetRef); });
                    }
                });

            using (fieldEl.Enter())
            {
                // Accept drops
                HandleDrops(paper, assetRef, fieldType, onChange);

                // Icon
                paper.Box($"{id}_ico")
                    .Width(16).Height(EditorTheme.RowHeight)
                    .IsNotInteractable()
                    .Text(icon, font).TextColor(iconColor)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                // Name
                paper.Box($"{id}_name")
                    .Height(EditorTheme.RowHeight).Clip()
                    .IsNotInteractable()
                    .Text(displayName, font)
                    .TextColor(instance != null ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                // Picker button (circle)
                paper.Box($"{id}_pick")
                    .Width(20).Height(EditorTheme.RowHeight)
                    .Text(EditorIcons.CircleDot, font).TextColor(EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                    .Rounded(3)
                    .OnClick((fieldType, assetRef, onChange), (cap, e) =>
                    {
                        e.StopPropagation();
                        OpenAssetSelector(cap.Item1, asset => { cap.Item2.SetInstance(asset); cap.Item3(cap.Item2); });
                    });
            }
        }
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
