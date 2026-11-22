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

/// <summary>The CSG editor's controls as compact columns, shared by the scene view overlay and the docked panel.</summary>
internal static class CSGSections
{
    /// <summary>A button: an icon, a short label, and what it does. Lit while <see cref="On"/>.</summary>
    internal readonly record struct Tile(string Icon, string Label, string Tip, System.Action Run, bool On = false);

    /// <summary>One column of the panel. The tip shows on its title.</summary>
    internal sealed record Column(string Id, string Title, string? Tip, Action<Paper, Scribe.FontFile> Body);

    internal const float ColumnWidth = 250f;
    private const float RowHeight = 22f;
    private const float LabelWidth = 60f;
    private const float WideLabelWidth = 100f;

    private static readonly SColor TileColor = SColor.FromArgb(28, 255, 255, 255);
    private static readonly SColor WarnText = SColor.FromArgb(255, 255, 170, 90);

    private static float Small => EditorTheme.FontSizeSmall - 1f;

    internal static void EmptyState(Paper paper, Scribe.FontFile font, float height)
    {
        using (paper.Column("csg_empty").Height(height).Padding(12, 12, 12, 12).Gap(6).Enter())
        {
            paper.Box("csg_empty_t").Height(22)
                .Text(Loc.Get("csg.no_model"), font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);

            Note(paper, font, "csg_empty_d", Loc.Get("csg.no_model_hint"));
            Actions(paper, font, "csg_empty_new", 1,
                new Tile(EditorIcons.CubesStacked, Loc.Get("csg.create_model"), Loc.Get("csg.create_model_tip"), () => CSGSelection.CreateModel()));
        }
    }

    /// <summary>The columns the current mode shows.</summary>
    internal static List<Column> Columns()
    {
        var columns = new List<Column>();
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return columns;

        switch (CSGEditMode.Current)
        {
            case CSGMode.Generate:
                AddGenerate(columns, SceneToolManager.Get<CSGGenerateTool>());
                break;
            case CSGMode.Surface:
                AddSurface(columns);
                break;
            case CSGMode.Clip:
                AddClip(columns, SceneToolManager.Get<CSGClipTool>());
                break;
            case CSGMode.Mesh:
                AddMesh(columns, SceneToolManager.Get<CSGMeshTool>());
                break;
            default:
                AddSelection(columns);
                AddShape(columns);
                break;
        }
        return columns;
    }

    /// <summary>Lays the columns out in rows that fit <paramref name="width"/>, each column an equal share.</summary>
    internal static void DrawColumns(Paper paper, Scribe.FontFile font, string id, List<Column> columns, float width)
    {
        int perRow = Math.Max(1, Math.Min(columns.Count, (int)(width / ColumnWidth)));
        for (int start = 0; start < columns.Count; start += perRow)
        {
            using (paper.Row($"{id}_r{start}").Height(UnitValue.Auto).Enter())
            {
                for (int i = start; i < start + perRow; i++)
                {
                    if (i >= columns.Count)
                    {
                        paper.Box($"{id}_pad{i}").Height(1).IsNotInteractable();
                        continue;
                    }

                    Column column = columns[i];
                    using (paper.Column(column.Id).Height(UnitValue.Auto).Padding(10, 10, 6, 8).Gap(4).Enter())
                    {
                        ElementBuilder title = paper.Box($"{column.Id}_t").Height(18)
                            .Text(column.Title.ToUpperInvariant(), EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.AccentText)
                            .FontSize(Small - 1f).Alignment(TextAlignment.MiddleLeft);
                        if (column.Tip != null) title.Tooltip(column.Title, column.Tip);
                        else title.IsNotInteractable();

                        column.Body(paper, font);
                    }
                }
            }
        }
    }

