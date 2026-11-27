// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI.Gizmo;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// The Rect transform tool: a rectangle over the active object's sprite or mesh in its own XY plane. Edges and corners
/// resize it by scaling the object while the opposite side stays put, the body moves it in the plane, and just outside a
/// corner turns it about its origin. UI elements have their own rect editor.
/// </summary>
[GlobalSceneTool]
public sealed class RectTool : SceneTool
{
    public override string Name => "Rect";
    public override string Icon => EditorIcons.VectorSquare;

    public override bool SuppressTransformGizmo => SceneTools.Transform == TransformTool.Rect;

    private enum Handle { None, Move, L, R, B, T, BL, BR, TR, TL, Rotate }

    private const string HandleControl = "rect_tool_handle";
    private const float PickRadius = 9f;
    private const float RotateRadius = 24f;
    private const float ClickThreshold = 4f;
    private const float MinSize = 1e-4f;

    private static readonly Color OutlineColor = new(0.80f, 0.75f, 0.80f, 1f);
    private static readonly Color MoveColor = new(0.40f, 1.00f, 0.60f, 1f);
    private static readonly Color HotColor = new(1f, 1f, 1f, 1f);

    private readonly Texture2D _handleTexture = Texture2D.ParseDefault(DefaultTexture.Handle);

    private Handle _hover, _active;
    private ControlID _control;
    private bool _moveIsClick;

    private GameObject? _target;
    private Float4x4 _startToWorld;
    private Float3 _startPosition, _startLocalScale, _startHitWorld;
    private Quaternion _startRotation;
    private Float2 _startMin, _startMax, _startHit;

    public override void OnSceneInput(SceneToolContext toolContext)
    {
        HandleContext ctx = toolContext.Handles;
        GameObject? go = toolContext.ActiveObject;
        if (SceneTools.Transform != TransformTool.Rect || go.IsNotValid() || go.RectTransform.IsValid() || !LocalRect(go, out Float2 min, out Float2 max))
        {
            if (_active != Handle.None) EndDrag(ctx);
            _hover = Handle.None;
            return;
        }
        if (_active != Handle.None && !ReferenceEquals(go, _target)) EndDrag(ctx);

        bool dragging = _active != Handle.None;
        Float4x4 toWorld = dragging ? _startToWorld : go.Transform.LocalToWorldMatrix;
        Float3 ToWorld(Float2 p) => Float4x4.TransformPoint(new Float3(p.X, p.Y, 0f), toWorld);

        DrawHandles(min, max, ToWorld);
        if (ctx.Blocked && !dragging)
        {
            _hover = Handle.None;
            return;
        }

        Float3 origin = ToWorld(Float2.Zero);
        Float3 normal = Float3.Cross(ToWorld(Float2.UnitX) - origin, ToWorld(Float2.UnitY) - origin);
        if (Float3.LengthSquared(normal) < 1e-12f) return;
        normal = Float3.Normalize(normal);

        Ray ray = ctx.MouseRay;
        if (!GizmoUtils.IntersectPlane(normal, origin, ray.Origin, ray.Direction, out float distance))
        {
            if (!dragging) _hover = Handle.None;
            return;
        }
        Float3 hitWorld = ray.Origin + ray.Direction * distance;
        Float3 local = Float4x4.TransformPoint(hitWorld, toWorld.Invert());
        Float2 hit = new(local.X, local.Y);

        Handle nearest = Register(ctx, min, max, ToWorld, out ControlID nearestId);
        _hover = dragging ? _active : nearest;

        if (!dragging && nearest != Handle.None && ctx.TryBeginDrag(nearestId))
        {
            _active = nearest;
            _control = nearestId;
            _moveIsClick = nearest == Handle.Move;
            _target = go;
            _startToWorld = toWorld;
            _startPosition = go.Transform.Position;
            _startLocalScale = go.Transform.LocalScale;
            _startRotation = go.Transform.Rotation;
            _startMin = min;
            _startMax = max;
            _startHit = hit;
            _startHitWorld = hitWorld;
            return;
        }
        if (!dragging) return;

        if (!ctx.PrimaryHeld)
        {
            if (_active == Handle.Move && _moveIsClick) SelectUnder(toolContext.Scene, ray, go, ctx.Ctrl);
            EndDrag(ctx);
            return;
        }
        if (_active == Handle.Move && _moveIsClick)
        {
            if (!ctx.DragExceededThreshold(_control, ClickThreshold)) return;
            _moveIsClick = false;
        }

        Undo.RecordObject(go, "Rect Tool");
        switch (_active)
        {
            case Handle.Move: Move(go, hitWorld - _startHitWorld); break;
            case Handle.Rotate: Rotate(go, normal, hitWorld); break;
            default: Resize(go, hit - _startHit, ctx.Shift); break;
        }
    }

