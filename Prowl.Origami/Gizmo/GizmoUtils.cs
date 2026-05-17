// Based on: https://github.com/urholaukkarinen/transform-gizmo - Dual licensed under MIT and Apache 2.0.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.OrigamiUI.Gizmo;

public static class GizmoUtils
{
    public const float Deg2Rad = MathF.PI / 180f;
    public const float Rad2Deg = 180f / MathF.PI;

    public static bool IntersectPlane(Float3 planeNormal, Float3 planeOrigin, Float3 rayOrigin, Float3 rayDirection, out float t)
    {
        var denom = Float3.Dot(planeNormal, rayDirection);
        if (MathF.Abs(denom) < 1e-8f)
        {
            t = 0;
            return false;
        }
        t = Float3.Dot(planeOrigin - rayOrigin, planeNormal) / denom;
        return t >= 0;
    }

    public static (float, float) RayToPlaneOrigin(Float3 planeNormal, Float3 planeOrigin, Float3 rayOrigin, Float3 rayDirection)
    {
        if (IntersectPlane(planeNormal, planeOrigin, rayOrigin, rayDirection, out float t))
        {
            var p = rayOrigin + rayDirection * t;
            var v = p - planeOrigin;
            return (t, MathF.Sqrt(Float3.Dot(v, v)));
        }
        return (t, float.MaxValue);
    }

    public static float RoundToInterval(float value, float interval)
        => MathF.Round(value / interval) * interval;

    public static Float3 PointOnPlane(Float3 planeNormal, Float3 planeOrigin, Ray ray)
    {
        if (IntersectPlane(planeNormal, planeOrigin, ray.Origin, ray.Direction, out float t))
            return ray.Origin + ray.Direction * t;
        return Float3.Zero;
    }

    public static Float3 SnapTranslationVector(Float3 delta, TransformGizmo gizmo)
    {
        var deltaLength = Float3.Length(delta);
        if (deltaLength > 1e-5f)
            return delta / deltaLength * RoundToInterval(deltaLength, gizmo.SnapDistance);
        return delta;
    }

    public static Float3 SnapTranslationPlane(Float3 delta, GizmoDirection direction, TransformGizmo gizmo)
    {
        var bitangent = PlaneBitangent(direction);
        var tangent = PlaneTangent(direction);
        bitangent = gizmo.Rotation * bitangent;
        tangent = gizmo.Rotation * tangent;
        var cb = Float3.Cross(delta, -bitangent);
        var ct = Float3.Cross(delta, tangent);
        var lb = Float3.Length(cb);
        var lt = Float3.Length(ct);
        var n = GizmoNormal(gizmo, direction);

        if (lb > 1e-5f && lt > 1e-5f)
        {
            return bitangent * RoundToInterval(lt, gizmo.SnapDistance) * Float3.Dot(ct / lt, n)
                   + tangent * RoundToInterval(lb, gizmo.SnapDistance) * Float3.Dot(cb / lb, n);
        }
        return delta;
    }

    public static float PlaneSize(TransformGizmo gizmo)
        => gizmo.ScaleFactor * (gizmo.GizmoSize * 0.1f + gizmo.StrokeWidth * 2.0f);

    public static Float3 PlaneBitangent(GizmoDirection direction) => direction switch
    {
        GizmoDirection.X => Float3.UnitY,
        GizmoDirection.Y => Float3.UnitZ,
        GizmoDirection.Z => Float3.UnitX,
        _ => Float3.Zero,
    };

    public static Float3 PlaneTangent(GizmoDirection direction) => direction switch
    {
        GizmoDirection.X => Float3.UnitZ,
        GizmoDirection.Y => Float3.UnitX,
        GizmoDirection.Z => Float3.UnitY,
        _ => Float3.Zero,
    };

    public static Color32 GizmoColor(TransformGizmo gizmo, bool focused, GizmoDirection direction)
    {
        var (r, g, b) = direction switch
        {
            GizmoDirection.X => (226, 55, 56),
            GizmoDirection.Y => (94, 234, 141),
            GizmoDirection.Z => (39, 117, 255),
            _ => (255, 255, 255),
        };
        byte alpha = focused ? (byte)255 : (byte)200;
        return Color32.FromArgb(alpha, (byte)r, (byte)g, (byte)b);
    }

