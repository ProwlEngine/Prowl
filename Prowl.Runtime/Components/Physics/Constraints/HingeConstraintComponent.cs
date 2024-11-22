// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;

[AddComponentMenu($"{Icons.FontAwesome6.HillRockslide}  Physics/{Icons.FontAwesome6.Joint}  Constraints/{Icons.FontAwesome6.Rotate}  Hinge Constraint")]
public sealed class HingeConstraintComponent : TwoBodyConstraintComponent<Hinge>
{

    [SerializeField, HideInInspector] private Vector3 _localOffsetA;
    [SerializeField, HideInInspector] private Vector3 _localHingeAxisA;
    [SerializeField, HideInInspector] private Vector3 _localOffsetB;
    [SerializeField, HideInInspector] private Vector3 _localHingeAxisB;
    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public Vector3 LocalOffsetA
    {
        get
        {
            return _localOffsetA;
        }
        set
        {
            _localOffsetA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalHingeAxisA
    {
        get
        {
            return _localHingeAxisA;
        }
        set
        {
            _localHingeAxisA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalOffsetB
    {
        get
        {
            return _localOffsetB;
        }
        set
        {
            _localOffsetB = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalHingeAxisB
    {
        get
        {
            return _localHingeAxisB;
        }
        set
        {
            _localHingeAxisB = value;
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

    internal override Hinge CreateConstraint()
    {
        return new Hinge
        {
            LocalOffsetA = _localOffsetA,
            LocalHingeAxisA = _localHingeAxisA,
            LocalOffsetB = _localOffsetB,
            LocalHingeAxisB = _localHingeAxisB,
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}