    private static void AddSelection(List<Column> columns)
    {
        List<CSGBrush> brushes = CSGSelection.Brushes();
        List<CSGGroup> groups = CSGSelection.Groups();

        if (brushes.Count == 0 && groups.Count == 0)
        {
            columns.Add(new Column("csg_sel", Loc.Get("csg.selection"), null, (paper, font) =>
            {
                Note(paper, font, "csg_sel_hint", Loc.Get("csg.select_hint"));
                if (CSGVisibility.HiddenCount > 0) Actions(paper, font, "csg_sel_show", 1, UnhideTile());
            }));
            return;
        }

        columns.Add(new Column("csg_sel", $"{Loc.Get("csg.selection")}  {brushes.Count + groups.Count}", null, (paper, font) =>
        {
            Segments(paper, font, "csg_sel_op", OperationTiles(OperationIndex(brushes, groups), CSGSelection.SetOperation));

            // One brush can be sized and placed by number, which reshapes it rather than scaling its transform
            if (brushes.Count == 1 && groups.Count == 0 && CSGSelection.TryBox(brushes[0], out Float3 centre, out Float3 size))
            {
                CSGBrush brush = brushes[0];
                Line(paper, font, "csg_sel_size", Loc.Get("csg.size"), () =>
                    Origami.Float3Field(paper, "csg_sel_size_v", size, v => CSGSelection.Resize(brush, v)).Show());
                Line(paper, font, "csg_sel_pos", Loc.Get("csg.position"), () =>
                    Origami.Float3Field(paper, "csg_sel_pos_v", centre, v => CSGSelection.MoveCentre(brush, v)).Show());
            }

            var actions = new List<Tile>
            {
                new(EditorIcons.Clone, Loc.Get("csg.duplicate"), ShortcutManager.GetTooltip(Loc.Get("csg.duplicate_tip"), "Scene/Duplicate"), CSGSelection.Duplicate),
                new(EditorIcons.ObjectGroup, Loc.Get("csg.group"), ShortcutManager.GetTooltip(Loc.Get("csg.group_selection_tip"), "CSG/GroupSelection"), CSGMenuItems.GroupSelection),
                new(EditorIcons.AnglesUp, Loc.Get("csg.earlier"), ShortcutManager.GetTooltip(Loc.Get("csg.earlier_tip"), "CSG/Earlier"), () => CSGSelection.MoveInOrder(-1)),
                new(EditorIcons.AnglesDown, Loc.Get("csg.later"), ShortcutManager.GetTooltip(Loc.Get("csg.later_tip"), "CSG/Later"), () => CSGSelection.MoveInOrder(1)),
                new(EditorIcons.Crosshairs, Loc.Get("csg.centre_pivot"), Loc.Get("csg.centre_pivot_tip"), CSGSelection.CentrePivot),
                new(EditorIcons.EyeSlash, Loc.Get("csg.hide"), ShortcutManager.GetTooltip(Loc.Get("csg.hide_selected_tip"), "CSG/Hide"), () => CSGVisibility.Hide(CSGSelection.Brushes())),
            };
            if (CSGVisibility.HiddenCount > 0) actions.Add(UnhideTile());
            Actions(paper, font, "csg_sel_acts", 2, [.. actions]);
        }));

        columns.Add(new Column("csg_xf", Loc.Get("csg.transform"), null, (paper, font) =>
        {
            Line(paper, font, "csg_sel_flip", Loc.Get("csg.flip"), () =>
                SmallButtons(paper, font, "csg_sel_flip_v", Loc.Get("csg.flip_tip"), ["X", "Y", "Z"], i => CSGSelection.Flip(AxisFor(i))));
            Line(paper, font, "csg_sel_rot", Loc.Get("csg.rotate"), () =>
                SmallButtons(paper, font, "csg_sel_rot_v", Loc.Get("csg.rotate_tip"), ["X-", "X+", "Y-", "Y+", "Z-", "Z+"], i =>
                {
                    Float3 axis = AxisFor(i / 2) * (i % 2 == 0 ? -SceneTools.RotateSnap : SceneTools.RotateSnap);
                    CSGSelection.RotateBy(axis, CSGSelection.SelectionCentre(), clone: false);
                }));
            Line(paper, font, "csg_sel_step", Loc.Get("csg.move_by"), () =>
                Origami.Float3Field(paper, "csg_sel_step_v", CSGSelection.ArrayOffset, v => CSGSelection.ArrayOffset = v).Show());
            Line(paper, font, "csg_sel_count", Loc.Get("csg.copies"), () =>
                Number(paper, "csg_sel_count_v", CSGSelection.ArrayCount, v => CSGSelection.ArrayCount = Math.Clamp(v, 1, 256)));
            Actions(paper, font, "csg_sel_array", 2,
                new Tile(EditorIcons.ArrowsUpDownLeftRight, Loc.Get("csg.move"), Loc.Get("csg.move_tip"), () => CSGSelection.MoveBy(CSGSelection.ArrayOffset, clone: false)),
                new Tile(EditorIcons.Clone, Loc.Get("csg.clone_array"), ShortcutManager.GetTooltip(Loc.Get("csg.clone_array_tip"), "CSG/RepeatClone"), CSGSelection.CloneArray));
        }));
    }

    private static Tile UnhideTile() => new(EditorIcons.Eye, Loc.Get("csg.unhide_all", new { count = CSGVisibility.HiddenCount }),
        ShortcutManager.GetTooltip(Loc.Get("csg.unhide_all_tip"), "CSG/UnhideAll"), CSGVisibility.UnhideAll);

