// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Operation = Prowl.Vector.Geometry.CSGOperation;
using SColor = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Draws new brushes: pick a work plane, drag out a footprint on it, then pull the height.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGGenerateTool : SceneTool
{
    private enum Stage
    {
        /// <summary>Hovering, no plane committed to yet.</summary>
        Idle,
        /// <summary>Dragging the footprint out on the locked work plane.</summary>
        Footprint,
        /// <summary>Pulling the height along the plane normal.</summary>
        Extrude,
    }

    public override string Name => "CSG Generate";
    public override string Icon => EditorIcons.Plus;
    public override string? Tooltip => "Draw new CSG brushes";
    public override int Order => 1;

    private const string DrawControl = "csg_generate";
    private const float MinimumFootprint = 1e-3f;

    private static readonly Color32 GuideColor = new(140, 200, 255, 200);
    private static readonly Color32 SolidColor = new(120, 220, 160, 255);
    private static readonly Color32 PlaneColor = new(140, 200, 255, 40);

    /// <summary>Whether drawing is the CSG mode in force.</summary>
    public bool Drawing => CSGEditMode.Is(CSGMode.Generate);

    /// <summary>Operation the next brush is created with.</summary>
    public Operation Operation { get; set; } = Operation.Additive;

    /// <summary>Which shape the next drag produces.</summary>
    public CSGGeneratorKind Generator { get; set; } = CSGGeneratorKind.Box;

    /// <summary>Parameters for the shapes that have them.</summary>
    public CSGGeneratorSettings Settings = CSGGeneratorSettings.Default;

    private Stage _stage = Stage.Idle;
    private Float3 _planeOrigin;
    private Quaternion _planeRotation = Quaternion.Identity;
    private Float3 _cornerA;
    private Float3 _cornerB;
    private float _height;

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    /// <summary>While armed, drawing owns the button so the gizmo cannot win the press.</summary>
    public override bool SuppressTransformGizmo => Drawing;

    public override void OnDeactivated() => Cancel();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        if (!Drawing)
        {
            Cancel();
            return;
        }

        HandleContext handles = ctx.Handles;

        // Escape abandons the shape in progress, and disarms only once there is nothing to abandon.
        if (handles.KeyDown(KeyCode.Escape))
        {
            if (_stage != Stage.Idle) Cancel();
            else CSGEditMode.Current = CSGMode.Object;
            return;
        }

        // While drawing, the tool owns the viewport so the press does not fall through to object picking.
        ControlID id = handles.GetControlID(DrawControl);
        handles.AddControl(id, HandleContext.BodyDistance, float.MaxValue - 1f);
        handles.RequestCursor(id, PaperCursor.Crosshair);

        switch (_stage)
        {
            case Stage.Idle: TickIdle(ctx, id); break;
            case Stage.Footprint: TickFootprint(ctx, id); break;
            case Stage.Extrude: TickExtrude(ctx); break;
        }
    }

    private void TickIdle(SceneToolContext ctx, ControlID id)
    {
        // No cursor preview while the pointer is on a panel.
        if (!ctx.Handles.ViewportHovered) return;
        if (!TryHoverPoint(ctx, out Float3 point, out Float3 normal)) return;

        _planeRotation = CSGBrushGeometry.FrameFor(normal);

        // Snap where the brush starts, then slide it back onto the surface so a slope keeps it
        Float3 snapped = CSGSnap.Value(ctx.Handles, point);
        _planeOrigin = snapped - normal * Float3.Dot(snapped - point, normal);

        DrawCursor(ctx, _planeOrigin, normal);

        if (!ctx.Handles.IsNearest(id) || !ctx.Handles.TryBeginDrag(id)) return;

        // The plane is locked at the first press.
        _cornerA = Float3.Zero;
        _cornerB = Float3.Zero;
        _height = 0f;
        _stage = Stage.Footprint;
    }

    private void TickFootprint(SceneToolContext ctx, ControlID id)
    {
        if (TryPlanePoint(ctx, out Float3 local))
            _cornerB = new Float3(CSGSnap.Value(ctx.Handles, local.X), local.Y, CSGSnap.Value(ctx.Handles, local.Z));

        DrawFootprint(ctx);

        if (!ctx.Handles.PrimaryUp) return;

        ctx.Handles.TryEndDrag(id);

        // A press without a drag is a misclick, not a zero size brush.
        if (MathF.Abs(_cornerB.X - _cornerA.X) < MinimumFootprint ||
            MathF.Abs(_cornerB.Z - _cornerA.Z) < MinimumFootprint)
        {
            _stage = Stage.Idle;
            return;
        }

        _stage = Stage.Extrude;
    }

    private void TickExtrude(SceneToolContext ctx)
    {
        _height = CSGSnap.Value(ctx.Handles, HeightUnderCursor(ctx));

        DrawPreview(ctx);

        if (ctx.Handles.KeyDown(KeyCode.Enter)) { Commit(ctx); return; }

        // The commit click is a raw press, so check the cursor is on the scene rather than a panel.
        if (ctx.Handles.PrimaryDown && ctx.Handles.ViewportHovered) Commit(ctx);
    }

    /// <summary>Where a new brush would start: the surface under the cursor, or the grid plane.</summary>
    private static bool TryHoverPoint(SceneToolContext ctx, out Float3 point, out Float3 normal)
    {
        Ray ray = ctx.Handles.MouseRay;

        if (TryHitBrush(ray, out point, out normal)) return true;

        // Grid plane through the origin, a ray from below hits its back face.
        float denom = ray.Direction.Y;
        if (MathF.Abs(denom) < 1e-6f) { normal = Float3.UnitY; return false; }

        float t = -ray.Origin.Y / denom;
        if (t < 0f) { normal = Float3.UnitY; return false; }

        point = ray.Origin + ray.Direction * t;
        normal = Float3.UnitY;
        return true;
    }

    private static bool TryHitBrush(Ray ray, out Float3 point, out Float3 normal)
    {
        point = Float3.Zero;
        normal = Float3.UnitY;

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return false;

        // The generated surfaces, so drawing works inside carved rooms and never on hidden or subtractive faces
        if (!model!.Raycast(ray, out CSGBrush brush, out int plane, out point, b => !CSGVisibility.IsHidden(b)) || plane < 0) return false;

        normal = CSGSelection.WorldNormal(brush, plane);
        return true;
    }

    /// <summary>The cursor on the locked work plane, in the plane's own frame.</summary>
    private bool TryPlanePoint(SceneToolContext ctx, out Float3 local)
    {
        local = Float3.Zero;

        Ray ray = ctx.Handles.MouseRay;
        Float3 normal = _planeRotation * Float3.UnitY;

        float denom = Float3.Dot(ray.Direction, normal);
        if (MathF.Abs(denom) < 1e-6f) return false;

        float t = Float3.Dot(_planeOrigin - ray.Origin, normal) / denom;
        if (t < 0f) return false;

        local = Quaternion.Inverse(_planeRotation) * (ray.Origin + ray.Direction * t - _planeOrigin);
        return true;
    }

    /// <summary>Height along the plane normal, from the ray against the plane containing the extrude axis that most faces the camera.</summary>
    private float HeightUnderCursor(SceneToolContext ctx)
    {
        Ray ray = ctx.Handles.MouseRay;

        Float3 up = _planeRotation * Float3.UnitY;
        Float3 anchor = _planeOrigin + _planeRotation * new Float3(
            (_cornerA.X + _cornerB.X) * 0.5f, 0f, (_cornerA.Z + _cornerB.Z) * 0.5f);

        // Of the two axes spanning the work plane, use whichever faces the camera more squarely.
        Float3 x = _planeRotation * Float3.UnitX;
        Float3 z = _planeRotation * Float3.UnitZ;
        Float3 toCamera = ray.Origin - anchor;
        Float3 normal = MathF.Abs(Float3.Dot(toCamera, x)) > MathF.Abs(Float3.Dot(toCamera, z)) ? x : z;

        float denom = Float3.Dot(ray.Direction, normal);
        if (MathF.Abs(denom) < 1e-6f) return _height;

        float t = Float3.Dot(anchor - ray.Origin, normal) / denom;
        if (t < 0f) return _height;

        return Float3.Dot(ray.Origin + ray.Direction * t - anchor, up);
    }

    // Groups this tool made to hold a multi brush shape, which new shapes never go into
    private readonly HashSet<GameObject> _shapeGroups = new();

    /// <summary>New brushes go into the selected group when there is one, else the model.</summary>
    private GameObject ParentFor(CSGModel model)
    {
        _shapeGroups.RemoveWhere(go => go.IsNotValid());
        foreach (CSGGroup group in CSGSelection.Groups())
        {
            if (_shapeGroups.Contains(group.GameObject)) continue;
            if (ReferenceEquals(CSGSelection.ModelOf(group.GameObject), model)) return group.GameObject;
        }
        return model.GameObject;
    }

    private void Commit(SceneToolContext ctx)
    {
        if (!CSGBrushGeometry.BoxFromDrag(_cornerA, _cornerB, _height, out Float3 center, out Float3 size))
        {
            Cancel();
            return;
        }

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) { Cancel(); return; }

        List<CSGShapePart> parts = CSGShapeBuilder.Build(Generator, size, Settings);
        if (parts.Count == 0) { Cancel(); return; }

        Float3 worldCentre = _planeOrigin + _planeRotation * center;

        // A shape that needs more than one brush gets a group so it moves and folds as one.
        GameObject root;
        if (parts.Count > 1)
        {
            root = CSGSelection.CreateObject(Generator.ToString(), ParentFor(model!));
            root.Transform.Position = worldCentre;
            root.Transform.Rotation = _planeRotation;

            _shapeGroups.Add(root);
            CSGGroup group = root.AddComponent<CSGGroup>();
            group.Operation = Operation;
        }
        else root = ParentFor(model!);

        for (int i = 0; i < parts.Count; i++)
        {
            GameObject go = CSGSelection.CreateObject(parts.Count > 1 ? $"Step {i + 1}" : Generator.ToString(), root);

            if (parts.Count > 1) go.Transform.LocalPosition = parts[i].Offset;
            else
            {
                go.Transform.Position = worldCentre + _planeRotation * parts[i].Offset;
                go.Transform.Rotation = _planeRotation;
            }

            // Whatever scale the parents carry, the shape lands in the world exactly as it was drawn
            Float4x4 drawn = Float4x4.CreateTRS(worldCentre + _planeRotation * parts[i].Offset, _planeRotation, Float3.One);
            CSGBrush brush = go.AddComponent<CSGBrush>();
            brush.Planes = CSGBrushGeometry.Transform(parts[i].Planes, go.Transform.WorldToLocalMatrix * drawn);
            ApplySmoothing(brush, parts[i].SmoothPlanes);

            // Children of a group fold into it, so the group carries the operation instead.
            brush.Operation = parts.Count > 1 ? Vector.Geometry.CSGOperation.Additive : Operation;
        }

        GameObject created = parts.Count > 1 ? root : root.Children[^1];
        Undo.RegisterCreatedObjectUndo(created, "Create CSG Brush");
        Selection.Select(created);

        model.SetDirty();

        _stage = Stage.Idle;
    }

    /// <summary>Puts the shape's curved faces in one smoothing group, unique per generated brush.</summary>
    private static void ApplySmoothing(CSGBrush brush, int smoothPlanes)
    {
        if (smoothPlanes <= 0) return;

        int group = CSGSurfaceSelection.UnusedSmoothingGroup();
        SurfaceData[] surfaces = brush.Surfaces;

        for (int i = 0; i < smoothPlanes && i < surfaces.Length; i++)
        {
            SurfaceData data = surfaces[i];
            data.SmoothingGroup = group;
            brush.SetSurface(i, data);
        }
    }

    /// <summary>Abandons an in progress brush, leaving the tool armed.</summary>
    public void Cancel() => _stage = Stage.Idle;

    private void DrawCursor(SceneToolContext ctx, Float3 point, Float3 normal)
    {
        ctx.Draw.Circle(point, normal, 0.15f, GuideColor);

        Float3 x = _planeRotation * Float3.UnitX;
        Float3 z = _planeRotation * Float3.UnitZ;
        ctx.Draw.Line(point - x * 0.6f, point + x * 0.6f, GuideColor);
        ctx.Draw.Line(point - z * 0.6f, point + z * 0.6f, GuideColor);
    }

    private void DrawFootprint(SceneToolContext ctx)
    {
        Span<Float3> corners = stackalloc Float3[4];
        FootprintCorners(0f, corners);

        ctx.Draw.Polygon(corners, PlaneColor);
        ctx.Draw.Polyline(corners, GuideColor, 2f, closed: true);

        ctx.Draw.LengthLabel(corners[0], corners[1], GuideColor);
        ctx.Draw.LengthLabel(corners[1], corners[2], GuideColor);
    }

    /// <summary>Draws the bounds as a faint cage with the shape inside it.</summary>
    private void DrawPreview(SceneToolContext ctx)
    {
        Span<Float3> bottom = stackalloc Float3[4];
        Span<Float3> top = stackalloc Float3[4];
        FootprintCorners(0f, bottom);
        FootprintCorners(_height, top);

        ctx.Draw.Polyline(bottom, GuideColor, 1f, closed: true);
        ctx.Draw.Polyline(top, GuideColor, 1f, closed: true);
        for (int i = 0; i < 4; i++) ctx.Draw.Line(bottom[i], top[i], GuideColor, 1f);

        DrawShapePreview(ctx);

        ctx.Draw.LengthLabel(bottom[0], top[0], SolidColor);
    }

    private void DrawShapePreview(SceneToolContext ctx)
    {
        if (!CSGBrushGeometry.BoxFromDrag(_cornerA, _cornerB, _height, out Float3 centre, out Float3 size))
            return;

        foreach (CSGShapePart part in CSGShapeBuilder.Build(Generator, size, Settings))
        {
            ConvexSolidResult solid = GeometryGenerator.ConvexFromPlanes(part.Planes);
            if (!solid.IsValid) continue;

            Float3 origin = centre + part.Offset;
            foreach (GeometryData.Edge edge in solid.Solid!.Edges)
            {
                ctx.Draw.Line(ToWorld(origin + edge.Vert1.Point),
                              ToWorld(origin + edge.Vert2.Point), SolidColor, 2f);
            }
        }
    }

    private Float3 ToWorld(Float3 local) => _planeOrigin + _planeRotation * local;

    private void FootprintCorners(float height, Span<Float3> corners)
    {
        float minX = MathF.Min(_cornerA.X, _cornerB.X), maxX = MathF.Max(_cornerA.X, _cornerB.X);
        float minZ = MathF.Min(_cornerA.Z, _cornerB.Z), maxZ = MathF.Max(_cornerA.Z, _cornerB.Z);

        corners[0] = ToWorld(minX, height, minZ);
        corners[1] = ToWorld(maxX, height, minZ);
        corners[2] = ToWorld(maxX, height, maxZ);
        corners[3] = ToWorld(minX, height, maxZ);
    }

    private Float3 ToWorld(float x, float y, float z)
        => _planeOrigin + _planeRotation * new Float3(x, y, z);
}
