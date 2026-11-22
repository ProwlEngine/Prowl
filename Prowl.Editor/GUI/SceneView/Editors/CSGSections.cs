// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Operation = Prowl.Vector.Geometry.CSGOperation;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>The CSG editor's controls as sections, shared by the scene view overlay and the docked panel.</summary>
internal static class CSGSections
{
    internal static void EmptyState(Paper paper, Scribe.FontFile font, float height)
    {
        using (paper.Column("csg_empty").Height(height).Padding(16, 16, 16, 16).Enter())
        {
            paper.Box("csg_empty_t").Height(24)
                .Text(Loc.Get("csg.no_model"), font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("csg_empty_d").Height(40)
                .Text(Loc.Get("csg.no_model_hint"), font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.Left);

            LabelledButton(paper, font, "csg_empty_new", Loc.Get("csg.create_model"),
                Loc.Get("csg.create_model_tip"), () => CSGSelection.CreateModel());
        }
    }

    internal static void DrawModelSection(Paper paper, Scribe.FontFile font, CSGModel model)
    {
        if (!Fold(paper, font, "csg_model_h", Loc.Get("csg.model"), ref _modelOpen)) return;

        EditorGUI.SettingsRow(paper, "csg_model_mat", Loc.Get("csg.default_material"), () =>
            PropertyGridUtils.DrawField(paper, "csg_model_mat_v", "", typeof(Material),
                model.DefaultMaterial, v => Change(model, () => model.DefaultMaterial = v as Material), 0));

        EditorGUI.SettingsRow(paper, "csg_model_texel", Loc.Get("csg.texel_scale"), () =>
            Origami.NumericField(paper, "csg_model_texel_v", model.DefaultTexelScale,
                v => Change(model, () => model.DefaultTexelScale = v)).Show());

        EditorGUI.SettingsRow(paper, "csg_model_render", Loc.Get("csg.render"), () =>
            Origami.Toggle(paper, "csg_model_render_v", !model.Flags.HasFlag(CSGModelFlags.DoNotRender),
                v => Change(model, () => SetFlag(model, CSGModelFlags.DoNotRender, !v))).Show());

        EditorGUI.SettingsRow(paper, "csg_model_collide", Loc.Get("csg.collide"), () =>
            Origami.Toggle(paper, "csg_model_collide_v", !model.Flags.HasFlag(CSGModelFlags.NoCollider),
                v => Change(model, () => SetFlag(model, CSGModelFlags.NoCollider, !v))).Show());

        EditorGUI.SettingsRow(paper, "csg_model_convex", Loc.Get("csg.convex_collider"), () =>
            Origami.Toggle(paper, "csg_model_convex_v", model.Flags.HasFlag(CSGModelFlags.ConvexCollider),
                v => Change(model, () => SetFlag(model, CSGModelFlags.ConvexCollider, v))).Show());

        string stats = Loc.Get("csg.stats", new
        {
            brushes = model.BrushCount,
            vertices = model.VertexCount,
            triangles = model.TriangleCount,
        });

        paper.Box("csg_model_stats").Height(20).Margin(Origami.Current.Metrics.PaddingLarge, 0, 4, 0)
            .Text(stats, font).TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

        LabelledButton(paper, font, "csg_model_rebuild", Loc.Get("csg.rebuild"), Loc.Get("csg.rebuild_tip"), model.Rebuild);

        DrawHelperSection(paper, font);
        DrawLightmapSection(paper, font, model);
    }

    // The model's own settings are rarely touched while building, so they start folded away
    private static bool _modelOpen, _helpersOpen, _lightmapOpen;

    /// <summary>A section header that folds its section away when clicked. True while open.</summary>
    private static bool Fold(Paper paper, Scribe.FontFile font, string id, string title, ref bool open)
    {
        bool isOpen = open;
        paper.Box(id).Height(24).Margin(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 6, 2)
            .Rounded(4).Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text($"{(isOpen ? EditorIcons.ChevronDown : EditorIcons.ChevronRight)}  {title}", font)
            .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
            .OnClick(id, (key, _) => Toggle(key));
        return open;
    }

    private static void Toggle(string id)
    {
        switch (id)
        {
            case "csg_model_h": _modelOpen = !_modelOpen; break;
            case "csg_help_h": _helpersOpen = !_helpersOpen; break;
            case "csg_lm_h": _lightmapOpen = !_lightmapOpen; break;
        }
    }

    private static void DrawHelperSection(Paper paper, Scribe.FontFile font)
    {
        if (!Fold(paper, font, "csg_help_h", Loc.Get("csg.helpers"), ref _helpersOpen)) return;

        ToggleRow(paper, "csg_help_col", Loc.Get("csg.helper_collision"), CSGHelperSurfaces.Flags.HasFlag(CSGHelperFlags.CollisionOnly),
            v => CSGHelperSurfaces.Set(CSGHelperFlags.CollisionOnly, v));
        ToggleRow(paper, "csg_help_inert", Loc.Get("csg.helper_inert"), CSGHelperSurfaces.Flags.HasFlag(CSGHelperFlags.Inert),
            v => CSGHelperSurfaces.Set(CSGHelperFlags.Inert, v));
        Hint(paper, font, "csg_help_hint", Loc.Get("csg.helpers_hint"));
    }

    /// <summary>Lightmap UVs are opt in and deferred. Stale UVs show a rebuild button rather than unwrapping mid edit.</summary>
    private static void DrawLightmapSection(Paper paper, Scribe.FontFile font, CSGModel model)
    {
        if (!Fold(paper, font, "csg_lm_h", Loc.Get("csg.lightmapping"), ref _lightmapOpen)) return;

        EditorGUI.SettingsRow(paper, "csg_lm_on", Loc.Get("csg.lightmap_uvs"), () =>
            Origami.Toggle(paper, "csg_lm_on_v", model.GenerateLightmapUVs,
                v => Change(model, () => model.GenerateLightmapUVs = v)).Show());

        if (!model.GenerateLightmapUVs)
        {
            Hint(paper, font, "csg_lm_hint", Loc.Get("csg.lightmap_off_hint"));
            return;
        }

        EditorGUI.SettingsRow(paper, "csg_lm_auto", Loc.Get("csg.lightmap_auto"), () =>
            Origami.Toggle(paper, "csg_lm_auto_v", model.AutoRebuildLightmapUVs,
                v => Change(model, () => model.AutoRebuildLightmapUVs = v)).Show());

        if (model.LightmapUVsStale)
        {
            LabelledButton(paper, font, "csg_lm_build", Loc.Get("csg.lightmap_rebuild"),
                Loc.Get("csg.lightmap_rebuild_tip"), () => Change(model, model.RebuildLightmapUVs));
        }
        else
        {
            Hint(paper, font, "csg_lm_ok", Loc.Get("csg.lightmap_ready"));
        }
    }

    internal static void DrawGenerateSection(Paper paper, Scribe.FontFile font, CSGGenerateTool? tool)
    {
        if (tool == null) return;

        EditorGUI.SectionHeader(paper, "csg_gen_h", Loc.Get("csg.generate"));

        Hint(paper, font, "csg_gen_hint", Loc.Get("csg.draw_active"));

        EditorGUI.SettingsRow(paper, "csg_gen_op", Loc.Get("csg.new_operation"), () =>
            Origami.ButtonGroup(paper, "csg_gen_op_v", (int)tool.Operation, idx => tool.Operation = (Operation)idx)
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(Loc.Get("csg.additive"), tooltip: Loc.Get("csg.additive_tip"))
                .Item(Loc.Get("csg.subtractive"), tooltip: Loc.Get("csg.subtractive_tip"))
                .Item(Loc.Get("csg.intersect"), tooltip: Loc.Get("csg.intersect_tip"))
                .Show());

        EditorGUI.SettingsRow(paper, "csg_gen_shape", Loc.Get("csg.shape"), () =>
            Origami.EnumDropdown(paper, "csg_gen_shape_v", tool.Generator, v => tool.Generator = v).Show());

        DrawGeneratorSettings(paper, font, "csg_gen", tool.Generator, tool.Settings, v => tool.Settings = v);
    }

    /// <summary>Only the parameters the chosen shape actually uses.</summary>
    internal static void DrawGeneratorSettings(Paper paper, Scribe.FontFile font, string prefix, CSGGeneratorKind kind, CSGGeneratorSettings settings, Action<CSGGeneratorSettings> apply)
    {
        switch (kind)
        {
            case CSGGeneratorKind.Cylinder:
                IntRow(paper, $"{prefix}_sides", Loc.Get("csg.sides"), settings.Sides,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Sides = v));
                FloatRow(paper, $"{prefix}_angle", Loc.Get("csg.offset_angle"), settings.OffsetAngle,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.OffsetAngle = v));
                ToggleRow(paper, $"{prefix}_corner", Loc.Get("csg.radius_to_corner"), settings.RadiusToCorner,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.RadiusToCorner = v));
                ToggleRow(paper, $"{prefix}_fit", Loc.Get("csg.recentre"), settings.Recentre,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Recentre = v));
                ToggleRow(paper, $"{prefix}_smooth", Loc.Get("csg.smooth_shading"), settings.SmoothShading,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.SmoothShading = v));
                break;

            case CSGGeneratorKind.Sphere:
                IntRow(paper, $"{prefix}_seg", Loc.Get("csg.segments"), settings.Sides,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Sides = v));
                IntRow(paper, $"{prefix}_rings", Loc.Get("csg.rings"), settings.Rings,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Rings = v));
                ToggleRow(paper, $"{prefix}_hemi", Loc.Get("csg.hemisphere"), settings.Hemisphere,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Hemisphere = v));
                ToggleRow(paper, $"{prefix}_ssmooth", Loc.Get("csg.smooth_shading"), settings.SmoothShading,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.SmoothShading = v));
                break;

            case CSGGeneratorKind.LinearStairs:
                IntRow(paper, $"{prefix}_total", Loc.Get("csg.total_steps"), settings.TotalSteps,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.TotalSteps = v));
                FloatRow(paper, $"{prefix}_sh", Loc.Get("csg.step_height"), settings.StepHeight,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.StepHeight = v));
                FloatRow(paper, $"{prefix}_sd", Loc.Get("csg.step_depth"), settings.StepDepth,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.StepDepth = v));
                FloatRow(paper, $"{prefix}_ed", Loc.Get("csg.extra_depth"), settings.ExtraDepth,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.ExtraDepth = v));
                FloatRow(paper, $"{prefix}_eh", Loc.Get("csg.extra_height"), settings.ExtraHeight,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.ExtraHeight = v));
                ToggleRow(paper, $"{prefix}_fill", Loc.Get("csg.filled_stairs"), settings.FilledStairs,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.FilledStairs = v));
                break;

            case CSGGeneratorKind.Polygon:
                Hint(paper, font, $"{prefix}_poly", Loc.Get("csg.polygon_hint"));
                break;

            case CSGGeneratorKind.Room:
                FloatRow(paper, $"{prefix}_wall", Loc.Get("csg.wall_thickness"), settings.WallThickness,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.WallThickness = MathF.Max(0.01f, v)));
                break;

            case CSGGeneratorKind.SpiralStairs:
                FloatRow(paper, $"{prefix}_ssh", Loc.Get("csg.step_height"), settings.StepHeight,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.StepHeight = v));
                IntRow(paper, $"{prefix}_spr", Loc.Get("csg.steps_per_turn"), settings.StepsPerRevolution,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.StepsPerRevolution = v));
                FloatRow(paper, $"{prefix}_inner", Loc.Get("csg.inner_radius"), settings.InnerRadius,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.InnerRadius = v));
                break;
        }
    }

    private delegate void SettingsEdit(ref CSGGeneratorSettings settings);

    private static void Update(CSGGeneratorSettings settings, Action<CSGGeneratorSettings> apply, SettingsEdit edit)
    {
        edit(ref settings);
        apply(settings);
    }

    private static void IntRow(Paper paper, string id, string label, int value, Action<int> set)
        => EditorGUI.SettingsRow(paper, id, label, () =>
            Origami.NumericField(paper, $"{id}_v", value, set).Show());

    private static void FloatRow(Paper paper, string id, string label, float value, Action<float> set)
        => EditorGUI.SettingsRow(paper, id, label, () =>
            Origami.NumericField(paper, $"{id}_v", value, set).Show());

    private static void ToggleRow(Paper paper, string id, string label, bool value, Action<bool> set)
        => EditorGUI.SettingsRow(paper, id, label, () =>
            Origami.Toggle(paper, $"{id}_v", value, set).Show());

    internal static void DrawMeshSection(Paper paper, Scribe.FontFile font, CSGMeshTool? tool)
    {
        if (tool == null) return;

        EditorGUI.SectionHeader(paper, "csg_mesh_h", Loc.Get("csg.mesh"));

        if (tool.Target.IsNotValid())
        {
            Hint(paper, font, "csg_mesh_none", Loc.Get("csg.mesh_one_brush"));
            return;
        }

        EditorGUI.SettingsRow(paper, "csg_mesh_filter", Loc.Get("csg.mesh_select"), () =>
            Origami.ButtonGroup(paper, "csg_mesh_filter_v", -1, idx => ToggleFilter(tool, idx))
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(FilterLabel(Loc.Get("csg.vertex"), tool.Filter.HasFlag(CSGElementFilter.Vertex)),
                    tooltip: Loc.Get("csg.mesh_select_tip"))
                .Item(FilterLabel(Loc.Get("csg.edge"), tool.Filter.HasFlag(CSGElementFilter.Edge)),
                    tooltip: Loc.Get("csg.mesh_select_tip"))
                .Item(FilterLabel(Loc.Get("csg.face"), tool.Filter.HasFlag(CSGElementFilter.Face)),
                    tooltip: Loc.Get("csg.mesh_select_tip"))
                .Show());

        if (tool.Filter == CSGElementFilter.None)
            Hint(paper, font, "csg_mesh_warn", Loc.Get("csg.mesh_nothing_selectable"));

        EditorGUI.SettingsRow(paper, "csg_mesh_chamfer", Loc.Get("csg.chamfer"), () =>
            Origami.NumericField(paper, "csg_mesh_chamfer_v", tool.ChamferDistance, v => tool.ChamferDistance = MathF.Max(0f, v)).Show());
        EditorGUI.SettingsRow(paper, "csg_mesh_extrude", Loc.Get("csg.extrude"), () =>
            Origami.NumericField(paper, "csg_mesh_extrude_v", tool.ExtrudeDistance, v => tool.ExtrudeDistance = v).Show());

        EditorGUI.SettingsRow(paper, "csg_mesh_actions", "", () =>
            Origami.ButtonGroup(paper, "csg_mesh_actions_v", -1, idx =>
                {
                    switch (idx)
                    {
                        case 0: tool.ChamferSelected(); break;
                        case 1: tool.ExtrudeSelected(); break;
                        case 2: tool.SnapSelectedToGrid(); break;
                        default: tool.DeleteSelectedFaces(); break;
                    }
                })
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(Loc.Get("csg.chamfer"), tooltip: Loc.Get("csg.chamfer_tip"))
                .Item(Loc.Get("csg.extrude"), tooltip: Loc.Get("csg.extrude_tip"))
                .Item(Loc.Get("csg.snap"), tooltip: ShortcutManager.GetTooltip(Loc.Get("csg.snap_tip"), "CSG/SnapToGrid"))
                .Item(Loc.Get("csg.delete_face"), tooltip: Loc.Get("csg.delete_face_tip"))
                .Show());

        paper.Box("csg_mesh_count").Height(18).Margin(Origami.Current.Metrics.PaddingLarge, 0, 2, 4)
            .Text(Loc.Get("csg.mesh_selected", new { count = tool.SelectedCount, faces = tool.SelectedFaceCount }), font)
            .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleLeft);

        if (tool.NonPlanar)
            Hint(paper, font, "csg_mesh_planar", Loc.Get("csg.mesh_non_planar"));

        Hint(paper, font, "csg_mesh_help", Loc.Get("csg.mesh_help"));
    }

    private static string FilterLabel(string text, bool on) => on ? text : $"({text})";

    private static void ToggleFilter(CSGMeshTool tool, int index)
    {
        CSGElementFilter flag = index switch
        {
            0 => CSGElementFilter.Vertex,
            1 => CSGElementFilter.Edge,
            _ => CSGElementFilter.Face,
        };

        tool.Filter = tool.Filter.HasFlag(flag) ? tool.Filter & ~flag : tool.Filter | flag;
    }

    internal static void DrawClipSection(Paper paper, Scribe.FontFile font, CSGClipTool? tool)
    {
        if (tool == null) return;

        EditorGUI.SectionHeader(paper, "csg_clip_h", Loc.Get("csg.clip"));

        int count = CSGSelection.Brushes().Count;
        Hint(paper, font, "csg_clip_hint", count > 0
            ? Loc.Get("csg.clip_selected", new { count })
            : Loc.Get("csg.clip_all"));

        EditorGUI.SettingsRow(paper, "csg_clip_mode", Loc.Get("csg.clip_mode"), () =>
            Origami.ButtonGroup(paper, "csg_clip_mode_v", (int)tool.Mode, idx => tool.Mode = (CSGClipMode)idx)
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(Loc.Get("csg.clip_front"), tooltip: Loc.Get("csg.clip_front_tip"))
                .Item(Loc.Get("csg.clip_back"), tooltip: Loc.Get("csg.clip_back_tip"))
                .Item(Loc.Get("csg.clip_split"), tooltip: Loc.Get("csg.clip_split_tip"))
                .Show());

        if (tool.HasPlane)
        {
            EditorGUI.SettingsRow(paper, "csg_clip_go", "", () =>
                Origami.ButtonGroup(paper, "csg_clip_go_v", -1, idx => { if (idx == 0) tool.Commit(); else tool.Cancel(); })
                    .Height(EditorTheme.RowHeight).FullWidth()
                    .Item(Loc.Get("csg.clip_commit"), tooltip: ShortcutManager.GetTooltip(Loc.Get("csg.clip_commit_tip"), "CSG/Confirm"))
                    .Item(Loc.Get("csg.clip_cancel"), tooltip: Loc.Get("csg.clip_cancel_tip"))
                    .Show());
        }

        Hint(paper, font, "csg_clip_help", Loc.Get("csg.clip_help"));
    }

    internal static void DrawSurfaceSection(Paper paper, Scribe.FontFile font, CSGSurfaceTool? tool)
    {
        if (tool == null) return;

        EditorGUI.SectionHeader(paper, "csg_surf_h", Loc.Get("csg.surfaces"));

        EditorGUI.SettingsRow(paper, "csg_surf_pick", Loc.Get("csg.select"), () =>
            Origami.ButtonGroup(paper, "csg_surf_pick_v", -1, idx =>
                {
                    switch (idx)
                    {
                        case 0: CSGSurfaceSelection.SelectFloors(); break;
                        case 1: CSGSurfaceSelection.SelectWalls(); break;
                        case 2: CSGSurfaceSelection.SelectCeilings(); break;
                        case 3: CSGSurfaceSelection.SelectCoplanar(); break;
                        default: CSGSurfaceSelection.SelectConnected(); break;
                    }
                })
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(Loc.Get("csg.floors"), tooltip: Loc.Get("csg.floors_tip"))
                .Item(Loc.Get("csg.walls"), tooltip: Loc.Get("csg.walls_tip"))
                .Item(Loc.Get("csg.ceilings"), tooltip: Loc.Get("csg.ceilings_tip"))
                .Item(Loc.Get("csg.coplanar"), tooltip: Loc.Get("csg.coplanar_tip"))
                .Item(Loc.Get("csg.connected"), tooltip: Loc.Get("csg.connected_tip"))
                .Show());

        if (CSGSurfaceSelection.Active is not CSGSurfaceRef active || !active.IsValid)
        {
            Hint(paper, font, "csg_surf_hint", Loc.Get("csg.surface_pick_hint"));
            return;
        }

        SurfaceData data = active.Data;

        paper.Box("csg_surf_count").Height(18).Margin(Origami.Current.Metrics.PaddingLarge, 0, 2, 4)
            .Text(Loc.Get("csg.surfaces_selected", new { count = CSGSurfaceSelection.Count }), font)
            .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleLeft);

        LabelledButton(paper, font, "csg_surf_owner", Loc.Get("csg.surface_owner", new { name = active.Brush.GameObject.Name }),
            Loc.Get("csg.surface_owner_tip"), () => Selection.Select(active.Brush.GameObject));

        EditorGUI.SectionHeader(paper, "csg_surf_mat_h", Loc.Get("csg.material"));
        Hint(paper, font, "csg_surf_paint", Loc.Get("csg.surface_paint_hint"));

        EditorGUI.SettingsRow(paper, "csg_surf_mat", Loc.Get("csg.material"), () =>
            PropertyGridUtils.DrawField(paper, "csg_surf_mat_v", "", typeof(Material),
                data.Material, v => CSGSurfaceSelection.SetMaterial(v as Material), 0));

        DrawPalette(paper, font);

        EditorGUI.SectionHeader(paper, "csg_surf_map_h", Loc.Get("csg.mapping"));

        EditorGUI.SettingsRow(paper, "csg_surf_offset", Loc.Get("csg.uv_offset"), () =>
            Origami.Float2Field(paper, "csg_surf_offset_v", data.Offset,
                v => CSGSurfaceSelection.SetOffset(v)).Show());

        EditorGUI.SettingsRow(paper, "csg_surf_scale", Loc.Get("csg.uv_scale"), () =>
            Origami.Float2Field(paper, "csg_surf_scale_v", data.Scale,
                v => CSGSurfaceSelection.SetScale(v)).Show());

        EditorGUI.SettingsRow(paper, "csg_surf_rot", Loc.Get("csg.uv_rotation"), () =>
            Origami.NumericField(paper, "csg_surf_rot_v", data.Rotation,
                v => CSGSurfaceSelection.SetRotation(v)).Show());

        EditorGUI.SettingsRow(paper, "csg_surf_rot90", Loc.Get("csg.uv_rotate_step"), () =>
            Origami.ButtonGroup(paper, "csg_surf_rot90_v", -1,
                    idx => CSGSurfaceSelection.RotateBy(idx == 0 ? -90f : 90f))
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item("-90", tooltip: Loc.Get("csg.uv_rotate_step_tip"))
                .Item("+90", tooltip: Loc.Get("csg.uv_rotate_step_tip"))
                .Show());

        AxisRow(paper, "csg_surf_fit", Loc.Get("csg.fit"), Loc.Get("csg.fit_tip"), CSGSurfaceSelection.Fit);
        AxisRow(paper, "csg_surf_flip", Loc.Get("csg.reverse"), Loc.Get("csg.reverse_tip"), CSGSurfaceSelection.Flip);
        AxisRow(paper, "csg_surf_reset", Loc.Get("csg.reset"), Loc.Get("csg.reset_tip"), CSGSurfaceSelection.Reset);

        EditorGUI.SettingsRow(paper, "csg_surf_mul", Loc.Get("csg.uv_scale_step"), () =>
            Origami.ButtonGroup(paper, "csg_surf_mul_v", -1,
                    idx => CSGSurfaceSelection.ScaleBy(idx == 0 ? 0.5f : 2f))
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item("/2", tooltip: Loc.Get("csg.uv_scale_step_tip"))
                .Item("x2", tooltip: Loc.Get("csg.uv_scale_step_tip"))
                .Show());

        DrawJustifyGrid(paper, font);

        LabelledButton(paper, font, "csg_surf_align", Loc.Get("csg.align"), Loc.Get("csg.align_tip"), CSGSurfaceSelection.AlignToActive);

        EditorGUI.SectionHeader(paper, "csg_surf_flags_h", Loc.Get("csg.shading_flags"));

        EditorGUI.SettingsRow(paper, "csg_surf_smooth", Loc.Get("csg.smoothing"), () =>
            Origami.ButtonGroup(paper, "csg_surf_smooth_v", -1,
                    idx => { if (idx == 0) CSGSurfaceSelection.Smooth(); else CSGSurfaceSelection.UnSmooth(); })
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(Loc.Get("csg.smooth"), tooltip: Loc.Get("csg.smooth_tip"))
                .Item(Loc.Get("csg.unsmooth"), tooltip: Loc.Get("csg.unsmooth_tip"))
                .Show());

        EditorGUI.SettingsRow(paper, "csg_surf_group", Loc.Get("csg.smoothing_group"), () =>
            Origami.NumericField(paper, "csg_surf_group_v", data.SmoothingGroup,
                v => CSGSurfaceSelection.SetSmoothingGroup(v)).Show());

        EditorGUI.SettingsRow(paper, "csg_surf_lock", Loc.Get("csg.texture_lock"), () =>
            Origami.Toggle(paper, "csg_surf_lock_v", !data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture),
                v => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.WorldSpaceTexture, !v)).Show());
        Hint(paper, font, "csg_surf_lock_hint", Loc.Get("csg.texture_lock_tip"));

        EditorGUI.SettingsRow(paper, "csg_surf_render", Loc.Get("csg.render"), () =>
            Origami.Toggle(paper, "csg_surf_render_v", !data.Flags.HasFlag(CSGSurfaceFlags.NoRender),
                v => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.NoRender, !v)).Show());

        EditorGUI.SettingsRow(paper, "csg_surf_collide", Loc.Get("csg.collide"), () =>
            Origami.Toggle(paper, "csg_surf_collide_v", !data.Flags.HasFlag(CSGSurfaceFlags.NoCollision),
                v => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.NoCollision, !v)).Show());

        LabelledButton(paper, font, "csg_surf_all", Loc.Get("csg.select_all_faces"),
            Loc.Get("csg.select_all_faces_tip"), CSGSurfaceSelection.SelectAllOfBrush);

        LabelledButton(paper, font, "csg_surf_same", Loc.Get("csg.select_same_material"),
            Loc.Get("csg.select_same_material_tip"), CSGSurfaceSelection.SelectSameMaterial);

        LabelledButton(paper, font, "csg_surf_copy", Loc.Get("csg.copy_surface"),
            Loc.Get("csg.copy_surface_tip"), CSGSurfaceSelection.CopyFromActive);
    }

    /// <summary>The materials to paint with as a row of buttons, a click puts one on every selected surface.</summary>
    private static void DrawPalette(Paper paper, Scribe.FontFile font)
    {
        List<Material> palette = CSGSurfaceSelection.Palette();
        if (palette.Count == 0) return;

        Material? current = CSGSurfaceSelection.Active is CSGSurfaceRef active && active.IsValid ? active.Data.Material : null;
        using (paper.Row("csg_palette").Height(UnitValue.Auto).WrapContent().Gap(3)
                   .Padding(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 2, 4).Enter())
        {
            for (int i = 0; i < palette.Count; i++)
            {
                Material material = palette[i];
                bool on = ReferenceEquals(material, current);
                paper.Box($"csg_palette_{i}").Width(UnitValue.Auto).Height(20).Padding(6, 6, 0, 0).Rounded(4)
                    .BackgroundColor(on ? EditorTheme.Accent : EditorTheme.Ink100)
                    .Hovered.BackgroundColor(on ? EditorTheme.AccentBright : EditorTheme.Hover).End()
                    .Text(material.Name, font).TextColor(on ? System.Drawing.Color.White : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(Loc.Get("csg.palette_tip"))
                    .OnClick(material, (m, _) => CSGSurfaceSelection.SetMaterial(m));
            }
        }
    }

    /// <summary>A U, V and both row, which is how most texture operations come.</summary>
    private static void AxisRow(Paper paper, string id, string label, string tooltip, Action<bool, bool> apply)
    {
        EditorGUI.SettingsRow(paper, id, label, () =>
            Origami.ButtonGroup(paper, $"{id}_v", -1, idx => apply(idx != 1, idx != 0))
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item("U", tooltip: tooltip)
                .Item("V", tooltip: tooltip)
                .Item("UV", tooltip: tooltip)
                .Show());
    }

    /// <summary>The nine way justify as a three by three grid.</summary>
    internal static void DrawJustifyGrid(Paper paper, Scribe.FontFile font)
    {
        SurfaceAlign[] order = [SurfaceAlign.Min, SurfaceAlign.Center, SurfaceAlign.Max];

        EditorGUI.SettingsRow(paper, "csg_surf_just", Loc.Get("csg.justify"), () =>
        {
            using (paper.Column("csg_just_grid").Height(UnitValue.Auto).Gap(2).Enter())
            {
                for (int row = 0; row < 3; row++)
                {
                    int r = row;
                    using (paper.Row($"csg_just_r{r}").Height(20).Gap(2).Enter())
                    {
                        for (int col = 0; col < 3; col++)
                        {
                            int c = col;
                            paper.Box($"csg_just_{r}{c}")
                                .Height(20).Rounded(4)
                                .BackgroundColor(EditorTheme.Ink100)
                                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                                .Text(JustifyGlyph(r, c), font).TextColor(EditorTheme.Ink500)
                                .FontSize(10f).Alignment(TextAlignment.MiddleCenter)
                                .Tooltip(Loc.Get("csg.justify"), Loc.Get("csg.justify_tip"))
                                .OnClick(0, (_, _) => CSGSurfaceSelection.Justify(order[c], order[r]));
                        }
                    }
                }
            }
        });
    }

    private static string JustifyGlyph(int row, int col)
    {
        if (row == 1 && col == 1) return "+";
        return (row, col) switch
        {
            (0, 0) => "\\", (0, 1) => "^", (0, 2) => "/",
            (1, 0) => "<", (1, 2) => ">",
            (2, 0) => "/", (2, 1) => "v", _ => "\\",
        };
    }

    internal static void DrawSelectionSection(Paper paper, Scribe.FontFile font, CSGObjectTool? tool)
    {
        EditorGUI.SectionHeader(paper, "csg_sel_h", Loc.Get("csg.selection"));

        List<CSGBrush> brushes = CSGSelection.Brushes();
        List<CSGGroup> groups = CSGSelection.Groups();
        bool any = brushes.Count > 0 || groups.Count > 0;

        if (!any)
        {
            Hint(paper, font, "csg_sel_hint", Loc.Get("csg.select_hint"));
            return;
        }

        EditorGUI.SettingsRow(paper, "csg_sel_op", Loc.Get("csg.operation"), () =>
            Origami.ButtonGroup(paper, "csg_sel_op_v", OperationIndex(brushes, groups),
                    idx => CSGSelection.SetOperation((Operation)idx))
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(Loc.Get("csg.additive"), tooltip: Loc.Get("csg.additive_tip"))
                .Item(Loc.Get("csg.subtractive"), tooltip: Loc.Get("csg.subtractive_tip"))
                .Item(Loc.Get("csg.intersect"), tooltip: Loc.Get("csg.intersect_tip"))
                .Show());

        // One brush can be sized and placed by number, which reshapes it rather than scaling its transform
        if (brushes.Count == 1 && groups.Count == 0 && CSGSelection.TryBox(brushes[0], out Float3 centre, out Float3 size))
        {
            CSGBrush brush = brushes[0];
            EditorGUI.SettingsRow(paper, "csg_sel_size", Loc.Get("csg.size"), () =>
                Origami.Float3Field(paper, "csg_sel_size_v", size, v => CSGSelection.Resize(brush, v)).Show());
            EditorGUI.SettingsRow(paper, "csg_sel_pos", Loc.Get("csg.position"), () =>
                Origami.Float3Field(paper, "csg_sel_pos_v", centre, v => CSGSelection.MoveCentre(brush, v)).Show());
        }

        EditorGUI.SettingsRow(paper, "csg_sel_flip", Loc.Get("csg.flip"), () =>
            Origami.ButtonGroup(paper, "csg_sel_flip_v", -1, idx => CSGSelection.Flip(AxisFor(idx)))
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item("X", tooltip: Loc.Get("csg.flip_tip"))
                .Item("Y", tooltip: Loc.Get("csg.flip_tip"))
                .Item("Z", tooltip: Loc.Get("csg.flip_tip"))
                .Show());

        EditorGUI.SettingsRow(paper, "csg_sel_step", Loc.Get("csg.move_by"), () =>
            Origami.Float3Field(paper, "csg_sel_step_v", CSGSelection.ArrayOffset, v => CSGSelection.ArrayOffset = v).Show());
        EditorGUI.SettingsRow(paper, "csg_sel_count", Loc.Get("csg.copies"), () =>
            Origami.NumericField(paper, "csg_sel_count_v", CSGSelection.ArrayCount, v => CSGSelection.ArrayCount = Math.Clamp(v, 1, 256)).Show());
        EditorGUI.SettingsRow(paper, "csg_sel_array", "", () =>
            Origami.ButtonGroup(paper, "csg_sel_array_v", -1, idx =>
                {
                    if (idx == 0) CSGSelection.MoveBy(CSGSelection.ArrayOffset, clone: false);
                    else CSGSelection.CloneArray();
                })
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(Loc.Get("csg.move"), tooltip: Loc.Get("csg.move_tip"))
                .Item(Loc.Get("csg.clone_array"), tooltip: Loc.Get("csg.clone_array_tip"))
                .Show());

        EditorGUI.SettingsRow(paper, "csg_sel_rot", Loc.Get("csg.rotate"), () =>
            Origami.ButtonGroup(paper, "csg_sel_rot_v", -1, idx =>
                {
                    Float3 axis = AxisFor(idx / 2) * (idx % 2 == 0 ? -SceneTools.RotateSnap : SceneTools.RotateSnap);
                    CSGSelection.RotateBy(axis, CSGSelection.SelectionCentre(), clone: false);
                })
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item("X-", tooltip: Loc.Get("csg.rotate_tip")).Item("X+", tooltip: Loc.Get("csg.rotate_tip"))
                .Item("Y-", tooltip: Loc.Get("csg.rotate_tip")).Item("Y+", tooltip: Loc.Get("csg.rotate_tip"))
                .Item("Z-", tooltip: Loc.Get("csg.rotate_tip")).Item("Z+", tooltip: Loc.Get("csg.rotate_tip"))
                .Show());

        EditorGUI.SettingsRow(paper, "csg_sel_order", Loc.Get("csg.order"), () =>
            Origami.ButtonGroup(paper, "csg_sel_order_v", -1, idx => CSGSelection.MoveInOrder(idx == 0 ? -1 : 1))
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(Loc.Get("csg.earlier"), tooltip: ShortcutManager.GetTooltip(Loc.Get("csg.earlier_tip"), "CSG/Earlier"))
                .Item(Loc.Get("csg.later"), tooltip: ShortcutManager.GetTooltip(Loc.Get("csg.later_tip"), "CSG/Later"))
                .Show());

        LabelledButton(paper, font, "csg_sel_pivot", Loc.Get("csg.centre_pivot"),
            Loc.Get("csg.centre_pivot_tip"), CSGSelection.CentrePivot);

        LabelledButton(paper, font, "csg_sel_dup", Loc.Get("csg.duplicate"),
            Loc.Get("csg.duplicate_tip"), CSGSelection.Duplicate);

        LabelledButton(paper, font, "csg_sel_group", Loc.Get("csg.group_selection"),
            ShortcutManager.GetTooltip(Loc.Get("csg.group_selection_tip"), "CSG/GroupSelection"), CSGMenuItems.GroupSelection);
    }

    /// <summary>The size and settings a generated shape was made with, changing them builds it again.</summary>
    internal static void DrawShapeSection(Paper paper, Scribe.FontFile font)
    {
        List<GameObject> selected = Selection.GetSelected<GameObject>().ToList();
        if (selected.Count != 1) return;
        CSGShape shape = selected[0].GetComponent<CSGShape>();
        if (shape.IsNotValid()) return;

        EditorGUI.SectionHeader(paper, "csg_shape_h", Loc.Get("csg.shape_settings", new { kind = shape.Kind.ToString() }));

        if (shape.Kind == CSGGeneratorKind.Polygon)
        {
            EditorGUI.SettingsRow(paper, "csg_shape_height", Loc.Get("csg.height"), () =>
                Origami.NumericField(paper, "csg_shape_height_v", shape.Height, v => Rebuild(shape, () => shape.Height = v)).Show());
            return;
        }

        EditorGUI.SettingsRow(paper, "csg_shape_size", Loc.Get("csg.size"), () =>
            Origami.Float3Field(paper, "csg_shape_size_v", shape.Size, v => Rebuild(shape, () => shape.Size = Maths.Max(v, new Float3(0.01f)))).Show());
        DrawGeneratorSettings(paper, font, "csg_shape", shape.Kind, shape.Settings, v => Rebuild(shape, () => shape.Settings = v));
    }

    private static void Rebuild(CSGShape shape, Action change)
    {
        Undo.RecordObject(shape, "Edit CSG Shape");
        change();
        CSGSelection.Regenerate(shape, "Edit CSG Shape");
    }

    internal static void DrawVisibilitySection(Paper paper, Scribe.FontFile font, CSGObjectTool? tool)
    {
        if (tool == null) return;

        EditorGUI.SectionHeader(paper, "csg_vis_h", Loc.Get("csg.visibility"));

        Hint(paper, font, "csg_vis_hint", Loc.Get("csg.visibility_hint"));

        LabelledButton(paper, font, "csg_vis_hide", Loc.Get("csg.hide_selected"),
            Loc.Get("csg.hide_selected_tip"), () => CSGVisibility.Hide(CSGSelection.Brushes()));

        LabelledButton(paper, font, "csg_vis_show", Loc.Get("csg.unhide_all", new { count = CSGVisibility.HiddenCount }),
            Loc.Get("csg.unhide_all_tip"), CSGVisibility.UnhideAll);
    }

    internal static string OperationLabel(Operation operation) => operation switch
    {
        Operation.Subtractive => "csg.subtractive",
        Operation.Intersect => "csg.intersect",
        _ => "csg.additive",
    };

    private static void LabelledButton(Paper paper, Scribe.FontFile font, string id, string label,
                                       string tooltip, System.Action onClick)
    {
        using (paper.Row(id).Height(EditorTheme.RowHeight + 4)
                   .Padding(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 2, 2)
                   .Enter())
        {
            Origami.Button(paper, $"{id}_b", label, onClick).FullWidth().Height(EditorTheme.RowHeight)
                .Tooltip(tooltip)
                .Show();
        }
    }

    private static void Hint(Paper paper, Scribe.FontFile font, string id, string text)
    {
        paper.Box(id).Height(UnitValue.Auto).Margin(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 2, 4)
            .Text(text, font).TextColor(EditorTheme.Ink300).Wrap(Scribe.TextWrapMode.Wrap)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.Left);
    }

    private static Float3 AxisFor(int index) => index switch
    {
        1 => Float3.UnitY,
        2 => Float3.UnitZ,
        _ => Float3.UnitX,
    };

    /// <summary>The shared operation of the selection, or -1 when they disagree.</summary>
    private static int OperationIndex(List<CSGBrush> brushes, List<CSGGroup> groups)
    {
        int result = -1;
        foreach (Operation op in brushes.Select(b => b.Operation).Concat(groups.Select(g => g.Operation)))
        {
            if (result == -1) result = (int)op;
            else if (result != (int)op) return -1;
        }
        return result;
    }

    private static void SetFlag(CSGModel model, CSGModelFlags flag, bool on)
        => model.Flags = on ? model.Flags | flag : model.Flags & ~flag;

    private static void Change(CSGModel model, Action change)
    {
        Undo.RecordObject(model, "Edit CSG Model");
        change();
        model.SetDirty();
    }
}
