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
        (CSGMode.Surface, "csg.mode_surface", "csg.mode_surface_tip", "CSG/ModeSurface", EditorIcons.BorderAll),
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

    public override Float3? SurfaceUnder(SceneToolContext ctx, Ray ray)
    {
        CSGModel? model = CSGSelection.ActiveModel();
        return model.IsValid() && CSGSelection.PickBrush(model!, CSGSelection.BrushesIn(model!), ray, out _, out float distance)
            ? ray.Origin + ray.Direction * distance : null;
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
        if (SceneToolManager.Get<CSGMeshTool>() is { Active: true } mesh)
        {
            MeshMenu(menu, mesh);
            menu.Submenu(Loc.Get("csg.menu.brush"), BrushCommands);
        }
        else
        {
            menu.Title($"{Loc.Get("csg.title")}  {Loc.Get(ModeLabel(CSGEditMode.Current))}");
            if (CSGEditMode.Is(CSGMode.Create)) ShapeCommands(menu);
            else if (CSGEditMode.Is(CSGMode.Surface)) SurfaceCommands(menu);
            else BrushCommands(menu);
        }

        menu.Separator();
        menu.Submenu(Loc.Get("csg.tools"), sub =>
        {
            foreach ((CSGMode mode, string label, _, string shortcut, _) in Modes)
                sub.Item(Loc.Get(label), () => CSGEditMode.Current = mode, shortcut: Keys(shortcut), on: CSGEditMode.Is(mode));
        });
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

    private static void MeshMenu(ContextBuilder menu, CSGMeshTool mesh)
    {
        switch (mesh.Mode)
        {
            case Prowl.Editor.Modeling.SelectMode.Vertex:
                menu.Title(Count(mesh.SelectedCount, "model.menu.point", "model.menu.points"));
                if (mesh.SelectedCount > 0) PointCommands(menu, mesh);
                break;
            case Prowl.Editor.Modeling.SelectMode.Edge:
                menu.Title(Count(mesh.SelectedEdgeCount, "model.menu.edge", "model.menu.edges"));
                if (mesh.SelectedEdgeCount > 0) EdgeCommands(menu, mesh);
                break;
            default:
                menu.Title(Count(mesh.SelectedFaceCount, "model.menu.face", "model.menu.faces"));
                if (mesh.SelectedFaceCount > 0) FaceCommands(menu, mesh);
                break;
        }
        if (mesh.SelectedCount > 0) menu.Separator();

        menu.Submenu(Loc.Get("model.menu.tools"), sub =>
        {
            Command(sub, "csg.loop_cut", mesh.LoopCut, "MeshEdit/LoopCut");
            Command(sub, "csg.knife", mesh.Knife, "MeshEdit/Knife");
        });
        menu.Submenu(Loc.Get("csg.menu.pivot"), sub =>
        {
            if (mesh.SelectedCount > 0) Command(sub, "csg.pivot_to_selection", mesh.PivotToSelection);
            Command(sub, "csg.pivot_to_cursor", mesh.PivotToCursor);
            Command(sub, "csg.centre_pivot", CSGSelection.CentrePivot);
        });
        menu.Submenu(Loc.Get("csg.menu.clean_up"), sub =>
        {
            Command(sub, "csg.merge_by_distance", mesh.MergeByDistanceSelected, "MeshEdit/MergeByDistance");
            Command(sub, "csg.limited_dissolve", mesh.LimitedDissolveSelected, "MeshEdit/LimitedDissolve");
            Command(sub, "csg.tris_to_quads", mesh.TrisToQuadsSelected, "MeshEdit/TrisToQuads");
        });
        menu.Submenu(Loc.Get("csg.symmetrize"), sub =>
        {
            for (int axis = 0; axis < 3; axis++)
            {
                int chosen = axis;
                string name = axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
                sub.Item(Loc.Get("csg.symmetrize_side", new { from = "+" + name, to = "-" + name }), () => mesh.Symmetrize(chosen, true));
            }
        });
        menu.Submenu(Loc.Get("model.menu.view"), sub =>
        {
            sub.Toggle(Loc.Get("csg.xray"), () => mesh.XRay = !mesh.XRay, () => mesh.XRay);
            sub.Toggle(Loc.Get("csg.snap_brushes"), () => mesh.SnapToBrushes = !mesh.SnapToBrushes, () => mesh.SnapToBrushes);
            sub.Toggle(Loc.Get("csg.proportional"), () => mesh.Proportional = !mesh.Proportional, () => mesh.Proportional);
        });
    }

    private static string Count(int count, string one, string many)
        => count == 1 ? Loc.Get(one) : Loc.Get(many, new { count });

    private static void PointCommands(ContextBuilder menu, CSGMeshTool mesh)
    {
        if (mesh.CanBevel) Command(menu, "csg.bevel", mesh.BevelSelected, "MeshEdit/Bevel");
        if (mesh.CanConnect) Command(menu, "csg.connect", mesh.ConnectSelected, "MeshEdit/Connect");
        MergeCommands(menu, mesh);
        if (mesh.CanSmooth)
        {
            Command(menu, "csg.smooth", mesh.SmoothSelected, "MeshEdit/Smooth");
            Command(menu, "csg.relax", mesh.RelaxSelected);
        }
        if (mesh.CanFlatten) Command(menu, "csg.flatten", mesh.FlattenSelected, "MeshEdit/Flatten");
        if (mesh.CanSnap) Command(menu, "csg.snap", mesh.SnapSelectedToGrid, "CSG/SnapToGrid");
        if (mesh.CanDissolve)
        {
            menu.Separator();
            Command(menu, "csg.dissolve", mesh.DissolveSelected, "MeshEdit/Dissolve");
        }
    }

    private static void EdgeCommands(ContextBuilder menu, CSGMeshTool mesh)
    {
        if (mesh.CanBevel) Command(menu, "csg.bevel", mesh.BevelSelected, "MeshEdit/Bevel");
        if (mesh.CanSubdivide)
        {
            Command(menu, "csg.subdivide", mesh.SubdivideSelected);
            Command(menu, "csg.collapse", mesh.CollapseSelected);
        }
        MergeCommands(menu, mesh);
        if (mesh.CanFlatten) Command(menu, "csg.flatten", mesh.FlattenSelected, "MeshEdit/Flatten");
        if (mesh.CanSnap) Command(menu, "csg.snap", mesh.SnapSelectedToGrid, "CSG/SnapToGrid");
        if (mesh.CanDissolve)
        {
            menu.Separator();
            Command(menu, "csg.dissolve", mesh.DissolveSelected, "MeshEdit/Dissolve");
        }
    }

    private static void FaceCommands(ContextBuilder menu, CSGMeshTool mesh)
    {
        if (mesh.CanExtrudeRegion) Command(menu, "csg.extrude_region", mesh.ExtrudeRegionSelected, "MeshEdit/Extrude");
        if (mesh.CanExtrude) Command(menu, "csg.extrude", mesh.ExtrudeSelected);
        if (mesh.CanInset) Command(menu, "csg.inset", mesh.InsetSelected, "MeshEdit/Inset");
        if (mesh.CanBevel) Command(menu, "csg.bevel", mesh.BevelSelected, "MeshEdit/Bevel");
        if (mesh.CanSubdivide) Command(menu, "csg.subdivide", mesh.SubdivideSelected);
        if (mesh.CanBridge) Command(menu, "csg.bridge", mesh.BridgeSelected, "MeshEdit/Bridge");
        if (mesh.CanTriangulate) Command(menu, "csg.triangulate", mesh.TriangulateSelected, "MeshEdit/Triangulate");
        if (mesh.CanFlatten) Command(menu, "csg.flatten", mesh.FlattenSelected, "MeshEdit/Flatten");
        if (mesh.CanSnap) Command(menu, "csg.snap", mesh.SnapSelectedToGrid, "CSG/SnapToGrid");
        if (mesh.CanSeparate) Command(menu, "csg.separate", mesh.SeparateSelected, "MeshEdit/Separate");
        menu.Separator();
        if (mesh.CanDissolve) Command(menu, "csg.dissolve", mesh.DissolveSelected, "MeshEdit/Dissolve");
        menu.Item(Loc.Get("csg.delete_face"), mesh.DeleteSelectedFaces, enabled: mesh.CanDeleteFaces, shortcut: Keys("Scene/Delete"), danger: true);
    }

    private static void MergeCommands(ContextBuilder menu, CSGMeshTool mesh)
    {
        if (!mesh.CanMerge) return;
        menu.Submenu(Loc.Get("csg.menu.merge"), sub =>
        {
            Command(sub, "csg.merge_centre", mesh.MergeAtCentre);
            if (mesh.CanMergeAtFirst) Command(sub, "csg.merge_first", mesh.MergeAtFirst);
            if (mesh.CanMergeAtActive) Command(sub, "csg.merge_active", mesh.MergeAtActive);
            Command(sub, "csg.merge_by_distance", mesh.MergeByDistanceSelected, "MeshEdit/MergeByDistance");
        });
    }

    private static void BrushCommands(ContextBuilder menu)
    {
        List<CSGBrush> brushes = CSGSelection.Brushes();
        List<CSGGroup> groups = CSGSelection.Groups();
        if (brushes.Count > 0 || groups.Count > 0)
        {
            menu.Submenu(Loc.Get("csg.operation"), sub =>
            {
                OperationItem(sub, "csg.additive", Operation.Additive, "CSG/SetAdditive", brushes, groups);
                OperationItem(sub, "csg.subtractive", Operation.Subtractive, "CSG/SetSubtractive", brushes, groups);
                OperationItem(sub, "csg.intersect", Operation.Intersect, "CSG/SetIntersecting", brushes, groups);
                if (groups.Count > 0)
                {
                    sub.Separator();
                    sub.Item(Loc.Get("csg.pass_through"), CSGSelection.SetPassThrough, shortcut: Keys("CSG/SetPassThrough"), on: groups.TrueForAll(g => g.PassThrough));
                }
            });
            if (SceneToolManager.Get<CSGClipTool>() is { } clip) Command(menu, "csg.cut_with_plane", clip.Begin);
            menu.Separator();
            Command(menu, "csg.duplicate", CSGSelection.Duplicate, "Scene/Duplicate");
            Command(menu, "csg.repeat_clone", CSGSelection.RepeatClone, "CSG/RepeatClone");
            Command(menu, "csg.group_selection", CSGMenuItems.GroupSelection, "CSG/GroupSelection");
            menu.Submenu(Loc.Get("model.menu.transform"), sub =>
            {
                Command(sub, "csg.flip_x", () => CSGSelection.Flip(Float3.UnitX), "CSG/FlipX");
                Command(sub, "csg.flip_y", () => CSGSelection.Flip(Float3.UnitY), "CSG/FlipY");
                Command(sub, "csg.flip_z", () => CSGSelection.Flip(Float3.UnitZ), "CSG/FlipZ");
                sub.Separator();
                Command(sub, "csg.rotate_left", () => RotateBySnap(-1f), "CSG/RotateLeft");
                Command(sub, "csg.rotate_right", () => RotateBySnap(1f), "CSG/RotateRight");
                sub.Separator();
                Command(sub, "csg.snap_to_grid", CSGSelection.SnapToGrid, "CSG/SnapToGrid");
                Command(sub, "csg.centre_pivot", CSGSelection.CentrePivot);
            });
            menu.Submenu(Loc.Get("csg.menu.order"), sub =>
            {
                Command(sub, "csg.earlier", () => CSGSelection.MoveInOrder(-1), "CSG/Earlier");
                Command(sub, "csg.later", () => CSGSelection.MoveInOrder(1), "CSG/Later");
            });
        }
        menu.Submenu(Loc.Get("model.menu.hide"), sub =>
        {
            if (brushes.Count > 0)
            {
                Command(sub, "csg.hide_selected", () => CSGVisibility.Hide(CSGSelection.Brushes()), "CSG/Hide");
                Command(sub, "csg.hide_unselected", CSGVisibility.HideUnselected, "CSG/HideUnselected");
            }
            Command(sub, "csg.toggle_visibility", CSGVisibility.ToggleAll, "CSG/ToggleVisibility");
            sub.Item(Loc.Get("csg.unhide_all", new { count = CSGVisibility.HiddenCount }), CSGVisibility.UnhideAll, shortcut: Keys("CSG/UnhideAll"));
        });
    }

    private static void OperationItem(ContextBuilder menu, string label, Operation operation, string shortcut, List<CSGBrush> brushes, List<CSGGroup> groups)
        => menu.Item(Loc.Get(label), () => SetOperation(operation), shortcut: Keys(shortcut),
            on: brushes.TrueForAll(b => b.Operation == operation) && groups.TrueForAll(g => g.Operation == operation));

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
        float width = CSGSections.Stacked ? CSGSections.ColumnWidth : MathF.Min(available, CSGSections.Width(columns));

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
