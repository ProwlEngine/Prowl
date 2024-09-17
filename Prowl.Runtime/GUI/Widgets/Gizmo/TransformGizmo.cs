// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.GUI;
// Based on: https://github.com/urholaukkarinen/transform-gizmo - Dual licensed under MIT and Apache 2.0.

public enum GizmoDirection { X, Y, Z, View }

public enum TransformKind { Axis, Plane }

public struct PickResult
{
    public Vector3 SubGizmoPoint;
    public double T;
    public bool Picked;

    public static PickResult None => new PickResult { Picked = false };
}

public struct GizmoResult
{
    public Vector3? TranslationDelta;
    public Vector3? TotalTranslation;

    public Vector3? Scale;
    public Vector3? ScaleDelta;
    public Vector3? StartScale;

    public Vector3? RotationAxis;
    public double? RotationDelta;
    public double? TotalRotation;
    public bool? IsViewAxis;
}

[Flags]
public enum TransformGizmoMode
{
    /// Rotate around the X axis
    RotateX = 1 << 0,
    /// Rotate around the Y axis
    RotateY = 1 << 1,
    /// Rotate around the Z axis
    RotateZ = 1 << 2,
    /// Rotate around the view forward axis
    RotateView = 1 << 3,
    /// Translate along the X axis
    TranslateX = 1 << 4,
    /// Translate along the Y axis
    TranslateY = 1 << 5,
    /// Translate along the Z axis
    TranslateZ = 1 << 6,
    /// Translate along the XY plane
    TranslateXY = 1 << 7,
    /// Translate along the XZ plane
    TranslateXZ = 1 << 8,
    /// Translate along the YZ plane
    TranslateYZ = 1 << 9,
    /// Translate along the view forward axis
    TranslateView = 1 << 10,
    /// Scale along the X axis
    ScaleX = 1 << 11,
    /// Scale along the Y axis
    ScaleY = 1 << 12,
    /// Scale along the Z axis
    ScaleZ = 1 << 13,
    /// Scale along the XY plane
    ScaleXY = 1 << 14,
    /// Scale along the XZ plane
    ScaleXZ = 1 << 15,
    /// Scale along the YZ plane
    ScaleYZ = 1 << 16,
    /// Scale uniformly in all directions
    ScaleUniform = 1 << 17,
    /// Rotate using an arcball (trackball)
    Arcball = 1 << 18,
}

public class TransformGizmo
{
    public bool IsOver => hoveredGizmo != null || focusedGizmo != null;

    public Rect Viewport;
    public readonly double GizmoSize = 75.0;
    public readonly double StrokeWidth = 4.0;

    public Vector3 ViewForward;
    public Vector3 ViewUp;
    public Vector3 ViewRight;
    public Matrix4x4 ViewMatrix;
    public Matrix4x4 ProjectionMatrix;
    public enum GizmoOrientation { Global, Local }
    public GizmoOrientation Orientation = GizmoOrientation.Local;

    public bool Snapping = false;
    public double SnapDistance = 1f;
    public double SnapAngle = 0.1f;

    public Vector3 Translation;
    public Quaternion Rotation;
    public Vector3 Scale;

    internal double ScaleFactor;
    internal double FocusDistance;
    internal Matrix4x4 ViewProjection;
    internal Matrix4x4 Model;
    internal Matrix4x4 ModelViewProjection;
    // internal Matrix4x4 InverseModelViewProjection;

    private readonly List<ISubGizmo> _subGizmos = [];
    internal readonly Gui _gui;
    internal TransformGizmoMode mode;

    private ISubGizmo? hoveredGizmo;
    private ISubGizmo? focusedGizmo;

    public TransformGizmo(Gui gui, TransformGizmoMode mode)
    {
        _gui = gui;
        SetMode(mode);
    }

    public void SetMode(TransformGizmoMode mode)
    {
        this.mode = mode;
        _subGizmos.Clear();
        CreateSubGizmos();
    }

    public void UpdateCamera(Rect viewport, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, Vector3 up, Vector3 forward, Vector3 right)
    {
        Viewport = viewport;
        ViewUp = up;
        ViewForward = forward;
        ViewRight = right;
        ViewMatrix = viewMatrix;
        ProjectionMatrix = projectionMatrix;
        ViewProjection = viewMatrix * projectionMatrix;

        ModelViewProjection = Model * ViewProjection;

        ScaleFactor = ModelViewProjection[15]
                      / ProjectionMatrix[0]
                      / Viewport.width
                      * 2.0;

        FocusDistance = ScaleFactor * (StrokeWidth / 2.0 + 5.0);
    }

    public void SetTransform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Translation = position;
        Rotation = rotation;
        Scale = scale;

