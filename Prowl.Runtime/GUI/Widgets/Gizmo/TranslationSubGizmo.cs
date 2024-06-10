using System;
using static Prowl.Runtime.GUI.ScaleSubGizmo;
using static Prowl.Runtime.GUI.TransformGizmo;

namespace Prowl.Runtime.GUI
{
    public class RotationSubGizmo : ISubGizmo
    {
        public struct RotationParams
        {
            public GizmoDirection Direction;
        }

        public struct RotationState
        {
            public double StartAxisAngle, StartRotationAngle, LastRotationAngle, CurrentDelta;
        }

        private RotationParams _params;
        private RotationState _state;
        private TransformGizmo _gizmo;
        internal bool focused;

        public RotationSubGizmo(TransformGizmo gizmo, RotationParams parameters)
        {
            _gizmo = gizmo;
            _params = parameters;
            _state = new RotationState();
        }

        public void SetFocused(bool focused)
        {
            this.focused = focused;
        }

        public static double Atan2(double y, double x) => Math.Atan2(y, x);
        public static Vector3 Cross(Vector3 x, Vector3 y) => Vector3.Cross(x, y);

        public bool Pick(Ray ray, Vector2 screenPos, out double t)
        {
            var radius = ArcRadius();
            var origin = _gizmo.Translation;
            var normal = GizmoUtils.GizmoNormal(_gizmo, _params.Direction);
            var tangent = Tangent();

            var (planeT, distFromGizmoOrigin) = GizmoUtils.RayToPlaneOrigin(normal, origin, ray.origin, ray.direction);
            var distFromGizmoEdge = Math.Abs(distFromGizmoOrigin - radius);

            var hitPos = ray.origin + ray.direction * planeT;
            var dirToOrigin = (origin - hitPos).normalized;
            var nearestCirclePos = hitPos + dirToOrigin * (distFromGizmoOrigin - radius);

            var offset = (nearestCirclePos - origin).normalized;

            double angle;
            if (_params.Direction == GizmoDirection.View)
            {
                angle = Atan2(Vector3.Dot(Cross(tangent, normal), offset), Vector3.Dot(tangent, offset));
                //Console.WriteLine($"radius: {radius}");
            }
            else
            {
                var forward = _gizmo.ViewForward * -1;
                angle = Atan2(Vector3.Dot(Cross(offset, forward), normal), Vector3.Dot(offset, forward));
            }

            var rotationAngle = RotationAngle(screenPos) ?? 0.0;
            _state.StartAxisAngle = angle;
            _state.StartRotationAngle = rotationAngle;
            _state.LastRotationAngle = rotationAngle;
            _state.CurrentDelta = 0;

            t = planeT;
            return distFromGizmoEdge <= _gizmo.FocusDistance && Math.Abs(angle) < ArcAngle();
        }

        public GizmoResult? Update(Ray ray, Vector2 screenPos)
        {
            var rotationAngle = RotationAngle(screenPos);
            if (!rotationAngle.HasValue)
                return null;

            if (_gizmo.Snapping)
            {
                rotationAngle = GizmoUtils.RoundToInterval(rotationAngle.Value - _state.StartRotationAngle, _gizmo.SnapAngle * Mathf.Deg2Rad)
                                + _state.StartRotationAngle;
            }

            var angleDelta = rotationAngle.Value - _state.LastRotationAngle;

            if (angleDelta > Math.PI)
                angleDelta -= Math.Tau;
            else if (angleDelta < -Math.PI)
                angleDelta += Math.Tau;

            _state.LastRotationAngle = rotationAngle.Value;
            _state.CurrentDelta += angleDelta;

            var normal = GizmoUtils.GizmoNormal(_gizmo, _params.Direction);

            return new GizmoResult {
                RotationAxis = normal,
                RotationDelta = -angleDelta,
                TotalRotation = _state.CurrentDelta,
                IsViewAxis = _params.Direction == GizmoDirection.View
            };
        }

