// Based on: https://github.com/urholaukkarinen/transform-gizmo - Dual licensed under MIT and Apache 2.0.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.OrigamiUI.Gizmo;

public enum GizmoDirection { X, Y, Z, View }

public enum TransformKind { Axis, Plane }

public struct PickResult
{
    public Float3 SubGizmoPoint;
    public float T;
    public bool Picked;

    public static PickResult None => new PickResult { Picked = false };
}

public struct GizmoResult
{
    public Float3? TranslationDelta;
    public Float3? TotalTranslation;

    public Float3? Scale;
    public Float3? ScaleDelta;
    public Float3? StartScale;

    public Float3? RotationAxis;
    public float? RotationDelta;
    public float? TotalRotation;
    public bool? IsViewAxis;
}

[Flags]
public enum TransformGizmoMode
{
    RotateX = 1 << 0,
    RotateY = 1 << 1,
    RotateZ = 1 << 2,
    RotateView = 1 << 3,
    TranslateX = 1 << 4,
    TranslateY = 1 << 5,
    TranslateZ = 1 << 6,
    TranslateXY = 1 << 7,
    TranslateXZ = 1 << 8,
    TranslateYZ = 1 << 9,
    TranslateView = 1 << 10,
    ScaleX = 1 << 11,
    ScaleY = 1 << 12,
    ScaleZ = 1 << 13,
    ScaleXY = 1 << 14,
    ScaleXZ = 1 << 15,
    ScaleYZ = 1 << 16,
    ScaleUniform = 1 << 17,
    Arcball = 1 << 18,

    // Common presets
    Translate = TranslateX | TranslateY | TranslateZ | TranslateXY | TranslateXZ | TranslateYZ,
    Rotate = RotateX | RotateY | RotateZ,
    ScaleAll = ScaleX | ScaleY | ScaleZ | ScaleUniform,
    Universal = Translate | Rotate | ScaleAll,
}

public class TransformGizmo
{
    public bool IsOver => _hoveredGizmo != null || _focusedGizmo != null;

    public Rect Viewport;
    public float GizmoSize = 75.0f;
    public float StrokeWidth = 4.0f;

    public Float3 ViewForward;
    public Float3 ViewUp;
    public Float3 ViewRight;
    public Float3 ViewPosition;
    public Float4x4 ViewMatrix;
    public Float4x4 ProjectionMatrix;

    public enum GizmoOrientation { Global, Local }
    public GizmoOrientation Orientation = GizmoOrientation.Local;

    public bool Snapping = false;
    public bool IsShiftDown = false;
    public bool IsMouseDown = false;
    public bool IsMouseUp = false;
    public float SnapDistance = 1f;
    public float SnapAngle = 15f;

    public Float3 Translation;
    public Quaternion Rotation;
    public Float3 Scale;

    internal float ScaleFactor;
    internal float FocusDistance;
    internal Float4x4 ViewProjection;
    internal Float4x4 Model;

    private readonly List<ISubGizmo> _subGizmos = [];
    internal TransformGizmoMode Mode;
    internal readonly GizmoDraw3D Draw3D = new();

    private ISubGizmo? _hoveredGizmo;
    private ISubGizmo? _focusedGizmo;

    public TransformGizmo(TransformGizmoMode mode)
    {
        SetMode(mode);
    }

    public void SetMode(TransformGizmoMode mode)
    {
        Mode = mode;
        _subGizmos.Clear();
        CreateSubGizmos();
    }

    public void UpdateCamera(Rect viewport, Float4x4 viewMatrix, Float4x4 projectionMatrix,
        Float3 up, Float3 forward, Float3 right, Float3 position)
    {
        Viewport = viewport;
        ViewUp = up;
        ViewForward = forward;
        ViewRight = right;
        ViewPosition = position;
        ViewMatrix = viewMatrix;
        ProjectionMatrix = projectionMatrix;
        ViewProjection = projectionMatrix * viewMatrix;

        RecalcScaleFactor();
    }

    public void SetTransform(Float3 position, Quaternion rotation, Float3 scale)
    {
        Translation = position;
        Rotation = rotation;
        Scale = scale;

        Model = Float4x4.CreateTRS(position, rotation, scale);
        RecalcScaleFactor();
    }

    private void RecalcScaleFactor()
    {
        var mvp = ViewProjection * Model;
        float vpWidth = MathF.Max(1, Viewport.Size.X);
        ScaleFactor = mvp[3, 3] / ProjectionMatrix[0, 0] / vpWidth * 2.0f;
        FocusDistance = ScaleFactor * (StrokeWidth / 2.0f + 5.0f);
    }

