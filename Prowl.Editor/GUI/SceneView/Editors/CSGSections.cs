// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Modeling;
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

    /// <summary>One column of the panel. The tip shows on its title. An empty title leaves the title out.</summary>
    internal sealed record Column(string Id, string Title, string? Tip, Action<Paper, Scribe.FontFile> Body, float Width = ColumnWidth);

    internal const float ColumnWidth = 250f;
    private const float NarrowColumnWidth = 180f;
    private const float IconColumnWidth = 110f;
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

    /// <summary>The options of the tool in use.</summary>
    internal static List<Column> Columns()
    {
        var columns = new List<Column>();
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return columns;

        if (CSGEditMode.Is(CSGMode.Select)) AddSelect(columns);
        else if (CSGEditMode.Is(CSGMode.Create)) AddGenerate(columns, SceneToolManager.Get<CSGGenerateTool>());
        else if (CSGEditMode.Is(CSGMode.Edit) && SceneToolManager.Get<CSGMeshTool>() is { Active: true } mesh) AddEdit(columns, mesh);
        else if (CSGEditMode.IsUV) AddSurface(columns);
        else if (SceneToolManager.Get<CSGClipTool>() is { Active: true } clip) AddClip(columns, clip);
        return columns;
    }

    /// <summary>The width the columns take side by side.</summary>
    internal static float Width(List<Column> columns)
    {
        float width = 0f;
        foreach (Column column in columns) width += column.Width;
        return width;
    }

    /// <summary>Lays the columns out side by side, starting a new row when the next one would pass <paramref name="width"/>.</summary>
    internal static void DrawColumns(Paper paper, Scribe.FontFile font, string id, List<Column> columns, float width)
    {
        int start = 0;
        while (start < columns.Count)
        {
            int end = start + 1;
            float used = columns[start].Width;
            while (end < columns.Count && used + columns[end].Width <= width) used += columns[end++].Width;

            using (paper.Row($"{id}_r{start}").Width(UnitValue.Auto).Height(UnitValue.Auto).Enter())
            {
                for (int i = start; i < end; i++)
                {
                    Column column = columns[i];
                    using (paper.Column(column.Id).Width(column.Width).Height(UnitValue.Auto).Padding(10, 10, 6, 8).Gap(4).Enter())
                    {
                        if (column.Title.Length > 0)
                        {
                            ElementBuilder title = paper.Box($"{column.Id}_t").Height(18)
                                .Text(column.Title.ToUpperInvariant(), EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.AccentText)
                                .FontSize(Small - 1f).Alignment(TextAlignment.MiddleCenter);
                            if (column.Tip != null) title.Tooltip(column.Title, column.Tip);
                            else title.IsNotInteractable();
                        }

                        column.Body(paper, font);
                    }
                }
            }
            start = end;
        }
    }

    private static void AddSelect(List<Column> columns)
    {
        columns.Add(new Column("csg_sel", "", null, (paper, font) =>
            Actions(paper, font, "csg_sel_pivot", 1,
                new Tile(EditorIcons.Crosshairs, Loc.Get("csg.centre_pivot"), Loc.Get("csg.centre_pivot_tip"), CSGSelection.CentrePivot)),
            NarrowColumnWidth));
    }

    private static void AddEdit(List<Column> columns, CSGMeshTool tool)
    {
        columns.Add(new Column("csg_edit", "", null, (paper, font) =>
        {
            Segments(paper, font, "csg_edit_mode",
            [
                new Tile(EditorIcons.CircleDot, Loc.Get("csg.mode_vertex"), Loc.Get("csg.mode_vertex_tip"), () => tool.SetMode(SelectMode.Vertex), tool.Mode == SelectMode.Vertex),
                new Tile(EditorIcons.Slash, Loc.Get("csg.mode_edge"), Loc.Get("csg.mode_edge_tip"), () => tool.SetMode(SelectMode.Edge), tool.Mode == SelectMode.Edge),
                new Tile(EditorIcons.Square, Loc.Get("csg.mode_face"), Loc.Get("csg.mode_face_tip"), () => tool.SetMode(SelectMode.Face), tool.Mode == SelectMode.Face),
            ], iconsOnly: true);
            Chips(paper, font, "csg_edit_toggles", (Loc.Get("csg.xray"), tool.XRay, Loc.Get("csg.xray_tip"), () => tool.XRay = !tool.XRay));
        }, NarrowColumnWidth));
    }

    private static void AddGenerate(List<Column> columns, CSGGenerateTool? tool)
    {
        if (tool == null) return;

        string hint = tool.Generator == CSGGeneratorKind.Polygon ? Loc.Get("csg.polygon_hint") : Loc.Get("csg.draw_active");
        columns.Add(new Column("csg_gen", Loc.Get("csg.shape"), hint, (paper, font) =>
        {
            IconGrid(paper, font, "csg_gen_shape", 2, ShapeTiles(tool));
            Note(paper, font, "csg_gen_name", ShapeName(tool.Generator), centred: true);
            Heading(paper, font, "csg_gen_op_t", Loc.Get("csg.operation"));
            IconGrid(paper, font, "csg_gen_op", 2,
            [
                new Tile(EditorIcons.WandMagicSparkles, Loc.Get("csg.auto"), Loc.Get("csg.auto_tip"), () => tool.AutoOperation = true, tool.AutoOperation),
                .. OperationTiles(tool.AutoOperation ? -1 : (int)tool.Operation, op => tool.Operation = op),
            ]);
            if (tool.Generator != CSGGeneratorKind.Polygon)
                Chips(paper, font, "csg_gen_place",
                    (Loc.Get("csg.from_centre"), tool.FromCentre, Loc.Get("csg.from_centre_tip"), () => tool.FromCentre = !tool.FromCentre),
                    (Loc.Get("csg.square_base"), tool.SquareBase, Loc.Get("csg.square_base_tip"), () => tool.SquareBase = !tool.SquareBase));
        }, IconColumnWidth));

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

    private static void AddClip(List<Column> columns, CSGClipTool tool)
    {
        columns.Add(new Column("csg_clip", Loc.Get("csg.cut_with_plane"), Loc.Get("csg.clip_help"), (paper, font) =>
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
                    new Tile(EditorIcons.Xmark, Loc.Get("csg.clip_cancel"), Loc.Get("csg.clip_cancel_tip"), tool.End));
            }
        }));
    }

    /// <summary>The selected surfaces' material, mapping and flags, for the UV tools.</summary>
    private static void AddSurface(List<Column> columns)
    {
        if (CSGSurfaceSelection.Active is not CSGSurfaceRef active || !active.IsValid)
        {
            columns.Add(new Column("csg_surf", Loc.Get("csg.surfaces_title"), Loc.Get("csg.surface_pick_hint"), (paper, font) =>
                Note(paper, font, "csg_surf_none", Loc.Get("csg.no_surfaces"))));
            return;
        }

        SurfaceData data = active.Data;
        string count = Loc.Get("csg.surfaces_selected", new { count = CSGSurfaceSelection.Count });

        columns.Add(new Column("csg_surf_mat", $"{Loc.Get("csg.material")}  {count}", Loc.Get("csg.surface_paint_hint"), (paper, font) =>
        {
            PropertyGridUtils.DrawField(paper, "csg_surf_mat_v", "", typeof(Material),
                data.Material, v => CSGSurfaceSelection.SetMaterial(v as Material), 0);
            DrawPalette(paper, font, data.Material);
        }));

        columns.Add(new Column("csg_surf_map", Loc.Get("csg.mapping"), null, (paper, font) =>
        {
            Line(paper, font, "csg_surf_offset", Loc.Get("csg.uv_offset"), () => Reset(paper, font, "csg_surf_offset_r",
                () => Origami.Float2Field(paper, "csg_surf_offset_v", data.Offset, v => CSGSurfaceSelection.SetOffset(v)).Show(),
                () => CSGSurfaceSelection.SetOffset(Float2.Zero)));
            Line(paper, font, "csg_surf_rot", Loc.Get("csg.uv_rotation"), () => Reset(paper, font, "csg_surf_rot_r",
                () => Number(paper, "csg_surf_rot_v", data.Rotation, v => CSGSurfaceSelection.SetRotation(v)),
                () => CSGSurfaceSelection.SetRotation(0f)));
            Line(paper, font, "csg_surf_scale", Loc.Get("csg.uv_scale"), () => Reset(paper, font, "csg_surf_scale_r",
                () => Origami.Float2Field(paper, "csg_surf_scale_v", data.Scale, v => CSGSurfaceSelection.SetScale(v)).Show(),
                () => CSGSurfaceSelection.SetScale(Float2.One)));
            AxisLine(paper, font, "csg_surf_fit", Loc.Get("csg.fit"), Loc.Get("csg.fit_tip"), CSGSurfaceSelection.Fit);
            Actions(paper, font, "csg_surf_acts", 2,
                new Tile(EditorIcons.AlignCenter, Loc.Get("csg.align"), Loc.Get("csg.align_tip"), CSGSurfaceSelection.AlignToActive),
                new Tile(EditorIcons.ArrowsRotate, Loc.Get("csg.reset_all"), Loc.Get("csg.reset_all_tip"), () =>
                {
                    CSGSurfaceSelection.SetOffset(Float2.Zero);
                    CSGSurfaceSelection.SetRotation(0f);
                    CSGSurfaceSelection.SetScale(Float2.One);
                }));
        }));

        columns.Add(new Column("csg_surf_flags", Loc.Get("csg.shading_flags"), null, (paper, font) =>
        {
            Segments(paper, font, "csg_surf_smooth",
                new Tile(EditorIcons.Circle, Loc.Get("csg.smooth"), Loc.Get("csg.smooth_tip"), CSGSurfaceSelection.Smooth, data.SmoothingGroup != 0),
                new Tile(EditorIcons.Square, Loc.Get("csg.unsmooth"), Loc.Get("csg.unsmooth_tip"), CSGSurfaceSelection.UnSmooth, data.SmoothingGroup == 0));
            Chips(paper, font, "csg_surf_flag_v",
                (Loc.Get("csg.texture_lock"), !data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture), Loc.Get("csg.texture_lock_tip"),
                    () => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.WorldSpaceTexture, !data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture))),
                (Loc.Get("csg.render"), !data.Flags.HasFlag(CSGSurfaceFlags.NoRender), null,
                    () => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.NoRender, !data.Flags.HasFlag(CSGSurfaceFlags.NoRender))),
                (Loc.Get("csg.collide"), !data.Flags.HasFlag(CSGSurfaceFlags.NoCollision), null,
                    () => CSGSurfaceSelection.SetFlag(CSGSurfaceFlags.NoCollision, !data.Flags.HasFlag(CSGSurfaceFlags.NoCollision))));
        }));
    }

    /// <summary>A control with a small reset button after it.</summary>
    private static void Reset(Paper paper, Scribe.FontFile font, string id, System.Action control, System.Action reset)
    {
        using (paper.Row($"{id}_c").Height(UnitValue.Auto).MinHeight(RowHeight).Enter())
            control();
        paper.Box(id).Width(22).Height(RowHeight).Rounded(5).Margin(4, 0, 0, 0)
            .BackgroundColor(TileColor)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.ArrowsRotate, font).TextColor(EditorTheme.Ink400)
            .FontSize(Small - 3f).Alignment(TextAlignment.MiddleCenter)
            .Tooltip(Loc.Get("csg.reset"))
            .OnClick(reset, (run, _) => run());
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

    internal static string OperationLabel(Operation operation) => operation switch
    {
        Operation.Subtractive => "csg.subtractive",
        Operation.Intersect => "csg.intersect",
        _ => "csg.additive",
    };

    /// <summary>A short label with a control filling the rest of the row.</summary>
    internal static void Line(Paper paper, Scribe.FontFile font, string id, string label, System.Action control, float labelWidth = LabelWidth, string? tooltip = null)
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

    /// <summary>Icon buttons in a grid, named by their tooltips.</summary>
    private static void IconGrid(Paper paper, Scribe.FontFile font, string id, int columns, Tile[] tiles)
    {
        using (paper.Column(id).Height(UnitValue.Auto).Gap(3).Enter())
        {
            for (int start = 0; start < tiles.Length; start += columns)
            {
                var row = new Tile[columns];
                int count = Math.Min(columns, tiles.Length - start);
                Array.Copy(tiles, start, row, 0, count);
                if (count == columns) Segments(paper, font, $"{id}_r{start}", row, iconsOnly: true);
                else
                {
                    using (paper.Row($"{id}_r{start}").Height(RowHeight).Gap(3).Enter())
                    {
                        Segments(paper, font, $"{id}_r{start}_s", row[..count], iconsOnly: true);
                        for (int i = count; i < columns; i++) paper.Box($"{id}_pad{start + i}").Height(RowHeight).IsNotInteractable();
                    }
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

    private static void Note(Paper paper, Scribe.FontFile font, string id, string text, bool warn = false, bool centred = false)
    {
        paper.Box(id).Height(UnitValue.Auto).IsNotInteractable()
            .Text(text, font).TextColor(warn ? WarnText : EditorTheme.Ink300).Wrap(Scribe.TextWrapMode.Wrap)
            .FontSize(Small).Alignment(centred ? TextAlignment.MiddleCenter : TextAlignment.Left);
    }

    /// <summary>A small centred title inside a column, for a group of its controls.</summary>
    private static void Heading(Paper paper, Scribe.FontFile font, string id, string text)
    {
        paper.Box(id).Height(16).Margin(0, 0, 4, 0).IsNotInteractable()
            .Text(text.ToUpperInvariant(), EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.AccentText)
            .FontSize(Small - 1f).Alignment(TextAlignment.MiddleCenter);
    }

    private static Float3 AxisFor(int index) => index switch
    {
        1 => Float3.UnitY,
        2 => Float3.UnitZ,
        _ => Float3.UnitX,
    };

    /// <summary>The shared operation of the selection, or -1 when they disagree.</summary>
    internal static int OperationIndex(List<CSGBrush> brushes, List<CSGGroup> groups)
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
