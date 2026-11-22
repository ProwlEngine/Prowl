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
        EditorGUI.SectionHeader(paper, "csg_model_h", Loc.Get("csg.model"), first: true);

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

        DrawHelperSection(paper);
        DrawLightmapSection(paper, font, model);
    }

    private static void DrawHelperSection(Paper paper)
    {
        EditorGUI.SectionHeader(paper, "csg_help_h", Loc.Get("csg.helpers"));

        ToggleRow(paper, "csg_help_col", Loc.Get("csg.helper_collision"), CSGHelperSurfaces.Flags.HasFlag(CSGHelperFlags.CollisionOnly),
            v => CSGHelperSurfaces.Set(CSGHelperFlags.CollisionOnly, v));
        ToggleRow(paper, "csg_help_inert", Loc.Get("csg.helper_inert"), CSGHelperSurfaces.Flags.HasFlag(CSGHelperFlags.Inert),
            v => CSGHelperSurfaces.Set(CSGHelperFlags.Inert, v));
    }

    /// <summary>Lightmap UVs are opt in and deferred. Stale UVs show a rebuild button rather than unwrapping mid edit.</summary>
    private static void DrawLightmapSection(Paper paper, Scribe.FontFile font, CSGModel model)
    {
        EditorGUI.SectionHeader(paper, "csg_lm_h", Loc.Get("csg.lightmapping"));

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

        Hint(paper, font, "csg_gen_hint",
            Loc.Get(tool.Drawing ? "csg.draw_active" : "csg.draw_inactive"));

        EditorGUI.SettingsRow(paper, "csg_gen_op", Loc.Get("csg.new_operation"), () =>
            Origami.ButtonGroup(paper, "csg_gen_op_v", (int)tool.Operation, idx => tool.Operation = (Operation)idx)
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item(Loc.Get("csg.additive"), tooltip: Loc.Get("csg.additive_tip"))
                .Item(Loc.Get("csg.subtractive"), tooltip: Loc.Get("csg.subtractive_tip"))
                .Item(Loc.Get("csg.intersect"), tooltip: Loc.Get("csg.intersect_tip"))
                .Show());

        EditorGUI.SettingsRow(paper, "csg_gen_shape", Loc.Get("csg.shape"), () =>
            Origami.EnumDropdown(paper, "csg_gen_shape_v", tool.Generator, v => tool.Generator = v).Show());

        DrawGeneratorSettings(paper, font, tool);

        LabelledButton(paper, font, "csg_gen_toggle",
            Loc.Get(tool.Drawing ? "csg.stop_drawing" : "csg.draw_box"),
            Loc.Get(tool.Drawing ? "csg.stop_drawing_tip" : "csg.draw_box_tip"),
            () => CSGEditMode.Current = tool.Drawing ? CSGMode.Object : CSGMode.Generate);
    }

    /// <summary>Only the parameters the chosen shape actually uses.</summary>
    private static void DrawGeneratorSettings(Paper paper, Scribe.FontFile font, CSGGenerateTool tool)
    {
        switch (tool.Generator)
        {
            case CSGGeneratorKind.Cylinder:
                IntRow(paper, "csg_gen_sides", Loc.Get("csg.sides"), tool.Settings.Sides,
                    v => tool.Settings.Sides = v);
                FloatRow(paper, "csg_gen_angle", Loc.Get("csg.offset_angle"), tool.Settings.OffsetAngle,
                    v => tool.Settings.OffsetAngle = v);
                ToggleRow(paper, "csg_gen_corner", Loc.Get("csg.radius_to_corner"), tool.Settings.RadiusToCorner,
                    v => tool.Settings.RadiusToCorner = v);
                ToggleRow(paper, "csg_gen_fit", Loc.Get("csg.recentre"), tool.Settings.Recentre,
                    v => tool.Settings.Recentre = v);
                ToggleRow(paper, "csg_gen_smooth", Loc.Get("csg.smooth_shading"), tool.Settings.SmoothShading,
                    v => tool.Settings.SmoothShading = v);
                break;

            case CSGGeneratorKind.Sphere:
                IntRow(paper, "csg_gen_seg", Loc.Get("csg.segments"), tool.Settings.Sides,
                    v => tool.Settings.Sides = v);
                IntRow(paper, "csg_gen_rings", Loc.Get("csg.rings"), tool.Settings.Rings,
                    v => tool.Settings.Rings = v);
                ToggleRow(paper, "csg_gen_hemi", Loc.Get("csg.hemisphere"), tool.Settings.Hemisphere,
                    v => tool.Settings.Hemisphere = v);
                ToggleRow(paper, "csg_gen_ssmooth", Loc.Get("csg.smooth_shading"), tool.Settings.SmoothShading,
                    v => tool.Settings.SmoothShading = v);
                break;

            case CSGGeneratorKind.LinearStairs:
                IntRow(paper, "csg_gen_total", Loc.Get("csg.total_steps"), tool.Settings.TotalSteps,
                    v => tool.Settings.TotalSteps = v);
                FloatRow(paper, "csg_gen_sh", Loc.Get("csg.step_height"), tool.Settings.StepHeight,
                    v => tool.Settings.StepHeight = v);
                FloatRow(paper, "csg_gen_sd", Loc.Get("csg.step_depth"), tool.Settings.StepDepth,
                    v => tool.Settings.StepDepth = v);
                FloatRow(paper, "csg_gen_ed", Loc.Get("csg.extra_depth"), tool.Settings.ExtraDepth,
                    v => tool.Settings.ExtraDepth = v);
                FloatRow(paper, "csg_gen_eh", Loc.Get("csg.extra_height"), tool.Settings.ExtraHeight,
                    v => tool.Settings.ExtraHeight = v);
                ToggleRow(paper, "csg_gen_fill", Loc.Get("csg.filled_stairs"), tool.Settings.FilledStairs,
                    v => tool.Settings.FilledStairs = v);
                break;

            case CSGGeneratorKind.SpiralStairs:
                FloatRow(paper, "csg_gen_ssh", Loc.Get("csg.step_height"), tool.Settings.StepHeight,
                    v => tool.Settings.StepHeight = v);
                IntRow(paper, "csg_gen_spr", Loc.Get("csg.steps_per_turn"), tool.Settings.StepsPerRevolution,
                    v => tool.Settings.StepsPerRevolution = v);
                FloatRow(paper, "csg_gen_inner", Loc.Get("csg.inner_radius"), tool.Settings.InnerRadius,
                    v => tool.Settings.InnerRadius = v);
                break;
        }
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

        paper.Box("csg_mesh_count").Height(18).Margin(Origami.Current.Metrics.PaddingLarge, 0, 2, 4)
            .Text(Loc.Get("csg.mesh_selected", new { count = tool.SelectedCount }), font)
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

        Hint(paper, font, "csg_clip_help", Loc.Get("csg.clip_help"));
    }

    internal static void DrawSurfaceSection(Paper paper, Scribe.FontFile font, CSGSurfaceTool? tool)
    {
        if (tool == null) return;

        EditorGUI.SectionHeader(paper, "csg_surf_h", Loc.Get("csg.surfaces"));

        LabelledButton(paper, font, "csg_surf_toggle",
            Loc.Get(tool.Active ? "csg.surface_stop" : "csg.surface_mode"),
            Loc.Get(tool.Active ? "csg.surface_stop_tip" : "csg.surface_mode_tip"),
            () => CSGEditMode.Current = tool.Active ? CSGMode.Object : CSGMode.Surface);

        if (CSGSurfaceSelection.Active is not CSGSurfaceRef active || !active.IsValid)
        {
            Hint(paper, font, "csg_surf_hint",
                Loc.Get(tool.Active ? "csg.surface_pick_hint" : "csg.surface_off_hint"));
            return;
        }

        SurfaceData data = active.Data;

        paper.Box("csg_surf_count").Height(18).Margin(Origami.Current.Metrics.PaddingLarge, 0, 2, 4)
            .Text(Loc.Get("csg.surfaces_selected", new { count = CSGSurfaceSelection.Count }), font)
            .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleLeft);

        EditorGUI.SettingsRow(paper, "csg_surf_mat", Loc.Get("csg.material"), () =>
            PropertyGridUtils.DrawField(paper, "csg_surf_mat_v", "", typeof(Material),
                data.Material, v => CSGSurfaceSelection.SetMaterial(v as Material), 0));

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

        LabelledButton(paper, font, "csg_sel_pivot", Loc.Get("csg.centre_pivot"),
            Loc.Get("csg.centre_pivot_tip"), CSGSelection.CentrePivot);

        LabelledButton(paper, font, "csg_sel_dup", Loc.Get("csg.duplicate"),
            Loc.Get("csg.duplicate_tip"), CSGSelection.Duplicate);
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

    /// <summary>The keys a mode answers to, one per line, with their current bindings.</summary>
    internal static void DrawKeys(Paper paper, Scribe.FontFile font, string[] shortcuts)
    {
        EditorGUI.SectionHeader(paper, "csg_keys_h", Loc.Get("csg.keys"));
        foreach (string id in shortcuts)
        {
            if (ShortcutManager.Find(id) is not { } shortcut) continue;
            using (paper.Row($"csg_key_{id}").Height(16).Padding(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 0, 0).Enter())
            {
                paper.Box($"csg_key_{id}_n").Height(16)
                    .Text(shortcut.DisplayName, font).TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                paper.Box($"csg_key_{id}_k").Height(16)
                    .Text(ShortcutManager.GetDisplayString(shortcut.Binding), font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
            }
        }
    }

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
        paper.Box(id).Height(32).Margin(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 2, 4)
            .Text(text, font).TextColor(EditorTheme.Ink300)
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
