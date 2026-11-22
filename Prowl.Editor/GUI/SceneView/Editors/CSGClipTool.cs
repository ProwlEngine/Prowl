// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Cuts the selected brushes with a plane through two points and the view direction, a third point tilts it.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGClipTool : SceneTool
{
    private enum Stage
    {
        /// <summary>Nothing placed. The next click sets the cut start and locks the work plane.</summary>
        Idle,
        /// <summary>One point placed. The cut follows the cursor across the locked plane.</summary>
        Aiming,
        /// <summary>The line is fixed. A further click tilts the cut.</summary>
        Tilting,
    }

    public override string Name => "CSG Clip";
    public override string Icon => EditorIcons.HandScissors;
    public override string? Tooltip => "Cut brushes with a plane";
    public override int Order => 3;

    private const string ClipControl = "csg_clip";

    private static readonly Color32 PlaneFill = new(255, 120, 120, 40);
    private static readonly Color32 PlaneEdge = new(255, 150, 140, 220);
    private static readonly Color32 DiscardColor = new(255, 90, 90, 160);
    private static readonly Color32 KeepColor = new(120, 220, 160, 220);

    /// <summary>Which half survives the cut.</summary>
    public CSGClipMode Mode { get; set; } = CSGClipMode.RemoveFront;

    public bool Active => CSGEditMode.Is(CSGMode.Clip);

    /// <summary>True once there is a cut worth applying.</summary>
    public bool HasPlane => _stage == Stage.Tilting;

    private Stage _stage = Stage.Idle;
    private object? _stageView;

    // The plane the clip points are placed on, locked at the first click.
    private Float3 _workOrigin;
    private Float3 _workNormal = Float3.UnitY;

    private Float3 _pointA;
    private Float3 _pointB;
    private Float3 _tilt;
    private bool _tilted;

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override bool SuppressTransformGizmo => Active;

    public override void OnDeactivated() => Cancel();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        if (!Active)
        {
            Cancel();
            return;
        }

        HandleContext handles = ctx.Handles;

        if (handles.KeyDown(KeyCode.Escape))
        {
            if (_stage != Stage.Idle) Cancel();
            else CSGEditMode.Current = CSGMode.Object;
            return;
        }

        // Tab cycles which half survives, so a cut can be reversed without starting over.
        if (handles.IsPressed("CSG/ClipSwapSide")) Mode = NextMode(Mode);
        if (_stage == Stage.Tilting && handles.IsPressed("CSG/Confirm")) { Commit(ctx); return; }

        ControlID id = handles.GetControlID(ClipControl);
        handles.AddControl(id, HandleContext.BodyDistance, float.MaxValue - 1f);
        handles.RequestCursor(id, PaperCursor.Crosshair);

        bool clicked = handles.IsNearest(id) && handles.TryBeginDrag(id);
        handles.TryEndDrag(id);

        // A cut in progress belongs to the scene view it started in
        if (_stage != Stage.Idle && !ReferenceEquals(ctx.View, _stageView)) return;

        switch (_stage)
        {
            case Stage.Idle: TickIdle(ctx, clicked); break;
            case Stage.Aiming: TickAiming(ctx, clicked); break;
            case Stage.Tilting: TickTilting(ctx, clicked); break;
        }

        DrawPreview(ctx);
    }

    private void TickIdle(SceneToolContext ctx, bool clicked)
    {
        if (!ctx.Handles.ViewportHovered) return;
        if (!TrySurfacePoint(ctx, out Float3 point, out Float3 normal)) return;
        point = SnapOnSurface(ctx, point, normal);

        // Shows which plane the cut will be drawn on before the click.
        _workOrigin = point;
        _workNormal = normal;
        _pointA = point;
        _pointB = point;

        ctx.Draw.Circle(point, normal, 0.12f, PlaneEdge);

        // Ctrl click picks which brushes the cut applies to, without leaving clip mode
        if (clicked && ctx.Handles.Ctrl)
        {
            CSGModel? model = CSGSelection.ActiveModel();
            if (model.IsValid() && CSGSelection.PickBrush(model!, CSGSelection.BrushesIn(model!), ctx.Handles.MouseRay, out CSGBrush brush, out _))
            {
                if (Selection.IsSelected(brush.GameObject)) Selection.RemoveFromSelection(brush.GameObject);
                else Selection.AddToSelection(brush.GameObject);
            }
            return;
        }

        if (clicked)
        {
            _stage = Stage.Aiming;
            _stageView = ctx.View;
        }
    }

    /// <summary>The point snapped to the grid, unless snapping would carry it off the surface it is on.</summary>
    private static Float3 SnapOnSurface(SceneToolContext ctx, Float3 point, Float3 normal)
    {
        Float3 snapped = CSGSnap.OnPlane(ctx.Handles, point, normal);
        Float3 eye = ctx.Handles.MouseRay.Origin;
        var toward = new Ray(eye, Float3.Normalize(snapped - eye));
        if (!TrySurfaceAlong(toward, out Float3 hit)) return point;
        return Float3.Distance(hit, snapped) < 1e-3f ? snapped : point;
    }

    private void TickAiming(SceneToolContext ctx, bool clicked)
    {
        if (TryWorkPlanePoint(ctx, out Float3 point)) _pointB = CSGSnap.OnPlane(ctx.Handles, point, _workNormal);

        if (!clicked) return;
        if (Float3.Distance(_pointA, _pointB) < 1e-3f) return;

        _stage = Stage.Tilting;
        _tilted = false;
    }

    private void TickTilting(SceneToolContext ctx, bool clicked)
    {
        if (!clicked) return;

        // The first click here tilts the plane off the view axis, the next one commits.
        if (!_tilted && ctx.Handles.ViewportHovered
            && TrySurfacePoint(ctx, out Float3 point, out Float3 normal)
            && CSGClip.PlaneFromPoints(_pointA, _pointB, SnapOnSurface(ctx, point, normal), out _))
        {
            _tilt = SnapOnSurface(ctx, point, normal);
            _tilted = true;
            return;
        }

        Commit(ctx);
    }

    private bool TryPlane(SceneToolContext ctx, out Plane plane)
    {
        if (_tilted) return CSGClip.PlaneFromPoints(_pointA, _pointB, _tilt, out plane);

        // Without a third point the cut runs straight down through the work plane.
        return CSGClip.PlaneFromEdge(_pointA, _pointB, _workNormal, out plane);
    }

    /// <summary>Where the cursor meets the locked work plane.</summary>
    private bool TryWorkPlanePoint(SceneToolContext ctx, out Float3 point)
    {
        point = Float3.Zero;
        Ray ray = ctx.Handles.MouseRay;

        float denom = Float3.Dot(ray.Direction, _workNormal);
        if (MathF.Abs(denom) < 1e-6f) return false;

        float t = Float3.Dot(_workOrigin - ray.Origin, _workNormal) / denom;
        if (t < 0f) return false;

        point = ray.Origin + ray.Direction * t;
        return true;
    }

    /// <summary>The generated surface under the cursor, or the grid when there is none.</summary>
    private static bool TrySurfacePoint(SceneToolContext ctx, out Float3 point, out Float3 normal)
        => TrySurfaceAlong(ctx.Handles.MouseRay, out point, out normal);

    private static bool TrySurfaceAlong(Ray ray, out Float3 point) => TrySurfaceAlong(ray, out point, out _);

    private static bool TrySurfaceAlong(Ray ray, out Float3 point, out Float3 normal)
    {
        point = Float3.Zero;
        normal = Float3.UnitY;

        CSGModel? model = CSGSelection.ActiveModel();

        if (model.IsValid() && model!.Raycast(ray, out CSGBrush brush, out int plane, out Float3 hit, b => !CSGVisibility.IsHidden(b)))
        {
            point = hit;
            normal = plane >= 0 ? CSGSelection.WorldNormal(brush, plane) : Float3.UnitY;
            return true;
        }

        float denom = ray.Direction.Y;
        if (MathF.Abs(denom) < 1e-6f) return false;

        float g = -ray.Origin.Y / denom;
        if (g < 0f) return false;

        point = ray.Origin + ray.Direction * g;
        normal = Float3.UnitY;
        return true;
    }

    private static List<CSGBrush> Brushes()
    {
        List<CSGBrush> selected = CSGSelection.Brushes();
        if (selected.Count > 0) return selected;

        // Nothing selected means the whole model.
        var all = new List<CSGBrush>();
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return all;

        foreach (GameObject go in model.GameObject.GetChildrenDeep())
        {
            CSGBrush brush = go.GetComponent<CSGBrush>();
            if (brush.IsValid() && brush.EnabledInHierarchy && !CSGVisibility.IsHidden(brush))
                all.Add(brush);
        }
        return all;
    }

    public void Commit(SceneToolContext ctx)
    {
        if (_stage != Stage.Tilting || !TryPlane(ctx, out Plane worldPlane)) { Cancel(); return; }

        List<CSGBrush> brushes = Brushes();
        if (brushes.Count == 0) { Cancel(); return; }

        foreach (CSGBrush brush in brushes) Undo.RecordObject(brush, "Clip CSG Brush");
        foreach (CSGBrush brush in brushes) ClipBrush(brush, worldPlane);

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid()) model!.SetDirty();

        Cancel();
    }

    private void ClipBrush(CSGBrush brush, Plane worldPlane)
    {
        Plane local = ToLocal(brush, worldPlane);

        // Leave a brush alone when the plane misses it.
        if (!CSGClip.Crosses(LocalCorners(brush), local)) return;

        List<Plane[]> pieces = CSGClip.Clip(brush.Planes, local, Mode);
        if (pieces.Count == 0) return;

        SurfaceData[] surfaces = (SurfaceData[])brush.Surfaces.Clone();
        int cutFace = brush.Planes.Length;
        SurfaceData cutSurface = CSGSelection.NearestSurface(brush, pieces[0][cutFace].Normal, cutFace);

        brush.Planes = pieces[0];
        brush.SetSurface(cutFace, cutSurface);

        // The other half of a split becomes the next sibling, textured like the original
        for (int i = 1; i < pieces.Count; i++)
        {
            GameObject go = CSGSelection.CreateObject(brush.GameObject.Name, brush.GameObject.Parent);
            go.Transform.LocalPosition = brush.Transform.LocalPosition;
            go.Transform.LocalRotation = brush.Transform.LocalRotation;
            go.Transform.LocalScale = brush.Transform.LocalScale;

            CSGBrush half = go.AddComponent<CSGBrush>();
            half.Planes = pieces[i];
            half.Operation = brush.Operation;
            half.Flags = brush.Flags;
            for (int s = 0; s < surfaces.Length; s++) half.SetSurface(s, surfaces[s]);
            half.SetSurface(cutFace, cutSurface);

            CSGSelection.PlaceAfter(go, brush.GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Split CSG Brush");
        }
    }

    private static Plane ToLocal(CSGBrush brush, Plane world)
    {
        Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;
        Float3 origin = Float4x4.TransformPoint(world.Normal * world.D, brush.Transform.WorldToLocalMatrix);

        // A plane normal goes back through the transpose of the forward transform, which stays right under non uniform scale
        Float3 n = world.Normal;
        Float3 local = new(Float3.Dot(toWorld.c0.XYZ, n), Float3.Dot(toWorld.c1.XYZ, n), Float3.Dot(toWorld.c2.XYZ, n));
        Float3 normal = Float3.NormalizeSafe(local, Float3.UnitY);

        return new Plane(normal, Float3.Dot(normal, origin));
    }

    private static List<Float3> LocalCorners(CSGBrush brush)
    {
        var corners = new List<Float3>();
        if (brush.Shape == null) return corners;

        foreach (var v in brush.Shape.Vertices) corners.Add(v.Point);
        return corners;
    }

    public void Cancel()
    {
        _stage = Stage.Idle;
        _tilted = false;
    }

    private static CSGClipMode NextMode(CSGClipMode mode) => mode switch
    {
        CSGClipMode.RemoveFront => CSGClipMode.RemoveBack,
        CSGClipMode.RemoveBack => CSGClipMode.Split,
        _ => CSGClipMode.RemoveFront,
    };

    private void DrawPreview(SceneToolContext ctx)
    {
        if (_stage == Stage.Idle) return;
        if (!TryPlane(ctx, out Plane plane)) return;

        ctx.Draw.Line(_pointA, _pointB, PlaneEdge, 2f);
        DrawPlaneQuad(ctx, plane);
        DrawOutcome(ctx, plane);
    }

    /// <summary>The cut plane, sized to span the brushes it crosses.</summary>
    private static void DrawPlaneQuad(SceneToolContext ctx, Plane plane)
    {
        Float3 centre = plane.Normal * plane.D;
        float extent = 1f;

        foreach (CSGBrush brush in Brushes())
        {
            Float3 p = brush.Transform.Position;
            extent = MathF.Max(extent, Float3.Distance(p, centre) + Float3.Length(brush.Transform.LocalScale) * 2f);
        }

        Quaternion frame = CSGBrushGeometry.FrameFor(plane.Normal);
        Float3 u = frame * Float3.UnitX, v = frame * Float3.UnitZ;

        Span<Float3> quad =
        [
            centre - u * extent - v * extent,
            centre + u * extent - v * extent,
            centre + u * extent + v * extent,
            centre - u * extent + v * extent,
        ];

        ctx.Draw.Polygon(quad, PlaneFill);
        ctx.Draw.Polyline(quad, PlaneEdge, 1f, closed: true);
    }

    /// <summary>Colours each brush's corners by which side of the cut they fall on.</summary>
    private void DrawOutcome(SceneToolContext ctx, Plane worldPlane)
    {
        foreach (CSGBrush brush in Brushes())
        {
            if (brush.Shape == null) continue;

            Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;
            foreach (var v in brush.Shape.Vertices)
            {
                Float3 world = Float4x4.TransformPoint(v.Point, toWorld);
                bool front = worldPlane.GetSignedDistanceToPoint(world) > 0f;

                bool kept = Mode == CSGClipMode.Split
                    || (Mode == CSGClipMode.RemoveFront ? !front : front);

                ctx.Draw.Dot(world, kept ? KeepColor : DiscardColor, 5f);
            }
        }
    }
}
