// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Popups;
using Prowl.PaperUI;
using Prowl.OrigamiUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;
using Prowl.Editor.Inspector;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// PropertyEditor for Transform fields. Shows a reference picker that lets
/// users select a Transform from a GameObject in the current scene.
/// </summary>
[CustomPropertyEditor(typeof(Transform))]
public class TransformPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var transform = value as Transform;
        var go = transform?.GameObject;
        bool hasValue = go.IsValid();

        string displayName = hasValue ? $"{go!.Name} (Transform)" : "None (Transform)";
        string icon = hasValue ? EditorIcons.ArrowsUpDownLeftRight : EditorIcons.Circle;

        // Check if a compatible payload is being dragged over this field
        bool isDragTarget = DragDrop.IsDragging && DragDrop.Payload is GameObjectDragPayload;

        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(4).Enter())
        {
            // Label
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            // Field row
            var fieldEl = paper.Row($"{id}_field")
                .Height(EditorTheme.RowHeight)
                .BackgroundColor(isDragTarget ? Color.FromArgb(60, EditorTheme.Purple400) : EditorTheme.Neutral300)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(3).ChildLeft(4).ChildRight(2).RowBetween(2)
                .BorderColor(isDragTarget ? EditorTheme.Purple400 : EditorTheme.Ink200).BorderWidth(1)
                .OnClick(go, (g, e) =>
                {
                    if (g.IsValid())
                        Selection.Ping(g!.Identifier);
                })
                .OnDoubleClick((onChange, go), (cap, _) =>
                {
                    if (cap.go.IsValid())
                        Selection.Select(cap.go!);
                    else
                        SelectorModal.Open("Select Transform", typeof(Transform), SelectorTabs.Scene, cap.onChange);
                });

            using (fieldEl.Enter())
            {
                // Accept GameObject drop → extract Transform
                if (!DragDrop.IsDragging && paper.IsParentHovered && DragDrop.Payload is GameObjectDragPayload goDrop)
                {
                    var droppedGO = goDrop.GameObjects.Length > 0 ? goDrop.GameObjects[0] : null;
                    if (droppedGO != null)
                        onChange(droppedGO.Transform);
                    DragDrop.EndDrag();
                }

                // Icon
                paper.Box($"{id}_ico")
                    .Width(16).Height(EditorTheme.RowHeight)
                    .IsNotInteractable()
                    .Text(icon, font)
                    .TextColor(hasValue ? EditorTheme.Purple400 : EditorTheme.Ink300)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                // Name
                paper.Box($"{id}_name")
                    .Height(EditorTheme.RowHeight).Clip()
                    .IsNotInteractable()
                    .Text(displayName, font)
                    .TextColor(hasValue ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                // Picker button
                paper.Box($"{id}_pick")
                    .Width(20).Height(EditorTheme.RowHeight)
                    .Text(EditorIcons.CircleDot, font).TextColor(EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                    .Rounded(3)
                    .OnClick(onChange, (cb, _) =>
                        SelectorModal.Open("Select Transform", typeof(Transform), SelectorTabs.Scene, cb));
            }
        }
    }
}