    public static Float3 GizmoLocalNormal(TransformGizmo gizmo, GizmoDirection direction) => direction switch
    {
        GizmoDirection.X => Float3.UnitX,
        GizmoDirection.Y => Float3.UnitY,
        GizmoDirection.Z => Float3.UnitZ,
        GizmoDirection.View => -gizmo.ViewForward,
        _ => Float3.Zero,
    };

    public static Float3 GizmoNormal(TransformGizmo gizmo, GizmoDirection direction)
    {
        Float3 norm = GizmoLocalNormal(gizmo, direction);
        if (gizmo.Orientation == TransformGizmo.GizmoOrientation.Local && direction != GizmoDirection.View)
            norm = gizmo.Rotation * norm;
        return norm;
    }

    /// <summary>
    /// Returns +1 or -1 to flip an axis so it always faces the camera.
    /// Arrows and plane handles use this to stay on the visible side of the gizmo.
    /// </summary>
    public static float AxisSign(TransformGizmo gizmo, GizmoDirection direction)
    {
        if (direction == GizmoDirection.View) return 1f;
        var worldAxis = GizmoNormal(gizmo, direction);
        var toCamera = gizmo.ViewPosition - gizmo.Translation;
        return Float3.Dot(toCamera, worldAxis) >= 0 ? 1f : -1f;
    }

    public static Float3 PlaneGlobalOrigin(TransformGizmo gizmo, GizmoDirection direction)
    {
        var origin = PlaneLocalOrigin(gizmo, direction);
        if (gizmo.Orientation == TransformGizmo.GizmoOrientation.Local)
            origin = gizmo.Rotation * origin;
        return origin + gizmo.Translation;
    }

    public static Float3 PlaneLocalOrigin(TransformGizmo gizmo, GizmoDirection direction)
    {
        var offset = gizmo.ScaleFactor * gizmo.GizmoSize * 0.5f;
        var bitangent = PlaneBitangent(direction);
        var tangent = PlaneTangent(direction);

        // Flip each axis independently so the plane handle stays on the camera-facing side
        var bDir = VectorToDirection(bitangent);
        var tDir = VectorToDirection(tangent);
        return (bitangent * AxisSign(gizmo, bDir) + tangent * AxisSign(gizmo, tDir)) * offset;
    }

    private static GizmoDirection VectorToDirection(Float3 v)
    {
        if (MathF.Abs(v.X) > 0.5f) return GizmoDirection.X;
        if (MathF.Abs(v.Y) > 0.5f) return GizmoDirection.Y;
        return GizmoDirection.Z;
    }

    public static float InnerCircleRadius(TransformGizmo gizmo)
        => gizmo.ScaleFactor * gizmo.GizmoSize * 0.2f;

    public static float OuterCircleRadius(TransformGizmo gizmo)
        => gizmo.ScaleFactor * (gizmo.GizmoSize + gizmo.StrokeWidth + 5.0f);

    public static PickResult PickArrow(TransformGizmo gizmo, Ray ray, GizmoDirection direction, TransformGizmoMode mode)
    {
        const float rayLength = 1e+14f;
        var normal = GizmoNormal(gizmo, direction) * AxisSign(gizmo, direction);
        var (start, end, length) = ArrowParams(gizmo, normal, mode);

        start += gizmo.Translation;
        end += gizmo.Translation;

        var (rayT, subGizmoT) = SegmentToSegment(ray.Origin, ray.Origin + ray.Direction * rayLength, start, end);
        var rayPoint = ray.Origin + ray.Direction * rayLength * rayT;
        var subGizmoPoint = start + normal * length * subGizmoT;
        var dist = Float3.Length(rayPoint - subGizmoPoint);

        return new PickResult { SubGizmoPoint = subGizmoPoint, T = rayT, Picked = dist <= gizmo.FocusDistance };
    }