    /// <summary>The size and settings a generated shape was made with, changing them builds it again.</summary>
    private static void AddShape(List<Column> columns)
    {
        List<GameObject> selected = Selection.GetSelected<GameObject>().ToList();
        if (selected.Count != 1) return;
        CSGShape shape = selected[0].GetComponent<CSGShape>();
        if (shape.IsNotValid()) return;

        columns.Add(new Column("csg_shape", Loc.Get("csg.shape_settings", new { kind = ShapeName(shape.Kind) }), null, (paper, font) =>
        {
            if (shape.Kind == CSGGeneratorKind.Polygon)
            {
                Line(paper, font, "csg_shape_height", Loc.Get("csg.height"), () =>
                    Number(paper, "csg_shape_height_v", shape.Height, v => Rebuild(shape, () => shape.Height = v)));
                return;
            }

            Line(paper, font, "csg_shape_size", Loc.Get("csg.size"), () =>
                Origami.Float3Field(paper, "csg_shape_size_v", shape.Size, v => Rebuild(shape, () => shape.Size = Maths.Max(v, new Float3(0.01f)))).Show());
            DrawGeneratorSettings(paper, font, "csg_shape", shape.Kind, shape.Settings, v => Rebuild(shape, () => shape.Settings = v));
        }));
    }

    private static void Rebuild(CSGShape shape, Action change)
    {
        Undo.RecordObject(shape, "Edit CSG Shape");
        change();
        CSGSelection.Regenerate(shape, "Edit CSG Shape");
    }

