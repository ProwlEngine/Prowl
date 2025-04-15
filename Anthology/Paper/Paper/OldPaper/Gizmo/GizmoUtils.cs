// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
/*
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Prowl.PaperUI.GUI;

public static class GizmoUtils
{
    public static bool IntersectPlane(Vector3 planeNormal, Vector3 planeOrigin, Vector3 rayOrigin, Vector3 rayDirection, out float t)
    {
        var denom = Vector3.Dot(planeNormal, rayDirection);
        if (Math.Abs(denom) < 1e-8)
        {
            t = 0;
            return false;
        }
        else
        {
            t = Vector3.Dot(planeOrigin - rayOrigin, planeNormal) / denom;
            return t >= 0;
        }
    }

    public static (float, float) RayToPlaneOrigin(Vector3 planeNormal, Vector3 planeOrigin, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (IntersectPlane(planeNormal, planeOrigin, rayOrigin, rayDirection, out float t))
        {
            var p = rayOrigin + rayDirection * t;
            var v = p - planeOrigin;
            var d2 = Vector3.Dot(v, v);
            return (t, MathF.Sqrt(d2));
        }
        else
        {
            return (t, float.MaxValue);
        }
    }

    public static float RoundToInterval(float value, float interval)
    {
        return MathF.Round(value / interval) * interval;
    }

    public static Vector3 PointOnPlane(Vector3 planeNormal, Vector3 planeOrigin, Ray ray)
    {
        if (IntersectPlane(planeNormal, planeOrigin, ray.origin, ray.direction, out float t))
        {
            return ray.origin + ray.direction * t;
        }
        else
        {
            return Vector3.Zero;
        }
    }

    public static Vector3 SnapTranslationVector(Vector3 delta, TransformGizmo gizmo)
    {
        var deltaLength = delta.Length();
        if (deltaLength > 1e-5)
        {
            return delta / deltaLength * RoundToInterval(deltaLength, gizmo.SnapDistance);
        }
        else
        {
            return delta;
        }
    }

    public static Vector3 SnapTranslationPlane(Vector3 delta, GizmoDirection direction, TransformGizmo gizmo)
    {
        var bitangent = PlaneBitangent(direction);
        var tangent = PlaneTangent(direction);
        bitangent = Vector3.Transform(bitangent, gizmo.Rotation);
        tangent = Vector3.Transform(tangent, gizmo.Rotation);
        var cb = Vector3.Cross(delta, -bitangent);
        var ct = Vector3.Cross(delta, tangent);
        var lb = cb.Length();
        var lt = ct.Length();
        var n = GizmoNormal(gizmo, direction);

        if (lb > 1e-5 && lt > 1e-5)
        {
            return bitangent * RoundToInterval(lt, gizmo.SnapDistance) * Vector3.Dot(ct / lt, n)
                   + tangent * RoundToInterval(lb, gizmo.SnapDistance) * Vector3.Dot(cb / lb, n);
        }
        else
        {
            return delta;
        }
    }

    public static float PlaneSize(TransformGizmo gizmo)
    {
        return (gizmo.ScaleFactor * (gizmo.GizmoSize * 0.1f + gizmo.StrokeWidth * 2.0f));
    }

    public static Vector3 PlaneBitangent(GizmoDirection direction)
    {
        switch (direction)
        {
            case GizmoDirection.X:
                return Vector3.UnitY;
            case GizmoDirection.Y:
                return Vector3.UnitZ;
            case GizmoDirection.Z:
                return Vector3.UnitX;
            default:
                return Vector3.Zero;
        }
    }

    public static Vector3 PlaneTangent(GizmoDirection direction)
    {
        switch (direction)
        {
            case GizmoDirection.X:
                return Vector3.UnitZ;
            case GizmoDirection.Y:
                return Vector3.UnitX;
            case GizmoDirection.Z:
                return Vector3.UnitY;
            default:
                return Vector3.Zero;
        }
    }

    public static Color GizmoColor(TransformGizmo gizmo, bool focused, GizmoDirection direction)
    {
        var col = direction switch
        {
            GizmoDirection.X => new Color(226, 55, 56, 255),
            GizmoDirection.Y => new Color(94, 234, 141, 255),
            GizmoDirection.Z => new Color(39, 117, 255, 255),
            _                => new Color(255, 255, 255, 255),
        };

        float alpha = focused ? 1f : 0.8f;
        col.r = (byte)(col.r * alpha);
        col.g = (byte)(col.g * alpha);
        col.b = (byte)(col.b * alpha);

        return col;
    }

    public static Vector3 GizmoLocalNormal(TransformGizmo gizmo, GizmoDirection direction)
    {
        return direction switch
        {
            GizmoDirection.X    => Vector3.UnitX,
            GizmoDirection.Y    => Vector3.UnitY,
            GizmoDirection.Z    => Vector3.UnitZ,
            GizmoDirection.View => -gizmo.ViewForward,
            _                   => Vector3.Zero,
        };
    }

    public static Vector3 GizmoNormal(TransformGizmo gizmo, GizmoDirection direction)
    {
        Vector3 norm = GizmoLocalNormal(gizmo, direction);

        if (gizmo.Orientation == TransformGizmo.GizmoOrientation.Local && direction != GizmoDirection.View)
        {
            Vector4 vec = Vector4.Transform(new Vector4(norm, 0), gizmo.Rotation);
            norm = new Vector3(vec.X, vec.Y, vec.Z);
        }

        return norm;
    }

    public static Vector3 PlaneGlobalOrigin(TransformGizmo gizmo, GizmoDirection direction)
    {
        var origin = PlaneLocalOrigin(gizmo, direction);
        if (gizmo.Orientation == TransformGizmo.GizmoOrientation.Local)
        {
            origin = Vector3.Transform(origin, gizmo.Rotation);
        }
        return origin + gizmo.Translation;
    }

    public static Vector3 PlaneLocalOrigin(TransformGizmo gizmo, GizmoDirection direction)
    {
        var offset = gizmo.ScaleFactor * gizmo.GizmoSize * 0.5f;
        var a = PlaneBitangent(direction);
        var b = PlaneTangent(direction);
        return (a + b) * offset;
    }

    public static float InnerCircleRadius(TransformGizmo gizmo)
    {
        return (gizmo.ScaleFactor * gizmo.GizmoSize) * 0.2f;
    }

    public static float OuterCircleRadius(TransformGizmo gizmo)
    {
        return gizmo.ScaleFactor * (gizmo.GizmoSize + gizmo.StrokeWidth + 5.0f);
    }

    public static PickResult PickArrow(TransformGizmo gizmo, Ray ray, GizmoDirection direction, TransformGizmoMode mode)
    {
        const float rayLength = 1e+14f;
        var normal = GizmoNormal(gizmo, direction);

        (Vector3 start, Vector3 end, float length) = ArrowParams(gizmo, normal, mode);

        start += gizmo.Translation;
        end += gizmo.Translation;

        var (rayT, subGizmoT) = SegmentToSegment(ray.origin, ray.origin + ray.direction * rayLength, start, end);
        var rayPoint = ray.origin + ray.direction * rayLength * rayT;
        var subGizmoPoint = start + normal * length * subGizmoT;
        var dist = Vector3.Distance(rayPoint, subGizmoPoint);

        var picked = dist <= gizmo.FocusDistance;

        return new PickResult
        {
            SubGizmoPoint = subGizmoPoint,
            T = rayT,
            Picked = picked
        };
    }

    public static PickResult PickPlane(TransformGizmo gizmo, Ray ray, GizmoDirection direction)
    {
        var origin = PlaneGlobalOrigin(gizmo, direction);
        var normal = GizmoNormal(gizmo, direction);
        var (t, distFromOrigin) = RayToPlaneOrigin(normal, origin, ray.origin, ray.direction);
        var rayPoint = ray.origin + ray.direction * t;
        var picked = distFromOrigin <= PlaneSize(gizmo);

        return new PickResult
        {
            SubGizmoPoint = rayPoint,
            T = t,
            Picked = picked
        };
    }

    public static PickResult PickCircle(TransformGizmo gizmo, Ray ray, float radius, bool filled)
    {
        var (t, distFromGizmoOrigin) = RayToPlaneOrigin(-gizmo.ViewForward, gizmo.Translation, ray.origin, ray.direction);
        var hitPos = ray.origin + ray.direction * t;
        var picked = filled ? distFromGizmoOrigin <= radius + gizmo.FocusDistance
            : Math.Abs(distFromGizmoOrigin - radius) <= radius + gizmo.FocusDistance;

        return new PickResult
        {
            SubGizmoPoint = hitPos,
            T = t,
            Picked = picked
        };
    }

    public static Matrix4x4 DrawPlane(TransformGizmo _gizmo, bool focused, Matrix4x4 transform, GizmoDirection direction)
    {
        if (_gizmo.Orientation == TransformGizmo.GizmoOrientation.Local)
            transform = Matrix4x4.CreateFromQuaternion(_gizmo.Rotation) * transform;

        using (_gizmo._gui.Draw3D.Matrix(transform * _gizmo.ViewProjection))
        {
            var color3 = GizmoColor(_gizmo, focused, direction);

            var scale = PlaneSize(_gizmo) * 0.5f;
            var bitangent = PlaneBitangent(direction) * scale;
            var tangent = PlaneTangent(direction) * scale;
            var origin3 = PlaneLocalOrigin(_gizmo, direction);

            var v1 = origin3 - bitangent - tangent;
            var v2 = origin3 + bitangent - tangent;
            var v3 = origin3 + bitangent + tangent;
            var v4 = origin3 - bitangent + tangent;

            List<Vector3> vertices = [v1, v2, v3, v4];

            _gizmo._gui.Draw3D.Polygon(vertices, new Stroke3D { Color = color3, Thickness = _gizmo.StrokeWidth });
            return transform;
        }
    }

    public static Matrix4x4 DrawCircle(TransformGizmo _gizmo, bool focused, Matrix4x4 transform)
    {
        // Negate forward and right as per your requirement
        var viewUp = _gizmo.ViewUp;
        var viewForward = -_gizmo.ViewForward;
        var viewRight = -_gizmo.ViewRight;

        // Construct the rotation matrix
        var rotation = new Matrix4x4()
        {
            M11 = viewUp.X,         M12 = viewUp.Y,         M13 = viewUp.Z,         M14 = 0,
            M21 = -viewForward.X,   M22 = -viewForward.Y,   M23 = -viewForward.Z,   M24 = 0,
            M31 = -viewRight.X,     M32 = -viewRight.Y,     M33 = -viewRight.Z,     M34 = 0,
            M41 = 0,                M42 = 0,                M43 = 0,                M44 = 1
        };

        transform = rotation * transform;

        using (_gizmo._gui.Draw3D.Matrix(transform * _gizmo.ViewProjection))
        {
            var color2 = GizmoColor(_gizmo, focused, GizmoDirection.View);
            _gizmo._gui.Draw3D.Circle(InnerCircleRadius(_gizmo), new Stroke3D { Color = color2, Thickness = _gizmo.StrokeWidth });
            return transform;
        }
    }

    public static Matrix4x4 DrawQuad(TransformGizmo _gizmo, bool focused, Matrix4x4 transform)
    {
        // Negate forward and right as per your requirement
        var viewUp = _gizmo.ViewUp;
        var viewForward = -_gizmo.ViewForward;
        var viewRight = -_gizmo.ViewRight;

        // Construct the rotation matrix
        var rotation = new Matrix4x4()
        {
            M11 = viewUp.X,         M12 = viewUp.Y,         M13 = viewUp.Z,         M14 = 0,
            M21 = -viewForward.X,   M22 = -viewForward.Y,   M23 = -viewForward.Z,   M24 = 0,
            M31 = -viewRight.X,     M32 = -viewRight.Y,     M33 = -viewRight.Z,     M34 = 0,
            M41 = 0,                M42 = 0,                M43 = 0,                M44 = 1
        };

        transform = rotation * transform;

        using (_gizmo._gui.Draw3D.Matrix(transform * _gizmo.ViewProjection))
        {
            var color2 = GizmoColor(_gizmo, focused, GizmoDirection.View);
            _gizmo._gui.Draw3D.Quad(InnerCircleRadius(_gizmo), new Stroke3D { Color = color2, Thickness = _gizmo.StrokeWidth });
            return transform;
        }
    }

    public static void DrawArrow(TransformGizmo _gizmo, bool focused, Matrix4x4 transform, GizmoDirection direction, TransformGizmoMode mode, float scale = 1f)
    {
        if (_gizmo.Orientation == TransformGizmo.GizmoOrientation.Local)
            transform = Matrix4x4.CreateFromQuaternion(_gizmo.Rotation) * transform;

        using (_gizmo._gui.Draw3D.Matrix(transform * _gizmo.ViewProjection))
        {
            var color = GizmoColor(_gizmo, focused, direction);
            var normal = GizmoLocalNormal(_gizmo, direction);

            (Vector3 start, Vector3 end, float length) = ArrowParams(_gizmo, normal, mode);

            end *= scale;

            var tip_stroke_width = 2.4f * _gizmo.StrokeWidth;
            var tip_length = (tip_stroke_width * _gizmo.ScaleFactor);
            var tip_start = end - normal * tip_length;

            _gizmo._gui.Draw3D.LineSegment(start, tip_start, new Stroke3D { Color = color, Thickness = _gizmo.StrokeWidth });
            bool isTranslate = mode == TransformGizmoMode.TranslateX || mode == TransformGizmoMode.TranslateY || mode == TransformGizmoMode.TranslateZ;
            if (isTranslate)
                _gizmo._gui.Draw3D.Arrow(tip_start, end, new Stroke3D { Color = color, Thickness = tip_stroke_width });
            else
                _gizmo._gui.Draw3D.LineSegment(tip_start, end, new Stroke3D { Color = color, Thickness = tip_stroke_width });
        }
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

    public static (Vector3, Vector3, float) ArrowParams(TransformGizmo _gizmo, Vector3 direction, TransformGizmoMode mode)
    {
        bool isTranslate = mode == TransformGizmoMode.TranslateX || mode == TransformGizmoMode.TranslateY || mode == TransformGizmoMode.TranslateZ;
        bool overlapping = ArrowModesOverlapping(mode, _gizmo.mode);

        var width = _gizmo.ScaleFactor * _gizmo.StrokeWidth;
        var gizmoSize = _gizmo.ScaleFactor * _gizmo.GizmoSize;
        Vector3 start;
        float length;
        if (isTranslate && overlapping)
        {
            start = direction * (width * 0.5f + InnerCircleRadius(_gizmo));
            length = gizmoSize - start.Length();
            length -= width * 2.0f;
            //if config.modes.len() > 1 {
            //    length -= width * 2.0;
            //}
        }
        else
        {
            length = gizmoSize;
            start = direction * (length + (width * 3.0f));

            length = length * 0.2f + width;
        }
        return (start,
            start + direction * length,
            length);

    }

    public static (float, float) SegmentToSegment(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
    {
        var da = a2 - a1;
        var db = b2 - b1;
        var la = da.LengthSquared();
        var lb = db.LengthSquared();
        var dd = Vector3.Dot(da, db);
        var d1 = a1 - b1;
        var d = Vector3.Dot(da, d1);
        var e = Vector3.Dot(db, d1);
        var n = la * lb - dd * dd;

        float sn;
        float tn;
        var sd = n;
        var td = n;

        if (n < 1e-8)
        {
            sn = 0.0f;
            sd = 1.0f;
            tn = e;
            td = lb;
        }
        else
        {
            sn = dd * e - lb * d;
            tn = la * e - dd * d;
            if (sn < 0.0f)
            {
                sn = 0.0f;
                tn = e;
                td = lb;
            }
            else if (sn > sd)
            {
                sn = sd;
                tn = e + dd;
                td = lb;
            }
        }

        if (tn < 0.0f)
        {
            tn = 0.0f;
            if (-d < 0.0f)
            {
                sn = 0.0f;
            }
            else if (-d > la)
            {
                sn = sd;
            }
            else
            {
                sn = -d;
                sd = la;
            }
        }
        else if (tn > td)
        {
            tn = td;
            if ((-d + dd) < 0.0f)
            {
                sn = 0.0f;
            }
            else if ((-d + dd) > la)
            {
                sn = sd;
            }
            else
            {
                sn = -d + dd;
                sd = la;
            }
        }

        var ta = MathF.Abs(sn) < 1e-8f ? 0.0f : sn / sd;
        var tb = MathF.Abs(tn) < 1e-8f ? 0.0f : tn / td;

        return (ta, tb);
    }

    public static Vector3 PointOnAxis(TransformGizmo gizmo, Ray ray, GizmoDirection direction)
    {
        var origin = gizmo.Translation;
        var dir = GizmoNormal(gizmo, direction);
        var (_, subGizmoT) = RayToRay(ray.origin, ray.direction, origin, dir);
        return origin + dir * subGizmoT;
    }

    public static (float, float) RayToRay(Vector3 a1, Vector3 aDir, Vector3 b1, Vector3 bDir)
    {
        var b = Vector3.Dot(aDir, bDir);
        var w = a1 - b1;
        var d = Vector3.Dot(aDir, w);
        var e = Vector3.Dot(bDir, w);
        var dot = 1.0f - b * b;
        float ta, tb;

        if (dot < 1e-8)
        {
            ta = 0;
            tb = e;
        }
        else
        {
            ta = (b * e - d) / dot;
            tb = (e - b * d) / dot;
        }

        return (ta, tb);
    }

    /// <summary>
    /// Creates a matrix that represents rotation between two 3D vectors.
    /// </summary>
    /// <param name="from">The source vector.</param>
    /// <param name="to">The target vector.</param>
    /// <returns>A rotation matrix that aligns the source vector to the target vector.</returns>
    public static Matrix4x4 RotationAlign(Vector3 from, Vector3 to)
    {
        var v = Vector3.Cross(from, to);
        var c = Vector3.Dot(from, to);
        var k = 1.0f / (1.0f + c);

        return new Matrix4x4(
            v.X * v.X * k + c,
            v.X * v.Y * k + v.Z,
            v.X * v.Z * k - v.Y,
            0,
            v.Y * v.X * k - v.Z,
            v.Y * v.Y * k + c,
            v.Y * v.Z * k + v.X,
            0,
            v.Z * v.X * k + v.Y,
            v.Z * v.Y * k - v.X,
            v.Z * v.Z * k + c,
            0,
            0,
            0,
            0,
            1
        );
    }

    public static Vector2? WorldToScreen(Rect viewport, Matrix4x4 mvp, Vector3 pos)
    {
        Vector4 posH = Vector4.Transform(new Vector4(pos, 1.0f), mvp);

        if (posH.W < 1e-10)
            return null;

        posH /= posH.W;
        posH.Y *= -1.0f;

        Vector2 center = viewport.Center;

        return new Vector2(
            center.X + posH.X * (viewport.width / 2.0f),
            center.Y + posH.Y * (viewport.height / 2.0f)
        );
    }

    internal static bool IsPointInPolygon(Vector2 mouse, List<Vector2> screenPoints)
    {
        int count = screenPoints.Count;
        bool inside = false;

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            if (((screenPoints[i].Y > mouse.Y) != (screenPoints[j].Y > mouse.Y)) &&
                (mouse.X < (screenPoints[j].X - screenPoints[i].X) * (mouse.Y - screenPoints[i].Y) / (screenPoints[j].Y - screenPoints[i].Y) + screenPoints[i].X))
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
*/