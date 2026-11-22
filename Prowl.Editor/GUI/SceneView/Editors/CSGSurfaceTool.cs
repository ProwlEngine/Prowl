// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;

using SColor = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Picks surfaces to texture. Clicking a face selects it rather than its brush.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGSurfaceTool : SceneTool
{
    public override string Name => "CSG Surface";
    public override string Icon => EditorIcons.Brush;
    public override string? Tooltip => "Select and texture CSG surfaces";
    public override int Order => 2;

    private const string SurfaceControl = "csg_surface";
    private const string EmptyControl = "csg_surface_empty";

    private static readonly Color32 HoverColor = new(255, 240, 160, 50);
    private static readonly Color32 HoverEdge = new(255, 245, 190, 255);
    private static readonly Color32 SelectedFill = new(255, 200, 90, 70);
    private static readonly Color32 SelectedEdge = new(255, 220, 120, 255);

    /// <summary>Whether surface picking is the CSG mode in force.</summary>
    public bool Active => CSGEditMode.Is(CSGMode.Surface);

    private CSGSurfaceRef _lastSmeared;

    // A Shift or Ctrl drag paints every surface it passes over into or out of the selection
    private enum Paint { None, Add, Remove }
    private Paint _paint;

    // What the model shows this frame, by the surface each piece came from
    private readonly Dictionary<CSGSurfaceRef, List<Float3[]>> _pieces = new();

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override bool SuppressTransformGizmo => Active;

    public override void OnDeactivated() => CSGSurfaceSelection.Clear();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        if (!Active) return;

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return;

        CSGSurfaceSelection.Prune();

        if (ctx.Handles.KeyDown(KeyCode.Escape))
        {
            if (CSGSurfaceSelection.Count == 0) CSGEditMode.Current = CSGMode.Object;
            CSGSurfaceSelection.Clear();
            return;
        }

        CollectPieces(model!);
        PickSurface(ctx, model!);
        DrawSelection();
    }

    private void CollectPieces(CSGModel model)
    {
        _pieces.Clear();
        foreach (CSGModelFace face in model.Faces())
        {
            var surface = new CSGSurfaceRef(face.Brush, face.PlaneIndex);
            if (!_pieces.TryGetValue(surface, out List<Float3[]>? list)) _pieces[surface] = list = new List<Float3[]>();
            list.Add(face.Corners);
        }
    }

    /// <summary>Picks against the model's generated surfaces so carved out faces are selectable.</summary>
    private void PickSurface(SceneToolContext ctx, CSGModel model)
    {
        HandleContext handles = ctx.Handles;

        // A click on nothing clears the surfaces only, rather than falling through to scene picking
        ControlID empty = handles.GetControlID(EmptyControl);
        handles.AddControl(empty, HandleContext.BodyDistance, float.MaxValue - 1f);
        if (handles.IsNearest(empty) && handles.TryBeginDrag(empty))
        {
            if (!handles.Shift && !handles.Ctrl) CSGSurfaceSelection.Clear();
            return;
        }

        if (_paint != Paint.None && !handles.PrimaryHeld)
        {
            _paint = Paint.None;
            handles.TryEndDrag(handles.GetControlID(SurfaceControl));
        }

        if (!model.Raycast(handles.MouseRay, out CSGBrush brush, out int plane, out Float3 point, b => !CSGVisibility.IsHidden(b))) return;
        if (plane < 0) return;

        var hit = new CSGSurfaceRef(brush, plane);
        if (!hit.IsValid) return;

        if (_paint == Paint.Add) CSGSurfaceSelection.Add(hit);
        else if (_paint == Paint.Remove) CSGSurfaceSelection.Remove(hit);

        ControlID id = handles.GetControlID(SurfaceControl);
        handles.AddControl(id, HandleContext.BodyDistance, Float3.Distance(handles.MouseRay.Origin, point));
        handles.RequestCursor(id, PaperCursor.Pointer);

        DrawSurface(hit, HoverColor, HoverEdge, 1.5f);

        // Holding the smear key paints the last picked surface's look onto whatever is under the cursor.
        if (handles.IsHeld("CSG/Smear"))
        {
            if (handles.PrimaryHeld && !hit.Equals(_lastSmeared))
            {
                _lastSmeared = hit;
                CSGSurfaceSelection.SmearOnto(hit);
            }
            return;
        }
        _lastSmeared = default;

        if (!handles.IsNearest(id) || !handles.TryBeginDrag(id)) return;

        if (handles.Shift)
        {
            CSGSurfaceSelection.Add(hit);
            _paint = Paint.Add;
        }
        else if (handles.Ctrl)
        {
            CSGSurfaceSelection.Toggle(hit);
            _paint = CSGSurfaceSelection.IsSelected(hit) ? Paint.Add : Paint.Remove;
        }
        else
        {
            CSGSurfaceSelection.Select(hit);
            handles.TryEndDrag(id);
        }
    }

    private void DrawSelection()
    {
        foreach (CSGSurfaceRef surface in CSGSurfaceSelection.Selected)
            DrawSurface(surface, SelectedFill, SelectedEdge, 2f);
    }

    /// <summary>Draws the pieces of a surface the model shows, which a carving brush may have cut down.</summary>
    private void DrawSurface(CSGSurfaceRef surface, Color32 fill, Color32 edge, float thickness)
    {
        if (!_pieces.TryGetValue(surface, out List<Float3[]>? pieces)) return;
        foreach (Float3[] corners in pieces)
        {
            CSGGizmos.Polygon(corners, fill);
            CSGGizmos.Polyline(corners, edge, thickness, closed: true);
        }
    }
}
