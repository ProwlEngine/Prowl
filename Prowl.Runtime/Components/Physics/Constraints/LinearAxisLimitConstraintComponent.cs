// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.LinesLeaning}  Linear Axis Limit Constraint")]
public sealed class LinearAxisLimitConstraintComponent : TwoBodyConstraintComponent<LinearAxisLimit>
{
    [SerializeField, HideInInspector] private Vector3 _localOffsetA;
    [SerializeField, HideInInspector] private Vector3 _localOffsetB;
    [SerializeField, HideInInspector] private Vector3 _localAxis;
    [SerializeField, HideInInspector] private float _minimumOffset = 0;
    [SerializeField, HideInInspector] private float _maximumOffset = 0;
    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public Vector3 LocalOffsetA
    {
        get => _localOffsetA;
        set
        {
            _localOffsetA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalOffsetB
    {
        get => _localOffsetB;
        set
        {
            _localOffsetB = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalAxis
    {
        get => _localAxis;
        set
        {
            _localAxis = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MinimumOffset
    {
        get => _minimumOffset;
        set
        {
            _minimumOffset = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MaximumOffset
    {
        get => _maximumOffset;
        set
        {
            _maximumOffset = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float SpringFrequency
    {
        get
        {
            return _springFrequency;
        }
        set
        {
            _springFrequency = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float SpringDampingRatio
    {
        get
        {
            return _springDampingRatio;
        }
        set
        {
            _springDampingRatio = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    internal override LinearAxisLimit CreateConstraint()
    {
        return new LinearAxisLimit
        {
            LocalOffsetA = _localOffsetA,
            LocalOffsetB = _localOffsetB,
            LocalAxis = _localAxis,

            MinimumOffset = _minimumOffset,
            MaximumOffset = _maximumOffset,

            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}
