// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;

[AddComponentMenu($"{Icons.FontAwesome6.HillRockslide}  Physics/{Icons.FontAwesome6.Joint}  Constraints/{Icons.FontAwesome6.Rotate}  Angular Axis Motor Constraint")]
public sealed class AngularAxisMotorConstraintComponent : TwoBodyConstraintComponent<AngularAxisMotor>
{
    [SerializeField, HideInInspector] private Vector3 _localAxisA;
    [SerializeField, HideInInspector] private float _targetVelocity;
    [SerializeField, HideInInspector] private float _motorSoftness = 0;
    [SerializeField, HideInInspector] private float _motorMaximumForce = 1000;

    [ShowInInspector]
    public Vector3 LocalAxisA
    {
        get { return _localAxisA; }
        set { _localAxisA = value; ConstraintData?.TryUpdateDescription(); }
    }

    [ShowInInspector]
    public float TargetVelocity
    {
        get { return _targetVelocity; }
        set { _targetVelocity = value; ConstraintData?.TryUpdateDescription(); }
    }

    [ShowInInspector]
    public float MotorSoftness
    {
        get { return _motorSoftness; }
        set { _motorSoftness = value; ConstraintData?.TryUpdateDescription(); }
    }

    [ShowInInspector]
    public float MotorMaximumForce
    {
        get { return _motorMaximumForce; }
        set { _motorMaximumForce = value; ConstraintData?.TryUpdateDescription(); }
    }

    internal override AngularAxisMotor CreateConstraint()
    {
        return new AngularAxisMotor
        {
            LocalAxisA = _localAxisA,
            TargetVelocity = _targetVelocity,
            Settings = new MotorSettings(_motorMaximumForce, _motorSoftness)
        };
    }
}