        public void Draw()
        {
            var transform = RotationMatrix();

            using (_gizmo._gui.Draw3D.Matrix(transform * _gizmo.ViewProjection))
            {
                var color = GizmoUtils.GizmoColor(_gizmo, focused, _params.Direction);
                var stroke = new Stroke3D() { Thickness = _gizmo.StrokeWidth, Color = color, AntiAliased = true };

                var radius = ArcRadius();

                if (!focused)
                {
                    var angle = ArcAngle();
                    _gizmo._gui.Draw3D.Arc(radius, (Math.PI / 2 - angle) * Mathf.Rad2Deg, (Math.PI / 2 + angle) * Mathf.Rad2Deg, stroke);
                }
                else
                {
                    var startAngle = _state.StartAxisAngle + Math.PI / 2;
                    var endAngle = startAngle + _state.CurrentDelta;

                    if (startAngle > endAngle)
                    {
                        (startAngle, endAngle) = (endAngle, startAngle);
                    }

                    endAngle += 1e-5;

                    var totalAngle = endAngle - startAngle;
                    var fullCircles = (int)Math.Abs(totalAngle / Math.Tau);

                    endAngle -= Math.Tau * fullCircles;

                    var startAngle2 = endAngle;
                    var endAngle2 = startAngle + Math.Tau;

                    if (Vector3.Dot(_gizmo.ViewForward, GizmoUtils.GizmoNormal(_gizmo, _params.Direction)) < 0)
                    {
                        (startAngle, endAngle) = (endAngle, startAngle);
                        (startAngle2, endAngle2) = (endAngle2, startAngle2);
                    }

                    _gizmo._gui.Draw3D.Polyline(new[]
                    {
                    new Vector3(Math.Cos(startAngle) * radius, 0, Math.Sin(startAngle) * radius),
                    Vector3.zero,
                    new Vector3(Math.Cos(endAngle) * radius, 0, Math.Sin(endAngle) * radius)
                }, stroke);

                    var w = stroke;
                    if (fullCircles > 0)
                    {
                        w.Color.alpha = (byte)(stroke.Color.alpha * Math.Min(0.25f * fullCircles, 1f));
                        _gizmo._gui.Draw3D.Sector(radius, startAngle2 * Mathf.Rad2Deg, endAngle2 * Mathf.Rad2Deg, w);
                    }

                    w.Color.alpha = (byte)(stroke.Color.alpha * Math.Min(0.25f * (fullCircles + 1), 1f));
                    _gizmo._gui.Draw3D.Sector(radius, startAngle * Mathf.Rad2Deg, endAngle * Mathf.Rad2Deg, w);

                    _gizmo._gui.Draw3D.Circle(radius, stroke);

                    if (_gizmo.Snapping)
                    {
                        var strokeWidth = stroke.Thickness / 2;
                        for (int i = 0; i <= Math.Tau / (_gizmo.SnapAngle * Mathf.Deg2Rad); i++)
                        {
                            var angle = i * (_gizmo.SnapAngle * Mathf.Deg2Rad) + endAngle;
                            var pos = new Vector3(Math.Cos(angle), 0, Math.Sin(angle));
                            _gizmo._gui.Draw3D.LineSegment(pos * radius * 1.1, pos * radius * 1.2, new Stroke3D() { Thickness = strokeWidth, Color = stroke.Color, AntiAliased = true });
                        }
                    }
                }
            }
        }

        private double ArcAngle()
        {
            var dot = Math.Abs(Vector3.Dot(GizmoUtils.GizmoNormal(_gizmo, _params.Direction), _gizmo.ViewForward));
            const double minDot = 0.990;
            const double maxDot = 0.995;

            var angle = Math.Min(1, Math.Max(0, dot - minDot) / (maxDot - minDot)) * (Math.PI / 2) + (Math.PI / 2);
            return Math.Abs(angle - Math.PI) < 1e-2 ? Math.PI : angle;
        }

        public static Matrix4x4 RotationFromTo(Vector3 fromVector, Vector3 toVector)
        {
            // Normalize the input vectors
            fromVector = Vector3.Normalize(fromVector);
            toVector = Vector3.Normalize(toVector);

            // Calculate the axis of rotation
            Vector3 axis = Vector3.Cross(fromVector, toVector);

            // Check if the vectors are parallel
            if (axis.sqrMagnitude < 1e-8f)
            {
                // If the vectors are parallel and pointing in the same direction, return the identity matrix
                if (Vector3.Dot(fromVector, toVector) > 0f)
                {
                    return Matrix4x4.Identity;
                }
                // If the vectors are parallel and pointing in opposite directions, find a perpendicular vector
                else
                {
                    axis = Vector3.Cross(fromVector, Vector3.right);
                    if (axis.sqrMagnitude < 1e-8f)
                    {
                        axis = Vector3.Cross(fromVector, Vector3.forward);
                    }
                }
            }

            // Calculate the angle between the vectors
            float angle = (float)Math.Acos(Vector3.Dot(fromVector, toVector));

            // Create the rotation matrix using axis-angle rotation
            return Matrix4x4.CreateFromAxisAngle(axis, angle);
        }

