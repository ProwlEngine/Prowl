using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;
using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
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

        DrawHeader(paper, go);
        EditorGUI.Divider(paper, "gi_sep_header", 6f);
        if (go.RectTransform.IsValid())
        {
            DrawRectTransform(paper, font, go);
        }
        else
        {
            DrawTransform(paper, font, go);
        }

        EditorGUI.Divider(paper, "gi_sep_transform", 6f);
        DrawComponents(paper, font, go);

        // Prefab instances included: a component added to an instance belongs to that instance and
        // is carried across refreshes. What a prefab instance cannot do is restructure the source.
        DrawAddComponentButton(paper, font, go);

        // Detect GO-level overrides (Name, Tag, Transform, etc.)
        if (go.IsPrefabInstance)
            PrefabUtility.RecordGameObjectOverrides(go);

        paper.Box("gi_bottom_pad").Height(20);
    }

    // Sections (Transform + each component) remember their collapsed state here; absent = expanded.
    private static readonly HashSet<string> s_collapsedSections = [];
    private static bool IsExpanded(string id) => !s_collapsedSections.Contains(id);
    private static void ToggleSection(string id) { if (!s_collapsedSections.Add(id)) s_collapsedSections.Remove(id); }

    // The section whose menu is showing. Origami only says whether some menu is open, so the section notes
    // itself each time its menu builds and is forgotten once no menu is open.
    private static string? s_menuSection;

    private static bool IsMenuOpen(string id)
    {
        if (!ContextMenu.IsOpen) s_menuSection = null;
        return s_menuSection == id;
    }

    private static bool SectionHeader(Paper paper, Prowl.Scribe.FontFile font, string id,
        string glyph, string title, Color titleColor, Action? trailing = null, Action? onDragStart = null,
        Action<ContextBuilder>? contextMenu = null)
    {
        bool expanded = IsExpanded(id);
        Scribe.FontFile semi = EditorTheme.FontSemiBold ?? font;
        Action<ContextBuilder>? menu = contextMenu == null ? null : b => { s_menuSection = id; contextMenu(b); };
        using (paper.Row($"{id}_head").Height(30).Padding(10, 8, 0, 0).Gap(7).Enter())
        {
            if (menu != null)
                Origami.RightClickMenu(paper, $"{id}_ctx", menu);

            Color highlight = Color.FromArgb(13, EditorTheme.Purple400);
            ElementBuilder clickRow = paper.Row($"{id}_hclick").Width(UnitValue.Stretch()).Height(30).Gap(7).Rounded(EditorTheme.Roundness)
                .BackgroundColor(IsMenuOpen(id) ? highlight : Color.Transparent)
                .Hovered.BackgroundColor(highlight).End()
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
            if (menu != null)
                EditorGUI.HeaderIconButton(paper, $"{id}_dots", EditorIcons.EllipsisVertical, Loc.Get("inspector.options"), () =>
                    Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu));
        }
        return expanded;
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
        if (gos.Count == 0) return;
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
        using (paper.Row("gim_header").Height(EditorTheme.RowHeight).Margin(0, 6).Gap(6).Enter())
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

        using (paper.Row("gim_tag_layer").Height(22).Gap(6).Enter())
        {
            string[] tagNames = [.. TagLayerManager.tags];
            bool tagMixed = gos.Select(g => g.Tag).Distinct().Count() > 1;
            int tagIdx = tagMixed ? -1 : Math.Max(0, TagLayerManager.tags.IndexOf(gos[0].Tag));

            DrawInlineLabeled(paper, "gim_tag_row", MixLabel(Loc.Get("inspector.tag"), tagMixed), font, () =>
                Origami.Dropdown(paper, "gim_tag", tagIdx,
                    v =>
                    {
                        if (v >= 0 && v < tagNames.Length)
                            Undo.ApplyGameObjectChanges(gos, "Change Tag", g => g.Tag, (g, x) => g.Tag = x, tagNames[v]);
                    }, tagNames).Show());

            string[] allLayers = TagLayerManager.layers;
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
        paper.Box("gim_transform_header").Height(22).PaddingLeft(8)
            .Text($"{EditorIcons.ArrowsUpDownLeftRight}  {Loc.Get("inspector.transform")}", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        Transform t0 = gos[0].Transform;

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
        foreach (Component c in gos[0].GetComponents<Component>())
        {
            if (c.HideFlags.HasFlag(HideFlags.Hide)) continue;
            if (c is RectTransform) continue; // handled by the transform row at the top
            Type ct = c.GetType();
            if (!orderedTypes.Contains(ct)) orderedTypes.Add(ct);
        }

        foreach (Type type in orderedTypes)
        {
            var instances = new List<object>(gos.Count);
            bool onAll = true;
            foreach (GameObject go in gos)
            {
                Component? match = null;
                foreach (Component c in go.GetComponents<Component>())
                {
                    if (c.HideFlags.HasFlag(HideFlags.Hide)) continue;
                    if (c.GetType() == type) { match = c; break; }
                }
                if (match.IsNotValid()) { onAll = false; break; }
                instances.Add(match);
            }
            if (!onAll) continue;

            string compId = $"gim_comp_{type.Name}";
            string icon = GetComponentIcon((Component)instances[0]);

            using (paper.Row($"{compId}_header")
                .Height(24).BackgroundColor(EditorTheme.Neutral300).Rounded(EditorTheme.Roundness).PaddingLeft(4).Gap(4).Enter())
            {
                bool allEn = instances.All(o => ((Component)o).Enabled);
                Origami.Checkbox(paper, $"{compId}_en", allEn,
                    v =>
                    {
                        var actions = new List<(Action, Action)>(instances.Count);
                        foreach (object o in instances)
                        {
                            var c = (Component)o;
                            Guid cid = c.Identifier;
                            bool old = c.Enabled;
                            actions.Add((() => { Component? x = Undo.FindComponent(cid); if (x.IsValid()) { x.Enabled = old; x.OnValidate(); } },
                                        () => { Component? x = Undo.FindComponent(cid); if (x.IsValid()) { x.Enabled = v; x.OnValidate(); } }
                            ));
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

    private static void DrawHeader(Paper paper, GameObject go)
    {
        Guid goId = go.Identifier;

        // Enabled toggle + Name + Static
        using (paper.Row("gi_header")
            .Height(26)
            .Margin(0, 0, 8, 4)
            .Padding(11, 11, 0, 0)
            .Gap(8)
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
                .CommitOnEnter().Show();

            Origami.Dropdown(paper, "gi_static", go.IsStatic ? 1 : 0,
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
                }, ["Dynamic", "Static"]).Width(90).Height(26).Show();
        }

        // Tag + Layer row (label-inside sel dropdowns)
        using (paper.Row("gi_tag_layer")
            .Height(26)
            .Margin(0, 0, 4, 0)
            .Padding(11, 11, 0, 0)
            .Gap(7)
            .Enter())
        {
            string[] tagNames = [.. TagLayerManager.tags];
            int tagIdx = TagLayerManager.tags.IndexOf(go.Tag);
            if (tagIdx < 0) tagIdx = 0;
            Origami.Dropdown(paper, "gi_tag", tagIdx,
                v =>
                {
                    if (v >= 0 && v < tagNames.Length)
                    {
                        string newTag = tagNames[v];
                        Undo.RecordGameObjectChange(go, "Change Tag", go.Tag, newTag, (g, x) => g.Tag = x);
                        go.Tag = newTag;
                    }
                }, tagNames).Inline(Loc.Get("inspector.tag")).Width(UnitValue.Stretch()).Height(26).Show();

            string[] allLayers = TagLayerManager.layers;
            var layerNames = new List<string>();
            var layerIndices = new List<int>();
            for (int i = 0; i < allLayers.Length; i++)
                if (!string.IsNullOrEmpty(allLayers[i])) { layerNames.Add(allLayers[i]); layerIndices.Add(i); }

            int selLayer = layerIndices.IndexOf(go.LayerIndex);
            if (selLayer < 0) selLayer = 0;
            Origami.Dropdown(paper, "gi_layer", selLayer,
                v =>
                {
                    if (v >= 0 && v < layerIndices.Count)
                    {
                        int newIdx = layerIndices[v];
                        Undo.RecordGameObjectChange(go, "Change Layer", go.LayerIndex, newIdx, (g, x) => g.LayerIndex = x);
                        go.LayerIndex = newIdx;
                    }
                }, layerNames).Inline(Loc.Get("inspector.layer")).Width(UnitValue.Stretch()).Height(26).Show();
        }
    }

    /// <summary>Set <see cref="GameObject.IsStatic"/> on every descendant of <paramref name="go"/> (recorded for undo).</summary>
    private static void ApplyStaticToChildren(GameObject go, bool value)
    {
        foreach (GameObject child in go.GetChildrenDeep())
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
        using (paper.Row(id).Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).Gap(4).Enter())
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
        Transform t = go.Transform;
        Guid goId = go.Identifier;

        bool expanded = SectionHeader(paper, font, $"gi_transform_{goId}", EditorIcons.ArrowsUpDownLeftRight,
            Loc.Get("inspector.transform"), EditorTheme.Ink500,
            () => EditorGUI.HeaderIconButton(paper, "gi_tf_reset", EditorIcons.ArrowRotateRight, Loc.Get("inspector.reset"), () => ResetTransform(go)),
            contextMenu: b => b.Item(Loc.Get("inspector.reset"), () => ResetTransform(go), icon: EditorIcons.ArrowRotateRight));
        if (!expanded) return;

        // Position
        Float3 pos = t.LocalPosition;
        EditorGUI.Row(paper, "gi_pos", Loc.Get("inspector.position"), () =>
            Origami.Float3Field(paper, "gi_pos_vf", pos, v => { Undo.RecordGameObjectChange(go, "Change Position", t.LocalPosition, v, (g, x) => g.Transform.LocalPosition = x, coalesce: true); t.LocalPosition = v; }).Show());

        // Rotation (as euler)
        Float3 euler = t.LocalEulerAngles;
        EditorGUI.Row(paper, "gi_rot", Loc.Get("inspector.rotation"), () =>
            Origami.Float3Field(paper, "gi_rot_vf", euler, v => { Undo.RecordGameObjectChange(go, "Change Rotation", t.LocalEulerAngles, v, (g, x) => g.Transform.LocalEulerAngles = x, coalesce: true); t.LocalEulerAngles = v; }).Show());

        // Scale
        Float3 scale = t.LocalScale;
        EditorGUI.Row(paper, "gi_scale", Loc.Get("inspector.scale"), () =>
            Origami.Float3Field(paper, "gi_scale_vf", scale, v => { Undo.RecordGameObjectChange(go, "Change Scale", t.LocalScale, v, (g, x) => g.Transform.LocalScale = x, coalesce: true); t.LocalScale = v; }).Show());
    }

    private static void ResetTransform(GameObject go)
    {
        Transform t = go.Transform;
        Guid goId = go.Identifier;
        (Float3 oldP, Float3 oldR, Float3 oldS) = (t.LocalPosition, t.LocalEulerAngles, t.LocalScale);
        Undo.RegisterAction("Reset Transform",
            undo: () => { GameObject? g = Undo.FindGO(goId); if (g.IsValid()) { g.Transform.LocalPosition = oldP; g.Transform.LocalEulerAngles = oldR; g.Transform.LocalScale = oldS; } },
            redo: () => { GameObject? g = Undo.FindGO(goId); if (g.IsValid()) { g.Transform.LocalPosition = Float3.Zero; g.Transform.LocalEulerAngles = Float3.Zero; g.Transform.LocalScale = Float3.One; } });
        t.LocalPosition = Float3.Zero;
        t.LocalEulerAngles = Float3.Zero;
        t.LocalScale = Float3.One;
    }

    private static readonly (Float2 min, Float2 max)[,] s_anchorPresets = new (Float2, Float2)[4, 4]
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
        RectTransform rt = go.RectTransform!;
        Transform t = go.Transform;

        paper.Box("gi_rt_header").Height(22).PaddingLeft(8)
            .Text($"{EditorIcons.VectorSquare}  Rect Transform", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
            .OnRightClick(rt, (r, _) => Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y,
                b => AddRemoveComponentItem(b, go, r)));

        // Top block: 4x4 anchor preset grid on the left, position + size fields filling the rest.
        using (paper.Row("gi_rt_top").Height(UnitValue.Auto).Gap(8).Margin(4, 4, 2, 2).Enter())
        {
            DrawAnchorPresetGrid(paper, go, rt);

            using (paper.Column("gi_rt_top_right").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
            {
                DrawPositionRow(paper, go, rt, t);
                DrawSizeRow(paper, go, rt);
                DrawPivotRow(paper, go, rt);
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
        Float3 euler = t.LocalEulerAngles;
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

        Float3 scale = t.LocalScale;
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
    private static void DrawAnchorPresetGrid(Paper paper, GameObject go, RectTransform rt)
    {
        const float CellSize = 22f;
        const float CellGap = 2f;
        const float GridPad = 4f;
        const float GridSize = CellSize * 4 + CellGap * 3 + GridPad * 2;

        using (paper.Column("gi_rt_apreset")
            .Width(GridSize).Height(GridSize)
            .BackgroundColor(EditorTheme.Neutral200)
            .BorderColor(EditorTheme.Ink100).BorderWidth(1)
            .Rounded(Origami.Current.Metrics.SmallRounding)
            .PaddingLeft(GridPad).PaddingRight(GridPad).PaddingTop(GridPad).PaddingBottom(GridPad)
            .Gap(CellGap)
            .Enter())
        {
            for (int row = 0; row < 4; row++)
            {
                using (paper.Row($"gi_rt_apreset_r{row}")
                    .Height(CellSize).Gap(CellGap).Enter())
                {
                    for (int col = 0; col < 4; col++)
                    {
                        (Float2 min, Float2 max) = s_anchorPresets[row, col];
                        DrawAnchorPresetCell(paper, $"gi_rt_acell_{row}_{col}", go, rt,
                            min, max, CellSize);
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

        Color bg = isActive ? EditorTheme.Purple400 : EditorTheme.Neutral300;
        Color hoverBg = isActive ? EditorTheme.Purple400 : EditorTheme.Neutral400;
        Color indicator = isActive ? EditorTheme.Neutral200 : EditorTheme.Purple400;

        using (paper.Box(id)
            .Width(cellSize).Height(cellSize)
            .BackgroundColor(bg)
            .Hovered.BackgroundColor(hoverBg).End()
            .BorderColor(EditorTheme.Ink100).BorderWidth(1)
            .Rounded(Origami.Current.Metrics.SmallRounding)
            .OnClick((go, minPreset, maxPreset), (cap, _) =>
            {
                (GameObject? capGo, Float2 capMin, Float2 capMax) = cap;
                RectTransform? r = capGo.RectTransform;
                if (r.IsNotValid()) return;

                // Changing the anchor preset re-parameterizes the rect but must keep it visually put,
                // so it re-solves SizeDelta/AnchoredPosition. Snapshot the full solved state both ways
                // so undo/redo restore the exact values rather than re-deriving them.
                Float2 oldMin = r.AnchorMin; Float2 oldMax = r.AnchorMax;
                Float2 oldSize = r.SizeDelta; Float2 oldPos = r.AnchoredPosition;

                SetAnchorsPreservingRect(r, capMin, capMax);

                Float2 newMin = r.AnchorMin; Float2 newMax = r.AnchorMax;
                Float2 newSize = r.SizeDelta; Float2 newPos = r.AnchoredPosition;

                Undo.RegisterAction("Change Anchor Preset",
                    undo: () => { RectTransform? rr = capGo.RectTransform; if (rr.IsValid()) { rr.AnchorMin = oldMin; rr.AnchorMax = oldMax; rr.SizeDelta = oldSize; rr.AnchoredPosition = oldPos; } },
                    redo: () => { RectTransform? rr = capGo.RectTransform; if (rr.IsValid()) { rr.AnchorMin = newMin; rr.AnchorMax = newMax; rr.SizeDelta = newSize; rr.AnchoredPosition = newPos; } });
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

    // Nebula axis colors, matching Origami's vector fields (X red, Y green, Z blue).
    private static readonly System.Drawing.Color s_axisXColor = System.Drawing.Color.FromArgb(255, 251, 113, 133);
    private static readonly System.Drawing.Color s_axisYColor = System.Drawing.Color.FromArgb(255, 74, 222, 128);
    private static readonly System.Drawing.Color s_axisZColor = System.Drawing.Color.FromArgb(255, 96, 165, 250);

    private static bool AxisStretched(float anchorMin, float anchorMax) => MathF.Abs(anchorMin - anchorMax) > 1e-5f;

    // Rect edges relative to their anchors, as functions of AnchoredPosition/SizeDelta/Pivot only
    // (the anchor pixel positions cancel, so no parent rect is needed for these).
    private static float OffsetMin(float anchored, float size, float pivot) => anchored - pivot * size;
    private static float OffsetMax(float anchored, float size, float pivot) => anchored + (1f - pivot) * size;

    // Solve (AnchoredPosition, SizeDelta) for one axis so its min edge (offsetMin) becomes newMin while
    // the max edge is held - i.e. dragging the Left/Bottom field keeps the opposite edge pinned.
    private static void SolveHoldMax(float anchored, float size, float pivot, float newMin, out float outAnchored, out float outSize)
    {
        float omax = OffsetMax(anchored, size, pivot);
        outSize = omax - newMin;
        outAnchored = newMin + pivot * outSize;
    }

    // Solve so the max edge (offsetMax) becomes newMax while the min edge is held (Right/Top field).
    private static void SolveHoldMin(float anchored, float size, float pivot, float newMax, out float outAnchored, out float outSize)
    {
        float omin = OffsetMin(anchored, size, pivot);
        outSize = newMax - omin;
        outAnchored = omin + pivot * outSize;
    }

    private static void NumCell(Paper paper, string id, string label, System.Drawing.Color color, float value, Action<float> onChange)
        => Origami.NumericField<float>(paper, id, value, onChange).DraggableLabel(label, color, compact: true).Show();

    private static void ApplyPosSize(GameObject g, (Float2 pos, Float2 size) v)
    {
        RectTransform? r = g.RectTransform;
        if (r.IsNotValid()) return;
        r.AnchoredPosition = v.pos;
        r.SizeDelta = v.size;
    }

    // Writes a new value into one axis component of AnchoredPosition/SizeDelta, coalescing the undo so
    // a scrub-drag collapses to a single step. Both are recorded together since a stretched-edge edit
    // moves position and size at once.
    private static void ApplyAxis(GameObject go, RectTransform rt, int axis, float newAnchored, float newSize, string desc)
    {
        Float2 pos = rt.AnchoredPosition, size = rt.SizeDelta;
        Float2 nPos = axis == 0 ? new Float2(newAnchored, pos.Y) : new Float2(pos.X, newAnchored);
        Float2 nSize = axis == 0 ? new Float2(newSize, size.Y) : new Float2(size.X, newSize);
        Undo.RecordGameObjectChange(go, desc, (pos, size), (nPos, nSize), ApplyPosSize, coalesce: true);
        rt.AnchoredPosition = nPos;
        rt.SizeDelta = nSize;
    }

    /// <summary>
    /// Position row. Each axis is contextual: a fixed axis shows Pos X / Pos Y, a
    /// stretched axis shows the edge inset (Left / Top) instead. Z always comes from Transform.LocalPosition.
    /// </summary>
    private static void DrawPositionRow(Paper paper, GameObject go, RectTransform rt, Transform t)
    {
        bool sx = AxisStretched(rt.AnchorMin.X, rt.AnchorMax.X);
        bool sy = AxisStretched(rt.AnchorMin.Y, rt.AnchorMax.Y);
        Float2 p = rt.AnchoredPosition, s = rt.SizeDelta, pv = rt.Pivot;

        EditorGUI.Row(paper, "gi_rt_pos", "Position", () =>
        {
            using (paper.Row("gi_rt_pos_r").Height(UnitValue.Auto).Gap(6).Enter())
            {
                if (sx)
                    NumCell(paper, "gi_rt_pos_x", "Left", s_axisXColor, OffsetMin(p.X, s.X, pv.X), nv =>
                    { SolveHoldMax(p.X, s.X, pv.X, nv, out float a, out float sz); ApplyAxis(go, rt, 0, a, sz, "Rect Left"); });
                else
                    NumCell(paper, "gi_rt_pos_x", "X", s_axisXColor, p.X, nv =>
                        ApplyAxis(go, rt, 0, nv, rt.SizeDelta.X, "Rect PosX"));

                if (sy)
                    NumCell(paper, "gi_rt_pos_y", "Top", s_axisYColor, -OffsetMax(p.Y, s.Y, pv.Y), nv =>
                    { SolveHoldMin(p.Y, s.Y, pv.Y, -nv, out float a, out float sz); ApplyAxis(go, rt, 1, a, sz, "Rect Top"); });
                else
                    NumCell(paper, "gi_rt_pos_y", "Y", s_axisYColor, p.Y, nv =>
                        ApplyAxis(go, rt, 1, nv, rt.SizeDelta.Y, "Rect PosY"));

                NumCell(paper, "gi_rt_pos_z", "Z", s_axisZColor, t.LocalPosition.Z, nv =>
                {
                    var nl = new Float3(t.LocalPosition.X, t.LocalPosition.Y, nv);
                    Undo.RecordGameObjectChange(go, "Rect PosZ", t.LocalPosition, nl, (g, x) => g.Transform.LocalPosition = x, coalesce: true);
                    t.LocalPosition = nl;
                });
            }
        }, labelWidth: EditorTheme.LabelWidth / 2f);
    }

    /// <summary>
    /// Size row. Contextual per axis: a fixed axis shows Width / Height, a stretched axis shows the
    /// opposite edge inset (Right / Bottom). An empty third column keeps it aligned with the Z field above.
    /// </summary>
    private static void DrawSizeRow(Paper paper, GameObject go, RectTransform rt)
    {
        bool sx = AxisStretched(rt.AnchorMin.X, rt.AnchorMax.X);
        bool sy = AxisStretched(rt.AnchorMin.Y, rt.AnchorMax.Y);
        Float2 p = rt.AnchoredPosition, s = rt.SizeDelta, pv = rt.Pivot;

        EditorGUI.Row(paper, "gi_rt_size", "Size", () =>
        {
            using (paper.Row("gi_rt_size_r").Height(UnitValue.Auto).Gap(6).Enter())
            {
                if (sx)
                    NumCell(paper, "gi_rt_size_x", "Right", s_axisXColor, -OffsetMax(p.X, s.X, pv.X), nv =>
                    { SolveHoldMin(p.X, s.X, pv.X, -nv, out float a, out float sz); ApplyAxis(go, rt, 0, a, sz, "Rect Right"); });
                else
                    NumCell(paper, "gi_rt_size_x", "W", s_axisXColor, s.X, nv =>
                        ApplyAxis(go, rt, 0, rt.AnchoredPosition.X, nv, "Rect Width"));

                if (sy)
                    NumCell(paper, "gi_rt_size_y", "Bottom", s_axisYColor, OffsetMin(p.Y, s.Y, pv.Y), nv =>
                    { SolveHoldMax(p.Y, s.Y, pv.Y, nv, out float a, out float sz); ApplyAxis(go, rt, 1, a, sz, "Rect Bottom"); });
                else
                    NumCell(paper, "gi_rt_size_y", "H", s_axisYColor, s.Y, nv =>
                        ApplyAxis(go, rt, 1, rt.AnchoredPosition.Y, nv, "Rect Height"));

                paper.Box("gi_rt_size_pad").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();
            }
        }, labelWidth: EditorTheme.LabelWidth / 2f);
    }

    private static void DrawPivotRow(Paper paper, GameObject go, RectTransform rt)
    {

        EditorGUI.Row(paper, "gi_rt_pivot", "Pivot", () =>
        {
            Origami.Float2Field(paper, "gi_rt_pivot_vf", rt.Pivot, v =>
            {
                Undo.RecordGameObjectChange(go, "Change Pivot", rt.Pivot, v,
                    (g, x) => { RectTransform? r = g.RectTransform; if (r.IsValid()) r.Pivot = x; }, coalesce: true);
                rt.Pivot = v;
            }).Show();
        }, labelWidth: EditorTheme.LabelWidth / 2f);

    }

    // ================================================================
    //  Components
    // ================================================================

    private static void DrawComponents(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var components = go.GetComponents<Component>().ToList();

        for (int i = 0; i < components.Count; i++)
        {
            Component comp = components[i];
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
                        .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                        Origami.Checkbox(paper, $"{compId}_en", comp.Enabled,
                            v => { bool old = comp.Enabled; Guid cId = comp.Identifier; Undo.RegisterAction("Toggle Component", () => { Component? c = Undo.FindComponent(cId); if (c.IsValid()) { c.Enabled = old; c.OnValidate(); } }, () => { Component? c = Undo.FindComponent(cId); if (c.IsValid()) { c.Enabled = v; c.OnValidate(); } }); comp.Enabled = v; comp.OnValidate(); })
                            .NoLabel().Show();
                },
                onDragStart: () => DragDrop.StartDrag(new ComponentDragPayload(go, comp)),
                contextMenu: b => BuildComponentContextMenu(b, go, comp, capturedI));

            if (expanded)
            {
                // Set overridden field names for PropertyGrid highlighting
                if (go.IsPrefabInstance)
                {
                    string pathPrefix = PrefabUtility.GetOverridePath(go, comp, "");

                    var overridden = new HashSet<string>();
                    // Overrides are stored on the instance root with root-relative paths.
                    GameObject? prefabRoot = PrefabUtility.GetPrefabInstanceRoot(go);
                    GameObject overrideHost = prefabRoot.IsValid() ? prefabRoot : go;
                    foreach (PropertyOverride ov in overrideHost.PrefabOverrides)
                    {
                        if (ov.Path.StartsWith(pathPrefix))
                            overridden.Add(ov.Path[pathPrefix.Length..].Split('.')[0]);
                    }
                    PropertyGridUtils.OverriddenFields = overridden.Count > 0 ? overridden : null;
                }

                // Component body use custom editor or default PropertyGrid. A broken custom editor, a
                // throwing property getter, or a bad [Button] must not take down the whole Inspector.
                // Contain it per component (finally keeps OverriddenFields from leaking to the next).
                try
                {
                    CustomEditor? customEditor = EditorRegistries.GetCustomEditor(comp.GetType());
                    if (customEditor != null)
                    {
                        customEditor.OnGUI(paper, compId, comp);
                        // The PropertyGrid draws [Button] methods itself, a custom editor does not.
                        DrawButtonMethods(paper, $"{compId}_btns", comp);
                    }
                    else
                        PropertyGridUtils.Draw(paper, compId, comp);
                }
                catch (Exception ex)
                {
                    Runtime.Debug.LogError($"[Inspector] Drawing {comp.GetType().Name} threw and was skipped: {ex.Message}");
                }
                finally
                {
                    PropertyGridUtils.OverriddenFields = null;
                }
            }

            // Auto-detect overrides by comparing against prefab source (index-based)
            if (go.IsPrefabInstance)
                PrefabUtility.RecordComponentOverrides(go, comp);

            EditorGUI.Divider(paper, $"{compId}_sep", 6f);
        }
    }

    /// <summary>Take a component off its object, undoably, restoring it where it sat.</summary>
    internal static void RemoveComponentWithUndo(Component comp)
    {
        GameObject go = comp.GameObject;
        if (go.IsNotValid()) return;

        EchoObject serialized = Echo.Serializer.Serialize(comp.GetType(), comp);
        Type compType = comp.GetType();
        Guid compId = comp.Identifier;
        int compIndex = comp.GetSiblingIndex() ?? 0;
        Guid goId = go.Identifier;

        Undo.RegisterAction("Remove Component",
            undo: () =>
            {
                GameObject? g = Undo.FindGO(goId);
                if (g.IsNotValid()) return;
                if (Echo.Serializer.Deserialize(serialized, compType) is not Component restored) return;

                restored.Identifier = compId;
                g.AddComponent(restored);
                restored.SetSiblingIndex(compIndex);
            },
            redo: () =>
            {
                GameObject? g = Undo.FindGO(goId);
                if (g.IsNotValid()) return;

                Component? c = g!.GetComponentByIdentifier(compId);
                if (c.IsValid()) g.RemoveComponent(c);
            });

        go.RemoveComponent(comp);
    }

    private static void AddRemoveComponentItem(ContextBuilder builder, GameObject go, Component comp)
    {
        builder.Item(Loc.Get("inspector.remove_component"), () =>
        {
            if (PrefabUtility.NeedsBreaking(comp))
            {
                Component target = comp;
                PrefabUtility.BreakThenRun([go], () => RemoveComponentWithUndo(target));
                return;
            }

            RemoveComponentWithUndo(comp);
        }, icon: EditorIcons.Trash, enabled: comp.CanDestroy());
    }

    private static void BuildComponentContextMenu(ContextBuilder builder, GameObject go, Component comp, int index)
    {
        // On an instance, what this component is supposed to be is whatever the prefab says, so Reset
        // means go back to that. Everywhere else there is nothing to go back to but the values a new
        // one of its type would have.
        bool provided = go.IsPrefabInstance && comp.SourceIdentifier != Guid.Empty;

        builder.Item(Loc.Get(provided ? "inspector.revert_component" : "inspector.reset"), () =>
        {
            if (provided) PrefabUtility.RevertComponentOverrides(go, comp);
            else PrefabUtility.ResetComponentToDefaults(go, comp);
        }, icon: EditorIcons.ArrowsRotate, enabled: !provided || PrefabUtility.HasComponentOverrides(go, comp));

        // Pushing one component back to the prefab, at the scale the user is working at rather than
        // the whole instance at once.
        if (provided && PrefabUtility.IsEditablePrefab(go.PrefabAssetId))
            builder.Item(Loc.Get("inspector.apply_component"), () => PrefabUtility.ApplyComponentOverrides(go, comp),
                icon: EditorIcons.Check, enabled: PrefabUtility.HasComponentOverrides(go, comp));

        builder.Separator();

        // Nothing records the removal of a component the prefab provides, so the next refresh would put
        // it back. Removing one therefore asks first and unlinks the instance.
        AddRemoveComponentItem(builder, go, comp);


        builder.Separator();

        builder.Item(Loc.Get("inspector.copy_component"), () => ComponentClipboard.Copy(comp),
            icon: EditorIcons.Copy);

        // Pasting a whole component is adding one, so it follows the same rule: allowed on a prefab
        // instance, where it becomes part of that instance rather than of the prefab.
        builder.Item(Loc.Get("inspector.paste_component_as_new"), () => ComponentClipboard.PasteAsNew(go),
            icon: EditorIcons.Paste, enabled: ComponentClipboard.CanPasteAsNew());

        // Values-only paste is fine on a prefab instance - RecordComponentOverrides picks the
        // changed fields up as overrides on the next frame.
        builder.Item(Loc.Get("inspector.paste_component_values"), () => ComponentClipboard.PasteValues(comp),
            icon: EditorIcons.ClipboardCheck, enabled: ComponentClipboard.CanPasteValues(comp.GetType()));

        builder.Separator();

        Guid moveCompId = comp.Identifier;
        builder.Item(Loc.Get("inspector.move_up"), () =>
        {
            if (index > 0)
            {
                int oldIdx = index; int newIdx = index - 1;
                Undo.RegisterAction("Move Component Up",
                    () => { Component? c = Undo.FindComponent(moveCompId); if (c.IsValid()) c.SetSiblingIndex(oldIdx); },
                    () => { Component? c = Undo.FindComponent(moveCompId); if (c.IsValid()) c.SetSiblingIndex(newIdx); });
                comp.SetSiblingIndex(newIdx);
            }
        }, icon: EditorIcons.ArrowUp, enabled: index > 0);

        builder.Item(Loc.Get("inspector.move_down"), () =>
        {
            int oldIdx = index; int newIdx = index + 1;
            Undo.RegisterAction("Move Component Down",
                () => { Component? c = Undo.FindComponent(moveCompId); if (c.IsValid()) c.SetSiblingIndex(oldIdx); },
                () => { Component? c = Undo.FindComponent(moveCompId); if (c.IsValid()) c.SetSiblingIndex(newIdx); });
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

    public static void DrawButtonMethods(Paper paper, string id, Component comp)
    {
        MethodInfo[] methods = comp.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        int btnIdx = 0;
        foreach (MethodInfo method in methods)
        {
            ButtonAttribute? btnAttr = method.GetCustomAttribute<ButtonAttribute>();
            if (btnAttr == null) continue;
            if (method.GetParameters().Length > 0) continue; // Only parameterless methods

            string label = btnAttr.Label ?? PropertyGridUtils.NicifyName(method.Name);
            Origami.Button(paper, $"{id}_{btnIdx++}", label, () => { try { method.Invoke(comp, null); } catch (Exception ex) { Runtime.Debug.LogError($"[Inspector] Button method {method.Name} on {comp.GetType().Name} threw: {ex.Message}"); } }).Show();
        }
    }

    // ================================================================
    //  Add Component
    // ================================================================

    private static void DrawAddComponentButton(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        using (paper.Row("gi_add_comp_row").Height(28).PaddingLeft(20).PaddingRight(20).Enter())
        {
            ElementBuilder trigger = paper.Box("gi_add_comp")
                .Height(28).Rounded(EditorTheme.Roundness)
                .BackgroundColor(EditorTheme.Ink100)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .OnClick(go, (g, _) => ToggleAddComponentPopup(g))
                // A GameObject with no components has no component header to right-click, so this is
                // the only place to reach "paste as new" on an empty object.
                .OnRightClick(go, (g, _) => Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y,
                    b => b.Item(Loc.Get("inspector.paste_component_as_new"), () => ComponentClipboard.PasteAsNew(g),
                        icon: EditorIcons.Paste, enabled: ComponentClipboard.CanPasteAsNew())));

            using (trigger.Enter())
            {
                ElementHandle trigHandle = paper.CurrentParent;

                paper.Box("gi_add_comp_lbl")
                    .Width(UnitValue.Stretch()).Height(28).IsNotInteractable()
                    .Text($"{EditorIcons.Plus}  {Loc.Get("inspector.add_component")}", font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);

                if (s_addComponentOpen && s_addComponentTarget == go)
                {
                    if (paper.IsKeyPressed(PaperKey.Escape))
                        CloseAddComponentPopup();
                    else
                    {
                        MenuTreePopup.Backdrop(paper, "gi_acp", CloseAddComponentPopup);
                        RenderAddComponentPopover(paper, trigHandle);
                    }
                }
            }
        }
    }

    // ================================================================
    //  Prefab Header
    // ================================================================

    private static void DrawPrefabHeader(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        GameObject? root = PrefabUtility.GetPrefabInstanceRoot(go);
        bool isRoot = PrefabUtility.IsInstanceRoot(go);
        bool isNested = PrefabUtility.IsNestedPrefabRoot(go);
        bool hasOverrides = PrefabUtility.HasAnyOverrides(go);

        AssetEntry? entry = EditorAssetBackend.Instance?.GetEntry(go.PrefabAssetId);
        bool isMissing = entry == null;
        bool canApply = !isMissing && PrefabUtility.IsEditablePrefab(go.PrefabAssetId);
        string prefabName = isMissing ? Loc.Get("inspector.missing") : System.IO.Path.GetFileNameWithoutExtension(entry!.Path);

        string label = isMissing ? Loc.Get("inspector.missing_prefab")
            : isNested ? Loc.Get("inspector.nested_prefab", new { name = prefabName })
            : Loc.Get("inspector.prefab", new { name = prefabName });

        Color barColor = isMissing ? Color.FromArgb(40, EditorTheme.Red300) : Color.FromArgb(40, EditorTheme.Purple400);
        Color borderColor = isMissing ? EditorTheme.Red300 : EditorTheme.Purple300;
        Color textColor = isMissing ? EditorTheme.Red300 : EditorTheme.Purple400;

        // Entire prefab section in a purple-tinted container
        using (paper.Column("gi_prefab_section")
            .Height(UnitValue.Auto)
            .BackgroundColor(barColor)
            .BorderColor(borderColor).BorderWidth(1)
            .Rounded(Origami.Current.Metrics.ContainerRounding).Margin(0, 4, 0, 4)
            .Padding(4, 4, 4, 4)
            .Enter())
        {
            // Top row: label + buttons
            using (paper.Row("gi_prefab_bar")
                .Height(24).Gap(4)
                .Enter())
            {
                paper.Box("gi_prefab_icon")
                    .Width(UnitValue.Stretch()).Height(24)
                    .Text($"{EditorIcons.Cubes}  {label}", font)
                    .TextColor(textColor)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                if (isMissing)
                {
                    // The asset is gone, so there is nothing to select, revert to or apply against.
                    // Unpacking is the only way out, and it was previously offered nowhere.
                    Origami.Button(paper, "gi_prefab_unpack", Loc.Get("inspector.unpack_prefab"),
                        () => { if (root.IsValid()) PrefabUtility.UnpackPrefabInstance(root); }).Width(80).Show();
                }
                else
                {
                    Origami.Button(paper, "gi_prefab_select", Loc.Get("inspector.select"), () => { Selection.Ping(go.PrefabAssetId); }).Width(55).Show();

                    if (isRoot && hasOverrides)
                    {
                        Origami.Button(paper, "gi_prefab_revert", Loc.Get("inspector.revert"), () => { if (root.IsValid()) PrefabUtility.RevertOverrides(root); }).Width(55).Show();

                        // A generated prefab (a model) is rebuilt from its source file on every
                        // import, so there is nothing to apply to.
                        if (canApply)
                            Origami.Button(paper, "gi_prefab_apply", Loc.Get("inspector.apply"), () => { if (root.IsValid()) PrefabUtility.ApplyOverrides(root); }).Width(50).Show();
                    }

                    // Only where the list can be acted on. A model is rebuilt from its source file on
                    // every import, so nothing in it can be applied and Revert above is the whole of
                    // what an instance of one can do.
                    if (isRoot && canApply && (hasOverrides || PrefabUtility.HasAnyAdditions(go)))
                        DrawOverridesButton(paper, font, go);
                }
            }
        }
    }

    // The override list opens in a popover. Inline it ran to a header and a row per entry with no
    // bound, and setting a flag down a deep instance puts hundreds of them above every component.
    private static bool s_overridesPopupOpen;
    private static GameObject? s_overridesPopupTarget;

    private static void ToggleOverridesPopup(GameObject target)
    {
        bool sameTarget = s_overridesPopupOpen && s_overridesPopupTarget == target;
        s_overridesPopupOpen = !sameTarget;
        s_overridesPopupTarget = sameTarget ? null : target;
    }

    private static void CloseOverridesPopup()
    {
        s_overridesPopupOpen = false;
        s_overridesPopupTarget = null;
    }

    private static void DrawOverridesButton(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        int count = PrefabUtility.CountOverrides(go);

        ElementBuilder trigger = paper.Box("gi_prefab_ov_btn")
            .Width(110).Height(24).Rounded(EditorTheme.Roundness)
            .BackgroundColor(EditorTheme.Ink100)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
            .OnClick(go, (g, _) => ToggleOverridesPopup(g));

        using (trigger.Enter())
        {
            ElementHandle trigHandle = paper.CurrentParent;

            paper.Box("gi_prefab_ov_btn_lbl")
                .Width(UnitValue.Stretch()).Height(24).IsNotInteractable()
                .Text(Loc.Get("inspector.override_count", new { count }), font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

            if (!s_overridesPopupOpen || s_overridesPopupTarget != go) return;

            if (paper.IsKeyPressed(PaperKey.Escape))
            {
                CloseOverridesPopup();
                return;
            }

            MenuTreePopup.Backdrop(paper, "gi_prefab_ov", CloseOverridesPopup);
            RenderOverridesPopover(paper, font, go, trigHandle);
        }
    }

    private static void RenderOverridesPopover(Paper paper, Prowl.Scribe.FontFile font, GameObject go, ElementHandle trigger)
    {
        const float width = 420f, padding = 5f, maxHeight = 420f;
        float triggerHeight = trigger.Data.LayoutRect.Size.Y > 0 ? (float)trigger.Data.LayoutRect.Size.Y : 24f;

        using (paper.Column("gi_prefab_ov_pop")
            .PositionType(PositionType.SelfDirected)
            .Position(0, triggerHeight + 4f)
            .Width(width).Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Popover)
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1)
            .DropShadow(0, 14, 40, -6, EditorTheme.Shadow)
            .Rounded(Origami.Current.Metrics.ContainerRounding)
            .Padding(padding, padding, padding, padding)
            .HookToParent()
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            Origami.ScrollView(paper, "gi_prefab_ov_scroll", width - padding * 2, maxHeight)
                .Padding(0)
                .Body(() => DrawOverridesContent(paper, font, go));
        }
    }

    /// <summary>
    /// What this instance overrides, grouped by the object and component each one is on, with what
    /// the prefab says beside what the instance says.
    /// <para/>
    /// The stored path is three source identifiers, which is the right thing to write down and
    /// unreadable to anyone looking at it, so nothing here shows the path itself.
    /// </summary>
    private static void DrawOverridesContent(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        // Overrides for the whole prefab instance are stored on its root.
        GameObject? prefabRoot = PrefabUtility.GetPrefabInstanceRoot(go);
        go = prefabRoot.IsValid() ? prefabRoot : go;
        bool canApply = PrefabUtility.IsEditablePrefab(go.PrefabAssetId);

        List<PrefabUtility.OverrideDescription> described = PrefabUtility.DescribeOverrides(go);
        List<PrefabUtility.AdditionDescription> additions = PrefabUtility.DescribeAdditions(go);
        if (described.Count == 0 && additions.Count == 0) return;

        using (paper.Column("gi_prefab_ov_list")
            .Height(UnitValue.Auto)
            .Enter())
        {
            int key = 0;
            foreach (IGrouping<string, PrefabUtility.OverrideDescription> group in described.GroupBy(d => d.Group))
            {
                DrawOverrideGroupHeader(paper, font, go, group.Key, group.First(), canApply, key++);

                foreach (PrefabUtility.OverrideDescription? entry in group)
                    DrawOverrideRow(paper, font, go, entry, canApply, key++);
            }

            foreach (PrefabUtility.AdditionDescription addition in additions)
                DrawAdditionRow(paper, font, go, addition, canApply, key++);
        }
    }

    /// <summary>
    /// Something the instance has that the prefab does not. A refresh keeps it and an apply leaves it
    /// behind, both correctly, and neither said it was there.
    /// </summary>
    private static void DrawAdditionRow(Paper paper, Prowl.Scribe.FontFile font, GameObject root,
        PrefabUtility.AdditionDescription addition, bool canApply, int key)
    {
        float fs = EditorTheme.FontSize;

        using (paper.Row($"gi_add_{key}")
            .Height(EditorTheme.RowHeight)
            .BackgroundColor(EditorTheme.Neutral300)
            .Rounded(EditorTheme.Roundness).Margin(0, 0, 0, 1)
            .PaddingLeft(6).Gap(4)
            .Enter())
        {
            paper.Box($"gi_add_name_{key}")
                .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                .Text($"{Loc.Get("inspector.added")}  {addition.Label}", font)
                .TextColor(EditorTheme.Green400)
                .FontSize(fs - 2).Alignment(TextAlignment.MiddleLeft);

            OverrideButton(paper, $"gi_add_remove_{key}", Loc.Get("inspector.remove"), 55,
                root, r => PrefabUtility.RemoveAddition(r, addition));

            if (canApply)
                OverrideButton(paper, $"gi_add_apply_{key}", Loc.Get("inspector.apply"), 45,
                    root, r => PrefabUtility.ApplyAddition(r, addition));
        }
    }

    /// <summary>One heading per object, or per component of one, with the actions for all of it.</summary>
    private static void DrawOverrideGroupHeader(Paper paper, Prowl.Scribe.FontFile font, GameObject root,
        string title, PrefabUtility.OverrideDescription first, bool canApply, int key)
    {
        using (paper.Row($"gi_ovg_{key}").Height(EditorTheme.RowHeight).Gap(4).Enter())
        {
            paper.Box($"gi_ovg_name_{key}")
                .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                .Text(title, EditorTheme.FontSemiBold ?? font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft);

            // Component level actions belong to the component, so a group standing for one offers
            // them here rather than making the user revert its members one at a time.
            if (string.IsNullOrEmpty(first.ComponentName)) return;

            Component? component = ComponentFor(root, first);
            if (component.IsNotValid()) return;

            OverrideButton(paper, $"gi_ovg_revert_{key}", Loc.Get("inspector.revert"), 50,
                root, _ => PrefabUtility.RevertComponentOverrides(component!.GameObject, component!));

            if (canApply)
                OverrideButton(paper, $"gi_ovg_apply_{key}", Loc.Get("inspector.apply"), 45,
                    root, _ => PrefabUtility.ApplyComponentOverrides(component!.GameObject, component!));
        }
    }

    private static void DrawOverrideRow(Paper paper, Prowl.Scribe.FontFile font, GameObject root,
        PrefabUtility.OverrideDescription entry, bool canApply, int key)
    {
        float fs = EditorTheme.FontSize;
        string path = entry.Path;

        using (paper.Row($"gi_ov_{key}")
            .Height(EditorTheme.RowHeight)
            .BackgroundColor(EditorTheme.Neutral300)
            .Rounded(EditorTheme.Roundness).Margin(12, 0, 0, 1)
            .PaddingLeft(6).Gap(4)
            .Enter())
        {
            paper.Box($"gi_ov_member_{key}")
                .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                .Text(entry.Resolvable ? entry.MemberName : $"{entry.MemberName}  ({Loc.Get("inspector.override_missing")})", font)
                .TextColor(entry.Resolvable ? EditorTheme.Purple400 : EditorTheme.Red400)
                .FontSize(fs - 2).Alignment(TextAlignment.MiddleLeft);

            if (entry.Resolvable)
            {
                // What it was against what it is, which is the question the list is read to answer.
                paper.Box($"gi_ov_value_{key}")
                    .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                    .Text($"{entry.SourceValue}  {EditorIcons.ArrowRight}  {entry.InstanceValue}", font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(fs - 2).Alignment(TextAlignment.MiddleLeft);

                OverrideButton(paper, $"gi_ov_revert_{key}", Loc.Get("inspector.revert"), 50,
                    root, r => PrefabUtility.RevertSingleOverride(r, path));

                if (canApply)
                    OverrideButton(paper, $"gi_ov_apply_{key}", Loc.Get("inspector.apply"), 45,
                        root, r => PrefabUtility.ApplySingleOverride(r, path));
            }
            else
            {
                OverrideButton(paper, $"gi_ov_remove_{key}", Loc.Get("inspector.remove_override"), 95,
                    root, r => PrefabUtility.RemoveOverride(r, path));
            }
        }
    }

    /// <summary>
    /// The component an entry is on, by identity and inside this instance. Two instances of one prefab
    /// have identically named objects, so a search by name lands on whichever comes first.
    /// </summary>
    private static Component? ComponentFor(GameObject root, PrefabUtility.OverrideDescription entry)
    {
        if (entry.ComponentIdentifier == Guid.Empty) return null;
        return root.GetComponentInChildrenByIdentifier(entry.ComponentIdentifier);
    }

    /// <summary>
    /// A small text button that acts on the instance root, looked up when it is clicked rather than
    /// captured: these operations replace what they touch, so a handler holding an object would be
    /// holding a dead one by the time it ran.
    /// </summary>
    private static void OverrideButton(Paper paper, string id, string label,
        float width, GameObject root, Action<GameObject> act)
    {
        Guid rootId = root.Identifier;

        Origami.Button(paper, id, label, () =>
            {
                GameObject? live = Undo.FindGO(rootId);
                if (live.IsValid()) act(live!);
            })
            .Small().Width(width).Height(EditorTheme.RowHeight).Ghost().Show();
    }


    // ================================================================
    //  Helpers
    // ================================================================

    private static string GetComponentIcon(Component comp) => EditorRegistries.GetComponentIcon(comp);

    // ================================================================
    //  Anchor editing that preserves screen position
    // ================================================================

    /// <summary>
    /// Change a RectTransform's anchors while keeping its on-screen rect fixed: the anchor reference
    /// shifts and <see cref="RectTransform.AnchoredPosition"/> / <see cref="RectTransform.SizeDelta"/>
    /// are back-solved against the parent rect so the element does not move.
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

        SolveAxis(parent.Min.X + newMin.X * parent.Size.X, parent.Min.X + newMax.X * parent.Size.X,
            world.Min.X, world.Max.X, rt.Pivot.X, out float sizeX, out float posX);
        SolveAxis(parent.Min.Y + newMin.Y * parent.Size.Y, parent.Min.Y + newMax.Y * parent.Size.Y,
            world.Min.Y, world.Max.Y, rt.Pivot.Y, out float sizeY, out float posY);

        rt.SizeDelta = new Float2(sizeX, sizeY);
        rt.AnchoredPosition = new Float2(posX, posY);
    }

    // Inverse of RectTransform.ComputeRect for one axis (single formula covering both fixed and
    // stretch anchors, matching ComputeRect: size = anchorSpan + SizeDelta, min = anchorMin +
    // AnchoredPosition - Pivot*SizeDelta). Given the target span [rMin,rMax] and the anchor pixel
    // positions, solve the SizeDelta/AnchoredPosition that reproduce it.
    private static void SolveAxis(float aMinPx, float aMaxPx,
        float rMin, float rMax, float pivot, out float size, out float pos)
    {
        size = (rMax - rMin) - (aMaxPx - aMinPx);
        pos = rMin - aMinPx + pivot * size;
    }

    // The rect this element is laid out inside: the parent RectTransform's computed rect, or the
    // canvas root rect when the element is a direct child of the canvas.
    private static Rect GetParentRect(RectTransform rt)
    {
        GameCanvas? canvas = rt.GameObject.GetComponentInParent<GameCanvas>(includeSelf: true);
        GameObject? parentGo = rt.GameObject.Parent;
        RectTransform? parentRt = parentGo.IsValid() ? parentGo.RectTransform : null;
        if (parentRt.IsValid() && parentGo != (canvas.IsValid() ? canvas.GameObject : null) &&
            parentRt.ComputedRect.Size.X > 0 && parentRt.ComputedRect.Size.Y > 0)
            return parentRt.ComputedRect;
        return canvas.IsValid() ? canvas.RootRect : default;
    }

    // ================================================================
    //  Add Component Popup
    // ================================================================

    private static bool s_addComponentOpen;
    private static GameObject? s_addComponentTarget;
    private static readonly MenuTreeState s_addComponentMenu = new();
    private static List<MenuTreeEntry>? s_cachedComponents;

    /// <summary>
    /// Drop the cached component list (which holds every Component <see cref="Type"/>,
    /// including user ones) so the script AssemblyLoadContext can be collected.
    /// </summary>
    public static void ClearAddComponentCache() => s_cachedComponents = null;

    private static void ToggleAddComponentPopup(GameObject target)
    {
        if (s_addComponentOpen && s_addComponentTarget == target)
        {
            CloseAddComponentPopup();
            return;
        }

        s_addComponentTarget = target;
        s_addComponentMenu.Reset();
        s_cachedComponents ??= GatherComponents();
        s_addComponentOpen = true;
    }

    private static void CloseAddComponentPopup()
    {
        s_addComponentOpen = false;
        s_addComponentTarget = null;
    }

    private static void RenderAddComponentPopover(Paper paper, ElementHandle trigHandle)
    {
        MenuTreePopup.Popover(paper, "gi_acp", trigHandle, s_cachedComponents ?? [], s_addComponentMenu,
            entry =>
            {
                if (s_addComponentTarget.IsValid() && entry.Tag is Type type)
                {
                    AddComponentWithUndo(s_addComponentTarget, type);
                    CloseAddComponentPopup();
                }
            },
            Loc.Get("popup.search_components"),
            Loc.Get("popup.no_components"));
    }

    /// <summary>
    /// Adds a component of <paramref name="type"/> to <paramref name="go"/> and registers a matching
    /// undo/redo step. Shared by the popup and the drag-a-script-onto-the-inspector path.
    /// </summary>
    public static Component? AddComponentWithUndo(GameObject go, Type type)
    {
        Component? addedComp = go.AddComponent(type);
        if (addedComp.IsValid())
        {
            Guid compId = addedComp.Identifier;
            Guid goId = go.Identifier;
            EchoObject serialized = Echo.Serializer.Serialize(addedComp.GetType(), addedComp);
            Type compType = addedComp.GetType();
            Undo.RegisterAction("Add Component",
                undo: () => { GameObject? g = Undo.FindGO(goId); if (g.IsNotValid()) return; Component? c = g.GetComponentByIdentifier(compId); if (c.IsValid()) g.RemoveComponent(c); },
                redo: () => { GameObject? g = Undo.FindGO(goId); if (g.IsNotValid()) return; var c = Echo.Serializer.Deserialize(serialized, compType) as Component; if (c.IsValid()) { c.Identifier = compId; g.AddComponent(c); } });
        }
        return addedComp;
    }

    private static List<MenuTreeEntry> GatherComponents()
    {
        var result = new List<MenuTreeEntry>();

        foreach (Type type in EditorUtils.GetAllTypes())
        {
            if (!typeof(Component).IsAssignableFrom(type) || type.IsAbstract) continue;
            if (type == typeof(Component)) continue;
            if (type.Name == "MissingComponent") continue;

            AddComponentMenuAttribute? menuAttr = type.GetCustomAttribute<AddComponentMenuAttribute>();
            string path = menuAttr?.Path ?? type.Name;
            string icon = menuAttr?.Icon ?? "";

            int lastSlash = path.LastIndexOf('/');
            string category = lastSlash >= 0 ? path[..lastSlash] : "";

            if (string.IsNullOrEmpty(icon))
            {
                icon = category switch
                {
                    var c when c.StartsWith("Rendering") => EditorIcons.Cube,
                    var c when c.StartsWith("Audio") => EditorIcons.VolumeHigh,
                    var c when c.StartsWith("Light") => EditorIcons.Sun,
                    var c when c.Contains("Collider") => EditorIcons.VectorSquare,
                    var c when c.Contains("Constraint") || c.Contains("Joint") => EditorIcons.Link,
                    var c when c.StartsWith("Physics") => EditorIcons.Atom,
                    var c when c.StartsWith("UI") => EditorIcons.Desktop,
                    var c when c.StartsWith("Effects") => EditorIcons.Burst,
                    var c when c.StartsWith("Terrain") => EditorIcons.Mountain,
                    _ => EditorIcons.PuzzlePiece
                };
            }

            result.Add(MenuTreeEntry.FromPath(path, icon, type));
        }

        return result;
    }
}
