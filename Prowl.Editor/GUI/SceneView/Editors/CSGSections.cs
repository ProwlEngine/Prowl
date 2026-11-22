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
using SColor = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>The CSG editor's controls as cards, shared by the scene view overlay and the docked panel.</summary>
internal static class CSGSections
{
    /// <summary>A clickable tile: an icon, a short label, and what it does.</summary>
    internal readonly record struct Tile(string Icon, string Label, string Tip, System.Action Run, bool On = false);

    private static readonly SColor CardColor = SColor.FromArgb(38, 0, 0, 0);
    private static readonly SColor TileColor = SColor.FromArgb(28, 255, 255, 255);

    // Cards that start folded away and have been opened
    private static readonly HashSet<string> _opened = [];

    internal static void EmptyState(Paper paper, Scribe.FontFile font, float height)
    {
        using (paper.Column("csg_empty").Height(height).Padding(16, 16, 16, 16).Gap(8).Enter())
        {
            paper.Box("csg_empty_t").Height(24)
                .Text(Loc.Get("csg.no_model"), font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleLeft);

            Hint(paper, font, "csg_empty_d", Loc.Get("csg.no_model_hint"));
            Actions(paper, font, "csg_empty_new", 1,
                new Tile(EditorIcons.CubesStacked, Loc.Get("csg.create_model"), Loc.Get("csg.create_model_tip"), () => CSGSelection.CreateModel()));
        }
    }

    internal static void DrawModelSection(Paper paper, Scribe.FontFile font, CSGModel model)
    {
        Card(paper, font, "csg_model", EditorIcons.CubesStacked, Loc.Get("csg.model"), () =>
        {
            Field(paper, font, "csg_model_mat", Loc.Get("csg.default_material"), () =>
                PropertyGridUtils.DrawField(paper, "csg_model_mat_v", "", typeof(Material),
                    model.DefaultMaterial, v => Change(model, () => model.DefaultMaterial = v as Material), 0));

            Field(paper, font, "csg_model_texel", Loc.Get("csg.texel_scale"), () =>
                Origami.NumericField(paper, "csg_model_texel_v", model.DefaultTexelScale,
                    v => Change(model, () => model.DefaultTexelScale = v)).Show());

            Switch(paper, font, "csg_model_render", Loc.Get("csg.render"), !model.Flags.HasFlag(CSGModelFlags.DoNotRender),
                v => Change(model, () => SetFlag(model, CSGModelFlags.DoNotRender, !v)));
            Switch(paper, font, "csg_model_collide", Loc.Get("csg.collide"), !model.Flags.HasFlag(CSGModelFlags.NoCollider),
                v => Change(model, () => SetFlag(model, CSGModelFlags.NoCollider, !v)));
            Switch(paper, font, "csg_model_convex", Loc.Get("csg.convex_collider"), model.Flags.HasFlag(CSGModelFlags.ConvexCollider),
                v => Change(model, () => SetFlag(model, CSGModelFlags.ConvexCollider, v)));

            Hint(paper, font, "csg_model_stats", Loc.Get("csg.stats", new
            {
                brushes = model.BrushCount,
                vertices = model.VertexCount,
                triangles = model.TriangleCount,
            }));

            Actions(paper, font, "csg_model_rebuild", 1,
                new Tile(EditorIcons.ArrowsRotate, Loc.Get("csg.rebuild"), Loc.Get("csg.rebuild_tip"), model.Rebuild));
        }, foldable: true);

        Card(paper, font, "csg_help", EditorIcons.Eye, Loc.Get("csg.helpers"), () =>
        {
            Switch(paper, font, "csg_help_col", Loc.Get("csg.helper_collision"), CSGHelperSurfaces.Flags.HasFlag(CSGHelperFlags.CollisionOnly),
                v => CSGHelperSurfaces.Set(CSGHelperFlags.CollisionOnly, v));
            Switch(paper, font, "csg_help_inert", Loc.Get("csg.helper_inert"), CSGHelperSurfaces.Flags.HasFlag(CSGHelperFlags.Inert),
                v => CSGHelperSurfaces.Set(CSGHelperFlags.Inert, v));
            Hint(paper, font, "csg_help_hint", Loc.Get("csg.helpers_hint"));
        }, foldable: true);

        Card(paper, font, "csg_lm", EditorIcons.Lightbulb, Loc.Get("csg.lightmapping"), () => DrawLightmap(paper, font, model), foldable: true);
    }

    /// <summary>Lightmap UVs are opt in and deferred. Stale UVs show a rebuild button rather than unwrapping mid edit.</summary>
    private static void DrawLightmap(Paper paper, Scribe.FontFile font, CSGModel model)
    {
        Switch(paper, font, "csg_lm_on", Loc.Get("csg.lightmap_uvs"), model.GenerateLightmapUVs,
            v => Change(model, () => model.GenerateLightmapUVs = v));

        if (!model.GenerateLightmapUVs)
        {
            Hint(paper, font, "csg_lm_hint", Loc.Get("csg.lightmap_off_hint"));
            return;
        }

        Switch(paper, font, "csg_lm_auto", Loc.Get("csg.lightmap_auto"), model.AutoRebuildLightmapUVs,
            v => Change(model, () => model.AutoRebuildLightmapUVs = v));

        if (model.LightmapUVsStale)
            Actions(paper, font, "csg_lm_build", 1, new Tile(EditorIcons.ArrowsRotate, Loc.Get("csg.lightmap_rebuild"),
                Loc.Get("csg.lightmap_rebuild_tip"), () => Change(model, model.RebuildLightmapUVs)));
        else
            Hint(paper, font, "csg_lm_ok", Loc.Get("csg.lightmap_ready"));
    }

