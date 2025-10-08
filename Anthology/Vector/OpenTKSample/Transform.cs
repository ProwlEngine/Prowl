// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace OpenTKSample;

/// <summary>
/// A class that represents the position, rotation, and scale of an object in a hierarchy.
/// This class manages parent-child relationships and calculates world-space transforms
/// by composing its local transform with its parent's world transform.
/// </summary>
public class Transform
{
    private Transform3DFloat _localTransform = Transform3DFloat.Identity;

    private Transform _parent;
    private readonly List<Transform> _children = [];
    public readonly ReadOnlyCollection<Transform> children;

    #region Local Properties

    /// <summary>The position of the transform relative to the parent. </summary>
    public Float3 localPosition
    {
        get => _localTransform.position;
        set => _localTransform.position = value;
    }

    /// <summary>The rotation of the transform relative to the parent.</summary>
    public Quaternion localRotation
    {
        get => _localTransform.rotation;
        set => _localTransform.rotation = Maths.Normalize(value);
    }

    /// <summary>The scale of the transform relative to the parent.</summary>
    public Float3 localScale
    {
        get => _localTransform.scale;
        set => _localTransform.scale = value;
    }

    /// <summary>The rotation of the transform relative to the parent, as euler angles in degrees.</summary>
    public Float3 localEulerAngles
    {
        get => _localTransform.eulerAngles;
        set => _localTransform.eulerAngles = value;
    }

    #endregion

    #region World Properties

    /// <summary>The world-space position of the transform.</summary>
    public Float3 position
    {
        get => localToWorldMatrix.c3.XYZ;
        set
        {
            if (_parent == null)
                localPosition = value;
            else
                localPosition = _parent.InverseTransformPoint(value);
        }
    }

    /// <summary>The world-space rotation of the transform.</summary>
    public Quaternion rotation
    {
        get
        {
            if (_parent == null) return localRotation;
            // Note: To get world rotation, we multiply parent's world rotation by our local rotation.
            // The matrix decomposition handles this correctly.
            return Maths.FromMatrix(localToWorldMatrix);
        }
        set
        {
            if (_parent == null)
                localRotation = value;
            else
                localRotation = Maths.Mul(Maths.Inverse(_parent.rotation), value);
        }
    }

    /// <summary>The world-space rotation of the transform, as euler angles in degrees.</summary>
    public Float3 eulerAngles
    {
        get => rotation.ToEulerDegrees();
        set => rotation = Maths.FromEulerDegrees(value, );
    }

    /// <summary>The world-space forward direction of this transform.</summary>
    public Float3 forward => Maths.Mul(rotation, Float3.UnitZ);

    /// <summary>The world-space up direction of this transform.</summary>
    public Float3 up => Maths.Mul(rotation, Float3.UnitY);

    /// <summary>The world-space right direction of this transform.</summary>
    public Float3 right => Maths.Mul(rotation, Float3.UnitX);


    #endregion

    #region Hierarchy Management

    /// <summary>
    /// Gets or sets the parent of this transform.
    /// </summary>
    public Transform parent
    {
        get => _parent;
        set
        {
            if (_parent == value) return;

            SetParent(value, false);
        }
    }

    public Transform()
    {
        children = new ReadOnlyCollection<Transform>(_children);
    }

    public void SetParent(Transform newParent, bool worldPositionStays = true)
    {
        if (parent == newParent) return;

        // Detach from the old parent
        _parent?._children.Remove(this);

        Float3 worldPos = position;
        Quaternion worldRot = rotation;

        parent = newParent;

        if (worldPositionStays)
        {
            position = worldPos;
            rotation = worldRot;
            // Setting world scale is complex and lossy so its often not desired.
        }

        // Attach to the new parent
        _parent?._children.Add(this);
    }

    #endregion

    #region Matrix & Space Conversions

    /// <summary>The matrix that transforms from local space to world space.</summary>
    public Float4x4 localToWorldMatrix => _parent == null ? _localTransform.ToMatrix() : _parent.localToWorldMatrix * _localTransform.ToMatrix();

    /// <summary>The matrix that transforms from world space to local space.</summary>
    public Float4x4 worldToLocalMatrix => Maths.Inverse(localToWorldMatrix);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Float3 TransformPoint(Float3 point) => Maths.TransformPoint(point, localToWorldMatrix);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Float3 InverseTransformPoint(Float3 point) => Maths.TransformPoint(point, worldToLocalMatrix);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Float3 TransformDirection(Float3 direction) => Maths.Mul(rotation, direction);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Float3 InverseTransformDirection(Float3 direction) => Maths.Mul(Maths.Inverse(rotation), direction);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LookAt(Float3 worldPosition, Float3 worldUp)
    {
        // The world-space rotation needed to look at the target
        Quaternion worldLookRotation = Maths.LookRotationSafe(worldPosition - this.position, worldUp);
        rotation = worldLookRotation;
    }

    #endregion
}
