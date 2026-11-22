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
    public bool Active => CSGEditMode.IsUV;

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
            _started = false;
            CSGSurfaceSelection.BeginTextureDrag();
        }
    }

    // A texture drag in progress: the face pressed, where on it, and whether rotate or scale has passed its dead zone
    private bool _dragging, _moved, _started;
    private CSGSurfaceRef _pressed;
    private Float3 _pressPoint, _pressNormal;
    private Float2 _pressScreen;
    private float _startAngle, _startDistance;

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
        if (!_moved || !OnPressPlane(handles.MouseRay, out Float3 at)) return;

        Color32 colour = SelectedEdge;
        ctx.Draw.Dot(_pressPoint, colour, 7f, HandleCap.Circle);

        switch (CSGEditMode.Current)
        {
            case CSGMode.UVMove:
            {
                Float3 target = Input.GetKey(KeyCode.V) ? NearestCorner(handles, at) ?? at : at;
                Float3 delta = target - _pressPoint;
                if (!Input.GetKey(KeyCode.V)) delta = CSGSnap.Value(handles, delta);
                CSGSurfaceSelection.MoveTextures(delta);
                CSGGizmos.Line(_pressPoint, _pressPoint + delta, colour, 1.5f);
                ctx.Draw.Dot(_pressPoint + delta, colour, 7f);
                break;
            }

            case CSGMode.UVRotate:
            {
                float radius = Float3.Distance(at, _pressPoint);
                if (!_started)
                {
                    if (Float2.Distance(handles.MousePosition, _pressScreen) < DeadZone) break;
                    _startAngle = CSGSurfaceSelection.AngleOnSurface(_pressNormal, _pressPoint, at);
                    _started = true;
                }

                float angle = CSGSurfaceSelection.AngleOnSurface(_pressNormal, _pressPoint, at) - _startAngle;
                if (SceneTools.RotationSnap != handles.Ctrl) angle = MathF.Round(angle / SceneTools.RotateSnap) * SceneTools.RotateSnap;
                CSGSurfaceSelection.RotateTextures(_pressPoint, angle);

                CSGGizmos.Circle(_pressPoint, _pressNormal, radius, colour, 1.5f);
                CSGGizmos.Line(_pressPoint, at, colour, 1.5f);
                ctx.Draw.Label(at, $"{angle:0.#}", colour);
                break;
            }

            case CSGMode.UVScale:
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
