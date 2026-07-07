using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Prefabs;
using Prowl.Editor.GUI.Popups;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

using Color = System.Drawing.Color;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.Editor.GUI;
using Prowl.Vector;
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
        Divider(paper, "gi_sep_header");
        if (go.RectTransform != null)
        {
            DrawRectTransform(paper, font, go);
        }
        else
        {
            DrawTransform(paper, font, go);
        }

        Divider(paper, "gi_sep_transform");
        DrawComponents(paper, font, go);

        // Only show Add Component if not a prefab instance (structure is fixed)
        if (!go.IsPrefabInstance || Application.IsPlaying)
            DrawAddComponentButton(paper, font, go);

        // Detect GO-level overrides (Name, Tag, Transform, etc.)
        if (go.IsPrefabInstance)
            PrefabUtility.DetectGOOverrides(go);

        paper.Box("gi_bottom_pad").Height(20);
    }

    private static UnitValue ST => UnitValue.StretchOne;

    // Sections (Transform + each component) remember their collapsed state here; absent = expanded.
    private static readonly HashSet<string> _collapsedSections = new();
    private static bool IsExpanded(string id) => !_collapsedSections.Contains(id);
    private static void ToggleSection(string id) { if (!_collapsedSections.Add(id)) _collapsedSections.Remove(id); }

    private static bool SectionHeader(Paper paper, Prowl.Scribe.FontFile font, string id,
        string glyph, string title, Color titleColor, Action? trailing = null, Action? onDragStart = null)
    {
        bool expanded = IsExpanded(id);
        var semi = EditorTheme.FontSemiBold ?? font;
        using (paper.Row($"{id}_head").Height(30).Padding(10, 8, 0, 0).RowBetween(7).Enter())
        {
            var clickRow = paper.Row($"{id}_hclick").Width(UnitValue.Stretch()).Height(30).RowBetween(7)
                .Hovered.BackgroundColor(Color.FromArgb(13, EditorTheme.Purple400)).End()
                .OnClick(id, (i, _) => ToggleSection(i));
            if (onDragStart != null)
                clickRow.OnDragStart(0, (_, _) => onDragStart());

            using (clickRow.Enter())
            {
                paper.Box($"{id}_chev").Width(11).Height(30).IsNotInteractable()
                    .Text(expanded ? EditorIcons.ChevronDown : EditorIcons.ChevronRight, font)
                    .TextColor(EditorTheme.Ink300).FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                paper.Box($"{id}_ico").Width(16).Height(30).IsNotInteractable()
                    .Text(glyph, font).TextColor(EditorTheme.AccentText).FontSize(14f).Alignment(TextAlignment.MiddleCenter);

                paper.Box($"{id}_title").Width(UnitValue.Stretch()).Height(30).IsNotInteractable()
                    .Text(title, semi).TextColor(titleColor)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
            }
            trailing?.Invoke();
        }
        return expanded;
    }

    // Small ghost icon button for section-header trailing actions.
    private static void HeaderIconBtn(Paper paper, Prowl.Scribe.FontFile font, string id, string glyph, Action onClick)
    {
        paper.Box(id).Width(22).Height(22).Rounded(5).Margin(0, 0, ST, ST)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(glyph, font).TextColor(EditorTheme.Ink300).FontSize(12f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => onClick());
    }

    private static void Divider(Paper paper, string id)
        => paper.Box(id).Width(UnitValue.Stretch()).Height(1).Margin(0, 0, 6, 6)
            .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

    private static void SelDropdown(Paper paper, Prowl.Scribe.FontFile font, string id,
        string? label, string value, string[] options, int current, Action<int> onSelect, bool chevron, UnitValue width)
    {
        using (paper.Row(id).Width(width).Height(26).Rounded(7).Padding(9, 9, 0, 0).RowBetween(6)
            .BackgroundColor(EditorTheme.Glass)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BorderColor(EditorTheme.BorderStrong).End()
            .OnClick(0, (_, _) => Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
            {
                for (int i = 0; i < options.Length; i++)
                {
                    int idx = i;
                    b.Item(options[idx], () => onSelect(idx), on: idx == current);
                }
            }))
            .Enter())
        {
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl").Width(UnitValue.Auto).Height(26).IsNotInteractable()
                    .Text(label, font).TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_val").Width(UnitValue.Stretch()).Height(26).IsNotInteractable()
                .Text(value, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(string.IsNullOrEmpty(label) ? TextAlignment.MiddleLeft : TextAlignment.MiddleRight);

            if (chevron)
                paper.Box($"{id}_chev").Width(10).Height(26).IsNotInteractable()
                    .Text(EditorIcons.ChevronDown, font).TextColor(EditorTheme.Ink300).FontSize(9f).Alignment(TextAlignment.MiddleCenter);
        }
    }

    // ================================================================
    //  Multi-object inspector
    // ================================================================

    /// <summary>
    /// Draws the inspector for several GameObjects at once. Shows the transform and the components whose
    /// type is present on every selected object; fields that differ across the selection are flagged as
    /// mixed, and edits apply to all.
    /// </summary>
    public static void DrawMulti(Paper paper, Prowl.Scribe.FontFile font, IReadOnlyList<GameObject> gos)
    {
        if (gos.Count == 1) { Draw(paper, font, gos[0]); return; }

        DrawMultiHeader(paper, font, gos);
        Origami.Separator(paper, "gim_sep_header").Show();
        DrawMultiTransform(paper, font, gos);
        Origami.Separator(paper, "gim_sep_transform").Show();
        DrawMultiComponents(paper, font, gos);

        paper.Box("gim_bottom_pad").Height(20);
    }

    private static string MixLabel(string label, bool mixed) => mixed ? $"{label}  (mixed)" : label;

    private static void DrawMultiHeader(Paper paper, Prowl.Scribe.FontFile font, IReadOnlyList<GameObject> gos)
    {
        using (paper.Row("gim_header").Height(EditorTheme.RowHeight).Margin(0, 6).RowBetween(6).Enter())
        {
            paper.Box("gim_icon").Margin(6, 6, 0, 6).FontSize(EditorTheme.FontSize * 1.5f).Width(UnitValue.Auto).Text(EditorIcons.Cubes, font);

            bool allEnabled = gos.All(g => g.Enabled);
            Origami.Checkbox(paper, "gim_enabled", allEnabled,
                v => Undo.ApplyGameObjectChanges(gos, "Toggle Enabled", g => g.Enabled, (g, x) => g.Enabled = x, v))
                .NoLabel().Show();

            paper.Box("gim_count").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                .IsNotInteractable()
                .Text($"{gos.Count} {Loc.Get("inspector.objects_selected")}", font)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            bool allStatic = gos.All(g => g.IsStatic);
            Origami.Checkbox(paper, "gim_static", allStatic,
                v => Undo.ApplyGameObjectChanges(gos, "Toggle Static", g => g.IsStatic, (g, x) => g.IsStatic = x, v))
                .LabelRight(Loc.Get("inspector.static")).Show();
        }

        using (paper.Row("gim_tag_layer").Height(22).RowBetween(6).Enter())
        {
            var tagNames = TagLayerManager.tags.ToArray();
            bool tagMixed = gos.Select(g => g.Tag).Distinct().Count() > 1;
            int tagIdx = tagMixed ? -1 : Math.Max(0, TagLayerManager.tags.IndexOf(gos[0].Tag));

            DrawInlineLabeled(paper, "gim_tag_row", MixLabel(Loc.Get("inspector.tag"), tagMixed), font, () =>
                Origami.Dropdown(paper, "gim_tag", tagIdx,
                    v =>
                    {
                        if (v >= 0 && v < tagNames.Length)
                            Undo.ApplyGameObjectChanges(gos, "Change Tag", g => g.Tag, (g, x) => g.Tag = x, tagNames[v]);
                    }, tagNames).Show());

            var allLayers = TagLayerManager.layers;
            var layerNames = new List<string>();
            var layerIndices = new List<int>();
            for (int i = 0; i < allLayers.Length; i++)
                if (!string.IsNullOrEmpty(allLayers[i])) { layerNames.Add(allLayers[i]); layerIndices.Add(i); }

            bool layerMixed = gos.Select(g => g.LayerIndex).Distinct().Count() > 1;
            int selLayer = layerMixed ? -1 : layerIndices.IndexOf(gos[0].LayerIndex);

            DrawInlineLabeled(paper, "gim_layer_row", MixLabel(Loc.Get("inspector.layer"), layerMixed), font, () =>
                Origami.Dropdown(paper, "gim_layer", selLayer,
                    v =>
                    {
                        if (v >= 0 && v < layerIndices.Count)
                            Undo.ApplyGameObjectChanges(gos, "Change Layer", g => g.LayerIndex, (g, x) => g.LayerIndex = x, layerIndices[v]);
                    }, layerNames.ToArray()).Show());
        }
    }

    private static void DrawMultiTransform(Paper paper, Prowl.Scribe.FontFile font, IReadOnlyList<GameObject> gos)
    {
        paper.Box("gim_transform_header").Height(22).ChildLeft(8)
            .Text($"{EditorIcons.ArrowsUpDownLeftRight}  {Loc.Get("inspector.transform")}", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        var t0 = gos[0].Transform;

        bool posMixed = gos.Any(g => !g.Transform.LocalPosition.Equals(t0.LocalPosition));
        EditorGUI.Row(paper, "gim_pos", MixLabel(Loc.Get("inspector.position"), posMixed), () =>
            Origami.Float3Field(paper, "gim_pos_vf", t0.LocalPosition, v =>
                Undo.ApplyGameObjectChanges(gos, "Change Position", g => g.Transform.LocalPosition, (g, x) => g.Transform.LocalPosition = x, v, coalesce: true)).Show());

        bool rotMixed = gos.Any(g => !g.Transform.LocalEulerAngles.Equals(t0.LocalEulerAngles));
        EditorGUI.Row(paper, "gim_rot", MixLabel(Loc.Get("inspector.rotation"), rotMixed), () =>
            Origami.Float3Field(paper, "gim_rot_vf", t0.LocalEulerAngles, v =>
                Undo.ApplyGameObjectChanges(gos, "Change Rotation", g => g.Transform.LocalEulerAngles, (g, x) => g.Transform.LocalEulerAngles = x, v, coalesce: true)).Show());

        bool scaleMixed = gos.Any(g => !g.Transform.LocalScale.Equals(t0.LocalScale));
        EditorGUI.Row(paper, "gim_scale", MixLabel(Loc.Get("inspector.scale"), scaleMixed), () =>
            Origami.Float3Field(paper, "gim_scale_vf", t0.LocalScale, v =>
                Undo.ApplyGameObjectChanges(gos, "Change Scale", g => g.Transform.LocalScale, (g, x) => g.Transform.LocalScale = x, v, coalesce: true)).Show());
    }

    private static void DrawMultiComponents(Paper paper, Prowl.Scribe.FontFile font, IReadOnlyList<GameObject> gos)
    {
        // Component types in the first object's order, kept only if present on every selected object.
        var orderedTypes = new List<Type>();
        foreach (var c in gos[0].GetComponents<MonoBehaviour>())
        {
            if (c.HideFlags.HasFlag(HideFlags.Hide)) continue;
            if (c is RectTransform) continue; // handled by the transform row at the top
            var ct = c.GetType();
            if (!orderedTypes.Contains(ct)) orderedTypes.Add(ct);
        }

        foreach (var type in orderedTypes)
        {
            var instances = new List<object>(gos.Count);
            bool onAll = true;
            foreach (var go in gos)
            {
                MonoBehaviour? match = null;
                foreach (var c in go.GetComponents<MonoBehaviour>())
                {
                    if (c.HideFlags.HasFlag(HideFlags.Hide)) continue;
                    if (c.GetType() == type) { match = c; break; }
                }
                if (match == null) { onAll = false; break; }
                instances.Add(match);
            }
            if (!onAll) continue;

            string compId = $"gim_comp_{type.Name}";
            string icon = GetComponentIcon((MonoBehaviour)instances[0]);

            using (paper.Row($"{compId}_header")
                .Height(24).BackgroundColor(EditorTheme.Neutral300).Rounded(3).ChildLeft(4).RowBetween(4).Enter())
            {
                bool allEn = instances.All(o => ((MonoBehaviour)o).Enabled);
                Origami.Checkbox(paper, $"{compId}_en", allEn,
                    v =>
                    {
                        var actions = new List<(Action, Action)>(instances.Count);
                        foreach (var o in instances)
                        {
                            var c = (MonoBehaviour)o;
                            var cid = c.Identifier;
                            bool old = c.Enabled;
                            actions.Add((() => { var x = Undo.FindComponent(cid); if (x != null) { x.Enabled = old; x.OnValidate(); } },
                                        () => { var x = Undo.FindComponent(cid); if (x != null) { x.Enabled = v; x.OnValidate(); } }));
                            c.Enabled = v; c.OnValidate();
                        }
                        Undo.RegisterActionGroup("Toggle Component", actions);
                    })
                    .NoLabel().Show();

                paper.Box($"{compId}_label")
                    .Height(24)
                    .Text($"{icon}  {type.Name}", font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
            }

            // Reflection-based multi grid (custom single-target editors are bypassed in multi mode).
            PropertyGridUtils.DrawMulti(paper, compId, instances);

            Origami.Separator(paper, $"{compId}_sep").Show();
        }
    }

    // ================================================================
    //  Header: Name, Enabled, Tag, Layer
    // ================================================================

    private static void DrawHeader(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var goId = go.Identifier;

        // Enabled toggle + Name + Static
        using (paper.Row("gi_header")
            .Height(26)
            .Margin(0, 0, 8, 4)
            .Padding(11, 11, 0, 0)
            .RowBetween(8)
            .Enter())
        {
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
                .Width(UnitValue.Stretch()).Height(26)
                .Show();

            SelDropdown(paper, font, "gi_static", null,
                go.IsStatic ? "Static" : "Dynamic",
                new[] { "Dynamic", "Static" }, go.IsStatic ? 1 : 0,
                idx =>
                {
                    bool v = idx == 1;
                    Undo.RecordGameObjectChange(go, "Toggle Static", go.IsStatic, v, (g, x) => g.IsStatic = x);
                    go.IsStatic = v;

                    // Offer to cascade the change to the whole subtree.
                    if (go.ChildCount > 0)
                        Origami.Confirm("Apply to Children?",
                            $"Also set Static = {v} on all child objects of \"{go.Name}\"?",
                            () => ApplyStaticToChildren(go, v));
                }, chevron: true, width: UnitValue.Pixels(90));
        }

        // Tag + Layer row (label-inside sel dropdowns)
        using (paper.Row("gi_tag_layer")
            .Height(26)
            .Margin(0, 0, 4, 0)
            .Padding(11, 11, 0, 0)
            .RowBetween(7)
            .Enter())
        {
            var tagNames = TagLayerManager.tags.ToArray();
            int tagIdx = TagLayerManager.tags.IndexOf(go.Tag);
            if (tagIdx < 0) tagIdx = 0;
            SelDropdown(paper, font, "gi_tag", Loc.Get("inspector.tag"), go.Tag, tagNames, tagIdx,
                v =>
                {
                    if (v >= 0 && v < tagNames.Length)
                    {
                        var newTag = tagNames[v];
                        Undo.RecordGameObjectChange(go, "Change Tag", go.Tag, newTag, (g, x) => g.Tag = x);
                        go.Tag = newTag;
                    }
                }, chevron: false, width: UnitValue.Stretch());

            var allLayers = TagLayerManager.layers;
            var layerNames = new List<string>();
            var layerIndices = new List<int>();
            for (int i = 0; i < allLayers.Length; i++)
                if (!string.IsNullOrEmpty(allLayers[i])) { layerNames.Add(allLayers[i]); layerIndices.Add(i); }

            int selLayer = layerIndices.IndexOf(go.LayerIndex);
            if (selLayer < 0) selLayer = 0;
            string layerVal = selLayer >= 0 && selLayer < layerNames.Count ? layerNames[selLayer] : "";
            SelDropdown(paper, font, "gi_layer", Loc.Get("inspector.layer"), layerVal, layerNames.ToArray(), selLayer,
                v =>
                {
                    if (v >= 0 && v < layerIndices.Count)
                    {
                        var newIdx = layerIndices[v];
                        Undo.RecordGameObjectChange(go, "Change Layer", go.LayerIndex, newIdx, (g, x) => g.LayerIndex = x);
                        go.LayerIndex = newIdx;
                    }
                }, chevron: false, width: UnitValue.Stretch());
        }
    }

    /// <summary>Set <see cref="GameObject.IsStatic"/> on every descendant of <paramref name="go"/> (recorded for undo).</summary>
    private static void ApplyStaticToChildren(GameObject go, bool value)
    {
        foreach (var child in go.GetChildrenDeep())
        {
            if (child.IsStatic == value) continue;
            Undo.RecordGameObjectChange(child, "Toggle Static", child.IsStatic, value, (g, x) => g.IsStatic = x);
            child.IsStatic = value;
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

        bool expanded = SectionHeader(paper, font, "gi_transform", EditorIcons.ArrowsUpDownLeftRight,
            Loc.Get("inspector.transform"), EditorTheme.Ink500, () =>
            {
                HeaderIconBtn(paper, font, "gi_tf_reset", EditorIcons.ArrowRotateRight, () => ResetTransform(go));
                HeaderIconBtn(paper, font, "gi_tf_dots", EditorIcons.EllipsisVertical, () =>
                    Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
                        b.Item(Loc.Get("inspector.reset"), () => ResetTransform(go), icon: EditorIcons.ArrowRotateRight)));
            });
        if (!expanded) return;

        // Position
        var pos = t.LocalPosition;
        EditorGUI.Row(paper, "gi_pos", Loc.Get("inspector.position"), () =>
            Origami.Float3Field(paper, "gi_pos_vf", pos, v => { Undo.RecordGameObjectChange(go, "Change Position", t.LocalPosition, v, (g, x) => g.Transform.LocalPosition = x, coalesce: true); t.LocalPosition = v; }).Show());

        // Rotation (as euler)
        var euler = t.LocalEulerAngles;
        EditorGUI.Row(paper, "gi_rot", Loc.Get("inspector.rotation"), () =>
            Origami.Float3Field(paper, "gi_rot_vf", euler, v => { Undo.RecordGameObjectChange(go, "Change Rotation", t.LocalEulerAngles, v, (g, x) => g.Transform.LocalEulerAngles = x, coalesce: true); t.LocalEulerAngles = v; }).Show());

        // Scale
        var scale = t.LocalScale;
        EditorGUI.Row(paper, "gi_scale", Loc.Get("inspector.scale"), () =>
            Origami.Float3Field(paper, "gi_scale_vf", scale, v => { Undo.RecordGameObjectChange(go, "Change Scale", t.LocalScale, v, (g, x) => g.Transform.LocalScale = x, coalesce: true); t.LocalScale = v; }).Show());
    }

    private static void ResetTransform(GameObject go)
    {
        var t = go.Transform;
        var goId = go.Identifier;
        var (oldP, oldR, oldS) = (t.LocalPosition, t.LocalEulerAngles, t.LocalScale);
        Undo.RegisterAction("Reset Transform",
            undo: () => { var g = Undo.FindGO(goId); if (g != null) { g.Transform.LocalPosition = oldP; g.Transform.LocalEulerAngles = oldR; g.Transform.LocalScale = oldS; } },
            redo: () => { var g = Undo.FindGO(goId); if (g != null) { g.Transform.LocalPosition = Float3.Zero; g.Transform.LocalEulerAngles = Float3.Zero; g.Transform.LocalScale = Float3.One; } });
        t.LocalPosition = Float3.Zero;
        t.LocalEulerAngles = Float3.Zero;
        t.LocalScale = Float3.One;
    }

    private static readonly (Float2 min, Float2 max)[,] AnchorPresets = new (Float2, Float2)[4, 4]
    {
        // Row 0: top fixed (TopLeft, TopCenter, TopRight) + horizontal-stretch top
        { (new(0f, 1f), new(0f, 1f)),     (new(0.5f, 1f), new(0.5f, 1f)),     (new(1f, 1f), new(1f, 1f)),     (new(0f, 1f), new(1f, 1f)) },
        // Row 1: middle fixed (MidLeft, MidCenter, MidRight) + horizontal-stretch middle
        { (new(0f, 0.5f), new(0f, 0.5f)), (new(0.5f, 0.5f), new(0.5f, 0.5f)), (new(1f, 0.5f), new(1f, 0.5f)), (new(0f, 0.5f), new(1f, 0.5f)) },
        // Row 2: bottom fixed (BottomLeft, BottomCenter, BottomRight) + horizontal-stretch bottom
        { (new(0f, 0f), new(0f, 0f)),     (new(0.5f, 0f), new(0.5f, 0f)),     (new(1f, 0f), new(1f, 0f)),     (new(0f, 0f), new(1f, 0f)) },
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


        // Anchors (Min/Max) exposed for fine-grained tweaking outside the preset grid.
        EditorGUI.Row(paper, "gi_rt_amin", "Anchor Min", () =>
        {
            Origami.Float2Field(paper, "gi_rt_amin_vf", rt.AnchorMin, v =>
            {
                Undo.Snapshot(rt);
                SetAnchorsPreservingRect(rt, v, rt.AnchorMax);
            }).Show();
        });

        EditorGUI.Row(paper, "gi_rt_amax", "Anchor Max", () =>
        {
            Origami.Float2Field(paper, "gi_rt_amax_vf", rt.AnchorMax, v =>
            {
                Undo.Snapshot(rt);
                SetAnchorsPreservingRect(rt, rt.AnchorMin, v);
            }).Show();
        });

        // Rotation (as euler) and scale come from the underlying Transform.
        var euler = t.LocalEulerAngles;
        EditorGUI.Row(paper, "gi_rt_rot", "Rotation", () =>
        {
            Origami.Float3Field(paper, "gi_rt_rot_vf", euler, v =>
            {
                Undo.RecordGameObjectChange(go, "Change Rotation", t.LocalEulerAngles, v,
                    (g, x) =>
                    {
                        g.Transform.LocalEulerAngles = x;
                        rt.MarkLayoutDirty();
                    }, coalesce: true);
                t.LocalEulerAngles = v;
                rt.MarkLayoutDirty();
            }).Show();
        });

        var scale = t.LocalScale;
        EditorGUI.Row(paper, "gi_rt_scale", "Scale", () =>
        {
            Origami.Float3Field(paper, "gi_rt_scale_vf", scale, v =>
            {
                Undo.RecordGameObjectChange(go, "Change Scale", t.LocalScale, v, (g, x) =>
                    {
                        g.Transform.LocalScale = x;
                        rt.MarkLayoutDirty();
                    },
                    coalesce: true);
                t.LocalScale = v;
                rt.MarkLayoutDirty();
            }).Show();
        });
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
                // The cell is drawn in the editor's Y-down PaperUI, so flip the anchor's
                // +Y-up Y to place a Y=1 (top) anchor at the visual top of the cell.
                const float DotSize = 4f;
                float range = inner - DotSize;
                float dx = 2f + (float)minPreset.X * range;
                float dy = 2f + (1f - (float)minPreset.Y) * range;
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
                    // Horizontal bar at a fixed Y flip the +Y-up anchor Y for Y-down PaperUI.
                    bx = 2f;
                    by = 2f + (1f - (float)minPreset.Y) * (inner - BarThickness);
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
                    // Stretch in both axes fill the cell.
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
        EditorGUI.Row(paper, "gi_rt_pos", "Position", () =>
        {
            // X/Y drive RectTransform.AnchoredPosition; Z drives Transform.LocalPosition (RectTransform has no Z).
            var value = new Float3(rt.AnchoredPosition.X, rt.AnchoredPosition.Y, t.LocalPosition.Z);
            Origami.Float3Field(paper, "gi_rt_apos_vf", value, v =>
            {
                if (v.X != rt.AnchoredPosition.X || v.Y != rt.AnchoredPosition.Y)
                {
                    var newAnchored = new Float2(v.X, v.Y);
                    Undo.RecordGameObjectChange(go, "Change Position", rt.AnchoredPosition, newAnchored, (g, x) => g.RectTransform.AnchoredPosition = x, coalesce: true);
                    rt.AnchoredPosition = newAnchored;
                }
                if (v.Z != t.LocalPosition.Z)
                {
                    var newLoc = new Float3(t.LocalPosition.X, t.LocalPosition.Y, v.Z);
                    Undo.RecordGameObjectChange(go, "Change Position", t.LocalPosition, newLoc, (g, x) => g.Transform.LocalPosition = x, coalesce: true);
                    t.LocalPosition = newLoc;
                }
            }).Show();
        }, labelWidth: EditorTheme.LabelWidth/2f);

    }

    /// <summary>Width / Height row driven by RectTransform.SizeDelta.</summary>
    private static void DrawSizeRow(Paper paper, Prowl.Scribe.FontFile font, GameObject go, RectTransform rt)
    {
        EditorGUI.Row(paper, "gi_rt_size", "Size", () =>
        {
            Origami.Float2Field(paper, "gi_rt_size_vf", rt.SizeDelta,v =>
            {
                Undo.RecordGameObjectChange(go, "Change SizeDelta", rt.SizeDelta, v,
                    (g, x) => { var r = g.RectTransform; if (r != null) r.SizeDelta = x; }, coalesce: true);
                rt.SizeDelta = v;
            }).Show();
        }, labelWidth: EditorTheme.LabelWidth/2f);
    }

    private static void DrawPivotRow(Paper paper, Prowl.Scribe.FontFile font, GameObject go, RectTransform rt)
    {

        EditorGUI.Row(paper, "gi_rt_pivot", "Pivot", () =>
        {
            Origami.Float2Field(paper, "gi_rt_pivot_vf", rt.Pivot,v =>
            {
                Undo.RecordGameObjectChange(go, "Change Pivot", rt.Pivot, v,
                    (g, x) => { var r = g.RectTransform; if (r != null) r.Pivot = x; }, coalesce: true);
                rt.Pivot = v;
            }).Show();
        }, labelWidth: EditorTheme.LabelWidth/2f);

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
            if (comp is RectTransform) continue; // drawn at the top in place of the Transform

            string compId = $"gi_comp_{comp.Identifier}";
            string compName = comp.GetType().Name;
            string icon = GetComponentIcon(comp);

            int capturedI = i;
            bool expanded = SectionHeader(paper, font, compId, icon, compName,
                comp.Enabled ? EditorTheme.Ink500 : EditorTheme.Ink300,
                trailing: () =>
                {
                    // Wrap the checkbox so its short toggle row centers vertically in the header.
                    using (paper.Box($"{compId}_en_wrap").Width(UnitValue.Auto).Height(UnitValue.Auto)
                        .Margin(0, 0, ST, ST).Enter())
                        Origami.Checkbox(paper, $"{compId}_en", comp.Enabled,
                            v => { var old = comp.Enabled; var cId = comp.Identifier; Undo.RegisterAction("Toggle Component", () => { var c = Undo.FindComponent(cId); if (c != null) { c.Enabled = old; c.OnValidate(); } }, () => { var c = Undo.FindComponent(cId); if (c != null) { c.Enabled = v; c.OnValidate(); } }); comp.Enabled = v; comp.OnValidate(); })
                            .NoLabel().Show();
                    HeaderIconBtn(paper, font, $"{compId}_gear", EditorIcons.EllipsisVertical, () =>
                        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
                            BuildComponentContextMenu(b, go, comp, capturedI)));
                },
                onDragStart: () => DragDrop.StartDrag(new ComponentDragPayload(go, comp)));

            if (expanded)
            {
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
                    // Overrides are stored on the instance root with root-relative paths.
                    var overrideHost = PrefabUtility.GetPrefabInstanceRoot(go) ?? go;
                    foreach (var ov in overrideHost.PrefabOverrides)
                    {
                        if (ov.Path.StartsWith(pathPrefix))
                            overridden.Add(ov.Path[pathPrefix.Length..].Split('.')[0]);
                    }
                    PropertyGridUtils.OverriddenFields = overridden.Count > 0 ? overridden : null;
                }

                // Component body use custom editor or default PropertyGrid
                var customEditor = CustomEditorRegistry.GetEditor(comp.GetType());
                if (customEditor != null)
                    customEditor.OnGUI(paper, compId, comp);
                else
                    PropertyGridUtils.Draw(paper, compId, comp);

                PropertyGridUtils.OverriddenFields = null;

                // Draw [Button] attributed methods
                DrawButtonMethods(paper, $"{compId}_btns", comp);
            }

            // Auto-detect overrides by comparing against prefab source (index-based)
            if (go.IsPrefabInstance)
                PrefabUtility.DetectComponentOverrides(go, comp);

            Divider(paper, $"{compId}_sep");
        }
    }

    private static void BuildComponentContextMenu(ContextBuilder builder, GameObject go, MonoBehaviour comp, int index)
    {
        builder.Item(Loc.Get("inspector.reset"), () =>
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
        builder.Item(Loc.Get("inspector.remove_component"), () =>
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
        builder.Item(Loc.Get("inspector.move_up"), () =>
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

        builder.Item(Loc.Get("inspector.move_down"), () =>
        {
            var oldIdx = index; var newIdx = index + 1;
            Undo.RegisterAction("Move Component Down",
                () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(oldIdx); },
                () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(newIdx); });
            comp.SetSiblingIndex(newIdx);
        }, icon: EditorIcons.ArrowDown);

        builder.Separator();

        builder.Item(Loc.Get("inspector.pop_out"), () =>
        {
            ComponentPopoutPanel.PopOut(go, comp);
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
                .Text($"{EditorIcons.Plus}  {Loc.Get("inspector.add_component")}", font)
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
        string prefabName = isMissing ? Loc.Get("inspector.missing") : System.IO.Path.GetFileNameWithoutExtension(entry!.Path);

        string label = isMissing ? Loc.Get("inspector.missing_prefab")
            : isNested ? Loc.Get("inspector.nested_prefab", new { name = prefabName })
            : Loc.Get("inspector.prefab", new { name = prefabName });

        var barColor = isMissing ? Color.FromArgb(40, EditorTheme.Red300) : Color.FromArgb(40, EditorTheme.Purple400);
        var borderColor = isMissing ? EditorTheme.Red300 : EditorTheme.Purple300;
        var textColor = isMissing ? EditorTheme.Red300 : EditorTheme.Purple400;

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
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                if (!isMissing)
                {
                    Origami.Button(paper, "gi_prefab_select", Loc.Get("inspector.select"), () => { Selection.Ping(go.PrefabAssetId); }).Width(55).Show();

                    if (isRoot && hasOverrides)
                    {
                        Origami.Button(paper, "gi_prefab_revert", Loc.Get("inspector.revert"), () => { if (root != null) PrefabUtility.RevertOverrides(root); }).Width(55).Show();

                        Origami.Button(paper, "gi_prefab_apply", Loc.Get("inspector.apply"), () => { if (root != null) PrefabUtility.ApplyOverrides(root); }).Width(50).Show();
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
        // Overrides for the whole prefab instance are stored on its root.
        go = PrefabUtility.GetPrefabInstanceRoot(go) ?? go;
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
                        .Text(Loc.Get("inspector.revert"), font).TextColor(EditorTheme.Ink400)
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
                        .Text(Loc.Get("inspector.apply"), font).TextColor(EditorTheme.Ink400)
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

    // ================================================================
    //  Anchor editing that preserves screen position
    // ================================================================

    /// <summary>
    /// Change a RectTransform's anchors while keeping its on-screen rect fixed: the anchor reference
    /// shifts and <see cref="RectTransform.AnchoredPosition"/> / <see cref="RectTransform.SizeDelta"/>
    /// are back-solved against the parent rect so the element does not move (Unity-style).
    /// </summary>
    private static void SetAnchorsPreservingRect(RectTransform rt, Float2 newMin, Float2 newMax)
    {
        Rect world = rt.ComputedRect;
        Rect parent = GetParentRect(rt);

        rt.AnchorMin = newMin;
        rt.AnchorMax = newMax;

        // Without a laid-out rect (parent or self) there is nothing to preserve; just apply anchors.
        if (parent.Size.X <= 0 || parent.Size.Y <= 0 || world.Size.X <= 0 || world.Size.Y <= 0)
            return;

        SolveAxis(newMin.X, newMax.X, parent.Min.X + newMin.X * parent.Size.X, parent.Min.X + newMax.X * parent.Size.X,
            world.Min.X, world.Max.X, rt.Pivot.X, out float sizeX, out float posX);
        SolveAxis(newMin.Y, newMax.Y, parent.Min.Y + newMin.Y * parent.Size.Y, parent.Min.Y + newMax.Y * parent.Size.Y,
            world.Min.Y, world.Max.Y, rt.Pivot.Y, out float sizeY, out float posY);

        rt.SizeDelta = new Float2(sizeX, sizeY);
        rt.AnchoredPosition = new Float2(posX, posY);
    }

    // Inverse of RectTransform.ComputeRect for one axis: given the target world span [rMin,rMax] and
    // the new anchors (in normalized + pixel form), solve the SizeDelta/AnchoredPosition that reproduce it.
    private static void SolveAxis(float aMin, float aMax, float aMinPx, float aMaxPx,
        float rMin, float rMax, float pivot, out float size, out float pos)
    {
        float rSize = rMax - rMin;
        if (Math.Abs(aMin - aMax) < 1e-6f)
        {
            // Fixed: keep the size; place the pivot so the rect stays put.
            size = rSize;
            pos = rMin - aMinPx + pivot * rSize;
        }
        else
        {
            // Stretch: SizeDelta is padding vs the anchor span, AnchoredPosition recenters it.
            size = (aMaxPx - aMinPx) - rSize;
            pos = ((rMin + rMax) - (aMinPx + aMaxPx)) * 0.5f;
        }
    }

    // The rect this element is laid out inside: the parent RectTransform's computed rect, or the
    // canvas root rect when the element is a direct child of the canvas.
    private static Rect GetParentRect(RectTransform rt)
    {
        var canvas = rt.GameObject.GetComponentInParent<GameCanvas>(includeSelf: true);
        var parentGo = rt.GameObject.Parent;
        var parentRt = parentGo?.RectTransform;
        if (parentRt != null && parentGo != canvas?.GameObject &&
            parentRt.ComputedRect.Size.X > 0 && parentRt.ComputedRect.Size.Y > 0)
            return parentRt.ComputedRect;
        return canvas?.RootRect ?? default;
    }
}