    public static PickResult PickPlane(TransformGizmo gizmo, Ray ray, GizmoDirection direction)
    {
        var origin = PlaneGlobalOrigin(gizmo, direction);
        var normal = GizmoNormal(gizmo, direction);
        var (t, distFromOrigin) = RayToPlaneOrigin(normal, origin, ray.Origin, ray.Direction);
        return new PickResult { SubGizmoPoint = ray.Origin + ray.Direction * t, T = t, Picked = distFromOrigin <= PlaneSize(gizmo) };
    }

    public static PickResult PickCircle(TransformGizmo gizmo, Ray ray, float radius, bool filled)
    {
        var (t, dist) = RayToPlaneOrigin(-gizmo.ViewForward, gizmo.Translation, ray.Origin, ray.Direction);
        var hitPos = ray.Origin + ray.Direction * t;
        var picked = filled ? dist <= radius + gizmo.FocusDistance
            : MathF.Abs(dist - radius) <= gizmo.FocusDistance;
        return new PickResult { SubGizmoPoint = hitPos, T = t, Picked = picked };
    }

    public static bool ArrowModesOverlapping(TransformGizmoMode mode, TransformGizmoMode gizmoModes)
    {
        return (mode == TransformGizmoMode.TranslateX && gizmoModes.HasFlag(TransformGizmoMode.ScaleX))
               || (mode == TransformGizmoMode.TranslateY && gizmoModes.HasFlag(TransformGizmoMode.ScaleY))
               || (mode == TransformGizmoMode.TranslateZ && gizmoModes.HasFlag(TransformGizmoMode.ScaleZ))
               || (mode == TransformGizmoMode.ScaleX && gizmoModes.HasFlag(TransformGizmoMode.TranslateX))
               || (mode == TransformGizmoMode.ScaleY && gizmoModes.HasFlag(TransformGizmoMode.TranslateY))
               || (mode == TransformGizmoMode.ScaleZ && gizmoModes.HasFlag(TransformGizmoMode.TranslateZ));
    }

    public static (Float3 start, Float3 end, float length) ArrowParams(TransformGizmo gizmo, Float3 direction, TransformGizmoMode mode)
    {
        bool isTranslate = mode == TransformGizmoMode.TranslateX || mode == TransformGizmoMode.TranslateY || mode == TransformGizmoMode.TranslateZ;
        bool isScale = mode == TransformGizmoMode.ScaleX || mode == TransformGizmoMode.ScaleY || mode == TransformGizmoMode.ScaleZ;
        bool overlapping = ArrowModesOverlapping(mode, gizmo.Mode);

        var width = gizmo.ScaleFactor * gizmo.StrokeWidth;
        var gizmoSize = gizmo.ScaleFactor * gizmo.GizmoSize;
        Float3 start;
        float length;

        if (overlapping)
        {
            if (isTranslate)
            {
                // Translate in universal mode: full arrow from inner circle to end
                start = direction * (width * 0.5f + InnerCircleRadius(gizmo));
                length = gizmoSize - Float3.Length(start);
                length -= width * 2.0f;
            }
            else
            {
                // Scale in universal mode: short stub at the tip
                length = gizmoSize;
                start = direction * (length + width * 3.0f);
                length = length * 0.2f + width;
            }
        }
        else
        {
            // Standalone mode: full arrow from inner circle to end
            start = direction * (width * 0.5f + InnerCircleRadius(gizmo));
            length = gizmoSize - Float3.Length(start);
            length -= width * 2.0f;
        }

        return (start, start + direction * length, length);
    }

