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

/// <summary>The CSG editor's panel inside the scene view: mode tabs with the active mode's options under them.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGOverlayTool : SceneTool
{
    public override string Name => "CSG";
    public override string Icon => EditorIcons.Cube;
    public override int Order => -1;


    private static readonly (CSGMode mode, string label, string tip, string shortcut, string icon)[] Modes =
    {
        (CSGMode.Object, "csg.mode_object", "csg.mode_object_tip", "CSG/ModeObject", EditorIcons.ArrowPointer),
        (CSGMode.Generate, "csg.mode_generate", "csg.mode_generate_tip", "CSG/ModeGenerate", EditorIcons.SquarePlus),
        (CSGMode.Surface, "csg.mode_surface", "csg.mode_surface_tip", "CSG/ModeSurface", EditorIcons.PaintRoller),
        (CSGMode.Clip, "csg.mode_clip", "csg.mode_clip_tip", "CSG/ModeClip", EditorIcons.Scissors),
        (CSGMode.Mesh, "csg.mode_mesh", "csg.mode_mesh_tip", "CSG/ModeMesh", EditorIcons.DrawPolygon),
    };

    private bool _collapsed;

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        if (!ctx.Handles.KeyboardCaptured)
        {
            if (ctx.Handles.IsPressed("CSG/ModeObject")) CSGEditMode.Current = CSGMode.Object;
            else if (ctx.Handles.IsPressed("CSG/ModeGenerate")) CSGEditMode.Current = CSGMode.Generate;
            else if (ctx.Handles.IsPressed("CSG/ModeMesh")) CSGEditMode.Current = CSGMode.Mesh;
            else if (ctx.Handles.IsPressed("CSG/ModeClip")) CSGEditMode.Current = CSGMode.Clip;
            else if (ctx.Handles.IsPressed("CSG/ModeSurface")) CSGEditMode.Current = CSGMode.Surface;

            // The mesh tool tapers a selected face on the same keys
            if (SceneToolManager.Get<CSGMeshTool>() is not { TapersFace: true })
            {
                if (ctx.Handles.IsPressed("CSG/HalveGrid")) StepGrid(-1);
                else if (ctx.Handles.IsPressed("CSG/DoubleGrid")) StepGrid(1);
            }
        }

        HandleShortcuts(ctx.Handles);

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid()) CSGHelperSurfaces.Draw(ctx, model!);
    }

    /// <summary>While a model is being worked on, the scene view's right click menu is the CSG one.</summary>
    public override bool ReplacesSceneContextMenu => true;

    /// <summary>In object mode the brush under the cursor is selected, other modes keep what they have.</summary>
    public override bool OnSceneContextPick(SceneToolContext ctx, Ray ray)
    {
        CSGModel? model = CSGSelection.ActiveModel();
        if (!CSGEditMode.Is(CSGMode.Object) || model.IsNotValid()) return true;

        if (CSGSelection.PickBrush(model!, CSGSelection.BrushesIn(model!), ray, out CSGBrush brush, out _)
            && !Selection.IsSelected(brush.GameObject))
            Selection.Select(brush.GameObject);
        return true;
    }

    public override void OnSceneContextMenu(SceneToolContext ctx, ContextBuilder menu)
    {
        menu.Title($"{Loc.Get("csg.title")}  {Loc.Get(ModeLabel(CSGEditMode.Current))}");
        foreach ((string label, System.Action run) in ModeCommands()) menu.Item(label, run);

        if (!CSGEditMode.Is(CSGMode.Object))
            menu.Submenu(Loc.Get("csg.selection"), sub =>
            {
                foreach ((string label, System.Action run) in SelectionCommands()) sub.Item(label, run);
            });

        menu.Submenu(Loc.Get("csg.mode"), sub =>
        {
            foreach ((CSGMode mode, string label, _, string shortcut, _) in Modes)
            {
                string keys = ShortcutManager.Find(shortcut) is { } def ? ShortcutManager.GetDisplayString(def.Binding) : "";
                sub.Item(Loc.Get(label), () => CSGEditMode.Current = mode, shortcut: keys, on: CSGEditMode.Is(mode));
            }
        });
        menu.Separator();
    }

    private static List<(string Label, System.Action Run)> SelectionCommands() =>
    [
        (Loc.Get("csg.additive"), () => SetOperation(Operation.Additive)),
        (Loc.Get("csg.subtractive"), () => SetOperation(Operation.Subtractive)),
        (Loc.Get("csg.intersect"), () => SetOperation(Operation.Intersect)),
        (Loc.Get("csg.duplicate"), CSGSelection.Duplicate),
        (Loc.Get("csg.group_selection"), CSGMenuItems.GroupSelection),
        (Loc.Get("csg.hide_selected"), () => CSGVisibility.Hide(CSGSelection.Brushes())),
        (Loc.Get("csg.unhide_all", new { count = CSGVisibility.HiddenCount }), CSGVisibility.UnhideAll),
        (Loc.Get("csg.centre_pivot"), CSGSelection.CentrePivot),
        (Loc.Get("csg.clone_array"), CSGSelection.CloneArray),
    ];

    /// <summary>The current mode's main commands.</summary>
    private static List<(string Label, System.Action Run)> ModeCommands()
    {
        CSGGenerateTool? generate = SceneToolManager.Get<CSGGenerateTool>();
        CSGClipTool? clip = SceneToolManager.Get<CSGClipTool>();
        CSGMeshTool? mesh = SceneToolManager.Get<CSGMeshTool>();

        return CSGEditMode.Current switch
        {
            CSGMode.Generate when generate != null =>
            [
                (CSGSections.ShapeName(CSGGeneratorKind.Box), () => generate.Generator = CSGGeneratorKind.Box),
                (CSGSections.ShapeName(CSGGeneratorKind.Cylinder), () => generate.Generator = CSGGeneratorKind.Cylinder),
                (CSGSections.ShapeName(CSGGeneratorKind.Sphere), () => generate.Generator = CSGGeneratorKind.Sphere),
                (CSGSections.ShapeName(CSGGeneratorKind.LinearStairs), () => generate.Generator = CSGGeneratorKind.LinearStairs),
                (CSGSections.ShapeName(CSGGeneratorKind.SpiralStairs), () => generate.Generator = CSGGeneratorKind.SpiralStairs),
                (CSGSections.ShapeName(CSGGeneratorKind.Room), () => generate.Generator = CSGGeneratorKind.Room),
                (CSGSections.ShapeName(CSGGeneratorKind.Polygon), () => generate.Generator = CSGGeneratorKind.Polygon),
                (Loc.Get("csg.subtractive"), () => generate.Operation = generate.Operation == Operation.Subtractive ? Operation.Additive : Operation.Subtractive),
            ],
            CSGMode.Surface =>
            [
                (Loc.Get("csg.floors"), CSGSurfaceSelection.SelectFloors),
                (Loc.Get("csg.walls"), CSGSurfaceSelection.SelectWalls),
                (Loc.Get("csg.ceilings"), CSGSurfaceSelection.SelectCeilings),
                (Loc.Get("csg.coplanar"), CSGSurfaceSelection.SelectCoplanar),
                (Loc.Get("csg.connected"), CSGSurfaceSelection.SelectConnected),
                (Loc.Get("csg.align"), CSGSurfaceSelection.AlignToActive),
            ],
            CSGMode.Clip when clip != null =>
            [
                (Loc.Get("csg.clip_front"), () => clip.Mode = CSGClipMode.RemoveFront),
                (Loc.Get("csg.clip_back"), () => clip.Mode = CSGClipMode.RemoveBack),
                (Loc.Get("csg.clip_split"), () => clip.Mode = CSGClipMode.Split),
                (Loc.Get("csg.clip_commit"), clip.Commit),
            ],
            CSGMode.Mesh when mesh != null =>
            [
                (Loc.Get("csg.chamfer"), mesh.ChamferSelected),
                (Loc.Get("csg.extrude"), mesh.ExtrudeSelected),
                (Loc.Get("csg.snap"), mesh.SnapSelectedToGrid),
                (Loc.Get("csg.delete_face"), mesh.DeleteSelectedFaces),
            ],
            _ => SelectionCommands(),
        };
    }

    /// <summary>Selection shortcuts, in every mode. The mesh tool keeps rotate and snap for its own points.</summary>
    private static void HandleShortcuts(HandleContext handles)
    {
        if (handles.KeyboardCaptured) return;
        bool mesh = CSGEditMode.Is(CSGMode.Mesh);

        // Modified bindings are tested first so plain Hide does not swallow Ctrl+H and Shift+H
        if (handles.IsPressed("CSG/HideUnselected")) { CSGVisibility.HideUnselected(); return; }
        if (handles.IsPressed("CSG/ToggleVisibility")) { CSGVisibility.ToggleAll(); return; }
        if (handles.IsPressed("CSG/Hide")) { CSGVisibility.Hide(CSGSelection.Brushes()); return; }
        if (handles.IsPressed("CSG/UnhideAll")) { CSGVisibility.UnhideAll(); return; }

        if (handles.IsPressed("CSG/SetAdditive")) { SetOperation(Operation.Additive); return; }
        if (handles.IsPressed("CSG/SetSubtractive")) { SetOperation(Operation.Subtractive); return; }
        if (handles.IsPressed("CSG/SetIntersecting")) { SetOperation(Operation.Intersect); return; }
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

    /// <summary>Sets the selection's operation, and while drawing also the operation the next brush gets.</summary>
    private static void SetOperation(Operation operation)
    {
        if (CSGEditMode.Is(CSGMode.Generate) && SceneToolManager.Get<CSGGenerateTool>() is { } generate) generate.Operation = operation;
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

    public override void OnSceneOverlayGUI(SceneToolContext ctx, Paper paper, string id, Float2 viewportSize)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        List<CSGSections.Column> columns = _collapsed ? [] : CSGSections.Columns();
        float available = MathF.Max(CSGSections.ColumnWidth, viewportSize.X - 80f);
        float width = MathF.Min(available, MathF.Max(HeaderWidth, columns.Count * CSGSections.ColumnWidth));

        using (paper.Column($"{id}_panel")
                   .PositionType(PositionType.SelfDirected)
                   .AnchorLeft(0).AnchorBottom(0)
                   .Width(width).Height(UnitValue.Auto)
                   .Rounded(0, 10, 0, 0)
                   .BackgroundColor(EditorTheme.Glass)
                   .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                   .Enter())
        {
            DrawHeader(paper, font, id, _collapsed, () => _collapsed = !_collapsed);
            if (_collapsed) return;

            paper.Box($"{id}_div").Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
            CSGSections.DrawColumns(paper, font, $"{id}_cols", columns, width);
        }
    }

    private const float HeaderWidth = 560f;

    /// <summary>The mode buttons along the top, with any warning about the model at the far end.</summary>
    internal static void DrawHeader(Paper paper, Scribe.FontFile font, string id, bool collapsed, System.Action? toggle)
    {
        using (paper.Row($"{id}_head").Height(30).Padding(8, 6, 0, 0).Gap(3).Enter())
        {
            paper.Box($"{id}_head_t").Width(UnitValue.Auto).Height(30).Margin(0, 6, 0, 0).IsNotInteractable()
                .Text(Loc.Get("csg.title"), EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            foreach ((CSGMode mode, string label, string tip, string shortcut, string icon) in Modes)
            {
                bool active = CSGEditMode.Is(mode);
                using (paper.Row($"{id}_mode_{mode}").Width(UnitValue.Auto).Height(22).Padding(7, 8, 0, 0).Gap(5).Rounded(5)
                           .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                           .BackgroundColor(active ? EditorTheme.Accent : SColor.Transparent)
                           .Hovered.BackgroundColor(active ? EditorTheme.AccentBright : EditorTheme.Hover).End()
                           .Tooltip(ShortcutManager.GetTooltip(Loc.Get(label), shortcut), Loc.Get(tip))
                           .OnClick(mode, (m, _) => CSGEditMode.Current = m)
                           .Enter())
                {
                    paper.Box($"{id}_mode_{mode}_i").Width(UnitValue.Auto).Height(22).IsNotInteractable()
                        .Text(icon, font).TextColor(active ? SColor.White : EditorTheme.AccentText)
                        .FontSize(EditorTheme.FontSizeSmall - 2f).Alignment(TextAlignment.MiddleCenter);
                    paper.Box($"{id}_mode_{mode}_t").Width(UnitValue.Auto).Height(22).IsNotInteractable()
                        .Text(Loc.Get(label), font).TextColor(active ? SColor.White : EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall - 1f).Alignment(TextAlignment.MiddleCenter);
                }
            }

            paper.Box($"{id}_head_gap").Height(30).IsNotInteractable();
            DrawWarnings(paper, font, id);

            paper.Box($"{id}_head_show")
                .Width(22).Height(22).Rounded(5).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .BackgroundColor(CSGHelperSurfaces.Flags == CSGHelperFlags.None ? SColor.Transparent : EditorTheme.Hover)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Text(EditorIcons.Eye, font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall - 2f).Alignment(TextAlignment.MiddleCenter)
                .Tooltip(Loc.Get("csg.helpers"), Loc.Get("csg.helpers_hint"))
                .OnClick(paper, (p, _) => Origami.ContextMenu((float)p.PointerPos.X, (float)p.PointerPos.Y, HelperMenu));

            if (toggle == null) return;
            paper.Box($"{id}_head_c")
                .Width(22).Height(22).Rounded(5).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Text(collapsed ? EditorIcons.ChevronUp : EditorIcons.ChevronDown, font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall - 4f).Alignment(TextAlignment.MiddleCenter)
                .Tooltip(Loc.Get("csg.collapse"), Loc.Get("csg.collapse_tip"))
                .OnClick(toggle, (t, _) => t());
        }
    }

    /// <summary>Which normally invisible surfaces the viewport draws.</summary>
    private static void HelperMenu(ContextBuilder menu)
    {
        menu.Title(Loc.Get("csg.helpers"));
        HelperItem(menu, "csg.helper_collision", CSGHelperFlags.CollisionOnly);
        HelperItem(menu, "csg.helper_inert", CSGHelperFlags.Inert);
        HelperItem(menu, "csg.helper_discarded", CSGHelperFlags.Discarded);
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

/// <summary>A tool strip button that starts a CSG model, so building can begin without a trip through the menus.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGStartTool : SceneTool
{
    public override string Name => "CSG Start";
    public override string Icon => EditorIcons.Cube;
    public override int Order => -2;

    public override void OnSceneInput(SceneToolContext ctx) { }

    public override void OnToolStripGUI(SceneToolContext ctx, Paper paper, string id)
    {
        if (CSGSelection.ActiveModel().IsValid()) return;
        EditorGUI.ToolStripButton(paper, id, EditorIcons.Cube_I, Loc.Get("csg.create_model_tip"), false, () => CSGSelection.CreateModel());
    }
}