        private Matrix4x4 RotationMatrix()
        {
            if (_params.Direction == GizmoDirection.View)
            {
                var r = new Matrix4x4(
                    _gizmo.ViewUp.x, _gizmo.ViewUp.y, _gizmo.ViewUp.z, 0,
                    -_gizmo.ViewForward.x, -_gizmo.ViewForward.y, -_gizmo.ViewForward.z, 0,
                    -_gizmo.ViewRight.x, -_gizmo.ViewRight.y, -_gizmo.ViewRight.z, 0,
                    0, 0, 0, 1
                );

                return r * Matrix4x4.CreateTranslation(_gizmo.Translation);
            }

            var localNormal = GizmoUtils.GizmoLocalNormal(_gizmo, _params.Direction);
            //var rotation = GizmoUtils.RotationAlign(Vector3.up, localNormal);
            var rotation = RotationFromTo(Vector3.up, localNormal);

            if (_gizmo.Orientation == GizmoOrientation.Local)
            {
                //rotation = Vector4.Transform(new Vector4(rotation, 0), _gizmo.Rotation).xyz;
                rotation = rotation * Matrix4x4.CreateFromQuaternion(_gizmo.Rotation);
            }

            var tangent = Tangent();
            var normal = GizmoUtils.GizmoNormal(_gizmo, _params.Direction);
            var forward = _gizmo.ViewForward * -1; // (_gizmo.LeftHanded ? -1 : 1);
            var angle = Atan2(Vector3.Dot(Vector3.Cross(tangent, forward), normal), Vector3.Dot(tangent, forward));

            //rotation = Matrix4x4.CreateFromQuaternion(Quaternion.AngleAxis(angle, normal)) * rotation;
            rotation = rotation * Matrix4x4.CreateFromAxisAngle(normal, angle);

            return rotation * Matrix4x4.CreateTranslation(_gizmo.Translation);
        }

        private double? RotationAngle(Vector2 cursorPos)
        {
            var gizmoPos = GizmoUtils.WorldToScreen(_gizmo.Viewport, _gizmo.ModelViewProjection, Vector3.zero);
            if (!gizmoPos.HasValue)
                return null;

            var delta = (cursorPos - gizmoPos.Value).normalized;

            if (double.IsNaN(delta.x) || double.IsNaN(delta.y))
                return null;

            var angle = Atan2(delta.y, delta.x);
            if (Vector3.Dot(_gizmo.ViewForward, GizmoUtils.GizmoNormal(_gizmo, _params.Direction)) < 0)
                angle *= -1;

            return angle;
        }

        private Vector3 Tangent()
        {
            var tangent = _params.Direction switch {
                GizmoDirection.X or GizmoDirection.Y => Vector3.forward,
                GizmoDirection.Z => -Vector3.up,
                GizmoDirection.View => -_gizmo.ViewRight,
                _ => throw new ArgumentOutOfRangeException()
            };

            if (_gizmo.Orientation == GizmoOrientation.Local && _params.Direction != GizmoDirection.View)
                tangent = _gizmo.Rotation * tangent;

            return tangent;
        }

        private double ArcRadius()
        {
            return _params.Direction == GizmoDirection.View
                ? GizmoUtils.OuterCircleRadius(_gizmo)
                : _gizmo.ScaleFactor * _gizmo.GizmoSize;
        }
    }

    public class ScaleSubGizmo : ISubGizmo
    {
        public struct ScaleParams
        {
            public TransformGizmoMode Mode;
            public GizmoDirection Direction;
            public TransformKind TransformKind;
        }
    
        public struct ScaleState
        {
            public double StartDelta;
            public Vector3 StartScale;
            public Vector3 Scale;
            public Vector3 ScaleDelta;
        }
    
        private ScaleParams _params;
        private ScaleState _state;
        private TransformGizmo _gizmo;
        internal bool focused;

        public ScaleSubGizmo(TransformGizmo gizmo, ScaleParams parameters)
        {
            _gizmo = gizmo;
            _params = parameters;
            _state = new ScaleState();
        }
    
        public void SetFocused(bool focused)
        {
            this.focused = focused;
        }
    
