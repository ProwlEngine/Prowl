using System;
using Prowl.Editor.GUI.Popups;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
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

        // Match the Origami PropertyGrid standard row (metrics, muted label, horizontal padding).
        var m = Origami.Current.Metrics;
        float rh = m.RowHeight;

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

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(rh).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).RowBetween(m.Padding).Enter())
        {
            // Label
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(rh).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .IsNotInteractable()
                    .Text(label, font).TextColor(Origami.Current.Ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            bool isDragTarget = EditorGUI.IsCompatibleDragTarget(fieldType);

            var fieldEl = paper.Row($"{id}_field")
                .Height(rh)
                .BackgroundColor(isDragTarget ? Color.FromArgb(60, EditorTheme.Purple400) : EditorTheme.Glass)
                .Hovered.BorderColor(EditorTheme.BorderStrong).End()
                .Rounded(6).Padding(m.SpacingLarge, m.PaddingSmall, 0, 0).RowBetween(m.SpacingLarge)
                .BorderColor(isDragTarget ? EditorTheme.Purple400 : EditorTheme.BorderSoft).BorderWidth(1)
                .OnClick((fieldType, assetRef, onChange, instance, isAsset, isInstance), (cap, e) =>
                {
                    if (cap.instance != null)
                        Selection.Ping(cap.instance!.AssetID);
                })
                .OnDoubleClick((fieldType, assetRef, onChange, instance, isAsset, isInstance), (cap, _) =>
                {
                    if (cap.isAsset)
                    {
                        // Asset -> focus in project
                        Selection.Ping(cap.instance!.AssetID);
                    }
                    else if (cap.isInstance)
                    {
                        // Instance -> select it in inspector
                        Selection.Select(cap.instance!);
                    }
                    else
                    {
                        // None -> open selector
                        OpenAssetSelector(cap.fieldType, asset => { cap.assetRef.SetInstance(asset); cap.onChange(cap.assetRef); });
                    }
                });

            using (fieldEl.Enter())
            {
                // Accept drops
                HandleDrops(paper, assetRef, fieldType, onChange);

                // Leading type icon (no chip background)
                paper.Box($"{id}_ico")
                    .Width(UnitValue.Auto).Height(rh).IsNotInteractable()
                    .Text(icon, font).TextColor(iconColor)
                    .FontSize(11f).Alignment(TextAlignment.MiddleCenter);

                // Name
                paper.Box($"{id}_name")
                    .Width(UnitValue.Stretch()).Height(rh).Clip()
                    .IsNotInteractable()
                    .Text(displayName, font)
                    .TextColor(instance != null ? EditorTheme.Ink500 : EditorTheme.Ink200)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                // Picker button
                paper.Box($"{id}_pick")
                    .Width(18).Height(18).Rounded(4).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .Text(EditorIcons.CircleDot, font).TextColor(EditorTheme.Ink200)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .Hovered.BackgroundColor(EditorTheme.Hover).End()
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
