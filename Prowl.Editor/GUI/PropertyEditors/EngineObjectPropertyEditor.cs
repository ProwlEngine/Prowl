using System;

using Prowl.Editor.GUI.Popups;
using Prowl.PaperUI;
using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;

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
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var eo = value as EngineObject;
        // Use the declared field type for the selector
        Type fieldType = _lastFieldType ?? typeof(EngineObject);
        _lastFieldType = null; // consume it

        bool isAsset = eo != null && eo.AssetID != Guid.Empty;
        string suffix = eo != null ? (isAsset ? eo.GetType().Name : "Instance") : fieldType.Name;
        string displayName = eo != null ? $"{eo.Name} ({suffix})" : $"None ({fieldType.Name})";
        string icon = eo != null ? EditorIcons.Cube : EditorIcons.Circle;

        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(4).Enter())
        {
            // Label
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            // Check if a compatible payload is being dragged over this field
            bool isDragTarget = false;
            if (DragDrop.IsDragging)
            {
                if (DragDrop.Payload is AssetDragPayload adp2 && adp2.AssetType != null && fieldType.IsAssignableFrom(adp2.AssetType))
                    isDragTarget = true;
                else if (DragDrop.Payload is GameObjectDragPayload && (typeof(GameObject).IsAssignableFrom(fieldType) || typeof(MonoBehaviour).IsAssignableFrom(fieldType)))
                    isDragTarget = true;
                else if (DragDrop.Payload is ComponentDragPayload cdp2 && fieldType.IsAssignableFrom(cdp2.Component.GetType()))
                    isDragTarget = true;
            }

            // Object field row
            var fieldEl = paper.Row($"{id}_field")
                .Height(EditorTheme.RowHeight)
                .BackgroundColor(isDragTarget ? System.Drawing.Color.FromArgb(60, EditorTheme.Purple400) : EditorTheme.Neutral300)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(3).ChildLeft(4).ChildRight(2).RowBetween(2)
                .BorderColor(isDragTarget ? EditorTheme.Purple400 : EditorTheme.Ink200).BorderWidth(1)
                .OnClick((eo, isAsset), (cap, e) =>
                {
                    // Single click: ping the asset (highlight without selecting)
                    if (cap.isAsset && cap.eo != null)
                        Selection.Ping(cap.eo.AssetID);
                })
                .OnDoubleClick((fieldType, onChange, eo), (cap, _) =>
                {
                    // Double click: select the instance or open selector
                    if (cap.eo != null)
                        Selection.Select(cap.eo);
                    else
                        OpenAssetSelector(cap.fieldType, cap.onChange);
                });

            using (fieldEl.Enter())
            {
                // Accept asset drop
                var assetDrop = DragDrop.AcceptDrop<AssetDragPayload>(paper.IsParentHovered,
                    dp => dp.AssetType != null && fieldType.IsAssignableFrom(dp.AssetType));
                if (assetDrop != null)
                {
                    var droppedAsset = Runtime.AssetDatabase.Get(assetDrop.AssetGuid);
                    if (droppedAsset != null)
                        onChange(droppedAsset);
                }

                // Accept GameObject drop
                if (!DragDrop.IsDragging && paper.IsParentHovered && DragDrop.Payload is GameObjectDragPayload goDrop)
                {
                    var go = goDrop.GameObjects.Length > 0 ? goDrop.GameObjects[0] : null;
                    if (go != null)
                    {
                        if (typeof(GameObject).IsAssignableFrom(fieldType))
                        {
                            onChange(go);
                        }
                        else if (typeof(MonoBehaviour).IsAssignableFrom(fieldType))
                        {
                            // Search GO for matching component
                            var comp = go.GetComponent(fieldType);
                            if (comp != null) onChange(comp);
                        }
                    }
                    DragDrop.EndDrag();
                }

                // Accept Component drop
                if (!DragDrop.IsDragging && paper.IsParentHovered && DragDrop.Payload is ComponentDragPayload compDrop)
                {
                    if (fieldType.IsAssignableFrom(compDrop.Component.GetType()))
                        onChange(compDrop.Component);
                    DragDrop.EndDrag();
                }

                // Icon
                paper.Box($"{id}_ico")
                    .Width(16).Height(EditorTheme.RowHeight)
                    .IsNotInteractable()
                    .Text(icon, font)
                    .TextColor(eo != null ? EditorTheme.Purple400 : EditorTheme.Ink300)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                // Name
                paper.Box($"{id}_name")
                    .Height(EditorTheme.RowHeight).Clip()
                    .IsNotInteractable()
                    .Text(displayName, font)
                    .TextColor(eo != null ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                // Picker circle button
                paper.Box($"{id}_pick")
                    .Width(20).Height(EditorTheme.RowHeight)
                    .Text(EditorIcons.CircleDot, font).TextColor(EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                    .Rounded(3)
                    .OnClick((fieldType, onChange), (cap, _) => OpenAssetSelector(cap.Item1, cap.Item2));
            }
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
        // Scene types (GameObject, MonoBehaviour subclasses) -> Scene tab
        // Asset types (Mesh, Material, etc.) -> Assets tab
        bool isSceneType = typeof(GameObject).IsAssignableFrom(type) || typeof(MonoBehaviour).IsAssignableFrom(type);
        var tabs = isSceneType ? SelectorTabs.Scene : SelectorTabs.Assets;

        SelectorModal.Open($"Select {type.Name}", type, tabs, onChange);
    }
}