        public bool Pick(Ray ray, Vector2 screenPos, out double t)
        {
            if (_params.Mode == TransformGizmoMode.ScaleUniform)
            {
                // If were scale Uniform and Translate view exists
                if (_gizmo.mode.HasFlag(TransformGizmoMode.TranslateView))
                {
                    // then we only show if ctrl is pressed
                    if (!_gizmo._gui.IsKeyDown(Veldrid.Key.ShiftLeft))
                    {
                        t = double.NaN;
                        return false;
                    }
                }
            }

            var pickResult = PickResult.None;
            switch (_params.TransformKind)
            {
                case TransformKind.Plane when _params.Direction == GizmoDirection.View:
                    pickResult = GizmoUtils.PickCircle(_gizmo, ray, GizmoUtils.InnerCircleRadius(_gizmo), true);
                    break;
                case TransformKind.Plane:
                    pickResult = GizmoUtils.PickPlane(_gizmo, ray, _params.Direction);
                    break;
                case TransformKind.Axis:
                    pickResult = GizmoUtils.PickArrow(_gizmo, ray, _params.Direction, _params.Mode);
                    break;
            }
    
            var startDelta = DistanceFromOrigin2D(_gizmo, screenPos);
            _state.StartDelta = startDelta.HasValue ? startDelta.Value : 0;
            _state.StartScale = _gizmo.Scale;
            _state.Scale = Vector3.one;
            _state.ScaleDelta = Vector3.one;

            t = pickResult.T;
            return pickResult.Picked;
        }
    
        public GizmoResult? Update(Ray ray, Vector2 screenPos)
        {
            var delta = DistanceFromOrigin2D(_gizmo, screenPos);
            if (!delta.HasValue)
                return null;

            delta /= _state.StartDelta;
    
            if (_gizmo.Snapping)
            {
                delta = GizmoUtils.RoundToInterval(delta.Value, _gizmo.SnapDistance);
            }
    
            delta = Math.Max(delta.Value, 1e-4) - 1.0;
    
            Vector3 direction;
            switch (_params.TransformKind)
            {
                case TransformKind.Axis:
                    direction = GizmoUtils.GizmoLocalNormal(_gizmo, _params.Direction);
                    break;
                case TransformKind.Plane when _params.Direction == GizmoDirection.View:
                    direction = Vector3.one;
                    break;
                case TransformKind.Plane:
                    direction = (GizmoUtils.PlaneBitangent(_params.Direction) +
                                 GizmoUtils.PlaneTangent(_params.Direction)).normalized;
                    break;
                default:
                    direction = Vector3.zero;
                    break;
            }
    
            var scale = Vector3.one + (direction * delta.Value);
            _state.ScaleDelta = Vector3.one + (scale - _state.Scale);
            _state.Scale = scale;

    
            return new GizmoResult { Scale = scale, StartScale = _state.StartScale, ScaleDelta = _state.ScaleDelta };
        }

        public void Draw()
        {
            if (_params.Mode == TransformGizmoMode.ScaleUniform)
            {
                // If were scale Uniform and Translate view exists
                if (_gizmo.mode.HasFlag(TransformGizmoMode.TranslateView))
                {
                    // then we only show if ctrl is pressed
                    if (!_gizmo._gui.IsKeyDown(Veldrid.Key.ShiftLeft))
                        return;
                }
            }

            Matrix4x4 transform = Matrix4x4.CreateTranslation(_gizmo.Translation);
            switch (_params.TransformKind)
            {
                case TransformKind.Axis:
                    GizmoUtils.DrawArrow(_gizmo, focused, transform, _params.Direction, _params.Mode);//, Vector3.Dot(_state.Scale, GizmoUtils.GizmoNormal(_gizmo, _params.Direction)));
                    break;
                case TransformKind.Plane when _params.Direction == GizmoDirection.View:
                    GizmoUtils.DrawQuad(_gizmo, focused, transform);
                    break;
                case TransformKind.Plane:
                    GizmoUtils.DrawPlane(_gizmo, focused, transform, _params.Direction);
                    break;
            }
        }

        private double? DistanceFromOrigin2D(TransformGizmo _gizmo, Vector2 cursorPos)
        {
            var gizmoPos = GizmoUtils.WorldToScreen(_gizmo.Viewport, _gizmo.ModelViewProjection, Vector3.zero);
            if (!gizmoPos.HasValue)
                return null;

            return Vector2.Distance(cursorPos, gizmoPos.Value);
        }
    }

    public class TranslationSubGizmo : ISubGizmo
    {
        public struct TranslationParams
        {
            public TransformGizmoMode Mode;
            public GizmoDirection Direction;
            public TransformKind TransformKind;
        }

        public struct TranslationState
        {
            public Vector3 StartPoint, LastPoint, CurrentDelta;
        }