        Model = Matrix4x4.TRS(position, rotation, scale);
        ModelViewProjection = Model * ViewProjection;
    }

    private void CreateSubGizmos()
    {
        // TODO: Editor Preferences to customize Gizmo
        AddTranslateAxis();
        AddTranslatePlanes();
        if (mode.HasFlag(TransformGizmoMode.TranslateView))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateView, Direction = GizmoDirection.View, TransformKind = TransformKind.Plane }));

        AddScaleAxis();
        if (mode.HasFlag(TransformGizmoMode.ScaleUniform))
            _subGizmos.Add(new ScaleSubGizmo(this, new() { Mode = TransformGizmoMode.ScaleUniform, Direction = GizmoDirection.View, TransformKind = TransformKind.Plane }));

        AddRotateAxis();
        if (mode.HasFlag(TransformGizmoMode.RotateView))
            _subGizmos.Add(new RotationSubGizmo(this, new() { Direction = GizmoDirection.View }));
    }

    private void AddRotateAxis()
    {
        if (mode.HasFlag(TransformGizmoMode.RotateX))
            _subGizmos.Add(new RotationSubGizmo(this, new() { Direction = GizmoDirection.X }));

        if (mode.HasFlag(TransformGizmoMode.RotateY))
            _subGizmos.Add(new RotationSubGizmo(this, new() { Direction = GizmoDirection.Y }));

        if (mode.HasFlag(TransformGizmoMode.RotateZ))
            _subGizmos.Add(new RotationSubGizmo(this, new() { Direction = GizmoDirection.Z }));
    }

    private void AddScaleAxis()
    {
        if (mode.HasFlag(TransformGizmoMode.ScaleX))
            _subGizmos.Add(new ScaleSubGizmo(this, new() { Mode = TransformGizmoMode.ScaleX, Direction = GizmoDirection.X, TransformKind = TransformKind.Axis }));

        if (mode.HasFlag(TransformGizmoMode.ScaleY))
            _subGizmos.Add(new ScaleSubGizmo(this, new() { Mode = TransformGizmoMode.ScaleY, Direction = GizmoDirection.Y, TransformKind = TransformKind.Axis }));

        if (mode.HasFlag(TransformGizmoMode.ScaleZ))
            _subGizmos.Add(new ScaleSubGizmo(this, new() { Mode = TransformGizmoMode.ScaleZ, Direction = GizmoDirection.Z, TransformKind = TransformKind.Axis }));
    }

    private void AddTranslateAxis()
    {
        if (mode.HasFlag(TransformGizmoMode.TranslateX))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateX, Direction = GizmoDirection.X, TransformKind = TransformKind.Axis }));

        if (mode.HasFlag(TransformGizmoMode.TranslateY))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateY, Direction = GizmoDirection.Y, TransformKind = TransformKind.Axis }));

        if (mode.HasFlag(TransformGizmoMode.TranslateZ))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateZ, Direction = GizmoDirection.Z, TransformKind = TransformKind.Axis }));
    }

    private void AddTranslatePlanes()
    {
        if (mode.HasFlag(TransformGizmoMode.TranslateXY))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateXY, Direction = GizmoDirection.X, TransformKind = TransformKind.Plane }));

        if (mode.HasFlag(TransformGizmoMode.TranslateXZ))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateXZ, Direction = GizmoDirection.Y, TransformKind = TransformKind.Plane }));

        if (mode.HasFlag(TransformGizmoMode.TranslateYZ))
            _subGizmos.Add(new TranslationSubGizmo(this, new() { Mode = TransformGizmoMode.TranslateYZ, Direction = GizmoDirection.Z, TransformKind = TransformKind.Plane }));
    }

    public GizmoResult? Update(Ray ray, Vector2 screenPos, bool blockPicking)
    {
        foreach (var subGizmo in _subGizmos)
            subGizmo.SetFocused(false);

        if (focusedGizmo == null && !blockPicking)
        {
            hoveredGizmo = null;

            List<(ISubGizmo, double)> pickResults = [];
            foreach (var subGizmo in _subGizmos)
            {
                if (subGizmo.Pick(ray, screenPos, out double t))
                {
                    pickResults.Add((subGizmo, t));
                }
            }

            // Sort by t
            //pickResults.Sort((a, b) => a.Item2.CompareTo(b.Item2));

            if (pickResults.Count > 0)
            {
                hoveredGizmo = pickResults[0].Item1;
                hoveredGizmo.SetFocused(true);
                if (_gui.IsPointerClick(MouseButton.Left))
                    focusedGizmo = hoveredGizmo;
            }
        }


        if (focusedGizmo != null)
        {
            hoveredGizmo = focusedGizmo;
            focusedGizmo.SetFocused(true);
            if (_gui.IsPointerUp(MouseButton.Left))
            {
                focusedGizmo = null;
            }
            else
            {
                var result = focusedGizmo.Update(ray, screenPos);
                return result;
            }
        }


        return null;
    }

    public void Draw()
    {
        using (_gui.Draw3D.Viewport(Viewport))
        {
            _subGizmos.ForEach(g => g.Draw());
        }
    }
}
