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
        /// <summary>Clicking out the corners of a polygon on the locked work plane.</summary>
        Outline,
    }

    // The corners of a polygon being drawn, in the work plane's frame
    private readonly List<Float2> _outline = new();

    public override string Name => "CSG Generate";
    public override string Icon => EditorIcons.Plus;
    public override string? Tooltip => "Draw new CSG brushes";
    public override int Order => 1;

    private const string DrawControl = "csg_generate";
    private const float MinimumFootprint = 1e-3f;

    private static readonly Color32 GuideColor = new(140, 200, 255, 200);

    /// <summary>Whether drawing is the CSG mode in force.</summary>
    public bool Drawing => CSGEditMode.Is(CSGMode.Generate);

    /// <summary>Operation the next brush is created with.</summary>
    public Operation Operation { get; set; } = Operation.Additive;

    /// <summary>Which shape the next drag produces.</summary>
    public CSGGeneratorKind Generator { get; set; } = CSGGeneratorKind.Box;

    /// <summary>Parameters for the shapes that have them.</summary>
    public CSGGeneratorSettings Settings = CSGGeneratorSettings.Default;

    private Stage _stage = Stage.Idle;
    private object? _stageView;
    private Float3 _planeOrigin;
    private Quaternion _planeRotation = Quaternion.Identity;
    private Float3 _cornerA;
    private Float3 _cornerB;
    private float _height;

    // Started on a model surface, so pulling into it means digging
    private bool _onSurface;
    private bool _hoverOnSurface;

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

        // A shape in progress belongs to the scene view it started in
        if (_stage != Stage.Idle && !ReferenceEquals(ctx.View, _stageView)) return;

        if (_stage == Stage.Idle) PickShapeKey(handles);

        switch (_stage)
        {
            case Stage.Idle: TickIdle(ctx, id); break;
            case Stage.Footprint: TickFootprint(ctx, id); break;
            case Stage.Extrude: TickExtrude(ctx); break;
            case Stage.Outline: TickOutline(ctx, id); break;
        }
    }

    private void TickIdle(SceneToolContext ctx, ControlID id)
    {
        // No cursor preview while the pointer is on a panel.
        if (!ctx.Handles.ViewportHovered) return;
        if (!TryHoverPoint(ctx, out Float3 point, out Float3 normal, out _hoverOnSurface)) return;

        _planeRotation = CSGBrushGeometry.FrameFor(normal);

        // Snap where the brush starts, then slide it back onto the surface so a slope keeps it
        Float3 snapped = CSGSnap.Value(ctx.Handles, point);
        _planeOrigin = snapped - normal * Float3.Dot(snapped - point, normal);

        DrawWorkGrid(ctx, _planeOrigin);
        DrawCursor(ctx, _planeOrigin, normal, Tint(ctx.Handles));

        if (!ctx.Handles.IsNearest(id) || !ctx.Handles.TryBeginDrag(id)) return;

        // The plane is locked at the first press.
        _cornerA = Float3.Zero;
        _cornerB = Float3.Zero;
        _height = 0f;
        _onSurface = _hoverOnSurface;
        _stage = Stage.Footprint;
        _stageView = ctx.View;

        if (Generator == CSGGeneratorKind.Polygon)
        {
            ctx.Handles.TryEndDrag(id);
            _outline.Clear();
            _outline.Add(Float2.Zero);
            _stage = Stage.Outline;
        }
    }

    /// <summary>Each click adds a corner. Clicking the first corner again, or Enter, closes the outline for its height.</summary>
    private void TickOutline(SceneToolContext ctx, ControlID id)
    {
        HandleContext handles = ctx.Handles;
        Float2 cursor = _outline[^1];
        if (TryPlanePoint(ctx, out Float3 local))
            cursor = new Float2(CSGSnap.Value(handles, local.X), CSGSnap.Value(handles, local.Z));

        if (handles.KeyDown(KeyCode.Backspace) && _outline.Count > 1) _outline.RemoveAt(_outline.Count - 1);

        Float3 first = ToWorld(new Float3(_outline[0].X, 0f, _outline[0].Y));
        bool nearFirst = _outline.Count >= 3 && handles.WorldToScreen(first) is Float2 screen && Float2.Distance(screen, handles.MousePosition) < 10f;

        var tint = Tint(handles);
        DrawWorkGrid(ctx, ToWorld(new Float3(cursor.X, 0f, cursor.Y)));
        var ring = new List<Float3>(_outline.Count + 1);
        foreach (Float2 p in _outline) ring.Add(ToWorld(new Float3(p.X, 0f, p.Y)));
        ring.Add(nearFirst ? first : ToWorld(new Float3(cursor.X, 0f, cursor.Y)));
        CSGGizmos.Polyline(ring.ToArray(), tint.Color, 2f, closed: false);
        foreach (Float3 corner in ring) ctx.Draw.Dot(corner, tint.Color, 6f);
        if (nearFirst) CSGGizmos.Circle(first, _planeRotation * Float3.UnitY, 0.2f, tint.Color, 2f);

        bool close = _outline.Count >= 3 && handles.IsPressed("CSG/Confirm");
        if (handles.IsNearest(id) && handles.TryBeginDrag(id))
        {
            handles.TryEndDrag(id);
            if (nearFirst) close = true;
            else if (Float2.Distance(cursor, _outline[^1]) > 1e-3f) _outline.Add(cursor);
        }

        if (!close) return;

        // The footprint corners frame the outline, which is what the height pull is measured beside
        Float2 min = _outline[0], max = _outline[0];
        foreach (Float2 p in _outline) { min = Maths.Min(min, p); max = Maths.Max(max, p); }
        _cornerA = new Float3(min.X, 0f, min.Y);
        _cornerB = new Float3(max.X, 0f, max.Y);
        _stage = Stage.Extrude;
    }

    private void TickFootprint(SceneToolContext ctx, ControlID id)
    {
        if (TryPlanePoint(ctx, out Float3 local))
            _cornerB = new Float3(CSGSnap.Value(ctx.Handles, local.X), local.Y, CSGSnap.Value(ctx.Handles, local.Z));

        HandleTyping(ctx.Handles);
        DrawWorkGrid(ctx, ToWorld(_cornerB));
        DrawFootprint(ctx, Tint(ctx.Handles));
        DrawTyped(ctx);

        if (!ctx.Handles.PrimaryUp) return;

        ctx.Handles.TryEndDrag(id);

        // A press without a drag is a misclick, not a zero size brush.
        if (!FootprintIsValid())
        {
            _stage = Stage.Idle;
            return;
        }

        _stage = Stage.Extrude;
    }

    private void TickExtrude(SceneToolContext ctx)
    {
        _height = CSGSnap.Value(ctx.Handles, HeightUnderCursor(ctx));

        HandleTyping(ctx.Handles);
        UpdateGhost(OperationFor(ctx.Handles));
        DrawPreview(ctx, Tint(ctx.Handles));
        DrawTyped(ctx);

        if (ctx.Handles.IsPressed("CSG/Confirm")) { Commit(ctx, OperationFor(ctx.Handles)); return; }

        // The commit click is a raw press, so check the cursor is on the scene rather than a panel.
        if (ctx.Handles.PrimaryDown && ctx.Handles.ViewportHovered) Commit(ctx, OperationFor(ctx.Handles));
    }

    private void PickShapeKey(HandleContext handles)
    {
        if (handles.IsPressed("CSG/ShapeBox")) Generator = CSGGeneratorKind.Box;
        else if (handles.IsPressed("CSG/ShapeCylinder")) Generator = CSGGeneratorKind.Cylinder;
        else if (handles.IsPressed("CSG/ShapeSphere")) Generator = CSGGeneratorKind.Sphere;
        else if (handles.IsPressed("CSG/ShapeLinearStairs")) Generator = CSGGeneratorKind.LinearStairs;
        else if (handles.IsPressed("CSG/ShapeSpiralStairs")) Generator = CSGGeneratorKind.SpiralStairs;
        else if (handles.IsPressed("CSG/ShapeRoom")) Generator = CSGGeneratorKind.Room;
        else if (handles.IsPressed("CSG/ShapePolygon")) Generator = CSGGeneratorKind.Polygon;
    }

    /// <summary>Where a new brush would start: the surface under the cursor, or the grid plane.</summary>
    private static bool TryHoverPoint(SceneToolContext ctx, out Float3 point, out Float3 normal, out bool onSurface)
    {
        Ray ray = ctx.Handles.MouseRay;

        onSurface = TryHitBrush(ray, out point, out normal);
        if (onSurface) return true;

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

    /// <summary>New brushes go into the selected group when there is one, else the model. A shape's own group is never used.</summary>
    private static GameObject ParentFor(CSGModel model)
    {
        foreach (CSGGroup group in CSGSelection.Groups())
        {
            if (group.HoldsShape) continue;
            if (ReferenceEquals(CSGSelection.ModelOf(group.GameObject), model)) return group.GameObject;
        }
        return model.GameObject;
    }

    /// <summary>
    /// The operation the brush will get. Pulling an additive brush into the surface it started on digs, and holding
    /// Shift swaps add and subtract for one brush.
    /// </summary>
    private Operation OperationFor(HandleContext handles)
    {
        Operation operation = Operation == Operation.Additive && _onSurface && _stage == Stage.Extrude && _height < 0f
            ? Operation.Subtractive
            : Operation;

        return !handles.Shift ? operation : operation switch
        {
            Operation.Additive => Operation.Subtractive,
            Operation.Subtractive => Operation.Additive,
            _ => operation,
        };
    }

    private (Color32 Color, string Label) Tint(HandleContext handles)
    {
        Operation operation = OperationFor(handles);
        return (CSGObjectTool.ColorFor(operation), Loc.Get(CSGSections.OperationLabel(operation)));
    }

    private void Commit(SceneToolContext ctx, Operation operation)
    {
        ClearGhost();
        CSGModel? model = CSGSelection.ActiveModel();
        Float3 size = Float3.Zero;
        GameObject? created = model.IsValid() ? Place(model!, operation, out size) : null;
        if (created.IsNotValid())
        {
            Cancel();
            return;
        }

        // Remembered so the shape's size and settings can be changed later
        CSGShape shape = created!.AddComponent<CSGShape>();
        shape.Kind = Generator;
        shape.Settings = Settings;
        shape.Size = size;
        if (Generator == CSGGeneratorKind.Polygon)
        {
            shape.Outline = [.. _outline];
            shape.Height = _height;
        }

        Undo.RegisterCreatedObjectUndo(created, "Create CSG Brush");
        Selection.Select(created);
        model!.SetDirty();
        Cancel();
    }

    /// <summary>Builds the drawn shape into the model and returns the object that holds it, or null when it encloses nothing.</summary>
    private GameObject? Place(CSGModel model, Operation operation, out Float3 size)
    {
        size = Float3.Zero;
        if (Generator == CSGGeneratorKind.Polygon) return PlacePolygon(model, operation);

        if (!ShapeFrame(out Float3 center, out size, out Quaternion rotation)) return null;

        List<CSGShapePart> parts = CSGShapeBuilder.Build(Generator, size, Settings);
        if (parts.Count == 0) return null;

        Float3 worldCentre = _planeOrigin + _planeRotation * center;

        // A shape that needs more than one brush gets a group so it moves and folds as one.
        GameObject root;
        if (parts.Count > 1)
        {
            root = CSGSelection.CreateObject(Generator.ToString(), ParentFor(model));
            root.Transform.Position = worldCentre;
            root.Transform.Rotation = rotation;

            // A hollow shape drawn subtractive digs itself in: its parts act on the model directly, so the walls stay
            // and the inside is carved out of whatever was there, rather than the whole shell being cut away
            bool digs = operation == Operation.Subtractive && parts.Exists(p => p.Operation == Operation.Subtractive);
            if (!digs)
            {
                CSGGroup group = root.AddComponent<CSGGroup>();
                group.HoldsShape = true;
                group.Operation = operation;
            }
        }
        else root = ParentFor(model);

        for (int i = 0; i < parts.Count; i++)
        {
            GameObject go = CSGSelection.CreateObject(parts.Count > 1 ? $"Step {i + 1}" : Generator.ToString(), root);

            if (parts.Count > 1) go.Transform.LocalPosition = parts[i].Offset;
            else
            {
                go.Transform.Position = worldCentre + rotation * parts[i].Offset;
                go.Transform.Rotation = rotation;
            }

            // Whatever scale the parents carry, the shape lands in the world exactly as it was drawn
            Float4x4 drawn = Float4x4.CreateTRS(worldCentre + rotation * parts[i].Offset, rotation, Float3.One);
            CSGBrush brush = go.AddComponent<CSGBrush>();
            brush.Planes = CSGBrushGeometry.Transform(parts[i].Planes, go.Transform.WorldToLocalMatrix * drawn);
            CSGSelection.ApplySmoothing(brush, parts[i].SmoothPlanes);

            // Parts of a shape carry their own operation, the group or the shape as a whole carries the drawn one
            brush.Operation = parts.Count > 1 ? parts[i].Operation : operation;
        }

        return parts.Count > 1 ? root : root.Children[^1];
    }

    private GameObject? PlacePolygon(CSGModel model, Operation operation)
    {
        Plane[]? planes = CSGShapeBuilder.PolygonPrism(_outline, _height);
        if (planes == null) return null;

        GameObject go = CSGSelection.CreateObject(Generator.ToString(), ParentFor(model));
        go.Transform.Position = _planeOrigin;
        go.Transform.Rotation = _planeRotation;

        Float4x4 drawn = Float4x4.CreateTRS(_planeOrigin, _planeRotation, Float3.One);
        CSGBrush brush = go.AddComponent<CSGBrush>();
        brush.Planes = CSGBrushGeometry.Transform(planes, go.Transform.WorldToLocalMatrix * drawn);
        brush.Operation = operation;
        return go;
    }

    // The shape as it would be placed, built into the model while the height is pulled so the real result shows
    private GameObject? _ghost;
    private (Float3 A, Float3 B, float Height, CSGGeneratorKind Kind, CSGGeneratorSettings Settings, Operation Operation) _ghostKey;

    private void UpdateGhost(Operation operation)
    {
        var key = (_cornerA, _cornerB, _height, Generator, Settings, operation);
        if (_ghost.IsValid() && key.Equals(_ghostKey)) return;

        ClearGhost();
        _ghostKey = key;

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return;

        _ghost = Place(model!, operation, out _);
        if (_ghost.IsValid()) HideGhost(_ghost!);
    }

    /// <summary>Left out of the hierarchy, picking, gizmos, saving and undo.</summary>
    private static void HideGhost(GameObject go)
    {
        go.HideFlags |= HideFlags.Hide | HideFlags.HideAndDontSave | HideFlags.NotEditable | HideFlags.NoGizmos;
        foreach (GameObject child in go.Children) HideGhost(child);
    }

    private void ClearGhost()
    {
        if (_ghost.IsValid()) _ghost!.Destroy();
        _ghost = null;
    }

    /// <summary>Abandons an in progress brush, leaving the tool armed.</summary>
    public void Cancel()
    {
        ClearGhost();
        _stage = Stage.Idle;
        ClearTyped();
    }

    // Sizes typed while drawing: width, depth and height, and which one the keys go to
    private readonly string[] _typed = ["", "", ""];
    private int _field = -1;

    private void ClearTyped()
    {
        _typed[0] = _typed[1] = _typed[2] = "";
        _field = -1;
    }

    /// <summary>
    /// Digits set an exact size while drawing: width first, Tab moves to depth and then height, Backspace corrects and
    /// Enter places the brush. A typed size overrides the cursor, which still picks the direction.
    /// </summary>
    private void HandleTyping(HandleContext handles)
    {
        if (handles.KeysActive)
        {
            foreach (char c in Input.InputString)
            {
                if (!char.IsDigit(c) && c != '.' && c != '-') continue;
                if (_field < 0) _field = 0;
                _typed[_field] += c;
            }

            if (_field >= 0 && handles.KeyDown(KeyCode.Backspace) && _typed[_field].Length > 0)
                _typed[_field] = _typed[_field][..^1];

            if (handles.KeyDown(KeyCode.Tab))
            {
                _field = (_field + 1) % 3;
                if (_field == 2 && _stage == Stage.Footprint && FootprintIsValid()) _stage = Stage.Extrude;
            }
        }

        if (Typed(0, out float width)) _cornerB = new Float3(_cornerA.X + Side(_cornerB.X - _cornerA.X) * width, _cornerB.Y, _cornerB.Z);
        if (Typed(1, out float depth)) _cornerB = new Float3(_cornerB.X, _cornerB.Y, _cornerA.Z + Side(_cornerB.Z - _cornerA.Z) * depth);
        if (_stage == Stage.Extrude && Typed(2, out float height)) _height = Side(_height) * height;
    }

    private bool Typed(int field, out float value)
        => float.TryParse(_typed[field], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0f;

    private static float Side(float value) => value < 0f ? -1f : 1f;

    private bool FootprintIsValid()
        => MathF.Abs(_cornerB.X - _cornerA.X) >= MinimumFootprint && MathF.Abs(_cornerB.Z - _cornerA.Z) >= MinimumFootprint;

    /// <summary>The typed sizes beside the shape, the one being typed into in brackets.</summary>
    private void DrawTyped(SceneToolContext ctx)
    {
        if (_field < 0) return;

        string Field(int i, string name)
        {
            string value = _typed[i].Length > 0 ? _typed[i] : "_";
            return i == _field ? $"[{name} {value}]" : $"{name} {value}";
        }

        Float3 at = ToWorld(new Float3(_cornerB.X, _height, _cornerB.Z));
        ctx.Draw.Label(at, $"{Field(0, "W")}  {Field(1, "D")}  {Field(2, "H")}", GuideColor);
    }

    /// <summary>The grid of the surface being drawn on, around the cursor, so snapping can be seen on walls and slopes too.</summary>
    private void DrawWorkGrid(SceneToolContext ctx, Float3 around)
    {
        const int Lines = 8;
        float step = CSGSnap.Grid;
        Float3 x = _planeRotation * Float3.UnitX;
        Float3 z = _planeRotation * Float3.UnitZ;

        // Lines through grid points of the plane's own frame, centred on the cursor's nearest grid point
        Float3 local = Quaternion.Inverse(_planeRotation) * (around - _planeOrigin);
        Float3 centre = _planeOrigin + _planeRotation * new Float3(CSGSnap.Round(local.X), 0f, CSGSnap.Round(local.Z));
        float reach = step * Lines;

        for (int i = -Lines; i <= Lines; i++)
        {
            byte alpha = (byte)(70 * (1f - MathF.Abs(i) / (float)(Lines + 1)));
            var colour = new Color32(GuideColor.R, GuideColor.G, GuideColor.B, alpha);
            CSGGizmos.Line(centre + x * (i * step) - z * reach, centre + x * (i * step) + z * reach, colour);
            CSGGizmos.Line(centre + z * (i * step) - x * reach, centre + z * (i * step) + x * reach, colour);
        }
    }

    private void DrawCursor(SceneToolContext ctx, Float3 point, Float3 normal, (Color32 Color, string Label) tint)
    {
        CSGGizmos.Circle(point, normal, 0.15f, tint.Color);

        Float3 x = _planeRotation * Float3.UnitX;
        Float3 z = _planeRotation * Float3.UnitZ;
        CSGGizmos.Line(point - x * 0.6f, point + x * 0.6f, tint.Color);
        CSGGizmos.Line(point - z * 0.6f, point + z * 0.6f, tint.Color);
        ctx.Draw.Label(point + normal * 0.3f, tint.Label, tint.Color);
    }

    private void DrawFootprint(SceneToolContext ctx, (Color32 Color, string Label) tint)
    {
        Span<Float3> corners = stackalloc Float3[4];
        FootprintCorners(0f, corners);

        CSGGizmos.Polygon(corners, new Color32(tint.Color.R, tint.Color.G, tint.Color.B, 40));
        CSGGizmos.Polyline(corners, tint.Color, 2f, closed: true);

        ctx.Draw.LengthLabel(corners[0], corners[1], tint.Color);
        ctx.Draw.LengthLabel(corners[1], corners[2], tint.Color);
    }

    /// <summary>Draws the bounds as a faint cage with the shape inside it, in the colour of its operation.</summary>
    private void DrawPreview(SceneToolContext ctx, (Color32 Color, string Label) tint)
    {
        Span<Float3> bottom = stackalloc Float3[4];
        Span<Float3> top = stackalloc Float3[4];
        FootprintCorners(0f, bottom);
        FootprintCorners(_height, top);

        CSGGizmos.Polyline(bottom, GuideColor, 1f, closed: true);
        CSGGizmos.Polyline(top, GuideColor, 1f, closed: true);
        for (int i = 0; i < 4; i++) CSGGizmos.Line(bottom[i], top[i], GuideColor, 1f);

        DrawShapePreview(ctx, tint.Color);

        ctx.Draw.LengthLabel(bottom[0], top[0], tint.Color);
        ctx.Draw.Label(top[2], tint.Label, tint.Color);
    }

    private void DrawShapePreview(SceneToolContext ctx, Color32 color)
    {
        if (Generator == CSGGeneratorKind.Polygon)
        {
            Plane[]? prism = CSGShapeBuilder.PolygonPrism(_outline, _height);
            if (prism == null || GeometryGenerator.ConvexFromPlanes(prism).Solid is not { } shape) return;
            foreach (GeometryData.Edge edge in shape.Edges)
                CSGGizmos.Line(ToWorld(edge.Vert1.Point), ToWorld(edge.Vert2.Point), color, 2f);
            return;
        }

        if (!ShapeFrame(out Float3 centre, out Float3 size, out Quaternion rotation)) return;

        Float3 worldCentre = ToWorld(centre);
        foreach (CSGShapePart part in CSGShapeBuilder.Build(Generator, size, Settings))
        {
            ConvexSolidResult solid = GeometryGenerator.ConvexFromPlanes(part.Planes);
            if (!solid.IsValid) continue;

            foreach (GeometryData.Edge edge in solid.Solid!.Edges)
            {
                CSGGizmos.Line(worldCentre + rotation * (part.Offset + edge.Vert1.Point),
                              worldCentre + rotation * (part.Offset + edge.Vert2.Point), color, 2f);
            }
        }
    }

    private Float3 ToWorld(Float3 local) => _planeOrigin + _planeRotation * local;

    /// <summary>
    /// The drawn box in the work plane's frame and the rotation its shape is built in. Stairs turn to climb toward
    /// where the drag ended, along whichever footprint side is longer in that direction.
    /// </summary>
    private bool ShapeFrame(out Float3 centre, out Float3 size, out Quaternion rotation)
    {
        rotation = _planeRotation;
        if (!CSGBrushGeometry.BoxFromDrag(_cornerA, _cornerB, _height, out centre, out size)) return false;
        if (Generator != CSGGeneratorKind.LinearStairs) return true;

        Float3 drag = _cornerB - _cornerA;
        float turn;
        if (MathF.Abs(drag.Z) >= MathF.Abs(drag.X)) turn = drag.Z >= 0f ? 0f : MathF.PI;
        else
        {
            turn = drag.X >= 0f ? MathF.PI * 0.5f : -MathF.PI * 0.5f;
            size = new Float3(size.Z, size.Y, size.X);
        }

        rotation = _planeRotation * Quaternion.AxisAngle(Float3.UnitY, turn);
        return true;
    }

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