    // A press in the rect that never moved is a click, which selects the nearest renderer under the pointer
    private static void SelectUnder(Scene scene, Ray ray, GameObject current, bool toggle)
    {
        GameObject? best = null;
        float bestDistance = float.MaxValue;
        foreach (GameObject go in scene.Objects)
        {
            if (!go.EnabledInHierarchy || go.HideFlags.HasFlag(HideFlags.Hide)) continue;
            foreach (Component component in go.GetComponents())
                if (component is IRaycastRenderer renderer && component.EnabledInHierarchy && renderer.Raycast(ray, out float distance) && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = go;
                }
        }
        if (best.IsNotValid() || ReferenceEquals(best, current)) return;
        if (toggle) Selection.ToggleSelection(best);
        else Selection.Select(best);
    }

    /// <summary>The rect the object's sprite or mesh covers in its own XY plane.</summary>
    internal static bool LocalRect(GameObject go, out Float2 min, out Float2 max)
    {
        SpriteRenderer? sprite = go.GetComponent<SpriteRenderer>();
        if (sprite.IsValid() && sprite.Sprite.IsValid())
        {
            min = sprite.Sprite.BoundsMin;
            max = sprite.Sprite.BoundsMax;
            if (sprite.FlipX) (min.X, max.X) = (-max.X, -min.X);
            if (sprite.FlipY) (min.Y, max.Y) = (-max.Y, -min.Y);
            return max.X - min.X > MinSize && max.Y - min.Y > MinSize;
        }
        MeshRenderer? mesh = go.GetComponent<MeshRenderer>();
        if (mesh.IsValid() && mesh.Mesh.IsValid())
        {
            AABB bounds = mesh.Mesh.bounds;
            min = new Float2(bounds.Min.X, bounds.Min.Y);
            max = new Float2(bounds.Max.X, bounds.Max.Y);
            return max.X - min.X > MinSize && max.Y - min.Y > MinSize;
        }
        min = max = Float2.Zero;
        return false;
    }

    /// <summary>
    /// The local scale and world position that move one side or corner of the rect to a new place while the opposite one
    /// stays where it is. Shift on a corner keeps the rect's proportions.
    /// </summary>
    internal static (Float3 LocalScale, Float3 Position) Resized(Float4x4 startToWorld, Float3 startPosition, Float3 startLocalScale,
        Float2 min, Float2 max, int dragX, int dragY, Float2 delta, bool keepAspect)
    {
        Float2 newMin = min, newMax = max;
        if (dragX < 0) newMin.X += delta.X; else if (dragX > 0) newMax.X += delta.X;
        if (dragY < 0) newMin.Y += delta.Y; else if (dragY > 0) newMax.Y += delta.Y;

        Float2 size = max - min;
        float kx = dragX == 0 ? 1f : (newMax.X - newMin.X) / size.X;
        float ky = dragY == 0 ? 1f : (newMax.Y - newMin.Y) / size.Y;
        if (keepAspect && dragX != 0 && dragY != 0)
            kx = ky = MathF.Abs(kx) > MathF.Abs(ky) ? kx : ky;
        if (MathF.Abs(kx) < MinSize) kx = MathF.CopySign(MinSize, kx == 0 ? 1 : kx);
        if (MathF.Abs(ky) < MinSize) ky = MathF.CopySign(MinSize, ky == 0 ? 1 : ky);

        Float2 fixedPoint = new(dragX < 0 ? max.X : min.X, dragY < 0 ? max.Y : min.Y);
        Float3 before = Float4x4.TransformPoint(new Float3(fixedPoint.X, fixedPoint.Y, 0f), startToWorld);
        Float3 after = Float4x4.TransformPoint(new Float3(fixedPoint.X * kx, fixedPoint.Y * ky, 0f), startToWorld);
        return (new Float3(startLocalScale.X * kx, startLocalScale.Y * ky, startLocalScale.Z), startPosition + before - after);
    }

    private void Resize(GameObject go, Float2 delta, bool keepAspect)
    {
        (int x, int y) = _active switch
        {
            Handle.L => (-1, 0), Handle.R => (1, 0), Handle.B => (0, -1), Handle.T => (0, 1),
            Handle.BL => (-1, -1), Handle.BR => (1, -1), Handle.TR => (1, 1), Handle.TL => (-1, 1),
            _ => (0, 0),
        };
        (Float3 scale, Float3 position) = Resized(_startToWorld, _startPosition, _startLocalScale, _startMin, _startMax, x, y, delta, keepAspect);
        go.Transform.LocalScale = scale;
        go.Transform.Position = position;
    }

    private void Move(GameObject go, Float3 delta)
    {
        Float3 position = _startPosition + delta;
        if (SceneTools.GridSnap && SceneTools.MoveSnap > 0f)
        {
            float step = SceneTools.MoveSnap;
            position = new Float3(MathF.Round(position.X / step) * step, MathF.Round(position.Y / step) * step, position.Z);
        }
        go.Transform.Position = position;
    }