    public static (float, float) SegmentToSegment(Float3 a1, Float3 a2, Float3 b1, Float3 b2)
    {
        var da = a2 - a1;
        var db = b2 - b1;
        var la = Float3.Dot(da, da);
        var lb = Float3.Dot(db, db);
        var dd = Float3.Dot(da, db);
        var d1 = a1 - b1;
        var d = Float3.Dot(da, d1);
        var e = Float3.Dot(db, d1);
        var n = la * lb - dd * dd;

        float sn, tn;
        var sd = n;
        var td = n;

        if (n < 1e-8f) { sn = 0; sd = 1; tn = e; td = lb; }
        else
        {
            sn = dd * e - lb * d;
            tn = la * e - dd * d;
            if (sn < 0) { sn = 0; tn = e; td = lb; }
            else if (sn > sd) { sn = sd; tn = e + dd; td = lb; }
        }

        if (tn < 0)
        {
            tn = 0;
            if (-d < 0) sn = 0;
            else if (-d > la) sn = sd;
            else { sn = -d; sd = la; }
        }
        else if (tn > td)
        {
            tn = td;
            if (-d + dd < 0) sn = 0;
            else if (-d + dd > la) sn = sd;
            else { sn = -d + dd; sd = la; }
        }

        return (MathF.Abs(sn) < 1e-8f ? 0 : sn / sd, MathF.Abs(tn) < 1e-8f ? 0 : tn / td);
    }

    public static Float3 PointOnAxis(TransformGizmo gizmo, Ray ray, GizmoDirection direction)
    {
        var origin = gizmo.Translation;
        var dir = GizmoNormal(gizmo, direction);
        var (_, subGizmoT) = RayToRay(ray.Origin, ray.Direction, origin, dir);
        return origin + dir * subGizmoT;
    }

    public static (float, float) RayToRay(Float3 a1, Float3 aDir, Float3 b1, Float3 bDir)
    {
        var b = Float3.Dot(aDir, bDir);
        var w = a1 - b1;
        var d = Float3.Dot(aDir, w);
        var e = Float3.Dot(bDir, w);
        var dot = 1.0f - b * b;
        if (dot < 1e-8f) return (0, e);
        return ((b * e - d) / dot, (e - b * d) / dot);
    }

    /// <summary>Project world point to screen via MVP.</summary>
    /// <remarks>
    /// Old code used System.Numerics row-major: Vector4.Transform(vec, mvp) = vec * mvp.
    /// Prowl uses column-major: Float4x4.TransformPoint(vec, mvp) = mvp * vec.
    /// The MVP matrices are already built in column-major order (proj * view * model),
    /// so TransformPoint gives the correct clip-space result.
    /// </remarks>
    public static Float2? WorldToScreen(Rect viewport, Float4x4 mvp, Float3 pos)
    {
        // Column-major: mvp * vec
        Float4 posH = Float4x4.TransformPoint(new Float4(pos.X, pos.Y, pos.Z, 1.0f), mvp);
        if (posH.W < 1e-10f) return null;
        posH /= posH.W;

        float cx = (float)(viewport.Min.X + viewport.Size.X / 2);
        float cy = (float)(viewport.Min.Y + viewport.Size.Y / 2);

        return new Float2(
            cx + posH.X * (float)(viewport.Size.X / 2),
            cy - posH.Y * (float)(viewport.Size.Y / 2)
        );
    }

    // ================================================================
    //  Drawing helpers (1:1 port from original GizmoUtils)
    // ================================================================

    public static Float4x4 DrawPlane(TransformGizmo gizmo, bool focused, Float4x4 transform, GizmoDirection direction)
    {
        if (gizmo.Orientation == TransformGizmo.GizmoOrientation.Local)
            transform = transform * Float4x4.CreateFromQuaternion(gizmo.Rotation);

        using (gizmo.Draw3D.Matrix(gizmo.ViewProjection * transform))
        {
            var color = GizmoColor(gizmo, focused, direction);
            var scale = PlaneSize(gizmo) * 0.5f;
            var bitangent = PlaneBitangent(direction) * scale;
            var tangent = PlaneTangent(direction) * scale;
            var origin = PlaneLocalOrigin(gizmo, direction);

            Float3[] verts = [origin - bitangent - tangent, origin + bitangent - tangent, origin + bitangent + tangent, origin - bitangent + tangent];
            gizmo.Draw3D.Polygon(verts, new Stroke3D { Color = color, Thickness = gizmo.StrokeWidth });
            return transform;
        }
    }