        private TranslationParams _params;
        private TranslationState _state;
        private TransformGizmo _gizmo;
        internal bool focused;

        public TranslationSubGizmo(TransformGizmo gizmo, TranslationParams parameters)
        {
            _gizmo = gizmo;
            _params = parameters;
            _state = new TranslationState();
        }

        public void SetFocused(bool focused)
        {
            this.focused = focused;
        }

        public bool Pick(Ray ray, Vector2 screenPos, out double t)
        {
            if (_params.Mode == TransformGizmoMode.TranslateView)
            {
                // If were TranslateView and scale Uniform exists
                if (_gizmo.mode.HasFlag(TransformGizmoMode.ScaleUniform))
                {
                    // then we only show if ctrl is notpressed
                    if (_gizmo._gui.IsKeyDown(Veldrid.Key.ShiftLeft))
                    {
                        t = double.NaN;
                        return false;
                    }
                }
            }

            var pickResult = PickResult.None;
            switch (_params.TransformKind)
            {
                case TransformKind.Plane when _params.Direction == GizmoDirection.View:
                    pickResult = GizmoUtils.PickCircle(_gizmo, ray, GizmoUtils.InnerCircleRadius(_gizmo), true);
                    break;
                case TransformKind.Plane:
                    pickResult = GizmoUtils.PickPlane(_gizmo, ray, _params.Direction);
                    break;
                case TransformKind.Axis:
                    pickResult = GizmoUtils.PickArrow(_gizmo, ray, _params.Direction, _params.Mode);
                    break;
            }

            _state.StartPoint = pickResult.SubGizmoPoint;
            _state.LastPoint = pickResult.SubGizmoPoint;
            _state.CurrentDelta = Vector3.zero;

            t = pickResult.T;
            return pickResult.Picked;
        }

        public GizmoResult? Update(Ray ray, Vector2 screenPos)
        {

            Vector3 newPoint;
            if (_params.TransformKind == TransformKind.Axis)
            {
                newPoint = GizmoUtils.PointOnAxis(_gizmo, ray, _params.Direction);
            }
            else
            {
                var normal = GizmoUtils.GizmoNormal(_gizmo, _params.Direction);
                var origin = GizmoUtils.PlaneGlobalOrigin(_gizmo, _params.Direction);
                newPoint = GizmoUtils.PointOnPlane(normal, origin, ray);
            }

            var newDelta = newPoint - _state.StartPoint;

            if (_gizmo.Snapping)
            {
                newDelta = _params.TransformKind == TransformKind.Axis
                    ? GizmoUtils.SnapTranslationVector(newDelta, _gizmo)
                    : GizmoUtils.SnapTranslationPlane(newDelta, _params.Direction, _gizmo);
                newPoint = _state.StartPoint + newDelta;
            }

            var translationDelta = newPoint - _state.LastPoint;
            var totalTranslation = newPoint - _state.StartPoint;

            if (_gizmo.Orientation == TransformGizmo.GizmoOrientation.Local)
            {
                //var inverseRotation = Quaternion.Inverse(_gizmo.Rotation);
                //translationDelta = Vector4.Transform(new Vector4(translationDelta, 0), inverseRotation).xyz;
                //totalTranslation = Vector4.Transform(new Vector4(totalTranslation, 0), inverseRotation).xyz;
            }

            _state.LastPoint = newPoint;
            _state.CurrentDelta = newDelta;

            return new GizmoResult {
                TranslationDelta = translationDelta,
                TotalTranslation = totalTranslation
            };
        }

        public void Draw()
        {
            if (_params.Mode == TransformGizmoMode.TranslateView)
            {
                // If were TranslateView and scale Uniform exists
                if (_gizmo.mode.HasFlag(TransformGizmoMode.ScaleUniform))
                {
                    // then we only show if ctrl is notpressed
                    if (_gizmo._gui.IsKeyDown(Veldrid.Key.ShiftLeft))
                        return;
                }
            }

            Matrix4x4 transform = Matrix4x4.CreateTranslation(_gizmo.Translation);
            switch (_params.TransformKind)
            {
                case TransformKind.Axis:
                    GizmoUtils.DrawArrow(_gizmo, focused, transform, _params.Direction, _params.Mode);
                    break;
                case TransformKind.Plane when _params.Direction == GizmoDirection.View:
                    GizmoUtils.DrawCircle(_gizmo, focused, transform);
                    break;
                case TransformKind.Plane:
                    GizmoUtils.DrawPlane(_gizmo, focused, transform, _params.Direction);
                    break;
            }
        }
    }
}