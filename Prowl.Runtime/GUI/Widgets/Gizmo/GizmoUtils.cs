using Prowl.Runtime.GUI.Graphics;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime.GUI
{
    public static class GizmoUtils
    {
        public static bool IntersectPlane(Vector3 planeNormal, Vector3 planeOrigin, Vector3 rayOrigin, Vector3 rayDirection, out double t)
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

        public static (double, double) RayToPlaneOrigin(Vector3 planeNormal, Vector3 planeOrigin, Vector3 rayOrigin, Vector3 rayDirection)
        {
            if (IntersectPlane(planeNormal, planeOrigin, rayOrigin, rayDirection, out double t))
            {
                var p = rayOrigin + rayDirection * t;
                var v = p - planeOrigin;
                var d2 = Vector3.Dot(v, v);
                return (t, Math.Sqrt(d2));
            }
            else
            {
                return (t, double.MaxValue);
            }
        }

        public static double RoundToInterval(double value, double interval)
        {
            return Math.Round(value / interval) * interval;
        }

        public static Vector3 PointOnPlane(Vector3 planeNormal, Vector3 planeOrigin, Ray ray)
        {
            if (IntersectPlane(planeNormal, planeOrigin, ray.origin, ray.direction, out double t))
            {
                return ray.origin + ray.direction * t;
            }
            else
            {
                return Vector3.zero;
            }
        }

        public static Vector3 SnapTranslationVector(Vector3 delta, TransformGizmo gizmo)
        {
            var deltaLength = delta.magnitude;
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
            var lb = cb.magnitude;
            var lt = ct.magnitude;
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

        public static double PlaneSize(TransformGizmo gizmo)
        {
            return (gizmo.ScaleFactor * (gizmo.GizmoSize * 0.1 + gizmo.StrokeWidth * 2.0));
        }

        public static Vector3 PlaneBitangent(GizmoDirection direction)
        {
            switch (direction)
            {
                case GizmoDirection.X:
                    return Vector3.up;
                case GizmoDirection.Y:
                    return Vector3.forward;
                case GizmoDirection.Z:
                    return Vector3.right;
                default:
                    return Vector3.zero;
            }
        }

        public static Vector3 PlaneTangent(GizmoDirection direction)
        {
            switch (direction)
            {
                case GizmoDirection.X:
                    return Vector3.forward;
                case GizmoDirection.Y:
                    return Vector3.right;
                case GizmoDirection.Z:
                    return Vector3.up;
                default:
                    return Vector3.zero;
            }
        }

        public static Color32 GizmoColor(TransformGizmo gizmo, bool focused, GizmoDirection direction)
        {
            var col = direction switch {
                GizmoDirection.X => new Color32(226, 55, 56, 255),
                GizmoDirection.Y => new Color32(94, 234, 141, 255),
                GizmoDirection.Z => new Color32(39, 117, 255, 255),
                _ => new Color32(255, 255, 255, 255),
            };

            double alpha = focused ? 1f : 0.8f;
            col.red = (byte)(col.red * alpha);
            col.green = (byte)(col.green * alpha);
            col.blue = (byte)(col.blue * alpha);

            return col;
        }

        public static Vector3 GizmoLocalNormal(TransformGizmo gizmo, GizmoDirection direction)
        {
            return direction switch {
                GizmoDirection.X => Vector3.right,
                GizmoDirection.Y => Vector3.up,
                GizmoDirection.Z => Vector3.forward,
                GizmoDirection.View => -gizmo.ViewForward,
                _ => Vector3.zero,
            };
        }

        public static Vector3 GizmoNormal(TransformGizmo gizmo, GizmoDirection direction)
        {
            var norm = GizmoLocalNormal(gizmo, direction);

            if (gizmo.Orientation == TransformGizmo.GizmoOrientation.Local && direction != GizmoDirection.View)
            {
                norm = Vector4.Transform(new Vector4(norm, 0), gizmo.Rotation).xyz;
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
            var offset = gizmo.ScaleFactor * gizmo.GizmoSize * 0.5;
            var a = PlaneBitangent(direction);
            var b = PlaneTangent(direction);
            return (a + b) * offset;
        }

        public static double InnerCircleRadius(TransformGizmo gizmo)
        {
            return (gizmo.ScaleFactor * gizmo.GizmoSize) * 0.2f;
        }

        public static double OuterCircleRadius(TransformGizmo gizmo)
        {
            return gizmo.ScaleFactor * (gizmo.GizmoSize + gizmo.StrokeWidth + 5.0);
        }

        public static PickResult PickArrow(TransformGizmo gizmo, Ray ray, GizmoDirection direction, TransformGizmoMode mode)
        {
            const double rayLength = 1e+14;
            var normal = GizmoNormal(gizmo, direction);

            (Vector3 start, Vector3 end, double length) = ArrowParams(gizmo, normal, mode);

            start += gizmo.Translation;
            end += gizmo.Translation;

            var (rayT, subGizmoT) = SegmentToSegment(ray.origin, ray.origin + ray.direction * rayLength, start, end);
            var rayPoint = ray.origin + ray.direction * rayLength * rayT;
            var subGizmoPoint = start + normal * length * subGizmoT;
            var dist = Vector3.Distance(rayPoint, subGizmoPoint);

            var picked = dist <= gizmo.FocusDistance;

            return new PickResult {
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

            return new PickResult {
                SubGizmoPoint = rayPoint,
                T = t,
                Picked = picked
            };
        }

        public static PickResult PickCircle(TransformGizmo gizmo, Ray ray, double radius, bool filled)
        {
            var (t, distFromGizmoOrigin) = RayToPlaneOrigin(-gizmo.ViewForward, gizmo.Translation, ray.origin, ray.direction);
            var hitPos = ray.origin + ray.direction * t;
            var picked = filled ? distFromGizmoOrigin <= radius + gizmo.FocusDistance 
                                : Math.Abs(distFromGizmoOrigin - radius) <= radius + gizmo.FocusDistance;

            return new PickResult {
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
                var color3 = GizmoUtils.GizmoColor(_gizmo, focused, direction);

                var scale = GizmoUtils.PlaneSize(_gizmo) * 0.5f;
                var bitangent = GizmoUtils.PlaneBitangent(direction) * scale;
                var tangent = GizmoUtils.PlaneTangent(direction) * scale;
                var origin3 = GizmoUtils.PlaneLocalOrigin(_gizmo, direction);

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
            var rotation = new Matrix4x4(
                new Vector4(viewUp, 0),
                new Vector4(-viewForward, 0),
                new Vector4(-viewRight, 0),
                new Vector4(0, 0, 0, 1)
            );

            transform = rotation * transform;

            using (_gizmo._gui.Draw3D.Matrix(transform * _gizmo.ViewProjection))
            {
                var color2 = GizmoUtils.GizmoColor(_gizmo, focused, GizmoDirection.View);
                _gizmo._gui.Draw3D.Circle(GizmoUtils.InnerCircleRadius(_gizmo), new Stroke3D { Color = color2, Thickness = _gizmo.StrokeWidth });
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
            var rotation = new Matrix4x4(
                new Vector4(viewUp, 0),
                new Vector4(-viewForward, 0),
                new Vector4(-viewRight, 0),
                new Vector4(0, 0, 0, 1)
            );

            transform = rotation * transform;

            using (_gizmo._gui.Draw3D.Matrix(transform * _gizmo.ViewProjection))
            {
                var color2 = GizmoUtils.GizmoColor(_gizmo, focused, GizmoDirection.View);
                _gizmo._gui.Draw3D.Quad(GizmoUtils.InnerCircleRadius(_gizmo), new Stroke3D { Color = color2, Thickness = _gizmo.StrokeWidth });
                return transform;
            }
        }

        public static void DrawArrow(TransformGizmo _gizmo, bool focused, Matrix4x4 transform, GizmoDirection direction, TransformGizmoMode mode, double scale = 1f)
        {
            if (_gizmo.Orientation == TransformGizmo.GizmoOrientation.Local)
                transform = Matrix4x4.CreateFromQuaternion(_gizmo.Rotation) * transform;

            using (_gizmo._gui.Draw3D.Matrix(transform * _gizmo.ViewProjection))
            {
                var color = GizmoUtils.GizmoColor(_gizmo, focused, direction);
                var normal = GizmoUtils.GizmoLocalNormal(_gizmo, direction);

                (Vector3 start, Vector3 end, double length) = ArrowParams(_gizmo, normal, mode);

                end *= scale;

                var tip_stroke_width = 2.4 * _gizmo.StrokeWidth;
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

        public static bool ArrowModesOverlapping(TransformGizmoMode mode, TransformGizmoMode gizmoModes){
            return (mode == TransformGizmoMode.TranslateX && gizmoModes.HasFlag(TransformGizmoMode.ScaleX))
                || (mode == TransformGizmoMode.TranslateY && gizmoModes.HasFlag(TransformGizmoMode.ScaleY))
                || (mode == TransformGizmoMode.TranslateZ && gizmoModes.HasFlag(TransformGizmoMode.ScaleZ))
                || (mode == TransformGizmoMode.ScaleX && gizmoModes.HasFlag(TransformGizmoMode.TranslateX))
                || (mode == TransformGizmoMode.ScaleY && gizmoModes.HasFlag(TransformGizmoMode.TranslateY))
                || (mode == TransformGizmoMode.ScaleZ && gizmoModes.HasFlag(TransformGizmoMode.TranslateZ));
        }

        public static (Vector3, Vector3, double) ArrowParams(TransformGizmo _gizmo, Vector3 direction, TransformGizmoMode mode)
        {
            bool isTranslate = mode == TransformGizmoMode.TranslateX || mode == TransformGizmoMode.TranslateY || mode == TransformGizmoMode.TranslateZ;
            bool overlapping = ArrowModesOverlapping(mode, _gizmo.mode);

            var width = _gizmo.ScaleFactor * _gizmo.StrokeWidth;
            var gizmoSize = _gizmo.ScaleFactor * _gizmo.GizmoSize;
            Vector3 start;
            double length;
            if (isTranslate && overlapping)
            {
                start = direction * (width * 0.5f + GizmoUtils.InnerCircleRadius(_gizmo));
                length = gizmoSize - start.magnitude;
                length -= width * 2.0;
                //if config.modes.len() > 1 {
                //    length -= width * 2.0;
                //}
            }
            else
            {
                length = gizmoSize;
                start = direction * (length + (width * 3.0));

                length = length * 0.2 + width;
            }
            return (start, 
                    start + direction * length,
                    length);

        }

        public static (double, double) SegmentToSegment(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
        {
            var da = a2 - a1;
            var db = b2 - b1;
            var la = da.sqrMagnitude;
            var lb = db.sqrMagnitude;
            var dd = Vector3.Dot(da, db);
            var d1 = a1 - b1;
            var d = Vector3.Dot(da, d1);
            var e = Vector3.Dot(db, d1);
            var n = la * lb - dd * dd;

            double sn;
            double tn;
            var sd = n;
            var td = n;

            if (n < 1e-8) {
                sn = 0.0;
                sd = 1.0;
                tn = e;
                td = lb;
            }
            else
            {
                sn = dd * e - lb * d;
                tn = la * e - dd * d;
                if (sn < 0.0) {
                    sn = 0.0;
                    tn = e;
                    td = lb;
                }
                else if (sn > sd) {
                    sn = sd;
                    tn = e + dd;
                    td = lb;
                }
            }

            if (tn < 0.0) {
                tn = 0.0;
                if (-d < 0.0) {
                    sn = 0.0;
                }
                else if( -d > la) {
                    sn = sd;
                }
                else
                {
                    sn = -d;
                    sd = la;
                }
            }
            else if (tn > td) {
                tn = td;
                if ((-d + dd) < 0.0) {
                    sn = 0.0;
                } else if ((-d + dd) > la) {
                    sn = sd;
                } else
                {
                    sn = -d + dd;
                    sd = la;
                }
            }

            var ta = MathD.Abs(sn) < 1e-8 ? 0.0 : sn / sd;
            var tb = MathD.Abs(tn) < 1e-8 ? 0.0 : tn / td;

            return (ta, tb);
        }

        public static Vector3 PointOnAxis(TransformGizmo gizmo, Ray ray, GizmoDirection direction)
        {
            var origin = gizmo.Translation;
            var dir = GizmoNormal(gizmo, direction);
            var (_, subGizmoT) = RayToRay(ray.origin, ray.direction, origin, dir);
            return origin + dir * subGizmoT;
        }

        public static (double, double) RayToRay(Vector3 a1, Vector3 aDir, Vector3 b1, Vector3 bDir)
        {
            var b = Vector3.Dot(aDir, bDir);
            var w = a1 - b1;
            var d = Vector3.Dot(aDir, w);
            var e = Vector3.Dot(bDir, w);
            var dot = 1.0f - b * b;
            double ta, tb;

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
            var k = 1.0 / (1.0 + c);

            return new Matrix4x4(
                v.x * v.x * k + c,
                v.x * v.y * k + v.z,
                v.x * v.z * k - v.y,
                0,
                v.y * v.x * k - v.z,
                v.y * v.y * k + c,
                v.y * v.z * k + v.x,
                0,
                v.z * v.x * k + v.y,
                v.z * v.y * k - v.x,
                v.z * v.z * k + c,
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

            if (posH.w < 1e-10)
                return null;

            posH /= posH.w;
            posH.y *= -1.0f;

            Vector2 center = viewport.Center;

            return new Vector2(
               center.x + posH.x * (viewport.width / 2.0),
               center.y + posH.y * (viewport.height / 2.0)
            );
        }

        internal static bool IsPointInPolygon(Vector2 mouse, UIBuffer<Vector2> screenPoints)
        {
            int count = screenPoints.Count;
            bool inside = false;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if (((screenPoints[i].y > mouse.y) != (screenPoints[j].y > mouse.y)) &&
                                       (mouse.x < (screenPoints[j].x - screenPoints[i].x) * (mouse.y - screenPoints[i].y) / (screenPoints[j].y - screenPoints[i].y) + screenPoints[i].x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}