// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
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

/// <summary>What a drag in the Surface mode does to the texture of the selected surfaces.</summary>
public enum SurfaceDrag { Move, Rotate, Scale }

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

    /// <summary>What a drag on the selected surfaces does to their texture.</summary>
    public SurfaceDrag Drag { get; set; } = SurfaceDrag.Move;

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

        // The transform tool keys pick what a drag does
        if (ctx.Handles.IsPressed("Scene/ToolTranslate")) Drag = SurfaceDrag.Move;
        else if (ctx.Handles.IsPressed("Scene/ToolRotate")) Drag = SurfaceDrag.Rotate;
        else if (ctx.Handles.IsPressed("Scene/ToolScale")) Drag = SurfaceDrag.Scale;

        if (ctx.Handles.KeyDown(KeyCode.Escape))
        {
            if (CSGSurfaceSelection.Count == 0) CSGEditMode.Current = CSGMode.Select;
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

        if (_dragging)
        {
            DragTexture(ctx, model);
            return;
        }

        if (_paint != Paint.None && !handles.PrimaryHeld)
        {
            _paint = Paint.None;
            handles.TryEndDrag(handles.GetControlID(SurfaceControl));
        }

        if (!model.Raycast(handles.MouseRay, out CSGBrush brush, out int plane, out Float3 point, out Float3 normal, b => !CSGVisibility.IsHidden(b))) return;
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
            // A drag on an unselected face works on that face alone, one on a selected face takes the whole selection
            if (!CSGSurfaceSelection.IsSelected(hit)) CSGSurfaceSelection.Select(hit);
            _dragging = true;
            _moved = false;
            _pressed = hit;
            _pressPoint = Input.GetKey(KeyCode.V) ? NearestCorner(handles, point) ?? point : point;
            _pressNormal = normal;
            _pressScreen = handles.MousePosition;
            _pointer.Reset(_pressScreen);
            _started = false;
            CSGSurfaceSelection.BeginTextureDrag();
        }
    }

    // A texture drag in progress: the face pressed, where on it, and whether rotate or scale has passed its dead zone
    private bool _dragging, _moved, _started;
    private CSGSurfaceRef _pressed;
    private Float3 _pressPoint, _pressNormal;
    private Float2 _pressScreen;
    private readonly SlowPointer _pointer = new();
    private float _startAngle, _startDistance;
    private Float3 _rotateFrom;

    private const float DeadZone = 12f;

    /// <summary>
    /// Moves, turns or scales the selected textures by the drag, on the plane of the face it began on. A click that
    /// never moved selects only that face.
    /// </summary>
    private void DragTexture(SceneToolContext ctx, CSGModel model)
    {
        HandleContext handles = ctx.Handles;
        if (!handles.PrimaryHeld)
        {
            _dragging = false;
            handles.TryEndDrag(handles.GetControlID(SurfaceControl));
            if (!_moved) CSGSurfaceSelection.Select(_pressed);
            return;
        }

        if (Float2.Distance(handles.MousePosition, _pressScreen) > 3f) _moved = true;
        // Shift slows the drag for fine changes
        if (!_moved || !OnPressPlane(handles.RayThrough(_pointer.Follow(handles)), out Float3 at)) return;

        Color32 colour = SelectedEdge;
        ctx.Draw.Dot(_pressPoint, colour, 7f, HandleCap.Circle);

        switch (Drag)
        {
            case SurfaceDrag.Move:
            {
                // Corners first, then edges, then the grid. V snaps to corners only
                bool cornersOnly = Input.GetKey(KeyCode.V);
                Float3? snapped = NearestCorner(handles, at) ?? (cornersOnly ? null : NearestEdgePoint(handles));
                Float3 delta = (snapped ?? at) - _pressPoint;
                if (snapped == null && !cornersOnly) delta = CSGSnap.Value(handles, delta);
                if (snapped is Float3 point) ctx.Draw.Dot(point, colour, 9f, HandleCap.Square);
                CSGSurfaceSelection.MoveTextures(delta);
                CSGGizmos.Line(_pressPoint, _pressPoint + delta, colour, 1.5f);
                ctx.Draw.Dot(_pressPoint + delta, colour, 7f);
                break;
            }

            case SurfaceDrag.Rotate:
            {
                float radius = Float3.Distance(at, _pressPoint);
                if (!_started)
                {
                    if (Float2.Distance(handles.MousePosition, _pressScreen) < DeadZone) break;
                    _startAngle = CSGSurfaceSelection.AngleOnSurface(_pressNormal, _pressPoint, at);
                    _rotateFrom = at;
                    _started = true;
                }

                float snap = SceneTools.RotationSnap != handles.Ctrl ? SceneTools.RotateSnap : 0f;
                float angle = CSGSurfaceSelection.AngleOnSurface(_pressNormal, _pressPoint, at) - _startAngle;
                if (snap > 0f) angle = MathF.Round(angle / snap) * snap;
                CSGSurfaceSelection.RotateTextures(_pressPoint, _rotateFrom, at, snap);

                CSGGizmos.Circle(_pressPoint, _pressNormal, radius, colour, 1.5f);
                CSGGizmos.Line(_pressPoint, at, colour, 1.5f);
                ctx.Draw.Label(at, $"{angle:0.#}", colour);
                break;
            }

            case SurfaceDrag.Scale:
            {
                float distance = Float3.Distance(at, _pressPoint);
                if (!_started)
                {
                    if (Float2.Distance(handles.MousePosition, _pressScreen) < DeadZone) break;
                    _startDistance = MathF.Max(1e-4f, distance);
                    _started = true;
                }

                float factor = distance / _startDistance;
                if (CSGSnap.Active(handles)) factor = MathF.Max(SceneTools.ScaleSnap, MathF.Round(factor / SceneTools.ScaleSnap) * SceneTools.ScaleSnap);
                factor = MathF.Max(0.01f, factor);
                CSGSurfaceSelection.ScaleTextures(_pressPoint, factor);

                CSGGizmos.Line(_pressPoint, at, colour, 1.5f);
                ctx.Draw.Label(at, $"x{factor:0.##}", colour);
                break;
            }
        }
    }

    private bool OnPressPlane(Ray ray, out Float3 point)
    {
        point = _pressPoint;
        float denom = Float3.Dot(_pressNormal, ray.Direction);
        if (MathF.Abs(denom) < 1e-6f) return false;
        float t = Float3.Dot(_pressPoint - ray.Origin, _pressNormal) / denom;
        if (t < 0f) return false;
        point = ray.Origin + ray.Direction * t;
        return true;
    }

    /// <summary>The point on a selected surface's edge nearest the cursor on screen, within a few pixels.</summary>
    private Float3? NearestEdgePoint(HandleContext handles)
    {
        Float3? best = null;
        float bestDistance = 8f;
        Float2 mouse = handles.MousePosition;
        foreach (CSGSurfaceRef surface in CSGSurfaceSelection.Selected)
        {
            if (!_pieces.TryGetValue(surface, out List<Float3[]>? pieces)) continue;
            foreach (Float3[] corners in pieces)
            {
                for (int i = 0; i < corners.Length; i++)
                {
                    Float3 a = corners[i], b = corners[(i + 1) % corners.Length];
                    if (handles.WorldToScreen(a) is not Float2 sa || handles.WorldToScreen(b) is not Float2 sb) continue;

                    Float2 edge = sb - sa;
                    float t = Math.Clamp(Float2.Dot(mouse - sa, edge) / MathF.Max(1e-6f, Float2.Dot(edge, edge)), 0f, 1f);
                    float distance = Float2.Distance(mouse, sa + edge * t);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = a + (b - a) * t;
                }
            }
        }
        return best;
    }

    /// <summary>The corner of a selected surface nearest the cursor on screen, within a few pixels.</summary>
    private Float3? NearestCorner(HandleContext handles, Float3 fallback)
    {
        Float3? best = null;
        float bestDistance = 12f;
        foreach (CSGSurfaceRef surface in CSGSurfaceSelection.Selected)
        {
            if (!_pieces.TryGetValue(surface, out List<Float3[]>? pieces)) continue;
            foreach (Float3[] corners in pieces)
                foreach (Float3 corner in corners)
                {
                    if (handles.WorldToScreen(corner) is not Float2 screen) continue;
                    float distance = Float2.Distance(screen, handles.MousePosition);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = corner;
                }
        }
        return best;
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
