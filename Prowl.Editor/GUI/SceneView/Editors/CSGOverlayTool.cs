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
using Prowl.Vector;

using Operation = Prowl.Vector.Geometry.CSGOperation;
using SColor = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>The CSG editor's bar inside the scene view: the tools, the selection's operation, and the options of the tool in use.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGOverlayTool : SceneTool
{
    public override string Name => "CSG";
    public override string Icon => EditorIcons.Cube;
    public override int Order => -1;

    private static readonly (CSGMode mode, string label, string tip, string shortcut, string icon)[] Modes =
    {
        (CSGMode.Select, "csg.mode_select", "csg.mode_select_tip", "CSG/ModeSelect", EditorIcons.ArrowPointer),
        (CSGMode.Create, "csg.mode_create", "csg.mode_create_tip", "CSG/ModeCreate", EditorIcons.SquarePlus),
        (CSGMode.Edit, "csg.mode_edit", "csg.mode_edit_tip", "CSG/ModeEdit", EditorIcons.DrawPolygon),
        (CSGMode.UVMove, "csg.mode_uv_move", "csg.mode_uv_move_tip", "CSG/ModeUVMove", EditorIcons.ArrowsUpDownLeftRight),
        (CSGMode.UVRotate, "csg.mode_uv_rotate", "csg.mode_uv_rotate_tip", "CSG/ModeUVRotate", EditorIcons.RotateRight),
        (CSGMode.UVScale, "csg.mode_uv_scale", "csg.mode_uv_scale_tip", "CSG/ModeUVScale", EditorIcons.Expand),
        (CSGMode.Pivot, "csg.mode_pivot", "csg.mode_pivot_tip", "CSG/ModePivot", EditorIcons.Bullseye),
    };

    private static readonly EditorGlyphIcon[] ModeIcons = Array.ConvertAll(Modes, m => new EditorGlyphIcon(m.icon));

    private static readonly (CSGGeneratorKind Kind, string Shortcut)[] ShapeKeys =
    {
        (CSGGeneratorKind.Box, "CSG/ShapeBox"),
        (CSGGeneratorKind.Cylinder, "CSG/ShapeCylinder"),
        (CSGGeneratorKind.Sphere, "CSG/ShapeSphere"),
        (CSGGeneratorKind.LinearStairs, "CSG/ShapeLinearStairs"),
        (CSGGeneratorKind.SpiralStairs, "CSG/ShapeSpiralStairs"),
        (CSGGeneratorKind.Room, "CSG/ShapeRoom"),
        (CSGGeneratorKind.Polygon, "CSG/ShapePolygon"),
    };


    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override void OnToolStripGUI(SceneToolContext ctx, Paper paper, string id)
    {
        EditorGUI.ToolStripGroup(paper, $"{id}_modes", () =>
        {
            for (int i = 0; i < Modes.Length; i++)
            {
                (CSGMode mode, string label, _, string shortcut, _) = Modes[i];
                EditorGUI.ToolStripButton(paper, $"{id}_{mode}", ModeIcons[i], ShortcutManager.GetTooltip(Loc.Get(label), shortcut),
                    CSGEditMode.Is(mode), () => CSGEditMode.Current = mode);
            }
        }, () => DrawOptions(ctx, paper, $"{id}_options"));
    }

    public override void OnSceneInput(SceneToolContext ctx)
    {
        CSGModel? model = CSGSelection.ActiveModel();

        // A modal operation owns every key. Edit keeps only the tool and grid keys, the rest are its own
        if (ctx.Handles.Modal)
        {
            if (model.IsValid()) CSGHelperSurfaces.Draw(ctx, model!);
            return;
        }
        bool edit = CSGEditMode.Is(CSGMode.Edit);

        if (!ctx.Handles.KeyboardCaptured)
        {
            foreach ((CSGMode mode, _, _, string shortcut, _) in Modes)
                if (ctx.Handles.IsPressed(shortcut)) CSGEditMode.Current = mode;

            if (!edit)
                foreach ((CSGGeneratorKind kind, string shortcut) in ShapeKeys)
                    if (ctx.Handles.IsPressed(shortcut)) StartShape(kind);

            // The mesh tool tapers a selected face on the same keys
            if (SceneToolManager.Get<CSGMeshTool>() is not { TapersFace: true })
            {
                if (ctx.Handles.IsPressed("CSG/HalveGrid")) StepGrid(-1);
                else if (ctx.Handles.IsPressed("CSG/DoubleGrid")) StepGrid(1);
            }
        }

        if (!edit) HandleShortcuts(ctx.Handles);

        if (model.IsValid()) CSGHelperSurfaces.Draw(ctx, model!);
    }

    /// <summary>Switches to Create with a shape, unless one is being drawn.</summary>
    private static void StartShape(CSGGeneratorKind kind)
    {
        if (SceneToolManager.Get<CSGGenerateTool>() is not { } create || create.Busy) return;
        create.Generator = kind;
        CSGEditMode.Current = CSGMode.Create;
    }

    /// <summary>While a model is being worked on, the scene view's right click menu is the CSG one.</summary>
    public override bool ReplacesSceneContextMenu => true;

    /// <summary>In Select and Edit the brush under the cursor is selected, the other tools keep what they have.</summary>
    public override bool OnSceneContextPick(SceneToolContext ctx, Ray ray)
    {
        CSGModel? model = CSGSelection.ActiveModel();
        if (!(CSGEditMode.Is(CSGMode.Select) || CSGEditMode.Is(CSGMode.Edit)) || model.IsNotValid()) return true;

        if (CSGSelection.PickBrush(model!, CSGSelection.BrushesIn(model!), ray, out CSGBrush brush, out _)
            && !Selection.IsSelected(brush.GameObject))
            Selection.Select(brush.GameObject);
        return true;
    }

    public override void OnSceneContextMenu(SceneToolContext ctx, ContextBuilder menu)
    {
        menu.Title($"{Loc.Get("csg.title")}  {Loc.Get(ModeLabel(CSGEditMode.Current))}");

        if (CSGEditMode.Is(CSGMode.Create)) ShapeCommands(menu);
        else if (CSGEditMode.IsUV) SurfaceCommands(menu);
        else
        {
            if (SceneToolManager.Get<CSGMeshTool>() is { Active: true } mesh) MeshCommands(menu, mesh);
            SelectionCommands(menu);
        }

        menu.Submenu(Loc.Get("csg.tools"), sub =>
        {
            foreach ((CSGMode mode, string label, _, string shortcut, _) in Modes)
                sub.Item(Loc.Get(label), () => CSGEditMode.Current = mode, shortcut: Keys(shortcut), on: CSGEditMode.Is(mode));
        });
        menu.Separator();
    }

    private static string Keys(string shortcut)
        => ShortcutManager.Find(shortcut) is { } def ? ShortcutManager.GetDisplayString(def.Binding) : "";

    private static void Command(ContextBuilder menu, string label, System.Action run, string shortcut = "")
        => menu.Item(Loc.Get(label), run, shortcut: shortcut.Length > 0 ? Keys(shortcut) : "");

    private static void ShapeCommands(ContextBuilder menu)
    {
        if (SceneToolManager.Get<CSGGenerateTool>() is not { } create) return;
        foreach ((CSGGeneratorKind kind, string shortcut) in ShapeKeys)
            menu.Item(CSGSections.ShapeName(kind), () => create.Generator = kind, shortcut: Keys(shortcut), on: create.Generator == kind);
        menu.Separator();
    }

    private static void SurfaceCommands(ContextBuilder menu)
    {
        Command(menu, "csg.select_floors", CSGSurfaceSelection.SelectFloors);
        Command(menu, "csg.select_walls", CSGSurfaceSelection.SelectWalls);
        Command(menu, "csg.select_ceilings", CSGSurfaceSelection.SelectCeilings);
        Command(menu, "csg.select_coplanar", CSGSurfaceSelection.SelectCoplanar);
        Command(menu, "csg.select_connected", CSGSurfaceSelection.SelectConnected);
        Command(menu, "csg.select_all_faces", CSGSurfaceSelection.SelectAllOfBrush);
        Command(menu, "csg.select_same_material", CSGSurfaceSelection.SelectSameMaterial);
        menu.Separator();
        Command(menu, "csg.align", CSGSurfaceSelection.AlignToActive);
        Command(menu, "csg.copy_surface", CSGSurfaceSelection.CopyFromActive);
        menu.Separator();
    }

    private static void MeshCommands(ContextBuilder menu, CSGMeshTool mesh)
    {
        bool any = false;
        if (mesh.CanChamfer) { Command(menu, "csg.chamfer", mesh.ChamferSelected); any = true; }
        if (mesh.CanExtrude) { Command(menu, "csg.extrude", mesh.ExtrudeSelected); any = true; }
        if (mesh.CanInset) { Command(menu, "csg.inset", mesh.InsetSelected); any = true; }
        if (mesh.CanSnap) { Command(menu, "csg.snap", mesh.SnapSelectedToGrid, "CSG/SnapToGrid"); any = true; }
        if (mesh.CanDeleteFaces) { Command(menu, "csg.delete_face", mesh.DeleteSelectedFaces); any = true; }
        if (any) menu.Separator();
    }

    private static void SelectionCommands(ContextBuilder menu)
    {
        if (SceneToolManager.Get<CSGClipTool>() is { } clip) Command(menu, "csg.cut_with_plane", clip.Begin);
        menu.Separator();
        Command(menu, "csg.duplicate", CSGSelection.Duplicate, "Scene/Duplicate");
        Command(menu, "csg.repeat_clone", CSGSelection.RepeatClone, "CSG/RepeatClone");
        Command(menu, "csg.group_selection", CSGMenuItems.GroupSelection, "CSG/GroupSelection");
        Command(menu, "csg.earlier", () => CSGSelection.MoveInOrder(-1), "CSG/Earlier");
        Command(menu, "csg.later", () => CSGSelection.MoveInOrder(1), "CSG/Later");
        menu.Separator();
        menu.Submenu(Loc.Get("csg.flip"), sub =>
        {
            Command(sub, "csg.flip_x", () => CSGSelection.Flip(Float3.UnitX), "CSG/FlipX");
            Command(sub, "csg.flip_y", () => CSGSelection.Flip(Float3.UnitY), "CSG/FlipY");
            Command(sub, "csg.flip_z", () => CSGSelection.Flip(Float3.UnitZ), "CSG/FlipZ");
        });
        Command(menu, "csg.rotate_left", () => RotateBySnap(-1f), "CSG/RotateLeft");
        Command(menu, "csg.rotate_right", () => RotateBySnap(1f), "CSG/RotateRight");
        Command(menu, "csg.snap_to_grid", CSGSelection.SnapToGrid, "CSG/SnapToGrid");
        Command(menu, "csg.centre_pivot", CSGSelection.CentrePivot);
        menu.Separator();
        Command(menu, "csg.hide_selected", () => CSGVisibility.Hide(CSGSelection.Brushes()), "CSG/Hide");
        menu.Item(Loc.Get("csg.unhide_all", new { count = CSGVisibility.HiddenCount }), CSGVisibility.UnhideAll, shortcut: Keys("CSG/UnhideAll"));
        menu.Separator();
    }

    /// <summary>Selection shortcuts, in every mode but Create, which only sets up the next brush. The mesh tool keeps rotate and snap for its own points.</summary>
    private static void HandleShortcuts(HandleContext handles)
    {
        if (handles.KeyboardCaptured) return;
        bool mesh = SceneToolManager.Get<CSGMeshTool>() is { Active: true };

        // Modified bindings are tested first so plain Hide does not swallow Ctrl+H and Shift+H
        if (handles.IsPressed("CSG/HideUnselected")) { CSGVisibility.HideUnselected(); return; }
        if (handles.IsPressed("CSG/ToggleVisibility")) { CSGVisibility.ToggleAll(); return; }
        if (handles.IsPressed("CSG/Hide")) { CSGVisibility.Hide(CSGSelection.Brushes()); return; }
        if (handles.IsPressed("CSG/UnhideAll")) { CSGVisibility.UnhideAll(); return; }

        if (handles.IsPressed("CSG/SetAdditive")) { SetOperation(Operation.Additive); return; }
        if (handles.IsPressed("CSG/SetSubtractive")) { SetOperation(Operation.Subtractive); return; }
        if (handles.IsPressed("CSG/SetIntersecting")) { SetOperation(Operation.Intersect); return; }
        if (CSGEditMode.Is(CSGMode.Create)) return;

        if (handles.IsPressed("CSG/SetPassThrough")) { CSGSelection.SetPassThrough(); return; }

        if (handles.IsPressed("Scene/Duplicate")) { CSGSelection.Duplicate(); return; }
        if (handles.IsPressed("CSG/RepeatClone")) { CSGSelection.RepeatClone(); return; }
        if (handles.IsPressed("CSG/GroupSelection")) { CSGMenuItems.GroupSelection(); return; }
        if (handles.IsPressed("CSG/Earlier")) { CSGSelection.MoveInOrder(-1); return; }
        if (handles.IsPressed("CSG/Later")) { CSGSelection.MoveInOrder(1); return; }

        if (handles.IsPressed("CSG/FlipX")) { CSGSelection.Flip(Float3.UnitX); return; }
        if (handles.IsPressed("CSG/FlipY")) { CSGSelection.Flip(Float3.UnitY); return; }
        if (handles.IsPressed("CSG/FlipZ")) { CSGSelection.Flip(Float3.UnitZ); return; }

        if (mesh) return;

        if (handles.IsPressed("CSG/SnapToGrid")) { CSGSelection.SnapToGrid(); return; }
        if (handles.IsPressed("CSG/RotateLeft")) { RotateBySnap(-1f); return; }
        if (handles.IsPressed("CSG/RotateRight")) { RotateBySnap(1f); return; }

        HandleNudge(handles);
    }

    private static void StepGrid(int direction)
    {
        SceneTools.StepMoveSnap(direction);
        CSGStatus.Flash(Loc.Get("csg.status_grid", new { size = SceneTools.MoveSnap.ToString(SceneTools.CurrentMoveSnapFormat) }));
    }

    /// <summary>Sets the selection's operation, or in Create the operation the next brush gets.</summary>
    private static void SetOperation(Operation operation)
    {
        if (CSGEditMode.Is(CSGMode.Create))
        {
            if (SceneToolManager.Get<CSGGenerateTool>() is { } generate) generate.Operation = operation;
            return;
        }
        CSGSelection.SetOperation(operation);
    }

    /// <summary>Arrow keys and page up/down move the selection one grid step.</summary>
    private static void HandleNudge(HandleContext handles)
    {
        float grid = CSGSnap.Grid;

        Float3 step;
        if (handles.IsPressed("CSG/NudgeLeft")) step = new Float3(-grid, 0, 0);
        else if (handles.IsPressed("CSG/NudgeRight")) step = new Float3(grid, 0, 0);
        else if (handles.IsPressed("CSG/NudgeBack")) step = new Float3(0, 0, -grid);
        else if (handles.IsPressed("CSG/NudgeForward")) step = new Float3(0, 0, grid);
        else if (handles.IsPressed("CSG/NudgeDown")) step = new Float3(0, -grid, 0);
        else if (handles.IsPressed("CSG/NudgeUp")) step = new Float3(0, grid, 0);
        else return;

        CSGSelection.MoveBy(step, clone: false);
    }

    private static void RotateBySnap(float sign)
        => CSGSelection.RotateBy(new Float3(0f, SceneTools.RotateSnap * sign, 0f), CSGSelection.SelectionCentre(), clone: false);

    /// <summary>The options of the tool in use, opening beside the tool buttons.</summary>
    private void DrawOptions(SceneToolContext ctx, Paper paper, string id)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        List<CSGSections.Column> columns = CSGSections.Columns();
        float available = MathF.Max(CSGSections.ColumnWidth, ctx.Handles.ViewportSize.X - 100f);
        float width = MathF.Min(available, CSGSections.Width(columns));

        using (paper.Column(id).Width(UnitValue.Auto).Height(UnitValue.Auto).Enter())
        {
            DrawHeader(paper, font, id);
            if (columns.Count == 0) return;

            paper.Box($"{id}_div").Width(width).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
            CSGSections.DrawColumns(paper, font, $"{id}_cols", columns, width);
        }
    }

    /// <summary>The active tool's name, any warning about the model and the helper surfaces menu.</summary>
    internal static void DrawHeader(Paper paper, Scribe.FontFile font, string id)
    {
        using (paper.Row($"{id}_head").Width(UnitValue.Auto).Height(30).Padding(8, 6, 0, 0).Gap(3).Enter())
        {
            // The tool these options belong to, picked from the scene view tool strip
            (_, string label, string tip, _, string icon) = Array.Find(Modes, m => CSGEditMode.Is(m.mode));
            paper.Box($"{id}_head_i").Width(UnitValue.Auto).Height(30).IsNotInteractable()
                .Text(icon ?? "", font).TextColor(EditorTheme.AccentText)
                .FontSize(EditorTheme.FontSizeSmall - 2f).Alignment(TextAlignment.MiddleCenter);
            paper.Box($"{id}_head_t").Width(UnitValue.Auto).Height(30).Margin(0, 6, 0, 0)
                .Tooltip(Loc.Get(label ?? "csg.title"), tip != null ? Loc.Get(tip) : "")
                .Text(Loc.Get(label ?? "csg.title"), EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            DrawWarnings(paper, font, id);

            paper.Box($"{id}_head_show")
                .Width(22).Height(22).Rounded(5).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .BackgroundColor(CSGHelperSurfaces.Flags == CSGHelperFlags.None ? SColor.Transparent : EditorTheme.Hover)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Text(EditorIcons.Eye, font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall - 2f).Alignment(TextAlignment.MiddleCenter)
                .Tooltip(Loc.Get("csg.helpers"), Loc.Get("csg.helpers_hint"))
                .OnClick(paper, (p, _) => Origami.ContextMenu((float)p.PointerPos.X, (float)p.PointerPos.Y, HelperMenu));
        }
    }

    /// <summary>Which normally invisible surfaces the viewport draws.</summary>
    private static void HelperMenu(ContextBuilder menu)
    {
        menu.Title(Loc.Get("csg.helpers"));
        HelperItem(menu, "csg.helper_collision", CSGHelperFlags.CollisionOnly);
        HelperItem(menu, "csg.helper_inert", CSGHelperFlags.Inert);
        HelperItem(menu, "csg.helper_discarded", CSGHelperFlags.Discarded);
        menu.Separator();
        bool subtractive = CSGHelperSurfaces.SubtractiveOutlines, intersect = CSGHelperSurfaces.IntersectOutlines;
        menu.Item(Loc.Get("csg.outline_subtractive"), () => CSGHelperSurfaces.SubtractiveOutlines = !subtractive, on: subtractive);
        menu.Item(Loc.Get("csg.outline_intersect"), () => CSGHelperSurfaces.IntersectOutlines = !intersect, on: intersect);
    }

    private static void HelperItem(ContextBuilder menu, string label, CSGHelperFlags flag)
    {
        bool on = CSGHelperSurfaces.Flags.HasFlag(flag);
        menu.Item(Loc.Get(label), () => CSGHelperSurfaces.Set(flag, !on), on: on);
    }

    private static string ModeLabel(CSGMode mode)
    {
        foreach ((CSGMode m, string label, _, _, _) in Modes)
            if (m == mode) return label;
        return "csg.title";
    }

    /// <summary>Brushes that no longer enclose a solid, and a moment's word on a refused edit. Nothing while all is well.</summary>
    private static void DrawWarnings(Paper paper, Scribe.FontFile font, string id)
    {
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return;

        List<CSGBrush> invalid = CSGSelection.BrushesIn(model!).Where(b => !b.IsShapeValid).ToList();
        if (invalid.Count > 0)
        {
            paper.Box($"{id}_warn_bad").Width(UnitValue.Auto).Height(22).Padding(6, 6, 0, 0).Rounded(5)
                .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Text(Loc.Get("csg.status_invalid", new { count = invalid.Count }), font).TextColor(InvalidText)
                .FontSize(EditorTheme.FontSizeSmall - 1f).Alignment(TextAlignment.MiddleCenter)
                .Tooltip(Loc.Get("csg.status_invalid_tip"))
                .OnClick(invalid, (brushes, _) =>
                {
                    Selection.Clear();
                    foreach (CSGBrush brush in brushes) Selection.AddToSelection(brush.GameObject);
                });
        }

        if (CSGStatus.Current is string flash)
        {
            paper.Box($"{id}_warn_flash").Width(UnitValue.Auto).Height(30).Padding(6, 6, 0, 0).IsNotInteractable()
                .Text(flash, font).TextColor(InvalidText)
                .FontSize(EditorTheme.FontSizeSmall - 1f).Alignment(TextAlignment.MiddleCenter);
        }
    }

    private static readonly SColor InvalidText = SColor.FromArgb(255, 255, 120, 110);
}