    private static void AddGenerate(List<Column> columns, CSGGenerateTool? tool)
    {
        if (tool == null) return;

        string hint = tool.Generator == CSGGeneratorKind.Polygon ? Loc.Get("csg.polygon_hint") : Loc.Get("csg.draw_active");
        columns.Add(new Column("csg_gen", Loc.Get("csg.shape"), hint, (paper, font) =>
        {
            Segments(paper, font, "csg_gen_shape", ShapeTiles(tool), iconsOnly: true);
            Note(paper, font, "csg_gen_name", ShapeName(tool.Generator));
            Segments(paper, font, "csg_gen_op",
            [
                new Tile(EditorIcons.WandMagicSparkles, Loc.Get("csg.auto"), Loc.Get("csg.auto_tip"), () => tool.AutoOperation = true, tool.AutoOperation),
                .. OperationTiles(tool.AutoOperation ? -1 : (int)tool.Operation, op => tool.Operation = op),
            ]);
        }));

        if (HasSettings(tool.Generator))
        {
            columns.Add(new Column("csg_gen_set", ShapeName(tool.Generator), null, (paper, font) =>
                DrawGeneratorSettings(paper, font, "csg_gen", tool.Generator, tool.Settings, v => tool.Settings = v)));
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
        void Set(SettingsEdit edit) => Update(settings, apply, edit);

        switch (kind)
        {
            case CSGGeneratorKind.Cylinder:
                Wide(paper, font, $"{prefix}_sides", Loc.Get("csg.sides"), () => Number(paper, $"{prefix}_sides_v", settings.Sides, v => Set((ref CSGGeneratorSettings s) => s.Sides = v)));
                Wide(paper, font, $"{prefix}_angle", Loc.Get("csg.offset_angle"), () => Number(paper, $"{prefix}_angle_v", settings.OffsetAngle, v => Set((ref CSGGeneratorSettings s) => s.OffsetAngle = v)));
                Chips(paper, font, $"{prefix}_flags",
                    (Loc.Get("csg.radius_to_corner"), settings.RadiusToCorner, null, () => Set((ref CSGGeneratorSettings s) => s.RadiusToCorner = !s.RadiusToCorner)),
                    (Loc.Get("csg.recentre"), settings.Recentre, null, () => Set((ref CSGGeneratorSettings s) => s.Recentre = !s.Recentre)),
                    (Loc.Get("csg.smooth_shading"), settings.SmoothShading, null, () => Set((ref CSGGeneratorSettings s) => s.SmoothShading = !s.SmoothShading)));
                break;

            case CSGGeneratorKind.Sphere:
                Wide(paper, font, $"{prefix}_seg", Loc.Get("csg.segments"), () => Number(paper, $"{prefix}_seg_v", settings.Sides, v => Set((ref CSGGeneratorSettings s) => s.Sides = v)));
                Wide(paper, font, $"{prefix}_rings", Loc.Get("csg.rings"), () => Number(paper, $"{prefix}_rings_v", settings.Rings, v => Set((ref CSGGeneratorSettings s) => s.Rings = v)));
                Chips(paper, font, $"{prefix}_flags",
                    (Loc.Get("csg.hemisphere"), settings.Hemisphere, null, () => Set((ref CSGGeneratorSettings s) => s.Hemisphere = !s.Hemisphere)),
                    (Loc.Get("csg.smooth_shading"), settings.SmoothShading, null, () => Set((ref CSGGeneratorSettings s) => s.SmoothShading = !s.SmoothShading)));
                break;

            case CSGGeneratorKind.LinearStairs:
                Wide(paper, font, $"{prefix}_total", Loc.Get("csg.steps"), () => Number(paper, $"{prefix}_total_v", settings.TotalSteps, v => Set((ref CSGGeneratorSettings s) => s.TotalSteps = v)), Loc.Get("csg.total_steps"));
                Wide(paper, font, $"{prefix}_sh", Loc.Get("csg.step_height"), () => Number(paper, $"{prefix}_sh_v", settings.StepHeight, v => Set((ref CSGGeneratorSettings s) => s.StepHeight = v)));
                Wide(paper, font, $"{prefix}_sd", Loc.Get("csg.step_depth"), () => Number(paper, $"{prefix}_sd_v", settings.StepDepth, v => Set((ref CSGGeneratorSettings s) => s.StepDepth = v)));
                Wide(paper, font, $"{prefix}_ed", Loc.Get("csg.extra_depth"), () => Number(paper, $"{prefix}_ed_v", settings.ExtraDepth, v => Set((ref CSGGeneratorSettings s) => s.ExtraDepth = v)));
                Wide(paper, font, $"{prefix}_eh", Loc.Get("csg.extra_height"), () => Number(paper, $"{prefix}_eh_v", settings.ExtraHeight, v => Set((ref CSGGeneratorSettings s) => s.ExtraHeight = v)));
                Chips(paper, font, $"{prefix}_flags",
                    (Loc.Get("csg.filled_stairs"), settings.FilledStairs, null, () => Set((ref CSGGeneratorSettings s) => s.FilledStairs = !s.FilledStairs)));
                break;

            case CSGGeneratorKind.Room:
                Wide(paper, font, $"{prefix}_wall", Loc.Get("csg.wall_thickness"), () => Number(paper, $"{prefix}_wall_v", settings.WallThickness,
                    v => Set((ref CSGGeneratorSettings s) => s.WallThickness = MathF.Max(0.01f, v))));
                break;

            case CSGGeneratorKind.SpiralStairs:
                Wide(paper, font, $"{prefix}_ssh", Loc.Get("csg.step_height"), () => Number(paper, $"{prefix}_ssh_v", settings.StepHeight, v => Set((ref CSGGeneratorSettings s) => s.StepHeight = v)));
                Wide(paper, font, $"{prefix}_spr", Loc.Get("csg.steps_per_turn"), () => Number(paper, $"{prefix}_spr_v", settings.StepsPerRevolution, v => Set((ref CSGGeneratorSettings s) => s.StepsPerRevolution = v)));
                Wide(paper, font, $"{prefix}_inner", Loc.Get("csg.inner_radius"), () => Number(paper, $"{prefix}_inner_v", settings.InnerRadius, v => Set((ref CSGGeneratorSettings s) => s.InnerRadius = v)));
                break;
        }
    }

    private delegate void SettingsEdit(ref CSGGeneratorSettings settings);

    private static void Update(CSGGeneratorSettings settings, Action<CSGGeneratorSettings> apply, SettingsEdit edit)
    {
        edit(ref settings);
        apply(settings);
    }

    private static void AddMesh(List<Column> columns, CSGMeshTool? tool)
    {
        if (tool == null) return;

        if (tool.Target.IsNotValid())
        {
            columns.Add(new Column("csg_mesh", Loc.Get("csg.mesh"), null, (paper, font) =>
                Note(paper, font, "csg_mesh_none", Loc.Get("csg.mesh_one_brush"))));
            return;
        }

        columns.Add(new Column("csg_mesh_pick", Loc.Get("csg.mesh_select"), Loc.Get("csg.mesh_help"), (paper, font) =>
        {
            Segments(paper, font, "csg_mesh_filter",
                FilterTile(tool, EditorIcons.CircleDot, "csg.vertex", CSGElementFilter.Vertex),
                FilterTile(tool, EditorIcons.Slash, "csg.edge", CSGElementFilter.Edge),
                FilterTile(tool, EditorIcons.SquareFull, "csg.face", CSGElementFilter.Face));

            Note(paper, font, "csg_mesh_count", Loc.Get("csg.mesh_selected", new { count = tool.SelectedCount, faces = tool.SelectedFaceCount }));
            if (tool.Filter == CSGElementFilter.None)
                Note(paper, font, "csg_mesh_warn", Loc.Get("csg.mesh_nothing_selectable"), warn: true);
            if (tool.NonPlanar)
                Note(paper, font, "csg_mesh_planar", Loc.Get("csg.mesh_non_planar"), warn: true);
        }));

        columns.Add(new Column("csg_mesh_edit", Loc.Get("csg.edit"), null, (paper, font) =>
        {
            Line(paper, font, "csg_mesh_chamfer", Loc.Get("csg.chamfer"), () =>
                Number(paper, "csg_mesh_chamfer_v", tool.ChamferDistance, v => tool.ChamferDistance = MathF.Max(0f, v)));
            Line(paper, font, "csg_mesh_extrude", Loc.Get("csg.extrude"), () =>
                Number(paper, "csg_mesh_extrude_v", tool.ExtrudeDistance, v => tool.ExtrudeDistance = v));
            Actions(paper, font, "csg_mesh_actions", 2,
                new Tile(EditorIcons.BezierCurve, Loc.Get("csg.chamfer"), Loc.Get("csg.chamfer_tip"), tool.ChamferSelected),
                new Tile(EditorIcons.ArrowUpFromBracket, Loc.Get("csg.extrude"), Loc.Get("csg.extrude_tip"), tool.ExtrudeSelected),
                new Tile(EditorIcons.Magnet, Loc.Get("csg.snap"), ShortcutManager.GetTooltip(Loc.Get("csg.snap_tip"), "CSG/SnapToGrid"), tool.SnapSelectedToGrid),
                new Tile(EditorIcons.Trash, Loc.Get("csg.delete_face"), Loc.Get("csg.delete_face_tip"), tool.DeleteSelectedFaces));
        }));
    }

    private static Tile FilterTile(CSGMeshTool tool, string icon, string label, CSGElementFilter flag)
        => new(icon, Loc.Get(label), Loc.Get("csg.mesh_select_tip"),
            () => tool.Filter = tool.Filter.HasFlag(flag) ? tool.Filter & ~flag : tool.Filter | flag, tool.Filter.HasFlag(flag));

    private static void AddClip(List<Column> columns, CSGClipTool? tool)
    {
        if (tool == null) return;

        columns.Add(new Column("csg_clip", Loc.Get("csg.clip"), Loc.Get("csg.clip_help"), (paper, font) =>
        {
            int count = CSGSelection.Brushes().Count;
            Note(paper, font, "csg_clip_hint", count > 0 ? Loc.Get("csg.clip_selected", new { count }) : Loc.Get("csg.clip_all"));

            Segments(paper, font, "csg_clip_mode",
                new Tile(EditorIcons.ArrowDown, Loc.Get("csg.clip_front"), Loc.Get("csg.clip_front_tip"), () => tool.Mode = CSGClipMode.RemoveFront, tool.Mode == CSGClipMode.RemoveFront),
                new Tile(EditorIcons.ArrowUp, Loc.Get("csg.clip_back"), Loc.Get("csg.clip_back_tip"), () => tool.Mode = CSGClipMode.RemoveBack, tool.Mode == CSGClipMode.RemoveBack),
                new Tile(EditorIcons.ArrowsUpDown, Loc.Get("csg.clip_split"), Loc.Get("csg.clip_split_tip"), () => tool.Mode = CSGClipMode.Split, tool.Mode == CSGClipMode.Split));

            if (tool.HasPlane)
            {
                Actions(paper, font, "csg_clip_go", 2,
                    new Tile(EditorIcons.Check, Loc.Get("csg.clip_commit"), ShortcutManager.GetTooltip(Loc.Get("csg.clip_commit_tip"), "CSG/Confirm"), tool.Commit),
                    new Tile(EditorIcons.Xmark, Loc.Get("csg.clip_cancel"), Loc.Get("csg.clip_cancel_tip"), tool.Cancel));
            }
        }));
    }

    private static void AddSurface(List<Column> columns)
    {
        bool picked = CSGSurfaceSelection.Active is CSGSurfaceRef active && active.IsValid;

        columns.Add(new Column("csg_surf_pick", Loc.Get("csg.select"), Loc.Get("csg.surface_pick_hint"), (paper, font) =>
        {
            Actions(paper, font, "csg_surf_by", 2,
                new Tile(EditorIcons.ArrowDown, Loc.Get("csg.floors"), Loc.Get("csg.floors_tip"), CSGSurfaceSelection.SelectFloors),
                new Tile(EditorIcons.BorderAll, Loc.Get("csg.walls"), Loc.Get("csg.walls_tip"), CSGSurfaceSelection.SelectWalls),
                new Tile(EditorIcons.ArrowUp, Loc.Get("csg.ceilings"), Loc.Get("csg.ceilings_tip"), CSGSurfaceSelection.SelectCeilings),
                new Tile(EditorIcons.LayerGroup, Loc.Get("csg.coplanar"), Loc.Get("csg.coplanar_tip"), CSGSurfaceSelection.SelectCoplanar),
                new Tile(EditorIcons.Link, Loc.Get("csg.connected"), Loc.Get("csg.connected_tip"), CSGSurfaceSelection.SelectConnected),
                new Tile(EditorIcons.Cube, Loc.Get("csg.all_faces"), Loc.Get("csg.select_all_faces_tip"), CSGSurfaceSelection.SelectAllOfBrush),
                new Tile(EditorIcons.Palette, Loc.Get("csg.same_material"), Loc.Get("csg.select_same_material_tip"), CSGSurfaceSelection.SelectSameMaterial));

            if (!picked)
            {
                Note(paper, font, "csg_surf_hint", Loc.Get("csg.surface_pick_hint"));
                return;
            }

            CSGSurfaceRef current = CSGSurfaceSelection.Active!.Value;
            Actions(paper, font, "csg_surf_owner", 1,
                new Tile(EditorIcons.Cube, $"{Loc.Get("csg.surfaces_selected", new { count = CSGSurfaceSelection.Count })}  {current.Brush.GameObject.Name}",
                    Loc.Get("csg.surface_owner_tip"), () => Selection.Select(current.Brush.GameObject)));
        }));

        if (!picked) return;

        SurfaceData data = CSGSurfaceSelection.Active!.Value.Data;

        columns.Add(new Column("csg_surf_mat", Loc.Get("csg.material"), Loc.Get("csg.surface_paint_hint"), (paper, font) =>
        {
            PropertyGridUtils.DrawField(paper, "csg_surf_mat_v", "", typeof(Material),
                data.Material, v => CSGSurfaceSelection.SetMaterial(v as Material), 0);
            DrawPalette(paper, font, data.Material);
        }));

        columns.Add(new Column("csg_surf_map", Loc.Get("csg.mapping"), null, (paper, font) =>
        {
            Line(paper, font, "csg_surf_offset", Loc.Get("csg.uv_offset"), () =>
                Origami.Float2Field(paper, "csg_surf_offset_v", data.Offset, v => CSGSurfaceSelection.SetOffset(v)).Show());
            Line(paper, font, "csg_surf_scale", Loc.Get("csg.uv_scale"), () =>
                Origami.Float2Field(paper, "csg_surf_scale_v", data.Scale, v => CSGSurfaceSelection.SetScale(v)).Show());
            Line(paper, font, "csg_surf_rot", Loc.Get("csg.uv_rotation"), () =>
                Number(paper, "csg_surf_rot_v", data.Rotation, v => CSGSurfaceSelection.SetRotation(v)));
            Line(paper, font, "csg_surf_step", "", () =>
                SmallButtons(paper, font, "csg_surf_step_v", Loc.Get("csg.uv_step_tip"), ["-90", "+90", "/2", "x2"], i =>
                {
                    if (i < 2) CSGSurfaceSelection.RotateBy(i == 0 ? -90f : 90f);
                    else CSGSurfaceSelection.ScaleBy(i == 2 ? 0.5f : 2f);
                }));
        }));

        columns.Add(new Column("csg_surf_align", Loc.Get("csg.align_title"), null, (paper, font) =>
        {
            AxisLine(paper, font, "csg_surf_fit", Loc.Get("csg.fit"), Loc.Get("csg.fit_tip"), CSGSurfaceSelection.Fit);
            AxisLine(paper, font, "csg_surf_flip", Loc.Get("csg.reverse"), Loc.Get("csg.reverse_tip"), CSGSurfaceSelection.Flip);
            AxisLine(paper, font, "csg_surf_reset", Loc.Get("csg.reset"), Loc.Get("csg.reset_tip"), CSGSurfaceSelection.Reset);

            using (paper.Row("csg_surf_just_row").Height(UnitValue.Auto).Gap(6).Enter())
            {
                DrawJustifyGrid(paper, font);
                using (paper.Column("csg_surf_just_acts").Height(UnitValue.Auto).Enter())
                {
                    Actions(paper, font, "csg_surf_tools", 1,
                        new Tile(EditorIcons.AlignCenter, Loc.Get("csg.align"), Loc.Get("csg.align_tip"), CSGSurfaceSelection.AlignToActive),
                        new Tile(EditorIcons.Copy, Loc.Get("csg.copy_surface"), Loc.Get("csg.copy_surface_tip"), CSGSurfaceSelection.CopyFromActive));
                }
            }
        }));

        columns.Add(new Column("csg_surf_flags", Loc.Get("csg.shading_flags"), null, (paper, font) =>
        {
            Segments(paper, font, "csg_surf_smooth",
                new Tile(EditorIcons.Circle, Loc.Get("csg.smooth"), Loc.Get("csg.smooth_tip"), CSGSurfaceSelection.Smooth, data.SmoothingGroup != 0),
                new Tile(EditorIcons.Square, Loc.Get("csg.unsmooth"), Loc.Get("csg.unsmooth_tip"), CSGSurfaceSelection.UnSmooth, data.SmoothingGroup == 0));
            Line(paper, font, "csg_surf_group", Loc.Get("csg.smooth_group"), () =>
                Number(paper, "csg_surf_group_v", data.SmoothingGroup, v => CSGSurfaceSelection.SetSmoothingGroup(v)));
            Chips(paper, font, "csg_surf_flag_v",
                (Loc.Get("csg.texture_lock"), !data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture), Loc.Get("csg.texture_lock_tip"),
                    () => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.WorldSpaceTexture, !data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture))),
                (Loc.Get("csg.render"), !data.Flags.HasFlag(CSGSurfaceFlags.NoRender), null,
                    () => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.NoRender, !data.Flags.HasFlag(CSGSurfaceFlags.NoRender))),
                (Loc.Get("csg.collide"), !data.Flags.HasFlag(CSGSurfaceFlags.NoCollision), null,
                    () => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.NoCollision, !data.Flags.HasFlag(CSGSurfaceFlags.NoCollision))));
        }));
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
                paper.Box($"csg_palette_{i}").Width(UnitValue.Auto).Height(20).Padding(7, 7, 0, 0).Rounded(10)
                    .BackgroundColor(on ? EditorTheme.Accent : TileColor)
                    .Hovered.BackgroundColor(on ? EditorTheme.AccentBright : EditorTheme.Hover).End()
                    .Text(material.Name, font).TextColor(on ? SColor.White : EditorTheme.Ink400)
                    .FontSize(Small).Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(Loc.Get("csg.palette_tip"))
                    .OnClick(material, (m, _) => CSGSurfaceSelection.SetMaterial(m));
            }
        }
    }

    /// <summary>A label with U, V and both buttons beside it, which is how most texture operations come.</summary>
    private static void AxisLine(Paper paper, Scribe.FontFile font, string id, string label, string tooltip, Action<bool, bool> apply)
        => Line(paper, font, id, label, () =>
            SmallButtons(paper, font, $"{id}_v", tooltip, ["U", "V", "UV"], i => apply(i != 1, i != 0)));

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

        using (paper.Column("csg_just_grid").Width(72).Height(UnitValue.Auto).Gap(2).Enter())
        {
            for (int row = 0; row < 3; row++)
            {
                using (paper.Row($"csg_just_r{row}").Height(20).Gap(2).Enter())
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int r = row, c = col;
                        paper.Box($"csg_just_{r}{c}").Width(22).Height(20).Rounded(4)
                            .BackgroundColor(TileColor)
                            .Hovered.BackgroundColor(EditorTheme.Hover).End()
                            .Text(glyphs[r * 3 + c], font).TextColor(EditorTheme.Ink500)
                            .FontSize(Small - 3f).Alignment(TextAlignment.MiddleCenter)
                            .Tooltip(Loc.Get("csg.justify"), Loc.Get("csg.justify_tip"))
                            .OnClick(0, (_, _) => CSGSurfaceSelection.Justify(order[c], order[r]));
                    }
                }
            }
        }
    }

    internal static string OperationLabel(Operation operation) => operation switch
    {
        Operation.Subtractive => "csg.subtractive",
        Operation.Intersect => "csg.intersect",
        _ => "csg.additive",
    };

    /// <summary>A short label with a control filling the rest of the row.</summary>
    private static void Line(Paper paper, Scribe.FontFile font, string id, string label, System.Action control, float labelWidth = LabelWidth, string? tooltip = null)
    {
        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(RowHeight).Gap(6).Enter())
        {
            ElementBuilder text = paper.Box($"{id}_l").Width(labelWidth).Height(RowHeight)
                .Text(label, font).TextColor(EditorTheme.Ink300)
                .FontSize(Small).Alignment(TextAlignment.MiddleLeft);
            if (tooltip != null) text.Tooltip(label, tooltip);
            else text.IsNotInteractable();

            using (paper.Row($"{id}_c").Height(UnitValue.Auto).MinHeight(RowHeight).Enter())
                control();
        }
    }

    private static void Wide(Paper paper, Scribe.FontFile font, string id, string label, System.Action control, string? tooltip = null)
        => Line(paper, font, id, label, control, WideLabelWidth, tooltip);

    /// <summary>A row of equal buttons for picking one of a few, the lit ones are current.</summary>
    internal static void Segments(Paper paper, Scribe.FontFile font, string id, params Tile[] tiles)
        => Segments(paper, font, id, tiles, iconsOnly: false);

    private static void Segments(Paper paper, Scribe.FontFile font, string id, Tile[] tiles, bool iconsOnly)
    {
        using (paper.Row(id).Height(RowHeight).Gap(3).Enter())
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                Tile tile = tiles[i];
                SColor ink = tile.On ? SColor.White : EditorTheme.Ink400;
                using (Button(paper, $"{id}_{i}", tile).Enter())
                {
                    paper.Box($"{id}_{i}_i").Width(UnitValue.Auto).Height(RowHeight)
                        .Margin(UnitValue.StretchOne, iconsOnly ? UnitValue.StretchOne : 5, 0, 0).IsNotInteractable()
                        .Text(tile.Icon, font).TextColor(tile.On ? SColor.White : EditorTheme.AccentText)
                        .FontSize(Small - 1f).Alignment(TextAlignment.MiddleCenter);
                    if (iconsOnly) continue;
                    paper.Box($"{id}_{i}_t").Width(UnitValue.Auto).Height(RowHeight)
                        .Margin(0, UnitValue.StretchOne, 0, 0).IsNotInteractable()
                        .Text(tile.Label, font).TextColor(ink)
                        .FontSize(Small).Alignment(TextAlignment.MiddleCenter);
                }
            }
        }
    }

    /// <summary>Command buttons with the icon beside the label.</summary>
    internal static void Actions(Paper paper, Scribe.FontFile font, string id, int columns, params Tile[] tiles)
    {
        using (paper.Column(id).Height(UnitValue.Auto).Gap(3).Enter())
        {
            for (int start = 0; start < tiles.Length; start += columns)
            {
                using (paper.Row($"{id}_r{start}").Height(RowHeight).Gap(3).Enter())
                {
                    for (int i = start; i < start + columns; i++)
                    {
                        if (i >= tiles.Length)
                        {
                            paper.Box($"{id}_pad{i}").Height(RowHeight).IsNotInteractable();
                            continue;
                        }

                        Tile tile = tiles[i];
                        using (Button(paper, $"{id}_{i}", tile).Padding(7, 6, 0, 0).Gap(6).Enter())
                        {
                            paper.Box($"{id}_{i}_i").Width(14).Height(RowHeight).IsNotInteractable()
                                .Text(tile.Icon, font).TextColor(EditorTheme.AccentText)
                                .FontSize(Small - 1f).Alignment(TextAlignment.MiddleCenter);
                            paper.Box($"{id}_{i}_t").Height(RowHeight).IsNotInteractable()
                                .Text(tile.Label, font).TextColor(EditorTheme.Ink500)
                                .FontSize(Small).Alignment(TextAlignment.MiddleLeft);
                        }
                    }
                }
            }
        }
    }

    private static ElementBuilder Button(Paper paper, string id, Tile tile)
        => paper.Row(id).Height(RowHeight).Rounded(5)
            .BackgroundColor(tile.On ? EditorTheme.Accent : TileColor)
            .Hovered.BackgroundColor(tile.On ? EditorTheme.AccentBright : EditorTheme.Hover).End()
            .Tooltip(tile.Label, tile.Tip)
            .OnClick(tile.Run, (run, _) => run());

    /// <summary>Small text buttons sharing a row, such as one per axis.</summary>
    private static void SmallButtons(Paper paper, Scribe.FontFile font, string id, string tooltip, string[] labels, Action<int> run)
    {
        using (paper.Row(id).Height(RowHeight).Gap(3).Enter())
        {
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                paper.Box($"{id}_{i}").Height(RowHeight).Rounded(5)
                    .BackgroundColor(TileColor)
                    .Hovered.BackgroundColor(EditorTheme.Hover).End()
                    .Text(labels[i], font).TextColor(EditorTheme.Ink500)
                    .FontSize(Small).Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(labels[i], tooltip)
                    .OnClick(0, (_, _) => run(index));
            }
        }
    }

    /// <summary>Toggles as small pills that wrap onto more rows when they run out of room.</summary>
    private static void Chips(Paper paper, Scribe.FontFile font, string id, params (string Label, bool On, string? Tip, System.Action Toggle)[] chips)
    {
        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(RowHeight).WrapContent().Gap(4).Enter())
        {
            for (int i = 0; i < chips.Length; i++)
            {
                (string label, bool on, string? tip, System.Action toggle) = chips[i];
                ElementBuilder chip = paper.Box($"{id}_{i}").Width(UnitValue.Auto).Height(20).Padding(8, 8, 0, 0).Rounded(10)
                    .Margin(0, 0, 1, 1)
                    .BackgroundColor(on ? EditorTheme.Accent : TileColor)
                    .Hovered.BackgroundColor(on ? EditorTheme.AccentBright : EditorTheme.Hover).End()
                    .Text(label, font).TextColor(on ? SColor.White : EditorTheme.Ink400)
                    .FontSize(Small).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(toggle, (t, _) => t());
                if (tip != null) chip.Tooltip(label, tip);
            }
        }
    }

    private static void Number<T>(Paper paper, string id, T value, Action<T> set) where T : struct, System.Numerics.INumber<T>
        => Origami.NumericField(paper, id, value, set).Show();

    private static void Note(Paper paper, Scribe.FontFile font, string id, string text, bool warn = false)
    {
        paper.Box(id).Height(UnitValue.Auto).IsNotInteractable()
            .Text(text, font).TextColor(warn ? WarnText : EditorTheme.Ink300).Wrap(Scribe.TextWrapMode.Wrap)
            .FontSize(Small).Alignment(TextAlignment.Left);
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
}