    internal static void DrawGenerateSection(Paper paper, Scribe.FontFile font, CSGGenerateTool? tool)
    {
        if (tool == null) return;

        Card(paper, font, "csg_gen", EditorIcons.Shapes, Loc.Get("csg.shape"), () =>
        {
            Tiles(paper, font, "csg_gen_shape", 4, ShapeTiles(tool));
            Hint(paper, font, "csg_gen_hint", tool.Generator == CSGGeneratorKind.Polygon ? Loc.Get("csg.polygon_hint") : Loc.Get("csg.draw_active"));
        });

        Card(paper, font, "csg_gen_op", EditorIcons.LayerGroup, Loc.Get("csg.new_operation"), () =>
            Tiles(paper, font, "csg_gen_op_t", 3, OperationTiles((int)tool.Operation, op => tool.Operation = op)));

        if (HasSettings(tool.Generator))
        {
            Card(paper, font, "csg_gen_set", EditorIcons.Gear, ShapeName(tool.Generator), () =>
                DrawGeneratorSettings(paper, font, "csg_gen", tool.Generator, tool.Settings, v => tool.Settings = v));
        }
    }

    private static Tile[] ShapeTiles(CSGGenerateTool tool)
    {
        CSGGeneratorKind[] kinds = Enum.GetValues<CSGGeneratorKind>();
        var tiles = new Tile[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            CSGGeneratorKind kind = kinds[i];
            tiles[i] = new Tile(ShapeIcon(kind), ShapeName(kind),
                ShortcutManager.GetTooltip(ShapeName(kind), $"CSG/Shape{kind}"), () => tool.Generator = kind, tool.Generator == kind);
        }
        return tiles;
    }

    internal static string ShapeName(CSGGeneratorKind kind) => Loc.Get(kind switch
    {
        CSGGeneratorKind.Cylinder => "csg.shape_cylinder",
        CSGGeneratorKind.Sphere => "csg.shape_sphere",
        CSGGeneratorKind.LinearStairs => "csg.shape_stairs",
        CSGGeneratorKind.SpiralStairs => "csg.shape_spiral",
        CSGGeneratorKind.Room => "csg.shape_room",
        CSGGeneratorKind.Polygon => "csg.shape_polygon",
        _ => "csg.shape_box",
    });

    private static string ShapeIcon(CSGGeneratorKind kind) => kind switch
    {
        CSGGeneratorKind.Cylinder => EditorIcons.Database,
        CSGGeneratorKind.Sphere => EditorIcons.Globe,
        CSGGeneratorKind.LinearStairs => EditorIcons.Stairs,
        CSGGeneratorKind.SpiralStairs => EditorIcons.ArrowsSpin,
        CSGGeneratorKind.Room => EditorIcons.DoorOpen,
        CSGGeneratorKind.Polygon => EditorIcons.DrawPolygon,
        _ => EditorIcons.Cube,
    };

    private static bool HasSettings(CSGGeneratorKind kind) => kind is not (CSGGeneratorKind.Box or CSGGeneratorKind.Polygon);

    private static Tile[] OperationTiles(int current, Action<Operation> set) =>
    [
        new(EditorIcons.SquarePlus, Loc.Get("csg.additive"), ShortcutManager.GetTooltip(Loc.Get("csg.additive_tip"), "CSG/SetAdditive"), () => set(Operation.Additive), current == (int)Operation.Additive),
        new(EditorIcons.SquareMinus, Loc.Get("csg.subtractive"), ShortcutManager.GetTooltip(Loc.Get("csg.subtractive_tip"), "CSG/SetSubtractive"), () => set(Operation.Subtractive), current == (int)Operation.Subtractive),
        new(EditorIcons.CropSimple, Loc.Get("csg.intersect"), ShortcutManager.GetTooltip(Loc.Get("csg.intersect_tip"), "CSG/SetIntersecting"), () => set(Operation.Intersect), current == (int)Operation.Intersect),
    ];