    private void Rotate(GameObject go, Float3 normal, Float3 hitWorld)
    {
        Float3 from = _startHitWorld - _startPosition, to = hitWorld - _startPosition;
        if (Float3.LengthSquared(from) < 1e-10f || Float3.LengthSquared(to) < 1e-10f) return;
        float angle = MathF.Atan2(Float3.Dot(Float3.Cross(from, to), normal), Float3.Dot(from, to));
        if (SceneTools.RotationSnap && SceneTools.RotateSnap > 0f)
        {
            float step = Maths.ToRadians(SceneTools.RotateSnap);
            angle = MathF.Round(angle / step) * step;
        }
        go.Transform.Rotation = Quaternion.AxisAngle(normal, angle) * _startRotation;
    }

    // One control per handle, ties going to the last registered: body, rotation zones, edges, then corners
    private static Handle Register(HandleContext ctx, Float2 min, Float2 max, Func<Float2, Float3> toWorld, out ControlID nearestId)
    {
        Handle nearest = Handle.None;
        ControlID nearestControl = ControlID.None;

        void Add(Handle handle, int index, float distance, float depth, PaperCursor cursor)
        {
            ControlID id = ctx.GetControlID(HandleControl, index);
            ctx.AddControl(id, distance, depth);
            ctx.RequestCursor(id, cursor);
            if (ctx.IsNearest(id)) { nearest = handle; nearestControl = id; }
        }

        Float2 mid = (min + max) * 0.5f;
        Float2[] corners = [min, new Float2(max.X, min.Y), max, new Float2(min.X, max.Y)];
        Float2?[] screen = new Float2?[4];
        for (int i = 0; i < 4; i++) screen[i] = ctx.WorldToScreen(toWorld(corners[i]));
        bool inside = screen[0].HasValue && screen[1].HasValue && screen[2].HasValue && screen[3].HasValue
            && GizmoUtils.IsPointInPolygon(ctx.MousePosition, [screen[0]!.Value, screen[1]!.Value, screen[2]!.Value, screen[3]!.Value]);
        if (inside)
            Add(Handle.Move, 0, HandleContext.BodyDistance, ctx.DepthOf(toWorld(mid)), PaperCursor.ResizeAll);
        else
            for (int i = 0; i < 4; i++)
            {
                Float3 corner = toWorld(corners[i]);
                Add(Handle.Rotate, 1 + i, ctx.DistanceToPoint(corner, RotateRadius), ctx.DepthOf(corner), PaperCursor.Pointer);
            }

        void Point(Handle handle, int index, Float2 at, PaperCursor cursor)
        {
            Float3 world = toWorld(at);
            Add(handle, index, ctx.DistanceToPoint(world, PickRadius), ctx.DepthOf(world), cursor);
        }
        Point(Handle.B, 5, new Float2(mid.X, min.Y), PaperCursor.ResizeVertical);
        Point(Handle.T, 6, new Float2(mid.X, max.Y), PaperCursor.ResizeVertical);
        Point(Handle.L, 7, new Float2(min.X, mid.Y), PaperCursor.ResizeHorizontal);
        Point(Handle.R, 8, new Float2(max.X, mid.Y), PaperCursor.ResizeHorizontal);
        Point(Handle.BL, 9, corners[0], PaperCursor.ResizeNWSE);
        Point(Handle.BR, 10, corners[1], PaperCursor.ResizeNESW);
        Point(Handle.TR, 11, corners[2], PaperCursor.ResizeNWSE);
        Point(Handle.TL, 12, corners[3], PaperCursor.ResizeNESW);

        nearestId = nearestControl;
        return nearest;
    }

    private void DrawHandles(Float2 min, Float2 max, Func<Float2, Float3> toWorld)
    {
        bool moving = _active == Handle.Move || (_active == Handle.None && _hover == Handle.Move);
        Color outline = moving ? MoveColor : OutlineColor;
        Float3 bl = toWorld(min), br = toWorld(new Float2(max.X, min.Y)), tr = toWorld(max), tl = toWorld(new Float2(min.X, max.Y));
        Debug.DrawLine(bl, br, outline);
        Debug.DrawLine(br, tr, outline);
        Debug.DrawLine(tr, tl, outline);
        Debug.DrawLine(tl, bl, outline);

        Float2 mid = (min + max) * 0.5f;
        DrawHandle(Handle.BL, bl);
        DrawHandle(Handle.BR, br);
        DrawHandle(Handle.TR, tr);
        DrawHandle(Handle.TL, tl);
        DrawHandle(Handle.B, toWorld(new Float2(mid.X, min.Y)));
        DrawHandle(Handle.T, toWorld(new Float2(mid.X, max.Y)));
        DrawHandle(Handle.L, toWorld(new Float2(min.X, mid.Y)));
        DrawHandle(Handle.R, toWorld(new Float2(max.X, mid.Y)));
    }

    private void DrawHandle(Handle handle, Float3 world)
    {
        bool hot = _active == handle || (_active == Handle.None && _hover == handle);
        Debug.DrawIcon(_handleTexture, world, 7.5f, hot ? HotColor : OutlineColor);
    }

    private void EndDrag(HandleContext ctx)
    {
        ctx.TryEndDrag(_control);
        _active = Handle.None;
        _control = ControlID.None;
        _moveIsClick = false;
        _target = null;
    }
}
