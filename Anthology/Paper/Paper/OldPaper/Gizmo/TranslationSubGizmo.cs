// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
/*
using System;
using System.Numerics;

using static Prowl.PaperUI.GUI.TransformGizmo;

namespace Prowl.PaperUI.GUI;

public class RotationSubGizmo : ISubGizmo
{
    public struct RotationParams
    {
        public GizmoDirection Direction;
    }

    public struct RotationState
    {
        public float StartAxisAngle, StartRotationAngle, LastRotationAngle, CurrentDelta;
    }

    private readonly RotationParams _params;
    private RotationState _state;
    private readonly TransformGizmo _gizmo;
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

    public static float Atan2(float y, float x) => MathF.Atan2(y, x);
    public static Vector3 Cross(Vector3 x, Vector3 y) => Vector3.Cross(x, y);

    public bool Pick(Ray ray, Vector2 screenPos, out float t)
    {
        var radius = ArcRadius();
        var origin = _gizmo.Translation;
        var normal = GizmoUtils.GizmoNormal(_gizmo, _params.Direction);
        var tangent = Tangent();

        var (planeT, distFromGizmoOrigin) = GizmoUtils.RayToPlaneOrigin(normal, origin, ray.origin, ray.direction);
        var distFromGizmoEdge = Math.Abs(distFromGizmoOrigin - radius);

        var hitPos = ray.origin + ray.direction * planeT;
        var dirToOrigin = Vector3.Normalize(origin - hitPos);
        var nearestCirclePos = hitPos + dirToOrigin * (distFromGizmoOrigin - radius);

        var offset = Vector3.Normalize(nearestCirclePos - origin);

        float angle;
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

        var rotationAngle = RotationAngle(screenPos) ?? 0.0f;
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
            rotationAngle = GizmoUtils.RoundToInterval(rotationAngle.Value - _state.StartRotationAngle, _gizmo.SnapAngle * MathUtilities.Deg2Rad)
                            + _state.StartRotationAngle;
        }

        var angleDelta = rotationAngle.Value - _state.LastRotationAngle;

        if (angleDelta > Math.PI)
            angleDelta -= MathF.Tau;
        else if (angleDelta < -Math.PI)
            angleDelta += MathF.Tau;

        _state.LastRotationAngle = rotationAngle.Value;
        _state.CurrentDelta += angleDelta;

        var normal = GizmoUtils.GizmoNormal(_gizmo, _params.Direction);

        return new GizmoResult
        {
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
            var stroke = new Stroke3D() { Thickness = _gizmo.StrokeWidth, Color = color };

            var radius = ArcRadius();

            if (!focused)
            {
                var angle = ArcAngle();
                _gizmo._gui.Draw3D.Arc(radius, (MathF.PI / 2 - angle) * MathUtilities.Rad2Deg, (MathF.PI / 2 + angle) * MathUtilities.Rad2Deg, stroke);
            }
            else
            {
                var startAngle = _state.StartAxisAngle + MathF.PI / 2;
                var endAngle = startAngle + _state.CurrentDelta;

                if (startAngle > endAngle)
                {
                    (startAngle, endAngle) = (endAngle, startAngle);
                }

                endAngle += 1e-5f;

                var totalAngle = endAngle - startAngle;
                var fullCircles = (int)Math.Abs(totalAngle / MathF.Tau);

                endAngle -= MathF.Tau * fullCircles;

                var startAngle2 = endAngle;
                var endAngle2 = startAngle + MathF.Tau;

                if (Vector3.Dot(_gizmo.ViewForward, GizmoUtils.GizmoNormal(_gizmo, _params.Direction)) < 0)
                {
                    (startAngle, endAngle) = (endAngle, startAngle);
                    (startAngle2, endAngle2) = (endAngle2, startAngle2);
                }

                _gizmo._gui.Draw3D.Polyline([
                    new Vector3(MathF.Cos(startAngle) * radius, 0, MathF.Sin(startAngle) * radius),
                    Vector3.Zero,
                    new Vector3(MathF.Cos(endAngle) * radius, 0, MathF.Sin(endAngle) * radius)
                ], stroke);

                var w = stroke;
                if (fullCircles > 0)
                {
                    w.Color.a = (byte)(stroke.Color.a * Math.Min(0.25f * fullCircles, 1f));
                    _gizmo._gui.Draw3D.Sector(radius, startAngle2 * MathUtilities.Rad2Deg, endAngle2 * MathUtilities.Rad2Deg, w);
                }

                w.Color.a = (byte)(stroke.Color.a * Math.Min(0.25f * (fullCircles + 1), 1f));
                _gizmo._gui.Draw3D.Sector(radius, startAngle * MathUtilities.Rad2Deg, endAngle * MathUtilities.Rad2Deg, w);

                _gizmo._gui.Draw3D.Circle(radius, stroke);

                if (_gizmo.Snapping)
                {
                    var strokeWidth = stroke.Thickness / 2;
                    for (int i = 0; i <= Math.Tau / (_gizmo.SnapAngle * MathUtilities.Deg2Rad); i++)
                    {
                        var angle = i * (_gizmo.SnapAngle * MathUtilities.Deg2Rad) + endAngle;
                        var pos = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
                        _gizmo._gui.Draw3D.LineSegment(pos * radius * 1.1f, pos * radius * 1.2f, new Stroke3D() { Thickness = strokeWidth, Color = stroke.Color });
                    }
                }
            }
        }
    }

    private float ArcAngle()
    {
        var dot = Math.Abs(Vector3.Dot(GizmoUtils.GizmoNormal(_gizmo, _params.Direction), _gizmo.ViewForward));
        const float minDot = 0.990f;
        const float maxDot = 0.995f;

        var angle = MathF.Min(1, MathF.Max(0, dot - minDot) / (maxDot - minDot)) * (MathF.PI / 2) + (MathF.PI / 2);
        return MathF.Abs(angle - MathF.PI) < 1e-2f ? MathF.PI : angle;
    }

    public static Matrix4x4 RotationFromTo(Vector3 fromVector, Vector3 toVector)
    {
        // Normalize the input vectors
        fromVector = Vector3.Normalize(fromVector);
        toVector = Vector3.Normalize(toVector);

        // Calculate the axis of rotation
        Vector3 axis = Vector3.Cross(fromVector, toVector);

        // Check if the vectors are parallel
        if (axis.LengthSquared() < 1e-8f)
        {
            // If the vectors are parallel and pointing in the same direction, return the identity matrix
            if (Vector3.Dot(fromVector, toVector) > 0f)
            {
                return Matrix4x4.Identity;
            }
            // If the vectors are parallel and pointing in opposite directions, find a perpendicular vector
            else
            {
                axis = Vector3.Cross(fromVector, Vector3.UnitX);
                if (axis.LengthSquared() < 1e-8f)
                {
                    axis = Vector3.Cross(fromVector, Vector3.UnitZ);
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
                _gizmo.ViewUp.X, _gizmo.ViewUp.Y, _gizmo.ViewUp.Z, 0,
                -_gizmo.ViewForward.X, -_gizmo.ViewForward.Y, -_gizmo.ViewForward.Z, 0,
                -_gizmo.ViewRight.X, -_gizmo.ViewRight.Y, -_gizmo.ViewRight.Z, 0,
                0, 0, 0, 1
            );

            return r * Matrix4x4.CreateTranslation(_gizmo.Translation);
        }

        var localNormal = GizmoUtils.GizmoLocalNormal(_gizmo, _params.Direction);
        //var rotation = GizmoUtils.RotationAlign(Vector3.up, localNormal);
        var rotation = RotationFromTo(Vector3.UnitY, localNormal);

        if (_gizmo.Orientation == GizmoOrientation.Local)
        {
            //rotation = Vector4.Transform(new Vector4(rotation, 0), _gizmo.Rotation).Xyz;
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

    private float? RotationAngle(Vector2 cursorPos)
    {
        var gizmoPos = GizmoUtils.WorldToScreen(_gizmo.Viewport, _gizmo.ModelViewProjection, Vector3.Zero);
        if (!gizmoPos.HasValue)
            return null;

        var delta = Vector2.Normalize(cursorPos - gizmoPos.Value);

        if (float.IsNaN(delta.X) || float.IsNaN(delta.Y))
            return null;

        var angle = Atan2(delta.Y, delta.X);
        if (Vector3.Dot(_gizmo.ViewForward, GizmoUtils.GizmoNormal(_gizmo, _params.Direction)) < 0)
            angle *= -1;

        return angle;
    }

    private Vector3 Tangent()
    {
        var tangent = _params.Direction switch
        {
            GizmoDirection.X or GizmoDirection.Y => Vector3.UnitZ,
            GizmoDirection.Z                     => -Vector3.UnitY,
            GizmoDirection.View                  => -_gizmo.ViewRight,
            _                                    => throw new ArgumentOutOfRangeException()
        };

        if (_gizmo.Orientation == GizmoOrientation.Local && _params.Direction != GizmoDirection.View)
            tangent = QuatTransform(_gizmo.Rotation, tangent);

        return tangent;
    }

    private float ArcRadius()
    {
        return _params.Direction == GizmoDirection.View
            ? GizmoUtils.OuterCircleRadius(_gizmo)
            : _gizmo.ScaleFactor * _gizmo.GizmoSize;
    }


    public Vector3 QuatTransform(Quaternion rotation, Vector3 point)
    {
        float x = rotation.X * 2.0f;
        float y = rotation.Y * 2.0f;
        float z = rotation.Z * 2.0f;
        float xx = rotation.X * x;
        float yy = rotation.Y * y;
        float zz = rotation.Z * z;
        float xy = rotation.X * y;
        float xz = rotation.X * z;
        float yz = rotation.Y * z;
        float wx = rotation.W * x;
        float wy = rotation.W * y;
        float wz = rotation.W * z;

        Vector3 res;
        res.X = (1.0f - (yy + zz)) * point.X + (xy - wz) * point.Y + (xz + wy) * point.Z;
        res.Y = (xy + wz) * point.X + (1.0f - (xx + zz)) * point.Y + (yz - wx) * point.Z;
        res.Z = (xz - wy) * point.X + (yz + wx) * point.Y + (1.0f - (xx + yy)) * point.Z;
        return res;
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
        public float StartDelta;
        public Vector3 StartScale;
        public Vector3 Scale;
        public Vector3 ScaleDelta;
    }

    private readonly ScaleParams _params;
    private ScaleState _state;
    private readonly TransformGizmo _gizmo;
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

    public bool Pick(Ray ray, Vector2 screenPos, out float t)
    {
        if (_params.Mode == TransformGizmoMode.ScaleUniform)
        {
            // If were scale Uniform and Translate view exists
            if (_gizmo.mode.HasFlag(TransformGizmoMode.TranslateView))
            {
                // then we only show if ctrl is pressed
                if (!_gizmo._gui.IsKeyDown(PaperKey.LeftShift))
                {
                    t = float.NaN;
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
        _state.Scale = Vector3.One;
        _state.ScaleDelta = Vector3.One;

        t = pickResult.T;
        return pickResult.Picked;
    }

    public GizmoResult? Update(Ray ray, Vector2 screenPos)
    {
        var delta = DistanceFromOrigin2D(_gizmo, screenPos);
        if (!delta.HasValue)
            return null;

        delta /= _state.StartDelta;

        //if (_gizmo.Snapping)
        //{
        //    delta = GizmoUtils.RoundToInterval(delta.Value, _gizmo.SnapDistance);
        //}

        delta = MathF.Max(delta.Value, 1e-4f) - 1.0f;

        Vector3 direction;
        switch (_params.TransformKind)
        {
            case TransformKind.Axis:
                direction = GizmoUtils.GizmoLocalNormal(_gizmo, _params.Direction);
                break;
            case TransformKind.Plane when _params.Direction == GizmoDirection.View:
                direction = Vector3.One * 0.01f;
                break;
            case TransformKind.Plane:
                direction = Vector3.Normalize(GizmoUtils.PlaneBitangent(_params.Direction) + GizmoUtils.PlaneTangent(_params.Direction));
                break;
            default:
                direction = Vector3.Zero;
                break;
        }

        var scale = Vector3.One + (direction * delta.Value);
        _state.ScaleDelta = Vector3.One + (scale - _state.Scale);
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
                if (!_gizmo._gui.IsKeyDown(PaperKey.LeftShift))
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

    private float? DistanceFromOrigin2D(TransformGizmo _gizmo, Vector2 cursorPos)
    {
        var gizmoPos = GizmoUtils.WorldToScreen(_gizmo.Viewport, _gizmo.ModelViewProjection, Vector3.Zero);
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

    private readonly TranslationParams _params;
    private TranslationState _state;
    private readonly TransformGizmo _gizmo;
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

    public bool Pick(Ray ray, Vector2 screenPos, out float t)
    {
        if (_params.Mode == TransformGizmoMode.TranslateView)
        {
            // If were TranslateView and scale Uniform exists
            if (_gizmo.mode.HasFlag(TransformGizmoMode.ScaleUniform))
            {
                // then we only show if ctrl is notpressed
                if (_gizmo._gui.IsKeyDown(PaperKey.LeftShift))
                {
                    t = float.NaN;
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
        _state.CurrentDelta = Vector3.Zero;

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

        if (_gizmo.Orientation == GizmoOrientation.Local)
        {
            //var inverseRotation = Quaternion.Inverse(_gizmo.Rotation);
            //translationDelta = Vector4.Transform(new Vector4(translationDelta, 0), inverseRotation).Xyz;
            //totalTranslation = Vector4.Transform(new Vector4(totalTranslation, 0), inverseRotation).Xyz;
        }

        _state.LastPoint = newPoint;
        _state.CurrentDelta = newDelta;

        return new GizmoResult
        {
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
                if (_gizmo._gui.IsKeyDown(PaperKey.LeftShift))
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
*/