    /// <summary>Only the parameters the chosen shape actually uses.</summary>
    internal static void DrawGeneratorSettings(Paper paper, Scribe.FontFile font, string prefix, CSGGeneratorKind kind, CSGGeneratorSettings settings, Action<CSGGeneratorSettings> apply)
    {
        switch (kind)
        {
            case CSGGeneratorKind.Cylinder:
                Pair(paper, font, $"{prefix}_cyl",
                    (Loc.Get("csg.sides"), () => Number(paper, $"{prefix}_sides", settings.Sides,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Sides = v))),
                    (Loc.Get("csg.offset_angle"), () => Number(paper, $"{prefix}_angle", settings.OffsetAngle,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.OffsetAngle = v))));
                Switch(paper, font, $"{prefix}_corner", Loc.Get("csg.radius_to_corner"), settings.RadiusToCorner,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.RadiusToCorner = v));
                Switch(paper, font, $"{prefix}_fit", Loc.Get("csg.recentre"), settings.Recentre,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Recentre = v));
                Switch(paper, font, $"{prefix}_smooth", Loc.Get("csg.smooth_shading"), settings.SmoothShading,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.SmoothShading = v));
                break;

            case CSGGeneratorKind.Sphere:
                Pair(paper, font, $"{prefix}_sph",
                    (Loc.Get("csg.segments"), () => Number(paper, $"{prefix}_seg", settings.Sides,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Sides = v))),
                    (Loc.Get("csg.rings"), () => Number(paper, $"{prefix}_rings", settings.Rings,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Rings = v))));
                Switch(paper, font, $"{prefix}_hemi", Loc.Get("csg.hemisphere"), settings.Hemisphere,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.Hemisphere = v));
                Switch(paper, font, $"{prefix}_ssmooth", Loc.Get("csg.smooth_shading"), settings.SmoothShading,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.SmoothShading = v));
                break;

            case CSGGeneratorKind.LinearStairs:
                Field(paper, font, $"{prefix}_total", Loc.Get("csg.total_steps"), () => Number(paper, $"{prefix}_total_v", settings.TotalSteps,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.TotalSteps = v)));
                Pair(paper, font, $"{prefix}_step",
                    (Loc.Get("csg.step_height"), () => Number(paper, $"{prefix}_sh", settings.StepHeight,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.StepHeight = v))),
                    (Loc.Get("csg.step_depth"), () => Number(paper, $"{prefix}_sd", settings.StepDepth,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.StepDepth = v))));
                Pair(paper, font, $"{prefix}_extra",
                    (Loc.Get("csg.extra_depth"), () => Number(paper, $"{prefix}_ed", settings.ExtraDepth,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.ExtraDepth = v))),
                    (Loc.Get("csg.extra_height"), () => Number(paper, $"{prefix}_eh", settings.ExtraHeight,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.ExtraHeight = v))));
                Switch(paper, font, $"{prefix}_fill", Loc.Get("csg.filled_stairs"), settings.FilledStairs,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.FilledStairs = v));
                break;

            case CSGGeneratorKind.Room:
                Field(paper, font, $"{prefix}_wall", Loc.Get("csg.wall_thickness"), () => Number(paper, $"{prefix}_wall_v", settings.WallThickness,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.WallThickness = MathF.Max(0.01f, v))));
                break;

            case CSGGeneratorKind.SpiralStairs:
                Pair(paper, font, $"{prefix}_spiral",
                    (Loc.Get("csg.step_height"), () => Number(paper, $"{prefix}_ssh", settings.StepHeight,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.StepHeight = v))),
                    (Loc.Get("csg.steps_per_turn"), () => Number(paper, $"{prefix}_spr", settings.StepsPerRevolution,
                        v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.StepsPerRevolution = v))));
                Field(paper, font, $"{prefix}_inner", Loc.Get("csg.inner_radius"), () => Number(paper, $"{prefix}_inner_v", settings.InnerRadius,
                    v => Update(settings, apply, (ref CSGGeneratorSettings s) => s.InnerRadius = v)));
                break;
        }
    }

    private delegate void SettingsEdit(ref CSGGeneratorSettings settings);

    private static void Update(CSGGeneratorSettings settings, Action<CSGGeneratorSettings> apply, SettingsEdit edit)
    {
        edit(ref settings);
        apply(settings);
    }

    internal static void DrawMeshSection(Paper paper, Scribe.FontFile font, CSGMeshTool? tool)
    {
        if (tool == null) return;

        if (tool.Target.IsNotValid())
        {
            Card(paper, font, "csg_mesh", EditorIcons.DrawPolygon, Loc.Get("csg.mesh"), () =>
                Hint(paper, font, "csg_mesh_none", Loc.Get("csg.mesh_one_brush")));
            return;
        }

        Card(paper, font, "csg_mesh_pick", EditorIcons.ArrowPointer, Loc.Get("csg.mesh_select"), () =>
        {
            Tiles(paper, font, "csg_mesh_filter", 3,
                FilterTile(tool, EditorIcons.CircleDot, "csg.vertex", CSGElementFilter.Vertex),
                FilterTile(tool, EditorIcons.Slash, "csg.edge", CSGElementFilter.Edge),
                FilterTile(tool, EditorIcons.SquareFull, "csg.face", CSGElementFilter.Face));

            Hint(paper, font, "csg_mesh_count", Loc.Get("csg.mesh_selected", new { count = tool.SelectedCount, faces = tool.SelectedFaceCount }));
            if (tool.Filter == CSGElementFilter.None)
                Hint(paper, font, "csg_mesh_warn", Loc.Get("csg.mesh_nothing_selectable"), warn: true);
            if (tool.NonPlanar)
                Hint(paper, font, "csg_mesh_planar", Loc.Get("csg.mesh_non_planar"), warn: true);
        });

        Card(paper, font, "csg_mesh_edit", EditorIcons.WandMagicSparkles, Loc.Get("csg.mesh"), () =>
        {
            Pair(paper, font, "csg_mesh_dist",
                (Loc.Get("csg.chamfer"), () => Number(paper, "csg_mesh_chamfer_v", tool.ChamferDistance, v => tool.ChamferDistance = MathF.Max(0f, v))),
                (Loc.Get("csg.extrude"), () => Number(paper, "csg_mesh_extrude_v", tool.ExtrudeDistance, v => tool.ExtrudeDistance = v)));

            Actions(paper, font, "csg_mesh_actions", 2,
                new Tile(EditorIcons.BezierCurve, Loc.Get("csg.chamfer"), Loc.Get("csg.chamfer_tip"), tool.ChamferSelected),
                new Tile(EditorIcons.ArrowUpFromBracket, Loc.Get("csg.extrude"), Loc.Get("csg.extrude_tip"), tool.ExtrudeSelected),
                new Tile(EditorIcons.Magnet, Loc.Get("csg.snap"), ShortcutManager.GetTooltip(Loc.Get("csg.snap_tip"), "CSG/SnapToGrid"), tool.SnapSelectedToGrid),
                new Tile(EditorIcons.Trash, Loc.Get("csg.delete_face"), Loc.Get("csg.delete_face_tip"), tool.DeleteSelectedFaces));

            Hint(paper, font, "csg_mesh_help", Loc.Get("csg.mesh_help"));
        });
    }

    private static Tile FilterTile(CSGMeshTool tool, string icon, string label, CSGElementFilter flag)
        => new(icon, Loc.Get(label), Loc.Get("csg.mesh_select_tip"),
            () => tool.Filter = tool.Filter.HasFlag(flag) ? tool.Filter & ~flag : tool.Filter | flag, tool.Filter.HasFlag(flag));

    internal static void DrawClipSection(Paper paper, Scribe.FontFile font, CSGClipTool? tool)
    {
        if (tool == null) return;

        Card(paper, font, "csg_clip", EditorIcons.Scissors, Loc.Get("csg.clip"), () =>
        {
            int count = CSGSelection.Brushes().Count;
            Hint(paper, font, "csg_clip_hint", count > 0 ? Loc.Get("csg.clip_selected", new { count }) : Loc.Get("csg.clip_all"));

            Label(paper, font, "csg_clip_mode_l", Loc.Get("csg.clip_mode"));
            Tiles(paper, font, "csg_clip_mode", 3,
                new Tile(EditorIcons.ArrowDown, Loc.Get("csg.clip_front"), Loc.Get("csg.clip_front_tip"), () => tool.Mode = CSGClipMode.RemoveFront, tool.Mode == CSGClipMode.RemoveFront),
                new Tile(EditorIcons.ArrowUp, Loc.Get("csg.clip_back"), Loc.Get("csg.clip_back_tip"), () => tool.Mode = CSGClipMode.RemoveBack, tool.Mode == CSGClipMode.RemoveBack),
                new Tile(EditorIcons.ArrowsUpDown, Loc.Get("csg.clip_split"), Loc.Get("csg.clip_split_tip"), () => tool.Mode = CSGClipMode.Split, tool.Mode == CSGClipMode.Split));

            if (tool.HasPlane)
            {
                Actions(paper, font, "csg_clip_go", 2,
                    new Tile(EditorIcons.Check, Loc.Get("csg.clip_commit"), ShortcutManager.GetTooltip(Loc.Get("csg.clip_commit_tip"), "CSG/Confirm"), tool.Commit),
                    new Tile(EditorIcons.Xmark, Loc.Get("csg.clip_cancel"), Loc.Get("csg.clip_cancel_tip"), tool.Cancel));
            }

            Hint(paper, font, "csg_clip_help", Loc.Get("csg.clip_help"));
        });
    }

    internal static void DrawSurfaceSection(Paper paper, Scribe.FontFile font, CSGSurfaceTool? tool)
    {
        if (tool == null) return;

        bool picked = CSGSurfaceSelection.Active is CSGSurfaceRef active && active.IsValid;

        Card(paper, font, "csg_surf_pick", EditorIcons.ArrowPointer, Loc.Get("csg.select"), () =>
        {
            Actions(paper, font, "csg_surf_by", 3,
                new Tile(EditorIcons.ArrowDown, Loc.Get("csg.floors"), Loc.Get("csg.floors_tip"), CSGSurfaceSelection.SelectFloors),
                new Tile(EditorIcons.BorderAll, Loc.Get("csg.walls"), Loc.Get("csg.walls_tip"), CSGSurfaceSelection.SelectWalls),
                new Tile(EditorIcons.ArrowUp, Loc.Get("csg.ceilings"), Loc.Get("csg.ceilings_tip"), CSGSurfaceSelection.SelectCeilings),
                new Tile(EditorIcons.LayerGroup, Loc.Get("csg.coplanar"), Loc.Get("csg.coplanar_tip"), CSGSurfaceSelection.SelectCoplanar),
                new Tile(EditorIcons.Link, Loc.Get("csg.connected"), Loc.Get("csg.connected_tip"), CSGSurfaceSelection.SelectConnected),
                new Tile(EditorIcons.BorderAll, Loc.Get("csg.all_faces"), Loc.Get("csg.select_all_faces_tip"), CSGSurfaceSelection.SelectAllOfBrush),
                new Tile(EditorIcons.Palette, Loc.Get("csg.same_material"), Loc.Get("csg.select_same_material_tip"), CSGSurfaceSelection.SelectSameMaterial));

            if (!picked)
            {
                Hint(paper, font, "csg_surf_hint", Loc.Get("csg.surface_pick_hint"));
                return;
            }

            CSGSurfaceRef current = CSGSurfaceSelection.Active!.Value;
            Hint(paper, font, "csg_surf_count", Loc.Get("csg.surfaces_selected", new { count = CSGSurfaceSelection.Count }));
            Actions(paper, font, "csg_surf_owner", 1,
                new Tile(EditorIcons.Cube, Loc.Get("csg.surface_owner", new { name = current.Brush.GameObject.Name }),
                    Loc.Get("csg.surface_owner_tip"), () => Selection.Select(current.Brush.GameObject)));
        });

        if (!picked) return;

        SurfaceData data = CSGSurfaceSelection.Active!.Value.Data;

        Card(paper, font, "csg_surf_mat", EditorIcons.Palette, Loc.Get("csg.material"), () =>
        {
            PropertyGridUtils.DrawField(paper, "csg_surf_mat_v", "", typeof(Material),
                data.Material, v => CSGSurfaceSelection.SetMaterial(v as Material), 0);
            DrawPalette(paper, font, data.Material);
            Hint(paper, font, "csg_surf_paint", Loc.Get("csg.surface_paint_hint"));
        });

        Card(paper, font, "csg_surf_map", EditorIcons.Ruler, Loc.Get("csg.mapping"), () =>
        {
            Field(paper, font, "csg_surf_offset", Loc.Get("csg.uv_offset"), () =>
                Origami.Float2Field(paper, "csg_surf_offset_v", data.Offset, v => CSGSurfaceSelection.SetOffset(v)).Show());
            Field(paper, font, "csg_surf_scale", Loc.Get("csg.uv_scale"), () =>
                Origami.Float2Field(paper, "csg_surf_scale_v", data.Scale, v => CSGSurfaceSelection.SetScale(v)).Show());
            Field(paper, font, "csg_surf_rot", Loc.Get("csg.uv_rotation"), () =>
                Number(paper, "csg_surf_rot_v", data.Rotation, v => CSGSurfaceSelection.SetRotation(v)));

            Actions(paper, font, "csg_surf_step", 2,
                new Tile(EditorIcons.RotateLeft, "-90", Loc.Get("csg.uv_rotate_step_tip"), () => CSGSurfaceSelection.RotateBy(-90f)),
                new Tile(EditorIcons.RotateRight, "+90", Loc.Get("csg.uv_rotate_step_tip"), () => CSGSurfaceSelection.RotateBy(90f)),
                new Tile(EditorIcons.Compress, Loc.Get("csg.scale_half"), Loc.Get("csg.uv_scale_step_tip"), () => CSGSurfaceSelection.ScaleBy(0.5f)),
                new Tile(EditorIcons.Expand, Loc.Get("csg.scale_double"), Loc.Get("csg.uv_scale_step_tip"), () => CSGSurfaceSelection.ScaleBy(2f)));

            AxisRow(paper, font, "csg_surf_fit", Loc.Get("csg.fit"), Loc.Get("csg.fit_tip"), CSGSurfaceSelection.Fit);
            AxisRow(paper, font, "csg_surf_flip", Loc.Get("csg.reverse"), Loc.Get("csg.reverse_tip"), CSGSurfaceSelection.Flip);
            AxisRow(paper, font, "csg_surf_reset", Loc.Get("csg.reset"), Loc.Get("csg.reset_tip"), CSGSurfaceSelection.Reset);

            using (paper.Row("csg_surf_just_row").Height(UnitValue.Auto).Gap(8).Enter())
            {
                using (paper.Column("csg_surf_just_l").Height(UnitValue.Auto).Gap(4).Enter())
                {
                    Label(paper, font, "csg_surf_just_t", Loc.Get("csg.justify"));
                    DrawJustifyGrid(paper, font);
                }
                using (paper.Column("csg_surf_just_r").Height(UnitValue.Auto).Gap(4).Enter())
                {
                    Label(paper, font, "csg_surf_tools_t", Loc.Get("csg.apply"));
                    Actions(paper, font, "csg_surf_tools", 1,
                        new Tile(EditorIcons.AlignCenter, Loc.Get("csg.align"), Loc.Get("csg.align_tip"), CSGSurfaceSelection.AlignToActive),
                        new Tile(EditorIcons.Copy, Loc.Get("csg.copy_surface"), Loc.Get("csg.copy_surface_tip"), CSGSurfaceSelection.CopyFromActive));
                }
            }
        });

        Card(paper, font, "csg_surf_flags", EditorIcons.CircleHalfStroke, Loc.Get("csg.shading_flags"), () =>
        {
            Tiles(paper, font, "csg_surf_smooth", 2,
                new Tile(EditorIcons.Circle, Loc.Get("csg.smooth"), Loc.Get("csg.smooth_tip"), CSGSurfaceSelection.Smooth, data.SmoothingGroup != 0),
                new Tile(EditorIcons.Square, Loc.Get("csg.unsmooth"), Loc.Get("csg.unsmooth_tip"), CSGSurfaceSelection.UnSmooth, data.SmoothingGroup == 0));
            Field(paper, font, "csg_surf_group", Loc.Get("csg.smoothing_group"), () =>
                Number(paper, "csg_surf_group_v", data.SmoothingGroup, v => CSGSurfaceSelection.SetSmoothingGroup(v)));

            Switch(paper, font, "csg_surf_lock", Loc.Get("csg.texture_lock"), !data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture),
                v => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.WorldSpaceTexture, !v), Loc.Get("csg.texture_lock_tip"));
            Switch(paper, font, "csg_surf_render", Loc.Get("csg.render"), !data.Flags.HasFlag(CSGSurfaceFlags.NoRender),
                v => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.NoRender, !v));
            Switch(paper, font, "csg_surf_collide", Loc.Get("csg.collide"), !data.Flags.HasFlag(CSGSurfaceFlags.NoCollision),
                v => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.NoCollision, !v));
        });
    }

    /// <summary>The materials to paint with as chips, a click puts one on every selected surface.</summary>
    private static void DrawPalette(Paper paper, Scribe.FontFile font, Material? current)
    {
        List<Material> palette = CSGSurfaceSelection.Palette();
        if (palette.Count == 0) return;

        using (paper.Row("csg_palette").Height(UnitValue.Auto).WrapContent().Gap(4).Enter())
        {
            for (int i = 0; i < palette.Count; i++)
            {
                Material material = palette[i];
                bool on = ReferenceEquals(material, current);
                paper.Box($"csg_palette_{i}").Width(UnitValue.Auto).Height(24).Padding(8, 8, 0, 0).Rounded(12)
                    .BackgroundColor(on ? EditorTheme.Accent : TileColor)
                    .Hovered.BackgroundColor(on ? EditorTheme.AccentBright : EditorTheme.Hover).End()
                    .Text(material.Name, font).TextColor(on ? SColor.White : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(Loc.Get("csg.palette_tip"))
                    .OnClick(material, (m, _) => CSGSurfaceSelection.SetMaterial(m));
            }
        }
    }

    /// <summary>A label with U, V and both buttons beside it, which is how most texture operations come.</summary>
    private static void AxisRow(Paper paper, Scribe.FontFile font, string id, string label, string tooltip, Action<bool, bool> apply)
    {
        using (paper.Row(id).Height(28).Gap(4).Enter())
        {
            paper.Box($"{id}_l").Height(28).IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            string[] axes = ["U", "V", "UV"];
            for (int i = 0; i < axes.Length; i++)
            {
                int index = i;
                paper.Box($"{id}_{i}").Width(44).Height(28).Rounded(6)
                    .BackgroundColor(TileColor)
                    .Hovered.BackgroundColor(EditorTheme.Hover).End()
                    .Text(axes[i], font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(label, tooltip)
                    .OnClick(0, (_, _) => apply(index != 1, index != 0));
            }
        }
    }

    /// <summary>The nine way justify as a three by three grid of arrows.</summary>
    private static void DrawJustifyGrid(Paper paper, Scribe.FontFile font)
    {
        SurfaceAlign[] order = [SurfaceAlign.Min, SurfaceAlign.Center, SurfaceAlign.Max];
        string[] glyphs =
        [
            EditorIcons.ArrowUp, EditorIcons.ArrowUp, EditorIcons.ArrowUp,
            EditorIcons.ArrowLeft, EditorIcons.Crosshairs, EditorIcons.ArrowRight,
            EditorIcons.ArrowDown, EditorIcons.ArrowDown, EditorIcons.ArrowDown,
        ];

        using (paper.Column("csg_just_grid").Width(96).Height(UnitValue.Auto).Gap(3).Enter())
        {
            for (int row = 0; row < 3; row++)
            {
                using (paper.Row($"csg_just_r{row}").Height(28).Gap(3).Enter())
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int r = row, c = col;
                        paper.Box($"csg_just_{r}{c}").Width(30).Height(28).Rounded(6)
                            .BackgroundColor(TileColor)
                            .Hovered.BackgroundColor(EditorTheme.Hover).End()
                            .Text(glyphs[r * 3 + c], font).TextColor(EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSizeSmall - 2f).Alignment(TextAlignment.MiddleCenter)
                            .Tooltip(Loc.Get("csg.justify"), Loc.Get("csg.justify_tip"))
                            .OnClick(0, (_, _) => CSGSurfaceSelection.Justify(order[c], order[r]));
                    }
                }
            }
        }
    }

    internal static void DrawSelectionSection(Paper paper, Scribe.FontFile font, CSGObjectTool? tool)
    {
        List<CSGBrush> brushes = CSGSelection.Brushes();
        List<CSGGroup> groups = CSGSelection.Groups();

        if (brushes.Count == 0 && groups.Count == 0)
        {
            Card(paper, font, "csg_sel", EditorIcons.ArrowPointer, Loc.Get("csg.selection"), () =>
                Hint(paper, font, "csg_sel_hint", Loc.Get("csg.select_hint")));
            return;
        }

        string count = (brushes.Count + groups.Count).ToString();
        Card(paper, font, "csg_sel", EditorIcons.ArrowPointer, Loc.Get("csg.selection"), () =>
        {
            Tiles(paper, font, "csg_sel_op", 3, OperationTiles(OperationIndex(brushes, groups), CSGSelection.SetOperation));

            // One brush can be sized and placed by number, which reshapes it rather than scaling its transform
            if (brushes.Count == 1 && groups.Count == 0 && CSGSelection.TryBox(brushes[0], out Float3 centre, out Float3 size))
            {
                CSGBrush brush = brushes[0];
                Field(paper, font, "csg_sel_size", Loc.Get("csg.size"), () =>
                    Origami.Float3Field(paper, "csg_sel_size_v", size, v => CSGSelection.Resize(brush, v)).Show());
                Field(paper, font, "csg_sel_pos", Loc.Get("csg.position"), () =>
                    Origami.Float3Field(paper, "csg_sel_pos_v", centre, v => CSGSelection.MoveCentre(brush, v)).Show());
            }

            Actions(paper, font, "csg_sel_acts", 2,
                new Tile(EditorIcons.Clone, Loc.Get("csg.duplicate"), ShortcutManager.GetTooltip(Loc.Get("csg.duplicate_tip"), "Scene/Duplicate"), CSGSelection.Duplicate),
                new Tile(EditorIcons.ObjectGroup, Loc.Get("csg.group"), ShortcutManager.GetTooltip(Loc.Get("csg.group_selection_tip"), "CSG/GroupSelection"), CSGMenuItems.GroupSelection),
                new Tile(EditorIcons.AnglesUp, Loc.Get("csg.earlier"), ShortcutManager.GetTooltip(Loc.Get("csg.earlier_tip"), "CSG/Earlier"), () => CSGSelection.MoveInOrder(-1)),
                new Tile(EditorIcons.AnglesDown, Loc.Get("csg.later"), ShortcutManager.GetTooltip(Loc.Get("csg.later_tip"), "CSG/Later"), () => CSGSelection.MoveInOrder(1)),
                new Tile(EditorIcons.Crosshairs, Loc.Get("csg.centre_pivot"), Loc.Get("csg.centre_pivot_tip"), CSGSelection.CentrePivot),
                new Tile(EditorIcons.EyeSlash, Loc.Get("csg.hide"), ShortcutManager.GetTooltip(Loc.Get("csg.hide_selected_tip"), "CSG/Hide"), () => CSGVisibility.Hide(CSGSelection.Brushes())));
        }, badge: count);

        Card(paper, font, "csg_xf", EditorIcons.ArrowsUpDownLeftRight, Loc.Get("csg.transform"), () =>
        {
            AxisButtons(paper, font, "csg_sel_flip", Loc.Get("csg.flip"), Loc.Get("csg.flip_tip"), ["X", "Y", "Z"],
                i => CSGSelection.Flip(AxisFor(i)));
            AxisButtons(paper, font, "csg_sel_rot", Loc.Get("csg.rotate"), Loc.Get("csg.rotate_tip"), ["X-", "X+", "Y-", "Y+", "Z-", "Z+"], i =>
            {
                Float3 axis = AxisFor(i / 2) * (i % 2 == 0 ? -SceneTools.RotateSnap : SceneTools.RotateSnap);
                CSGSelection.RotateBy(axis, CSGSelection.SelectionCentre(), clone: false);
            });
        });

        Card(paper, font, "csg_arr", EditorIcons.CubesStacked, Loc.Get("csg.array"), () =>
        {
            Field(paper, font, "csg_sel_step", Loc.Get("csg.move_by"), () =>
                Origami.Float3Field(paper, "csg_sel_step_v", CSGSelection.ArrayOffset, v => CSGSelection.ArrayOffset = v).Show());
            Field(paper, font, "csg_sel_count", Loc.Get("csg.copies"), () =>
                Number(paper, "csg_sel_count_v", CSGSelection.ArrayCount, v => CSGSelection.ArrayCount = Math.Clamp(v, 1, 256)));
            Actions(paper, font, "csg_sel_array", 2,
                new Tile(EditorIcons.ArrowsUpDownLeftRight, Loc.Get("csg.move"), Loc.Get("csg.move_tip"), () => CSGSelection.MoveBy(CSGSelection.ArrayOffset, clone: false)),
                new Tile(EditorIcons.Clone, Loc.Get("csg.clone_array"), ShortcutManager.GetTooltip(Loc.Get("csg.clone_array_tip"), "CSG/RepeatClone"), CSGSelection.CloneArray));
        }, foldable: true);
    }

    /// <summary>A label with a row of small buttons beside it, one per axis.</summary>
    private static void AxisButtons(Paper paper, Scribe.FontFile font, string id, string label, string tooltip, string[] axes, Action<int> run)
    {
        Label(paper, font, $"{id}_l", label);
        using (paper.Row(id).Height(28).Gap(4).Enter())
        {
            for (int i = 0; i < axes.Length; i++)
            {
                int index = i;
                paper.Box($"{id}_{i}").Height(28).Rounded(6)
                    .BackgroundColor(TileColor)
                    .Hovered.BackgroundColor(EditorTheme.Hover).End()
                    .Text(axes[i], font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(label, tooltip)
                    .OnClick(0, (_, _) => run(index));
            }
        }
    }

    /// <summary>The size and settings a generated shape was made with, changing them builds it again.</summary>
    internal static void DrawShapeSection(Paper paper, Scribe.FontFile font)
    {
        List<GameObject> selected = Selection.GetSelected<GameObject>().ToList();
        if (selected.Count != 1) return;
        CSGShape shape = selected[0].GetComponent<CSGShape>();
        if (shape.IsNotValid()) return;

        Card(paper, font, "csg_shape", ShapeIcon(shape.Kind), Loc.Get("csg.shape_settings", new { kind = ShapeName(shape.Kind) }), () =>
        {
            if (shape.Kind == CSGGeneratorKind.Polygon)
            {
                Field(paper, font, "csg_shape_height", Loc.Get("csg.height"), () =>
                    Number(paper, "csg_shape_height_v", shape.Height, v => Rebuild(shape, () => shape.Height = v)));
                return;
            }

            Field(paper, font, "csg_shape_size", Loc.Get("csg.size"), () =>
                Origami.Float3Field(paper, "csg_shape_size_v", shape.Size, v => Rebuild(shape, () => shape.Size = Maths.Max(v, new Float3(0.01f)))).Show());
            DrawGeneratorSettings(paper, font, "csg_shape", shape.Kind, shape.Settings, v => Rebuild(shape, () => shape.Settings = v));
        });
    }

    private static void Rebuild(CSGShape shape, Action change)
    {
        Undo.RecordObject(shape, "Edit CSG Shape");
        change();
        CSGSelection.Regenerate(shape, "Edit CSG Shape");
    }

    internal static void DrawVisibilitySection(Paper paper, Scribe.FontFile font, CSGObjectTool? tool)
    {
        if (tool == null || CSGVisibility.HiddenCount == 0) return;

        Card(paper, font, "csg_vis", EditorIcons.EyeSlash, Loc.Get("csg.visibility"), () =>
        {
            Hint(paper, font, "csg_vis_hint", Loc.Get("csg.visibility_hint"));
            Actions(paper, font, "csg_vis_show", 1,
                new Tile(EditorIcons.Eye, Loc.Get("csg.unhide_all", new { count = CSGVisibility.HiddenCount }),
                    ShortcutManager.GetTooltip(Loc.Get("csg.unhide_all_tip"), "CSG/UnhideAll"), CSGVisibility.UnhideAll));
        });
    }

    internal static string OperationLabel(Operation operation) => operation switch
    {
        Operation.Subtractive => "csg.subtractive",
        Operation.Intersect => "csg.intersect",
        _ => "csg.additive",
    };

    /// <summary>
    /// A rounded card with an icon and title along the top. A foldable card starts folded and opens when its title is
    /// clicked. The badge is a small count beside the title.
    /// </summary>
    private static void Card(Paper paper, Scribe.FontFile font, string id, string icon, string title, Action body, bool foldable = false, string? badge = null)
    {
        bool open = !foldable || _opened.Contains(id);

        using (paper.Column(id).Height(UnitValue.Auto).Margin(8, 8, 0, 8).Rounded(8).Clip()
                   .BackgroundColor(CardColor).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            ElementBuilder header = paper.Row($"{id}_h").Height(32).Padding(10, 10, 0, 0).Gap(8);
            if (foldable)
            {
                header.Hovered.BackgroundColor(EditorTheme.Hover).End()
                    .OnClick(id, (key, _) => { if (!_opened.Remove(key)) _opened.Add(key); });
            }

            using (header.Enter())
            {
                paper.Box($"{id}_hi").Width(18).Height(32).IsNotInteractable()
                    .Text(icon, font).TextColor(EditorTheme.AccentText)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                paper.Box($"{id}_ht").Height(32).IsNotInteractable()
                    .Text(title, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                if (badge != null)
                {
                    paper.Box($"{id}_hb").Width(UnitValue.Auto).Height(20).Padding(7, 7, 0, 0).Rounded(10)
                        .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                        .BackgroundColor(TileColor)
                        .Text(badge, font).TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall - 2f).Alignment(TextAlignment.MiddleCenter);
                }

                if (foldable)
                {
                    paper.Box($"{id}_hc").Width(14).Height(32).IsNotInteractable()
                        .Text(open ? EditorIcons.ChevronDown : EditorIcons.ChevronRight, font).TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall - 4f).Alignment(TextAlignment.MiddleCenter);
                }
            }

            if (!open) return;

            paper.Box($"{id}_d").Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
            using (paper.Column($"{id}_b").Height(UnitValue.Auto).Padding(10, 10, 10, 10).Gap(8).Enter())
                body();
        }
    }

    /// <summary>Tiles with the icon over the label, for picking one of a few. Lit tiles are the current choice.</summary>
    internal static void Tiles(Paper paper, Scribe.FontFile font, string id, int columns, params Tile[] tiles)
    {
        Grid(paper, id, columns, tiles.Length, 52, i =>
        {
            Tile tile = tiles[i];
            using (TileBox(paper, $"{id}_{i}", tile, 52).Enter())
            {
                paper.Box($"{id}_{i}_i").Height(26).Margin(0, 0, 6, 0).IsNotInteractable()
                    .Text(tile.Icon, font).TextColor(tile.On ? SColor.White : EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
                paper.Box($"{id}_{i}_t").Height(18).IsNotInteractable()
                    .Text(tile.Label, font).TextColor(tile.On ? SColor.White : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall - 1f).Alignment(TextAlignment.MiddleCenter);
            }
        });
    }

    /// <summary>Buttons with the icon beside the label, for commands.</summary>
    internal static void Actions(Paper paper, Scribe.FontFile font, string id, int columns, params Tile[] tiles)
    {
        Grid(paper, id, columns, tiles.Length, 30, i =>
        {
            Tile tile = tiles[i];
            using (TileBox(paper, $"{id}_{i}", tile, 30, row: true).Padding(10, 8, 0, 0).Gap(8).Enter())
            {
                paper.Box($"{id}_{i}_i").Width(16).Height(30).IsNotInteractable()
                    .Text(tile.Icon, font).TextColor(EditorTheme.AccentText)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                paper.Box($"{id}_{i}_t").Height(30).IsNotInteractable()
                    .Text(tile.Label, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            }
        });
    }

    private static ElementBuilder TileBox(Paper paper, string id, Tile tile, float height, bool row = false)
        => (row ? paper.Row(id) : paper.Column(id)).Height(height).Rounded(6)
            .BackgroundColor(tile.On ? EditorTheme.Accent : TileColor)
            .Hovered.BackgroundColor(tile.On ? EditorTheme.AccentBright : EditorTheme.Hover).End()
            .Tooltip(tile.Label, tile.Tip)
            .OnClick(tile.Run, (run, _) => run());

    /// <summary>Lays out <paramref name="count"/> cells in rows of equal width, padding the last row so cells line up.</summary>
    private static void Grid(Paper paper, string id, int columns, int count, float height, Action<int> cell)
    {
        using (paper.Column(id).Height(UnitValue.Auto).Gap(4).Enter())
        {
            for (int start = 0; start < count; start += columns)
            {
                using (paper.Row($"{id}_r{start}").Height(height).Gap(4).Enter())
                {
                    for (int i = start; i < start + columns; i++)
                    {
                        if (i < count) cell(i);
                        else paper.Box($"{id}_pad{i}").Height(height).IsNotInteractable();
                    }
                }
            }
        }
    }

    /// <summary>A small label over a full width control.</summary>
    private static void Field(Paper paper, Scribe.FontFile font, string id, string label, System.Action control)
    {
        using (paper.Column(id).Height(UnitValue.Auto).Gap(4).Enter())
        {
            Label(paper, font, $"{id}_l", label);
            using (paper.Row($"{id}_c").Height(UnitValue.Auto).MinHeight(EditorTheme.RowHeight).Enter())
                control();
        }
    }

    /// <summary>Two labelled controls side by side.</summary>
    private static void Pair(Paper paper, Scribe.FontFile font, string id, (string Label, System.Action Control) left, (string Label, System.Action Control) right)
    {
        using (paper.Row(id).Height(UnitValue.Auto).Gap(8).Enter())
        {
            Field(paper, font, $"{id}_a", left.Label, left.Control);
            Field(paper, font, $"{id}_b", right.Label, right.Control);
        }
    }

    private static void Label(Paper paper, Scribe.FontFile font, string id, string text)
    {
        paper.Box(id).Height(16).IsNotInteractable()
            .Text(text, font).TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSizeSmall - 1f).Alignment(TextAlignment.MiddleLeft);
    }

    /// <summary>A label with a switch at the far end of the row.</summary>
    private static void Switch(Paper paper, Scribe.FontFile font, string id, string label, bool value, Action<bool> set, string? tooltip = null)
    {
        using (paper.Row(id).Height(EditorTheme.RowHeight).Gap(8).Enter())
        {
            ElementBuilder text = paper.Box($"{id}_l").Height(EditorTheme.RowHeight)
                .Text(label, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            if (tooltip != null) text.Tooltip(label, tooltip);

            using (paper.Row($"{id}_w").Width(UnitValue.Auto).Height(EditorTheme.RowHeight).Enter())
                Origami.Switch(paper, $"{id}_v", value, set).NoLabel().Show();
        }
    }

    private static void Number<T>(Paper paper, string id, T value, Action<T> set) where T : struct, System.Numerics.INumber<T>
        => Origami.NumericField(paper, id, value, set).Show();

    private static void Hint(Paper paper, Scribe.FontFile font, string id, string text, bool warn = false)
    {
        paper.Box(id).Height(UnitValue.Auto).IsNotInteractable()
            .Text(text, font).TextColor(warn ? WarnText : EditorTheme.Ink300).Wrap(Scribe.TextWrapMode.Wrap)
            .FontSize(EditorTheme.FontSizeSmall - 1f).Alignment(TextAlignment.Left);
    }

    private static readonly SColor WarnText = SColor.FromArgb(255, 255, 170, 90);

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
