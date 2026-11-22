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
    private static readonly Color32 InvalidColor = new(255, 80, 70, 255);

    private readonly List<CSGBrush> _brushes = new();

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override void OnDeactivated() => _brushes.Clear();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        // The modes are exclusive so a click in surface mode does not also select the brush.
        if (!CSGEditMode.Is(CSGMode.Object)) return;

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return;

        CollectBrushes(model!);
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
            if (CSGVisibility.IsHidden(brush)) continue;
            if (!brush.IsShapeValid)
            {
                ctx.Draw.Circle(brush.Transform.Position, ctx.Camera.GameObject.Transform.Forward, 0.25f, InvalidColor, 2f);
                ctx.Draw.Label(brush.Transform.Position, brush.GameObject.Name, InvalidColor);
                continue;
            }

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
