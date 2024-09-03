// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;

[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.Rotate}  Angular Axis Gear Motor Constraint")]
public sealed class AngularAxisGearMotorConstraintComponent : TwoBodyConstraintComponent<AngularAxisGearMotor>
{
    [SerializeField, HideInInspector] private Vector3 _localAxisA;
    [SerializeField, HideInInspector] private float _velocityScale;
    [SerializeField, HideInInspector] private float _motorSoftness = 0;
    [SerializeField, HideInInspector] private float _motorMaximumForce = 1000;

    [ShowInInspector]
    public Vector3 LocalAxisA
    {
        get { return _localAxisA; }
        set { _localAxisA = value; ConstraintData?.TryUpdateDescription(); }
    }

    [ShowInInspector]
    public float VelocityScale
    {
        get { return _velocityScale; }
        set { _velocityScale = value; ConstraintData?.TryUpdateDescription(); }
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

    internal override AngularAxisGearMotor CreateConstraint()
    {
        return new AngularAxisGearMotor
        {
            LocalAxisA = _localAxisA,
            VelocityScale = _velocityScale,
            Settings = new MotorSettings(_motorMaximumForce, _motorSoftness)
        };
    }
}
