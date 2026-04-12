// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

/// <summary>
/// PropertyEditor for Transform fields. Shows a reference picker that lets
/// users select a Transform from a GameObject in the current scene.
/// </summary>
[CustomPropertyEditor(typeof(Transform))]
public class TransformPropertyEditor : PropertyEditor
{
    private static bool _selectorOpen;
    private static Action<object?>? _selectorCallback;

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
                    // Single click: ping the owning GO in hierarchy
                    if (g.IsValid())
                        Selection.Ping(g!.Identifier);
                })
                .OnDoubleClick((onChange, go), (cap, _) =>
                {
                    // Double click: select the GO or open picker
                    if (cap.go.IsValid())
                        Selection.Select(cap.go!);
                    else
                        OpenSelector(cap.onChange);
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
                    .OnClick(onChange, (cb, _) => OpenSelector(cb));
            }
        }
    }

    private static void OpenSelector(Action<object?> onChange)
    {
        _selectorOpen = true;
        _selectorCallback = onChange;
    }

    /// <summary>
    /// Draw the Transform selector modal listing all GameObjects in the scene.
    /// Call from EditorApplication overlay drawing.
    /// </summary>
    public static void DrawSelectorModal(Paper paper)
    {
        if (!_selectorOpen) return;

        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var scene = Runtime.Resources.Scene.Current;
        var allGOs = scene?.AllObjects.Where(g =>
            !g.HideFlags.HasFlag(HideFlags.Hide) &&
            !g.HideFlags.HasFlag(HideFlags.HideAndDontSave))
            .Take(200).ToList() ?? new List<GameObject>();

        // Fullscreen blocker
        paper.Box("tf_sel_overlay")
            .PositionType(PositionType.SelfDirected).Position(0, 0)
            .Size(UnitValue.Stretch(), UnitValue.Stretch())
            .BackgroundColor(Color.FromArgb(120, 0, 0, 0))
            .Layer(Layer.Overlay)
            .OnClick(0, (_, _) => _selectorOpen = false);

        // Modal window
        using (paper.Column("tf_sel_modal")
            .Size(350, 400)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(Layer.Overlay)
            .Enter())
        {
            // Header
            using (paper.Row("tf_sel_header")
                .Height(32).ChildLeft(12).ChildRight(8).RowBetween(8)
                .BackgroundColor(EditorTheme.Neutral200)
                .Enter())
            {
                paper.Box("tf_sel_title").Height(32)
                    .Text("Select Transform", font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                paper.Box("tf_sel_spacer");

                paper.Box("tf_sel_close")
                    .Width(24).Height(24).Rounded(4)
                    .Hovered.BackgroundColor(Color.FromArgb(255, 180, 60, 60)).End()
                    .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) => _selectorOpen = false);
            }

            // List
            using (ScrollView.Begin(paper, "tf_sel_scroll", 350, 360, paddingLeft: 4, paddingRight: 4, paddingTop: 4))
            {
                // None option
                paper.Box("tf_sel_none")
                    .Height(EditorTheme.RowHeight).ChildLeft(8)
                    .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                    .Rounded(3)
                    .Text("None", font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                    .OnClick(0, (_, _) =>
                    {
                        _selectorCallback?.Invoke(null);
                        _selectorOpen = false;
                    });

                for (int i = 0; i < allGOs.Count; i++)
                {
                    var go = allGOs[i];
                    string goIcon = GetGOIcon(go);
                    string path = GetHierarchyPath(go);

                    using (paper.Row($"tf_sel_{i}")
                        .Height(EditorTheme.RowHeight).ChildLeft(12).RowBetween(4)
                        .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                        .Rounded(3)
                        .OnClick(go, (g, _) =>
                        {
                            _selectorCallback?.Invoke(g.Transform);
                            _selectorOpen = false;
                        })
                        .Enter())
                    {
                        paper.Box($"tf_sel_{i}_ico")
                            .Width(14).Height(EditorTheme.RowHeight)
                            .Text(goIcon, font).TextColor(EditorTheme.Ink400)
                            .FontSize(9f).Alignment(TextAlignment.MiddleCenter);

                        paper.Box($"tf_sel_{i}_name")
                            .Height(EditorTheme.RowHeight).Clip()
                            .Text(go.Name, font).TextColor(EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                        if (!string.IsNullOrEmpty(path))
                        {
                            paper.Box($"tf_sel_{i}_path")
                                .Width(UnitValue.Auto).Height(EditorTheme.RowHeight).ChildRight(4)
                                .Text($"({path})", font).TextColor(EditorTheme.Ink300)
                                .FontSize(EditorTheme.FontSize - 4).Alignment(TextAlignment.MiddleRight);
                        }
                    }
                }

                if (allGOs.Count == 0)
                {
                    paper.Box("tf_sel_empty").Height(40)
                        .Text("No GameObjects in scene", font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleCenter);
                }
            }
        }
    }

    private static string GetGOIcon(GameObject go)
    {
        if (go.GetComponent<Camera>() != null) return EditorIcons.Camera;
        if (go.GetComponent<Light>() != null) return EditorIcons.Sun;
        if (go.GetComponent<MeshRenderer>() != null) return EditorIcons.Cube;
        return EditorIcons.Circle;
    }

    private static string GetHierarchyPath(GameObject go)
    {
        var parent = go.Parent;
        if (!parent.IsValid()) return "";
        var parts = new List<string>();
        while (parent.IsValid())
        {
            parts.Add(parent.Name);
            parent = parent.Parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