    private void CreateSubGizmos()
    {
        AddTranslateAxis();
        AddTranslatePlanes();
        if (Mode.HasFlag(TransformGizmoMode.TranslateView))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateView, Direction = GizmoDirection.View, TransformKind = TransformKind.Plane }));

        AddScaleAxis();
        if (Mode.HasFlag(TransformGizmoMode.ScaleUniform))
            _subGizmos.Add(new ScaleSubGizmo(this, new() { Mode = TransformGizmoMode.ScaleUniform, Direction = GizmoDirection.View, TransformKind = TransformKind.Plane }));

        AddRotateAxis();
        if (Mode.HasFlag(TransformGizmoMode.RotateView))
            _subGizmos.Add(new RotationSubGizmo(this, new() { Direction = GizmoDirection.View }));
    }

    private void AddRotateAxis()
    {
        if (Mode.HasFlag(TransformGizmoMode.RotateX))
            _subGizmos.Add(new RotationSubGizmo(this, new() { Direction = GizmoDirection.X }));
        if (Mode.HasFlag(TransformGizmoMode.RotateY))
            _subGizmos.Add(new RotationSubGizmo(this, new() { Direction = GizmoDirection.Y }));
        if (Mode.HasFlag(TransformGizmoMode.RotateZ))
            _subGizmos.Add(new RotationSubGizmo(this, new() { Direction = GizmoDirection.Z }));
    }

    private void AddScaleAxis()
    {
        if (Mode.HasFlag(TransformGizmoMode.ScaleX))
            _subGizmos.Add(new ScaleSubGizmo(this, new() { Mode = TransformGizmoMode.ScaleX, Direction = GizmoDirection.X, TransformKind = TransformKind.Axis }));
        if (Mode.HasFlag(TransformGizmoMode.ScaleY))
            _subGizmos.Add(new ScaleSubGizmo(this, new() { Mode = TransformGizmoMode.ScaleY, Direction = GizmoDirection.Y, TransformKind = TransformKind.Axis }));
        if (Mode.HasFlag(TransformGizmoMode.ScaleZ))
            _subGizmos.Add(new ScaleSubGizmo(this, new() { Mode = TransformGizmoMode.ScaleZ, Direction = GizmoDirection.Z, TransformKind = TransformKind.Axis }));
    }

    private void AddTranslateAxis()
    {
        if (Mode.HasFlag(TransformGizmoMode.TranslateX))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateX, Direction = GizmoDirection.X, TransformKind = TransformKind.Axis }));
        if (Mode.HasFlag(TransformGizmoMode.TranslateY))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateY, Direction = GizmoDirection.Y, TransformKind = TransformKind.Axis }));
        if (Mode.HasFlag(TransformGizmoMode.TranslateZ))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateZ, Direction = GizmoDirection.Z, TransformKind = TransformKind.Axis }));
    }

    private void AddTranslatePlanes()
    {
        if (Mode.HasFlag(TransformGizmoMode.TranslateXY))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateXY, Direction = GizmoDirection.X, TransformKind = TransformKind.Plane }));
        if (Mode.HasFlag(TransformGizmoMode.TranslateXZ))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateXZ, Direction = GizmoDirection.Y, TransformKind = TransformKind.Plane }));
        if (Mode.HasFlag(TransformGizmoMode.TranslateYZ))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateYZ, Direction = GizmoDirection.Z, TransformKind = TransformKind.Plane }));
    }

    public GizmoResult? Update(Ray ray, Float2 screenPos, bool blockPicking)
    {
        foreach (var subGizmo in _subGizmos)
            subGizmo.SetFocused(false);

        if (_focusedGizmo == null && !blockPicking)
        {
            _hoveredGizmo = null;

            List<(ISubGizmo, float)> pickResults = [];
            foreach (var subGizmo in _subGizmos)
            {
                if (subGizmo.Pick(ray, screenPos, out float t))
                    pickResults.Add((subGizmo, t));
            }

            if (pickResults.Count > 0)
            {
                _hoveredGizmo = pickResults[0].Item1;
                _hoveredGizmo.SetFocused(true);

                if (IsMouseDown)
                    _focusedGizmo = _hoveredGizmo;
            }
        }

        if (_focusedGizmo != null)
        {
            _hoveredGizmo = _focusedGizmo;
            _focusedGizmo.SetFocused(true);

            if (IsMouseUp)
            {
                _focusedGizmo = null;
            }
            else
            {
                return _focusedGizmo.Update(ray, screenPos);
            }
        }

        return null;
    }

    public void Draw(Prowl.Quill.Canvas canvas)
    {
        Draw3D.Begin(canvas, Viewport, ViewProjection);
        foreach (var g in _subGizmos)
            g.Draw(canvas);
    }
}
