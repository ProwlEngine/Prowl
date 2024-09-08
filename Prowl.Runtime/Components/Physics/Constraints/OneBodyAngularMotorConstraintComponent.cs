// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.ArrowsSpin}  One Body Angular Motor")]
public sealed class OneBodyAngularMotorConstraintComponent : OneBodyConstraintComponent<OneBodyAngularMotor>
{
    [SerializeField, HideInInspector] private Vector3 _targetVelocity;
    [SerializeField, HideInInspector] private float _motorSoftness = 0.02f;
    [SerializeField, HideInInspector] private float _motorMaximumForce = 10000000;

    public Vector3 TargetVelocity
    {
        get
        {
            return _targetVelocity;
        }
        set
        {
            _targetVelocity = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MotorSoftness
    {
        get
        {
            return _motorSoftness;
        }
        set
        {
            _motorSoftness = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MotorMaximumForce
    {
        get
        {
            return _motorMaximumForce;
        }
        set
        {
            _motorMaximumForce = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    internal override OneBodyAngularMotor CreateConstraint()
    {
        return new()
        {
            TargetVelocity = _targetVelocity,
            Settings = new MotorSettings(_motorMaximumForce, _motorSoftness)
        };
    }
}
