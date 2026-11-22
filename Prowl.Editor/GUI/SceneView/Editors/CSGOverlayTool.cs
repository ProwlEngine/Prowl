// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

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

    private const float PanelWidth = 268f;

    private static readonly (CSGMode mode, string label, string tip, string shortcut)[] Modes =
    {
        (CSGMode.Object, "csg.mode_object", "csg.mode_object_tip", "CSG/ModeObject"),
        (CSGMode.Generate, "csg.mode_generate", "csg.mode_generate_tip", "CSG/ModeGenerate"),
        (CSGMode.Surface, "csg.mode_surface", "csg.mode_surface_tip", "CSG/ModeSurface"),
        (CSGMode.Clip, "csg.mode_clip", "csg.mode_clip_tip", "CSG/ModeClip"),
        (CSGMode.Mesh, "csg.mode_mesh", "csg.mode_mesh_tip", "CSG/ModeMesh"),
    };

    // The keys each mode answers to, shown under its options
    private static readonly string[] SelectionKeys = ["CSG/Hide", "CSG/UnhideAll", "CSG/SetAdditive", "CSG/SetSubtractive", "Scene/Duplicate"];
    private static readonly string[] ObjectKeys = [.. SelectionKeys, "CSG/SnapToGrid", "CSG/RotateLeft", "CSG/RotateRight", "CSG/NudgeLeft", "CSG/NudgeUp"];
    private static readonly string[] GenerateKeys = ["CSG/SetAdditive", "CSG/SetSubtractive", "CSG/SetIntersecting", "CSG/Confirm"];
    private static readonly string[] SurfaceKeys = ["CSG/Smear", "CSG/Hide", "CSG/UnhideAll"];
    private static readonly string[] ClipKeys = ["CSG/ClipSwapSide", "CSG/Confirm"];
    private static readonly string[] MeshKeys = ["CSG/HalveGrid", "CSG/DoubleGrid", "CSG/RotateLeft", "CSG/SnapToGrid"];

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
                if (ctx.Handles.IsPressed("CSG/HalveGrid")) SceneTools.StepMoveSnap(-1);
                else if (ctx.Handles.IsPressed("CSG/DoubleGrid")) SceneTools.StepMoveSnap(1);
            }
        }

        HandleShortcuts(ctx.Handles);

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid()) CSGHelperSurfaces.Draw(ctx, model!);
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

        if (handles.IsPressed("CSG/FlipX")) { CSGSelection.Flip(Float3.UnitX); return; }
        if (handles.IsPressed("CSG/FlipY")) { CSGSelection.Flip(Float3.UnitY); return; }
        if (handles.IsPressed("CSG/FlipZ")) { CSGSelection.Flip(Float3.UnitZ); return; }

        if (mesh) return;

        if (handles.IsPressed("CSG/SnapToGrid")) { CSGSelection.SnapToGrid(); return; }
        if (handles.IsPressed("CSG/RotateLeft")) { RotateBySnap(-1f); return; }
        if (handles.IsPressed("CSG/RotateRight")) { RotateBySnap(1f); return; }

        HandleNudge(handles);
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

        float maxHeight = MathF.Max(120f, viewportSize.Y - 90f);

        using (paper.Column($"{id}_panel")
                   .PositionType(PositionType.SelfDirected)
                   .Position(56, 12)
                   .Width(PanelWidth).Height(UnitValue.Auto)
                   .Rounded(9).Padding(0, 0, 0, 6)
                   .BackgroundColor(EditorTheme.Glass)
                   .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                   .Enter())
        {
            DrawTitleBar(paper, font, id);
            if (_collapsed) return;

            DrawModeTabs(paper, font, id);

            Origami.ScrollView(paper, $"{id}_scroll", PanelWidth, maxHeight).Body(() =>
            {
                using (paper.Column($"{id}_body").Height(UnitValue.Auto).Padding(0, 0, 2, 8).Enter())
                    DrawModeBody(paper, font);
            });
        }
    }

    private void DrawTitleBar(Paper paper, Scribe.FontFile font, string id)
    {
        using (paper.Row($"{id}_title").Height(22).Padding(8, 6, 0, 0).Enter())
        {
            paper.Box($"{id}_title_t").Height(22)
                .Text(Loc.Get("csg.title"), font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_title_c")
                .Width(18).Height(18).Rounded(4)
                .BackgroundColor(SColor.Transparent)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Text(_collapsed ? EditorIcons.ChevronDown : EditorIcons.ChevronUp, font)
                .TextColor(EditorTheme.Ink500).FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                .Tooltip(Loc.Get("csg.collapse"), Loc.Get("csg.collapse_tip"))
                .OnClick(_ => _collapsed = !_collapsed);
        }
    }

    private static void DrawModeTabs(Paper paper, Scribe.FontFile font, string id)
    {
        using (paper.Row($"{id}_modes").Height(24).Padding(6, 6, 0, 6).Gap(3).Enter())
        {
            foreach ((CSGMode mode, string label, string tip, string shortcut) in Modes)
            {
                bool active = CSGEditMode.Is(mode);

                paper.Box($"{id}_mode_{mode}")
                    .Height(24).Rounded(5)
                    .BackgroundColor(active ? EditorTheme.Accent : EditorTheme.Ink100)
                    .Hovered.BackgroundColor(active ? EditorTheme.AccentBright : EditorTheme.Hover).End()
                    .Text(Loc.Get(label), font)
                    .TextColor(active ? SColor.White : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(ShortcutManager.GetTooltip(Loc.Get(label), shortcut), Loc.Get(tip))
                    .OnClick(mode, (m, _) => CSGEditMode.Current = m);
            }
        }
    }

    private static void DrawModeBody(Paper paper, Scribe.FontFile font)
    {
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return;

        switch (CSGEditMode.Current)
        {
            case CSGMode.Generate:
                CSGSections.DrawGenerateSection(paper, font, SceneToolManager.Get<CSGGenerateTool>());
                break;

            case CSGMode.Surface:
                CSGSections.DrawSurfaceSection(paper, font, SceneToolManager.Get<CSGSurfaceTool>());
                break;

            case CSGMode.Clip:
                CSGSections.DrawClipSection(paper, font, SceneToolManager.Get<CSGClipTool>());
                break;

            case CSGMode.Mesh:
                CSGSections.DrawMeshSection(paper, font, SceneToolManager.Get<CSGMeshTool>());
                break;

            default:
                CSGSections.DrawSelectionSection(paper, font, SceneToolManager.Get<CSGObjectTool>());
                CSGSections.DrawVisibilitySection(paper, font, SceneToolManager.Get<CSGObjectTool>());
                CSGSections.DrawModelSection(paper, font, model!);
                break;
        }

        CSGSections.DrawKeys(paper, font, CSGEditMode.Current switch
        {
            CSGMode.Generate => GenerateKeys,
            CSGMode.Surface => SurfaceKeys,
            CSGMode.Clip => ClipKeys,
            CSGMode.Mesh => MeshKeys,
            _ => ObjectKeys,
        });
    }
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