    public static Float4x4 DrawCircle(TransformGizmo gizmo, bool focused, Float4x4 transform)
    {
        var viewUp = gizmo.ViewUp;
        var viewForward = -gizmo.ViewForward;
        var viewRight = -gizmo.ViewRight;

        var rotation = new Float4x4(
            new Float4(viewUp.X, viewUp.Y, viewUp.Z, 0),
            new Float4(-viewForward.X, -viewForward.Y, -viewForward.Z, 0),
            new Float4(-viewRight.X, -viewRight.Y, -viewRight.Z, 0),
            new Float4(0, 0, 0, 1)
        );

        transform = transform * rotation;

        using (gizmo.Draw3D.Matrix(gizmo.ViewProjection * transform))
        {
            var color = GizmoColor(gizmo, focused, GizmoDirection.View);
            gizmo.Draw3D.Circle(InnerCircleRadius(gizmo), new Stroke3D { Color = color, Thickness = gizmo.StrokeWidth });
            return transform;
        }
    }

    public static Float4x4 DrawQuad(TransformGizmo gizmo, bool focused, Float4x4 transform)
    {
        var viewUp = gizmo.ViewUp;
        var viewForward = -gizmo.ViewForward;
        var viewRight = -gizmo.ViewRight;

        var rotation = new Float4x4(
            new Float4(viewUp.X, viewUp.Y, viewUp.Z, 0),
            new Float4(-viewForward.X, -viewForward.Y, -viewForward.Z, 0),
            new Float4(-viewRight.X, -viewRight.Y, -viewRight.Z, 0),
            new Float4(0, 0, 0, 1)
        );

        transform = transform * rotation;

        using (gizmo.Draw3D.Matrix(gizmo.ViewProjection * transform))
        {
            var color = GizmoColor(gizmo, focused, GizmoDirection.View);
            gizmo.Draw3D.Quad(InnerCircleRadius(gizmo), new Stroke3D { Color = color, Thickness = gizmo.StrokeWidth });
            return transform;
        }
    }

    public static void DrawArrow(TransformGizmo gizmo, bool focused, Float4x4 transform, GizmoDirection direction, TransformGizmoMode mode, float scale = 1f)
    {
        if (gizmo.Orientation == TransformGizmo.GizmoOrientation.Local)
            transform = transform * Float4x4.CreateFromQuaternion(gizmo.Rotation);

        using (gizmo.Draw3D.Matrix(gizmo.ViewProjection * transform))
        {
            var color = GizmoColor(gizmo, focused, direction);
            var normal = GizmoLocalNormal(gizmo, direction) * AxisSign(gizmo, direction);
            var (start, end, length) = ArrowParams(gizmo, normal, mode);

            end *= scale;

            var tipStrokeWidth = 2.4f * gizmo.StrokeWidth;
            var tipLength = tipStrokeWidth * gizmo.ScaleFactor;
            var tipStart = end - normal * tipLength;

            gizmo.Draw3D.LineSegment(start, tipStart, new Stroke3D { Color = color, Thickness = gizmo.StrokeWidth });
            bool isTranslate = mode == TransformGizmoMode.TranslateX || mode == TransformGizmoMode.TranslateY || mode == TransformGizmoMode.TranslateZ;
            if (isTranslate)
                gizmo.Draw3D.Arrow(tipStart, end, new Stroke3D { Color = color, Thickness = tipStrokeWidth });
            else
                gizmo.Draw3D.LineSegment(tipStart, end, new Stroke3D { Color = color, Thickness = tipStrokeWidth });
        }
    }

    public static bool IsPointInPolygon(Float2 mouse, List<Float2> screenPoints)
    {
        int count = screenPoints.Count;
        bool inside = false;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            if (((screenPoints[i].Y > mouse.Y) != (screenPoints[j].Y > mouse.Y)) &&
                (mouse.X < (screenPoints[j].X - screenPoints[i].X) * (mouse.Y - screenPoints[i].Y) / (screenPoints[j].Y - screenPoints[i].Y) + screenPoints[i].X))
                inside = !inside;
        }
        return inside;
    }
}
