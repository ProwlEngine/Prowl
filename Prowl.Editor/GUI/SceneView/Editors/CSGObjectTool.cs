// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;

using Operation = Prowl.Vector.Geometry.CSGOperation;
using SColor = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Selection and whole brush manipulation for CSG models: pick, switch operation, flip, recentre pivots, clone and hide.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGObjectTool : SceneTool
{
    public override string Name => "CSG Object";
    public override string Icon => EditorIcons.Cube;
    public override string? Tooltip => "Select and edit CSG brushes";

    private const string BrushControl = "csg_brush";
    private const string TransformControl = "sceneview_transform";

    private static readonly Color32 AdditiveColor = new(120, 200, 255, 255);
    private static readonly Color32 SubtractiveColor = new(255, 140, 120, 255);
    private static readonly Color32 IntersectColor = new(220, 200, 120, 255);
    private static readonly Color32 SelectedColor = new(255, 220, 90, 255);

    private readonly List<CSGBrush> _brushes = new();

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ModelOf(ctx.ActiveObject).IsValid();

    public override void OnDeactivated() => _brushes.Clear();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        // The modes are exclusive so a click in surface mode does not also select the brush.
        if (!CSGEditMode.Is(CSGMode.Object)) return;

        CSGModel? model = CSGSelection.ModelOf(ctx.ActiveObject);
        if (model.IsNotValid()) return;

        CollectBrushes(model!);
        HandleShortcuts(ctx);
        HandleCloneDrag(ctx);
        PickBrush(ctx, model!);
        DrawBrushes(ctx);
    }

    private void CollectBrushes(CSGModel model)
    {
        _brushes.Clear();
        foreach (GameObject go in model.GameObject.GetChildrenDeep())
        {
            if (!go.EnabledInHierarchy) continue;

            CSGBrush brush = go.GetComponent<CSGBrush>();
            if (brush.IsValid() && brush.EnabledInHierarchy) _brushes.Add(brush);
        }
    }

    /// <summary>Picks against the model's generated surfaces so the inside of a carved room can be clicked.</summary>
    private void PickBrush(SceneToolContext ctx, CSGModel model)
    {
        HandleContext handles = ctx.Handles;

        if (!CSGSelection.PickBrush(model, _brushes, handles.MouseRay, out CSGBrush hit, out float distance)) return;

        ControlID id = handles.GetControlID(BrushControl);
        handles.AddControl(id, HandleContext.BodyDistance, distance);

        if (!handles.IsNearest(id) || !handles.TryBeginDrag(id)) return;

        GameObject target = SelectionTargetFor(hit);
        if (handles.Shift) Selection.AddToSelection(target);
        else if (handles.Ctrl)
        {
            if (Selection.IsSelected(target)) Selection.RemoveFromSelection(target);
            else Selection.AddToSelection(target);
        }
        else Selection.Select(target);
    }

    /// <summary>What a click on this brush selects. The outermost group marked to handle as one stands in for it.</summary>
    private static GameObject SelectionTargetFor(CSGBrush brush)
    {
        GameObject target = brush.GameObject;

        for (GameObject? current = brush.GameObject.Parent; current.IsValid(); current = current!.Parent)
        {
            CSGGroup group = current!.GetComponent<CSGGroup>();
            if (group.IsValid() && group.HandleAsOne) target = current;
        }

        return target;
    }

    private void HandleShortcuts(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        if (handles.KeyboardCaptured) return;

        // Modified bindings are tested first so plain Hide does not swallow Ctrl+H and Shift+H.
        if (handles.IsPressed("CSG/HideUnselected")) { CSGVisibility.HideUnselected(); return; }
        if (handles.IsPressed("CSG/ToggleVisibility")) { CSGVisibility.ToggleAll(); return; }
        if (handles.IsPressed("CSG/Hide")) { CSGVisibility.Hide(CSGSelection.Brushes()); return; }
        if (handles.IsPressed("CSG/UnhideAll")) { CSGVisibility.UnhideAll(); return; }

        if (handles.IsPressed("CSG/SetAdditive")) { CSGSelection.SetOperation(Operation.Additive); return; }
        if (handles.IsPressed("CSG/SetSubtractive")) { CSGSelection.SetOperation(Operation.Subtractive); return; }
        if (handles.IsPressed("CSG/SetIntersecting")) { CSGSelection.SetOperation(Operation.Intersect); return; }
        if (handles.IsPressed("CSG/SetPassThrough")) { CSGSelection.SetPassThrough(); return; }

        if (handles.IsPressed("Scene/Duplicate")) { CSGSelection.Duplicate(); return; }
        if (handles.IsPressed("CSG/SnapToGrid")) { CSGSelection.SnapToGrid(); return; }

        if (handles.IsPressed("CSG/FlipX")) { CSGSelection.Flip(Float3.UnitX); return; }
        if (handles.IsPressed("CSG/FlipY")) { CSGSelection.Flip(Float3.UnitY); return; }
        if (handles.IsPressed("CSG/FlipZ")) { CSGSelection.Flip(Float3.UnitZ); return; }

        if (handles.IsPressed("CSG/RotateLeft")) { RotateBySnap(-1f); return; }
        if (handles.IsPressed("CSG/RotateRight")) { RotateBySnap(1f); return; }

        // Nudging stays on raw arrow and page keys rather than shortcut bindings.
        if (handles.Ctrl && handles.Shift) HandleNudge(handles);
    }

    /// <summary>Arrow keys and page up/down move the selection one grid step.</summary>
    private static bool HandleNudge(HandleContext handles)
    {
        float grid = CSGSnap.Grid;

        Float3 step;
        if (handles.KeyDown(KeyCode.Left)) step = new Float3(-grid, 0, 0);
        else if (handles.KeyDown(KeyCode.Right)) step = new Float3(grid, 0, 0);
        else if (handles.KeyDown(KeyCode.Down)) step = new Float3(0, 0, -grid);
        else if (handles.KeyDown(KeyCode.Up)) step = new Float3(0, 0, grid);
        else if (handles.KeyDown(KeyCode.PageDown)) step = new Float3(0, -grid, 0);
        else if (handles.KeyDown(KeyCode.PageUp)) step = new Float3(0, grid, 0);
        else return false;

        CSGSelection.MoveBy(step, clone: false);
        return true;
    }

    private static void RotateBySnap(float sign)
        => CSGSelection.RotateBy(new Float3(0f, SceneTools.RotateSnap * sign, 0f),
                                 CSGSelection.SelectionCentre(), clone: false);

    /// <summary>Shift pressing the transform gizmo leaves a copy behind and drags the duplicate.</summary>
    private void HandleCloneDrag(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        if (!handles.PrimaryDown || !handles.Shift || handles.Blocked) return;
        if (!handles.IsNearest(handles.GetControlID(TransformControl))) return;

        CSGSelection.Duplicate();
    }

    private void DrawBrushes(SceneToolContext ctx)
    {
        foreach (CSGBrush brush in _brushes)
        {
            if (CSGVisibility.IsHidden(brush) || !brush.IsShapeValid) continue;

            bool selected = Selection.IsSelected(brush.GameObject);
            Color32 color = selected ? SelectedColor : ColorFor(brush.Operation);

            DrawBrushEdges(ctx, brush, color, selected ? 2f : 1f);
        }
    }

    private static void DrawBrushEdges(SceneToolContext ctx, CSGBrush brush, Color32 color, float thickness)
    {
        var shape = brush.Shape;
        if (shape == null) return;

        Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;
        foreach (var edge in shape.Edges)
        {
            Float3 a = Float4x4.TransformPoint(edge.Vert1.Point, toWorld);
            Float3 b = Float4x4.TransformPoint(edge.Vert2.Point, toWorld);
            ctx.Draw.Line(a, b, color, thickness);
        }
    }

    private static Color32 ColorFor(Operation operation) => operation switch
    {
        Operation.Subtractive => SubtractiveColor,
        Operation.Intersect => IntersectColor,
        _ => AdditiveColor,
    };
}
