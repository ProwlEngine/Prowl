// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;

[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.Rotate}  Angular Hinge Constraint")]
public sealed class AngularHingeConstraintComponent : TwoBodyConstraintComponent<AngularHinge>
{
    [SerializeField, HideInInspector] private Vector3 _localHingeAxisA;
    [SerializeField, HideInInspector] private Vector3 _localHingeAxisB;
    [SerializeField, HideInInspector] private float _springFrequency = 30;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public Vector3 LocalHingeAxisA
    {
        get { return _localHingeAxisA; }
        set { _localHingeAxisA = value; ConstraintData?.TryUpdateDescription(); }
    }

    [ShowInInspector]
    public Vector3 LocalHingeAxisB
    {
        get { return _localHingeAxisB; }
        set { _localHingeAxisB = value; ConstraintData?.TryUpdateDescription(); }
    }

    [ShowInInspector]
    public float SpringFrequency
    {
        get { return _springFrequency; }
        set { _springFrequency = value; ConstraintData?.TryUpdateDescription(); }
    }

    [ShowInInspector]
    public float SpringDampingRatio
    {
        get { return _springDampingRatio; }
        set { _springDampingRatio = value; ConstraintData?.TryUpdateDescription(); }
    }

    internal override AngularHinge CreateConstraint()
    {
        return new AngularHinge
        {
            LocalHingeAxisA = _localHingeAxisA,
            LocalHingeAxisB = _localHingeAxisB,
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}
