using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Prefabs;
using Prowl.Editor.Widgets;
using Prowl.Editor.Widgets.Popups;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Draws the inspector for a selected GameObject: header, transform, components, add component button.
/// </summary>
public static class GameObjectInspector
{
    public static void Draw(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        // Prefab header bar (if this GO is a prefab instance)
        if (go.IsPrefabInstance)
            DrawPrefabHeader(paper, font, go);

        DrawHeader(paper, font, go);
        EditorGUI.Separator(paper, "gi_sep_header");
        if (go.RectTransform != null)
        {
            DrawRectTransform(paper, font, go);
        }
        else
        {
            DrawTransform(paper, font, go);
        }

        EditorGUI.Separator(paper, "gi_sep_transform");
        DrawComponents(paper, font, go);

        // Only show Add Component if not a prefab instance (structure is fixed)
        if (!go.IsPrefabInstance || Application.IsPlaying)
            DrawAddComponentButton(paper, font, go);

        // Detect GO-level overrides (Name, Tag, Transform, etc.)
        if (go.IsPrefabInstance)
            PrefabUtility.DetectGOOverrides(go);

        paper.Box("gi_bottom_pad").Height(20);
    }

    // ================================================================
    //  Header: Name, Enabled, Tag, Layer
    // ================================================================

    private static void DrawHeader(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var goId = go.Identifier;

        // Enabled toggle + Name + Static
        using (paper.Row("gi_header")
            .Height(EditorTheme.RowHeight)
            .Margin(0, 6)
            .RowBetween(6)
            .Enter())
        {
            paper.Box("gi_icon").Margin(6, 6, 0, 6).FontSize(EditorTheme.FontSize * 1.5f).Width(UnitValue.Auto).Text(EditorIcons.Cube, font);

            Origami.Checkbox(paper, "gi_enabled", go.Enabled,
                v => { Undo.RecordGameObjectChange(go, "Toggle Enabled", go.Enabled, v, (g, x) => g.Enabled = x); go.Enabled = v; })
                .NoLabel().Show();

            Origami.TextField(paper, "gi_name", go.Name, v =>
                {
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        Undo.RecordGameObjectChange(go, "Change Name", go.Name, v, (g, x) => g.Name = x, coalesce: true);
                        go.Name = v;
                    }
                })
                .Width(UnitValue.Stretch())
                .Show();

            Origami.Checkbox(paper, "gi_static", go.IsStatic,
                v => { Undo.RecordGameObjectChange(go, "Toggle Static", go.IsStatic, v, (g, x) => g.IsStatic = x); go.IsStatic = v; })
                .LabelRight("Static").Show();
        }

        // Tag + Layer row (dropdowns)
        using (paper.Row("gi_tag_layer")
            .Height(22)
            .RowBetween(6)
            .Enter())
        {
            // Tag dropdown
            var tagNames = TagLayerManager.tags.ToArray();
            int tagIdx = TagLayerManager.tags.IndexOf(go.Tag);
            if (tagIdx < 0) tagIdx = 0;

            DrawInlineLabeled(paper, "gi_tag_row", "Tag", font, () =>
                Origami.Dropdown(paper, "gi_tag", tagIdx,
                    v =>
                    {
                        if (v >= 0 && v < tagNames.Length)
                        {
                            var newTag = tagNames[v];
                            Undo.RecordGameObjectChange(go, "Change Tag", go.Tag, newTag, (g, x) => g.Tag = x);
                            go.Tag = newTag;
                        }
                    }, tagNames)
                    .Show());

            // Layer dropdown (filter out empty entries)
            var allLayers = TagLayerManager.layers;
            var layerNames = new List<string>();
            var layerIndices = new List<int>();
            for (int i = 0; i < allLayers.Length; i++)
            {
                if (!string.IsNullOrEmpty(allLayers[i]))
                {
                    layerNames.Add(allLayers[i]);
                    layerIndices.Add(i);
                }
            }

            int selectedLayerIdx = layerIndices.IndexOf(go.LayerIndex);
            if (selectedLayerIdx < 0) selectedLayerIdx = 0;

            DrawInlineLabeled(paper, "gi_layer_row", "Layer", font, () =>
                Origami.Dropdown(paper, "gi_layer", selectedLayerIdx,
                    v =>
                    {
                        if (v >= 0 && v < layerIndices.Count)
                        {
                            var newIdx = layerIndices[v];
                            Undo.RecordGameObjectChange(go, "Change Layer", go.LayerIndex, newIdx, (g, x) => g.LayerIndex = x);
                            go.LayerIndex = newIdx;
                        }
                    }, layerNames.ToArray())
                    .Show());
        }
    }

    /// <summary>
    /// Renders an auto-width label followed by the caller's control filling the remainder.
    /// Used for the Tag/Layer header where we want compact label gutters, not the inspector's
    /// fixed-width LabelWidth gutter.
    /// </summary>
    private static void DrawInlineLabeled(Paper paper, string id, string label,
        Prowl.Scribe.FontFile font, Action drawControl)
    {
        using (paper.Row(id).Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).RowBetween(4).Enter())
        {
            paper.Box($"{id}_lbl")
                .Width(UnitValue.Auto).Height(EditorTheme.RowHeight)
                .Margin(4, 4, 0, 0)
                .IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize);

            using (paper.Box($"{id}_ctl").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).Enter())
            {
                drawControl();
            }
        }
    }

    // ================================================================
    //  Transform
    // ================================================================

    private static void DrawTransform(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var t = go.Transform;
        var goId = go.Identifier;

        paper.Box("gi_transform_header").Height(22).ChildLeft(8)
            .Text($"{EditorIcons.ArrowsUpDownLeftRight}  Transform", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        // Position
        var pos = t.LocalPosition;
        InspectorRow.Draw(paper, "gi_pos", "Position", () =>
            Origami.Float3Field(paper, "gi_pos_vf", pos, v => { Undo.RecordGameObjectChange(go, "Change Position", t.LocalPosition, v, (g, x) => g.Transform.LocalPosition = x, coalesce: true); t.LocalPosition = v; }).Show());

        // Rotation (as euler)
        var euler = t.LocalEulerAngles;
        InspectorRow.Draw(paper, "gi_rot", "Rotation", () =>
            Origami.Float3Field(paper, "gi_rot_vf", euler, v => { Undo.RecordGameObjectChange(go, "Change Rotation", t.LocalEulerAngles, v, (g, x) => g.Transform.LocalEulerAngles = x, coalesce: true); t.LocalEulerAngles = v; }).Show());

        // Scale
        var scale = t.LocalScale;
        InspectorRow.Draw(paper, "gi_scale", "Scale", () =>
            Origami.Float3Field(paper, "gi_scale_vf", scale, v => { Undo.RecordGameObjectChange(go, "Change Scale", t.LocalScale, v, (g, x) => g.Transform.LocalScale = x, coalesce: true); t.LocalScale = v; }).Show());
    }

    /// <summary>
    /// Anchor presets arranged as a 4x4 grid, mirroring Unity's RectTransform popup:
    /// the inner 3x3 are fixed-corner anchors, the outer row/column are stretch presets,
    /// and the bottom-right corner is "stretch all". Item is (AnchorMin, AnchorMax).
    /// </summary>
    private static readonly (Float2 min, Float2 max)[,] AnchorPresets = new (Float2, Float2)[4, 4]
    {
        // Row 0: top fixed (TL, TC, TR) + horizontal-stretch top
        { (new(0f, 0f), new(0f, 0f)),     (new(0.5f, 0f), new(0.5f, 0f)),     (new(1f, 0f), new(1f, 0f)),     (new(0f, 0f), new(1f, 0f)) },
        // Row 1: middle fixed (ML, MC, MR) + horizontal-stretch middle
        { (new(0f, 0.5f), new(0f, 0.5f)), (new(0.5f, 0.5f), new(0.5f, 0.5f)), (new(1f, 0.5f), new(1f, 0.5f)), (new(0f, 0.5f), new(1f, 0.5f)) },
        // Row 2: bottom fixed (BL, BC, BR) + horizontal-stretch bottom
        { (new(0f, 1f), new(0f, 1f)),     (new(0.5f, 1f), new(0.5f, 1f)),     (new(1f, 1f), new(1f, 1f)),     (new(0f, 1f), new(1f, 1f)) },
        // Row 3: vertical-stretch (left, center, right) + stretch-all
        { (new(0f, 0f), new(0f, 1f)),     (new(0.5f, 0f), new(0.5f, 1f)),     (new(1f, 0f), new(1f, 1f)),     (new(0f, 0f), new(1f, 1f)) },
    };

    private static bool ApproxEq(Float2 a, Float2 b)
        => System.Math.Abs(a.X - b.X) < 1e-4f && System.Math.Abs(a.Y - b.Y) < 1e-4f;

    private static void DrawRectTransform(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var rt = go.RectTransform!;
        var t = go.Transform;

        paper.Box("gi_rt_header").Height(22).ChildLeft(8)
            .Text($"{EditorIcons.VectorSquare}  Rect Transform", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        // Top block: 4x4 anchor preset grid on the left, position + size fields filling the rest.
        using (paper.Row("gi_rt_top").Height(UnitValue.Auto).RowBetween(8).Margin(4, 4, 2, 2).Enter())
        {
            DrawAnchorPresetGrid(paper, font, go, rt);

            using (paper.Column("gi_rt_top_right").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
            {
                DrawPositionRow(paper, font, go, rt, t);
                DrawSizeRow(paper, font, go, rt);
                DrawPivotRow(paper, font, go, rt);
            }
        }

        // Anchors (Min/Max) — exposed for fine-grained tweaking outside the preset grid.
        EditorGUI.Vector2Field(paper, "gi_rt_amin", "Anchor Min", rt.AnchorMin)
            .OnValueChanged(v =>
            {
                Undo.RecordGameObjectChange(go, "Change Anchor Min", rt.AnchorMin, v,
                    (g, x) => { var r = g.RectTransform; if (r != null) r.AnchorMin = x; }, coalesce: true);
                rt.AnchorMin = v;
            });

        EditorGUI.Vector2Field(paper, "gi_rt_amax", "Anchor Max", rt.AnchorMax)
            .OnValueChanged(v =>
            {
                Undo.RecordGameObjectChange(go, "Change Anchor Max", rt.AnchorMax, v,
                    (g, x) => { var r = g.RectTransform; if (r != null) r.AnchorMax = x; }, coalesce: true);
                rt.AnchorMax = v;
            });

        // Rotation (as euler) and scale come from the underlying Transform.
        var euler = t.LocalEulerAngles;
        EditorGUI.Vector3Field(paper, "gi_rt_rot", "Rotation", euler)
            .OnValueChanged(v => { Undo.RecordGameObjectChange(go, "Change Rotation", t.LocalEulerAngles, v, (g, x) => g.Transform.LocalEulerAngles = x, coalesce: true); t.LocalEulerAngles = v; });

        var scale = t.LocalScale;
        EditorGUI.Vector3Field(paper, "gi_rt_scale", "Scale", scale)
            .OnValueChanged(v => { Undo.RecordGameObjectChange(go, "Change Scale", t.LocalScale, v, (g, x) => g.Transform.LocalScale = x, coalesce: true); t.LocalScale = v; });
    }

    /// <summary>
    /// Renders the 4x4 anchor preset grid. The active preset is highlighted; clicking a cell
    /// applies its (AnchorMin, AnchorMax) to the RectTransform.
    /// </summary>
    private static void DrawAnchorPresetGrid(Paper paper, Prowl.Scribe.FontFile font, GameObject go, RectTransform rt)
    {
        const float CellSize = 22f;
        const float CellGap = 2f;
        const float GridPad = 4f;
        const float GridSize = CellSize * 4 + CellGap * 3 + GridPad * 2;

        using (paper.Column("gi_rt_apreset")
            .Width(GridSize).Height(GridSize)
            .BackgroundColor(EditorTheme.Neutral200)
            .BorderColor(EditorTheme.Ink100).BorderWidth(1)
            .Rounded(3)
            .ChildLeft(GridPad).ChildRight(GridPad).ChildTop(GridPad).ChildBottom(GridPad)
            .ColBetween(CellGap)
            .Enter())
        {
            for (int row = 0; row < 4; row++)
            {
                using (paper.Row($"gi_rt_apreset_r{row}")
                    .Height(CellSize).RowBetween(CellGap).Enter())
                {
                    for (int col = 0; col < 4; col++)
                    {
                        var preset = AnchorPresets[row, col];
                        DrawAnchorPresetCell(paper, $"gi_rt_acell_{row}_{col}", go, rt,
                            preset.min, preset.max, CellSize);
                    }
                }
            }
        }
    }

    private static void DrawAnchorPresetCell(Paper paper, string id, GameObject go, RectTransform rt,
        Float2 minPreset, Float2 maxPreset, float cellSize)
    {
        bool isFixed = ApproxEq(minPreset, maxPreset);
        bool isActive = ApproxEq(rt.AnchorMin, minPreset) && ApproxEq(rt.AnchorMax, maxPreset);

        var bg = isActive ? EditorTheme.Purple400 : EditorTheme.Neutral300;
        var hoverBg = isActive ? EditorTheme.Purple400 : EditorTheme.Neutral400;
        var indicator = isActive ? EditorTheme.Neutral200 : EditorTheme.Purple400;

        using (paper.Box(id)
            .Width(cellSize).Height(cellSize)
            .BackgroundColor(bg)
            .Hovered.BackgroundColor(hoverBg).End()
            .BorderColor(EditorTheme.Ink100).BorderWidth(1)
            .Rounded(2)
            .OnClick((go, minPreset, maxPreset), (cap, _) =>
            {
                var (capGo, capMin, capMax) = cap;
                var r = capGo.RectTransform;
                if (r == null) return;
                var oldMin = r.AnchorMin;
                var oldMax = r.AnchorMax;
                Undo.RegisterAction("Change Anchor Preset",
                    undo: () => { var rr = capGo.RectTransform; if (rr != null) { rr.AnchorMin = oldMin; rr.AnchorMax = oldMax; } },
                    redo: () => { var rr = capGo.RectTransform; if (rr != null) { rr.AnchorMin = capMin; rr.AnchorMax = capMax; } });
                r.AnchorMin = capMin;
                r.AnchorMax = capMax;
            })
            .Enter())
        {
            // Inner drawable area (cell minus 2px border on each side).
            float inner = cellSize - 4f;

            if (isFixed)
            {
                // Fixed anchor: small dot positioned at the preset corner of the cell.
                const float DotSize = 4f;
                float range = inner - DotSize;
                float dx = 2f + (float)minPreset.X * range;
                float dy = 2f + (float)minPreset.Y * range;
                paper.Box($"{id}_dot")
                    .PositionType(PositionType.SelfDirected)
                    .Position(dx, dy)
                    .Width(DotSize).Height(DotSize)
                    .BackgroundColor(indicator)
                    .IsNotInteractable()
                    .Rounded(2);
            }
            else
            {
                // Stretch preset: a bar visualizing the spanned axis (or a filled square for stretch-all).
                bool stretchX = !ApproxEq(new Float2(minPreset.X, 0), new Float2(maxPreset.X, 0));
                bool stretchY = !ApproxEq(new Float2(0, minPreset.Y), new Float2(0, maxPreset.Y));

                const float BarThickness = 4f;
                float bx, by, bw, bh;

                if (stretchX && !stretchY)
                {
                    bx = 2f;
                    by = 2f + (float)minPreset.Y * (inner - BarThickness);
                    bw = inner;
                    bh = BarThickness;
                }
                else if (stretchY && !stretchX)
                {
                    bx = 2f + (float)minPreset.X * (inner - BarThickness);
                    by = 2f;
                    bw = BarThickness;
                    bh = inner;
                }
                else
                {
                    // Stretch in both axes — fill the cell.
                    bx = 2f;
                    by = 2f;
                    bw = inner;
                    bh = inner;
                }

                paper.Box($"{id}_bar")
                    .PositionType(PositionType.SelfDirected)
                    .Position(bx, by)
                    .Width(bw).Height(bh)
                    .BackgroundColor(indicator)
                    .IsNotInteractable()
                    .Rounded(1);
            }
        }
    }

    /// <summary>
    /// Pos X / Pos Y / Pos Z row. X and Y come from RectTransform.AnchoredPosition,
    /// Z comes from Transform.LocalPosition (RectTransform doesn't track Z).
    /// </summary>
    private static void DrawPositionRow(Paper paper, Prowl.Scribe.FontFile font, GameObject go, RectTransform rt, Transform t)
    {
        using (paper.Row("gi_rt_pos").Height(EditorTheme.RowHeight).RowBetween(4)
            .Margin(UnitValue.Auto, EditorTheme.Spacing).Enter())
        {
            paper.Box("gi_rt_pos_lbl")
                .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                .IsNotInteractable()
                .Alignment(TextAlignment.MiddleLeft)
                .Text("Position", font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            EditorGUI.FloatFieldWithInternalLabel(paper, "gi_rt_posx", (float)rt.AnchoredPosition.X, "X",
                Color.FromArgb(255, 200, 80, 80))
                .OnValueChanged(v =>
                {
                    var oldVal = rt.AnchoredPosition;
                    var newVal = new Float2(v, (float)oldVal.Y);
                    Undo.RecordGameObjectChange(go, "Change AnchoredPosition", oldVal, newVal,
                        (g, x) => { var r = g.RectTransform; if (r != null) r.AnchoredPosition = x; }, coalesce: true);
                    rt.AnchoredPosition = newVal;
                });

            EditorGUI.FloatFieldWithInternalLabel(paper, "gi_rt_posy", (float)rt.AnchoredPosition.Y, "Y",
                Color.FromArgb(255, 80, 200, 80))
                .OnValueChanged(v =>
                {
                    var oldVal = rt.AnchoredPosition;
                    var newVal = new Float2((float)oldVal.X, v);
                    Undo.RecordGameObjectChange(go, "Change AnchoredPosition", oldVal, newVal,
                        (g, x) => { var r = g.RectTransform; if (r != null) r.AnchoredPosition = x; }, coalesce: true);
                    rt.AnchoredPosition = newVal;
                });

            EditorGUI.FloatFieldWithInternalLabel(paper, "gi_rt_posz", (float)t.LocalPosition.Z, "Z",
                Color.FromArgb(255, 80, 80, 200))
                .OnValueChanged(v =>
                {
                    var oldVal = t.LocalPosition;
                    var newVal = new Float3((float)oldVal.X, (float)oldVal.Y, v);
                    Undo.RecordGameObjectChange(go, "Change Position Z", oldVal, newVal,
                        (g, x) => g.Transform.LocalPosition = x, coalesce: true);
                    t.LocalPosition = newVal;
                });
        }
    }

    /// <summary>Width / Height row driven by RectTransform.SizeDelta.</summary>
    private static void DrawSizeRow(Paper paper, Prowl.Scribe.FontFile font, GameObject go, RectTransform rt)
    {
        using (paper.Row("gi_rt_size").Height(EditorTheme.RowHeight).RowBetween(4)
            .Margin(UnitValue.Auto, EditorTheme.Spacing).Enter())
        {
            paper.Box("gi_rt_size_lbl")
                .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                .IsNotInteractable()
                .Alignment(TextAlignment.MiddleLeft)
                .Text("Size", font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            EditorGUI.FloatFieldWithInternalLabel(paper, "gi_rt_w", (float)rt.SizeDelta.X, "W",
                Color.FromArgb(255, 200, 80, 80))
                .OnValueChanged(v =>
                {
                    var oldVal = rt.SizeDelta;
                    var newVal = new Float2(v, (float)oldVal.Y);
                    Undo.RecordGameObjectChange(go, "Change SizeDelta", oldVal, newVal,
                        (g, x) => { var r = g.RectTransform; if (r != null) r.SizeDelta = x; }, coalesce: true);
                    rt.SizeDelta = newVal;
                });

            EditorGUI.FloatFieldWithInternalLabel(paper, "gi_rt_h", (float)rt.SizeDelta.Y, "H",
                Color.FromArgb(255, 80, 200, 80))
                .OnValueChanged(v =>
                {
                    var oldVal = rt.SizeDelta;
                    var newVal = new Float2((float)oldVal.X, v);
                    Undo.RecordGameObjectChange(go, "Change SizeDelta", oldVal, newVal,
                        (g, x) => { var r = g.RectTransform; if (r != null) r.SizeDelta = x; }, coalesce: true);
                    rt.SizeDelta = newVal;
                });
        }
    }

    private static void DrawPivotRow(Paper paper, Prowl.Scribe.FontFile font, GameObject go, RectTransform rt)
    {
        using (paper.Row("gi_rt_pivot").Height(EditorTheme.RowHeight).RowBetween(4)
            .Margin(UnitValue.Auto, EditorTheme.Spacing).Enter())
        {
            paper.Box("gi_rt_pivot_lbl")
                .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                .IsNotInteractable()
                .Alignment(TextAlignment.MiddleLeft)
                .Text("Pivot", font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            EditorGUI.FloatFieldWithInternalLabel(paper, "gi_rt_pivx", (float)rt.Pivot.X, "X",
                Color.FromArgb(255, 200, 80, 80))
                .OnValueChanged(v =>
                {
                    var oldVal = rt.Pivot;
                    var newVal = new Float2(v, (float)oldVal.Y);
                    Undo.RecordGameObjectChange(go, "Change Pivot", oldVal, newVal,
                        (g, x) => { var r = g.RectTransform; if (r != null) r.Pivot = x; }, coalesce: true);
                    rt.Pivot = newVal;
                });

            EditorGUI.FloatFieldWithInternalLabel(paper, "gi_rt_pivy", (float)rt.Pivot.Y, "Y",
                Color.FromArgb(255, 80, 200, 80))
                .OnValueChanged(v =>
                {
                    var oldVal = rt.Pivot;
                    var newVal = new Float2((float)oldVal.X, v);
                    Undo.RecordGameObjectChange(go, "Change Pivot", oldVal, newVal,
                        (g, x) => { var r = g.RectTransform; if (r != null) r.Pivot = x; }, coalesce: true);
                    rt.Pivot = newVal;
                });
        }
    }

    // ================================================================
    //  Components
    // ================================================================

    private static void DrawComponents(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var components = go.GetComponents<MonoBehaviour>().ToList();

        for (int i = 0; i < components.Count; i++)
        {
            var comp = components[i];
            if (comp.HideFlags.HasFlag(HideFlags.Hide)) continue;

            string compId = $"gi_comp_{comp.Identifier}";
            string compName = comp.GetType().Name;
            string icon = GetComponentIcon(comp);

            // Component foldout header draggable for component references
            using (paper.Row($"{compId}_header")
                .Height(24)
                .BackgroundColor(EditorTheme.Neutral300)
                .Rounded(3)
                .ChildLeft(4)
                .RowBetween(4)
                .OnDragStart((go, comp), (cap, _) =>
                {
                    DragDrop.StartDrag(new ComponentDragPayload(cap.Item1, cap.Item2));
                })
                .Enter())
            {
                // Enabled toggle
                Origami.Checkbox(paper, $"{compId}_en", comp.Enabled,
                    v => { var old = comp.Enabled; var cId = comp.Identifier; Undo.RegisterAction("Toggle Component", () => { var c = Undo.FindComponent(cId); if (c != null) { c.Enabled = old; c.OnValidate(); } }, () => { var c = Undo.FindComponent(cId); if (c != null) { c.Enabled = v; c.OnValidate(); } }); comp.Enabled = v; comp.OnValidate(); })
                    .NoLabel().Show();

                // Icon + Name (click to fold)
                paper.Box($"{compId}_label")
                    .Height(24)
                    .Text($"{icon}  {compName}", font)
                    .TextColor(comp.Enabled ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                // Spacer
                paper.Box($"{compId}_spacer");

                // Context menu button
                using (paper.Box($"{compId}_gear")
                    .Width(20).Height(24).Rounded(3)
                    .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                    .Text(EditorIcons.EllipsisVertical, font).TextColor(EditorTheme.Ink400)
                    .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(i, (ci, _) =>
                    {
                        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
                            BuildComponentContextMenu(b, go, comp, ci));
                    })
                    .Enter())
                {
                }
            }

            // Set overridden field names for PropertyGrid highlighting
            if (go.IsPrefabInstance)
            {
                string goPath = PrefabUtility.BuildGOPath(go);
                var allComps = go.GetComponents<MonoBehaviour>().ToList();
                int compIdx = allComps.IndexOf(comp);
                string pathPrefix = string.IsNullOrEmpty(goPath)
                    ? $"c{compIdx}."
                    : $"{goPath}.c{compIdx}.";

                var overridden = new HashSet<string>();
                foreach (var ov in go.PrefabOverrides)
                {
                    if (ov.Path.StartsWith(pathPrefix))
                        overridden.Add(ov.Path[pathPrefix.Length..].Split('.')[0]);
                }
                PropertyGrid.OverriddenFields = overridden.Count > 0 ? overridden : null;
            }

            // Component body use custom editor or default PropertyGrid
            var customEditor = CustomEditorRegistry.GetEditor(comp.GetType());
            if (customEditor != null)
            {
                customEditor.OnGUI(paper, compId, comp);
            }
            else
            {
                PropertyGrid.Draw(paper, compId, comp);
            }

            PropertyGrid.OverriddenFields = null;

            // Auto-detect overrides by comparing against prefab source (index-based)
            if (go.IsPrefabInstance)
                PrefabUtility.DetectComponentOverrides(go, comp);

            // Draw [Button] attributed methods
            DrawButtonMethods(paper, $"{compId}_btns", comp);

            Origami.Separator(paper, $"{compId}_sep").Show();
        }
    }

    private static void BuildComponentContextMenu(ContextBuilder builder, GameObject go, MonoBehaviour comp, int index)
    {
        builder.Item("Reset", () =>
        {
            // TODO: Reset to default values
            Runtime.Debug.Log($"Reset {comp.GetType().Name}");
        }, icon: EditorIcons.ArrowsRotate);

        builder.Separator();

        bool canRemove = comp.CanDestroy();
        // Block removing prefab components in editor
        if (go.IsPrefabInstance && go.PrefabComponentCount >= 0)
        {
            int compIdx = go._components.IndexOf(comp);
            if (compIdx >= 0 && compIdx < go.PrefabComponentCount)
                canRemove = false;
        }
        builder.Item("Remove Component", () =>
        {
            var serialized = Echo.Serializer.Serialize(comp.GetType(), comp);
            var compType = comp.GetType();
            var compId = comp.Identifier;
            var goId = go.Identifier;
            Undo.RegisterAction("Remove Component",
                undo: () =>
                {
                    var g = Undo.FindGO(goId);
                    if (g == null) return;
                    var restored = Echo.Serializer.Deserialize(serialized, compType) as MonoBehaviour;
                    if (restored != null) { restored.Identifier = compId; g.AddComponent(restored); }
                },
                redo: () =>
                {
                    var g = Undo.FindGO(goId);
                    if (g == null) return;
                    var c = g.GetComponentByIdentifier(compId);
                    if (c != null) g.RemoveComponent(c);
                });
            go.RemoveComponent(comp);
        }, icon: EditorIcons.Trash, enabled: canRemove);

        builder.Separator();

        var moveCompId = comp.Identifier;
        builder.Item("Move Up", () =>
        {
            if (index > 0)
            {
                var oldIdx = index; var newIdx = index - 1;
                Undo.RegisterAction("Move Component Up",
                    () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(oldIdx); },
                    () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(newIdx); });
                comp.SetSiblingIndex(newIdx);
            }
        }, icon: EditorIcons.ArrowUp, enabled: index > 0);

        builder.Item("Move Down", () =>
        {
            var oldIdx = index; var newIdx = index + 1;
            Undo.RegisterAction("Move Component Down",
                () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(oldIdx); },
                () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(newIdx); });
            comp.SetSiblingIndex(newIdx);
        }, icon: EditorIcons.ArrowDown);

        builder.Separator();

        builder.Item("Pop Out", () =>
        {
            Panels.ComponentPopoutPanel.PopOut(go, comp);
        }, icon: EditorIcons.ArrowUpRightFromSquare);
    }

    // ================================================================
    //  [Button] Methods
    // ================================================================

    public static void DrawButtonMethods(Paper paper, string id, MonoBehaviour comp)
    {
        var methods = comp.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        int btnIdx = 0;
        foreach (var method in methods)
        {
            var btnAttr = method.GetCustomAttribute<ButtonAttribute>();
            if (btnAttr == null) continue;
            if (method.GetParameters().Length > 0) continue; // Only parameterless methods

            string label = btnAttr.Label ?? NicifyName(method.Name);
            Origami.Button(paper, $"{id}_{btnIdx++}", label, () => { method.Invoke(comp, null); }).Show();
        }
    }

    // ================================================================
    //  Add Component
    // ================================================================

    private static void DrawAddComponentButton(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        using (paper.Row("gi_add_comp_row").Height(28).ChildLeft(20).ChildRight(20).Enter())
        {
            paper.Box("gi_add_comp")
                .Height(28).Rounded(4)
                .BackgroundColor(EditorTheme.Ink100)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text($"{EditorIcons.Plus}  Add Component", font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter)
                .OnClick(go, (g, _) => AddComponentPopup.Open(g));
        }
    }

    private static void BuildAddComponentMenu(ContextBuilder builder, GameObject go)
    {
        // Gather all MonoBehaviour types
        var componentTypes = new List<(string path, string icon, Type type)>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract) continue;
                if (type == typeof(MonoBehaviour)) continue;

                var menuAttr = type.GetCustomAttribute<AddComponentMenuAttribute>();
                string path = menuAttr?.Path ?? type.Name;
                string icon = menuAttr?.Icon ?? EditorIcons.PuzzlePiece;
                componentTypes.Add((path, icon, type));
            }
        }

        // Build menu tree from paths
        var categories = new Dictionary<string, List<(string name, string icon, Type type)>>();

        foreach (var (path, icon, type) in componentTypes.OrderBy(c => c.path))
        {
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                string category = path[..lastSlash];
                string name = path[(lastSlash + 1)..];
                if (!categories.ContainsKey(category))
                    categories[category] = new();
                categories[category].Add((name, icon, type));
            }
            else
            {
                if (!categories.ContainsKey(""))
                    categories[""] = new();
                categories[""].Add((path, icon, type));
            }
        }

        // Root items
        if (categories.TryGetValue("", out var rootItems))
        {
            foreach (var (name, icon, type) in rootItems)
                builder.Item(name, () => go.AddComponent(type), icon: icon);
        }

        // Categories as submenus
        foreach (var (category, items) in categories.Where(kv => kv.Key != "").OrderBy(kv => kv.Key))
        {
            builder.Submenu(category, sub =>
            {
                foreach (var (name, icon, type) in items)
                    sub.Item(name, () => go.AddComponent(type), icon: icon);
            });
        }
    }

    // ================================================================
    //  Prefab Header
    // ================================================================

    private static void DrawPrefabHeader(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var root = PrefabUtility.GetPrefabInstanceRoot(go);
        bool isRoot = PrefabUtility.IsInstanceRoot(go);
        bool isNested = PrefabUtility.IsNestedPrefabRoot(go);
        bool hasOverrides = PrefabUtility.HasAnyOverrides(go);

        var entry = EditorAssetDatabase.Instance?.GetEntry(go.PrefabAssetId);
        bool isMissing = entry == null;
        string prefabName = isMissing ? "Missing" : System.IO.Path.GetFileNameWithoutExtension(entry!.Path);

        string label = isMissing ? "Missing Prefab!"
            : isNested ? $"Nested Prefab: {prefabName}"
            : $"Prefab: {prefabName}";

        var barColor = isMissing ? Color.FromArgb(40, 220, 80, 80) : Color.FromArgb(40, EditorTheme.Purple400);
        var borderColor = isMissing ? Color.FromArgb(255, 220, 80, 80) : EditorTheme.Purple300;
        var textColor = isMissing ? Color.FromArgb(255, 220, 80, 80) : EditorTheme.Purple400;

        // Entire prefab section in a purple-tinted container
        using (paper.Column("gi_prefab_section")
            .Height(UnitValue.Auto)
            .BackgroundColor(barColor)
            .BorderColor(borderColor).BorderWidth(1)
            .Rounded(4).Margin(0, 4, 0, 4)
            .Padding(4, 4, 4, 4)
            .Enter())
        {
            // Top row: label + buttons
            using (paper.Row("gi_prefab_bar")
                .Height(24).RowBetween(4)
                .Enter())
            {
                paper.Box("gi_prefab_icon")
                    .Width(UnitValue.Stretch()).Height(24)
                    .Text($"{EditorIcons.Cubes}  {label}", font)
                    .TextColor(textColor)
                    .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                if (!isMissing)
                {
                    Origami.Button(paper, "gi_prefab_select", "Select", () => { Selection.Ping(go.PrefabAssetId); }).Width(55).Show();

                    if (isRoot && hasOverrides)
                    {
                        Origami.Button(paper, "gi_prefab_revert", "Revert", () => { if (root != null) PrefabUtility.RevertOverrides(root); }).Width(55).Show();

                        Origami.Button(paper, "gi_prefab_apply", "Apply", () => { if (root != null) PrefabUtility.ApplyOverrides(root); }).Width(50).Show();
                    }
                }
            }

            // Overrides list inline
            if (hasOverrides)
            {
                paper.Box("gi_prefab_ov_sep").Height(1)
                    .BackgroundColor(borderColor).Margin(0, 2, 0, 2);

                DrawOverridesContent(paper, font, go);
            }
        }
    }

    private static void DrawOverridesContent(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        float fs = EditorTheme.FontSize;
        var overrides = go.PrefabOverrides;

        using (paper.Column("gi_prefab_ov_list")
            .Height(UnitValue.Auto)
            .Margin(8, 0, 4, 4)
            .Enter())
        {
            for (int i = 0; i < overrides.Count; i++)
            {
                int idx = i;
                var ov = overrides[i];

                using (paper.Row($"gi_ov_{i}")
                    .Height(EditorTheme.RowHeight)
                    .BackgroundColor(EditorTheme.Neutral300)
                    .Rounded(3).Margin(0, 0, 0, 1)
                    .ChildLeft(6).RowBetween(4)
                    .Enter())
                {
                    // Override path
                    paper.Box($"gi_ov_path_{i}")
                        .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                        .Text(ov.Path, font).TextColor(EditorTheme.Purple400)
                        .FontSize(fs - 2).Alignment(TextAlignment.MiddleLeft);

                    // Revert single
                    paper.Box($"gi_ov_revert_{i}")
                        .Width(50).Height(EditorTheme.RowHeight).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text("Revert", font).TextColor(EditorTheme.Ink400)
                        .FontSize(fs - 2).Alignment(TextAlignment.MiddleCenter)
                        .OnClick((go, idx), (cap, _) =>
                        {
                            if (cap.idx < cap.go.PrefabOverrides.Count)
                            {
                                var overridePath = cap.go.PrefabOverrides[cap.idx].Path;
                                PrefabUtility.RevertSingleOverride(cap.go, overridePath);
                            }
                        });

                    // Apply single
                    paper.Box($"gi_ov_apply_{i}")
                        .Width(45).Height(EditorTheme.RowHeight).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text("Apply", font).TextColor(EditorTheme.Ink400)
                        .FontSize(fs - 2).Alignment(TextAlignment.MiddleCenter)
                        .OnClick((go, idx), (cap, _) =>
                        {
                            if (cap.idx < cap.go.PrefabOverrides.Count)
                            {
                                var singleOv = cap.go.PrefabOverrides[cap.idx];
                                PrefabUtility.ApplySingleOverride(cap.go, singleOv);
                            }
                        });
                }
            }
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static string GetComponentIcon(MonoBehaviour comp) => ComponentIconRegistry.GetIcon(comp);

    private static string NicifyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // Insert spaces before capitals: "MyMethod" -> "My Method"
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                result.Append(' ');
            result.Append(name[i]);
        }
        return result.ToString();
    }
}
