// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Icons.FontAwesome6.HillRockslide}  Physics/{Icons.FontAwesome6.Joint}  Constraints/{Icons.FontAwesome6.Ruler}  Distance Servo")]
public sealed class DistanceServoConstraintComponent : TwoBodyConstraintComponent<DistanceServo>
{
    [SerializeField, HideInInspector] private Vector3 _localOffsetA;
    [SerializeField, HideInInspector] private Vector3 _localOffsetB;

    [SerializeField, HideInInspector] private float _targetDistance;
    [SerializeField, HideInInspector] private float _servoMaximumSpeed = 10;
    [SerializeField, HideInInspector] private float _servoBaseSpeed = 1;
    [SerializeField, HideInInspector] private float _servoMaximumForce = 1000;

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
    public float TargetDistance
    {
        get
        {
            return _targetDistance;
        }
        set
        {
            _targetDistance = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float ServoMaximumSpeed
    {
        get
        {
            return _servoMaximumSpeed;
        }
        set
        {
            _servoMaximumSpeed = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float ServoBaseSpeed
    {
        get
        {
            return _servoBaseSpeed;
        }
        set
        {
            _servoBaseSpeed = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float ServoMaximumForce
    {
        get
        {
            return _servoMaximumForce;
        }
        set
        {
            _servoMaximumForce = value;
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

    internal override DistanceServo CreateConstraint()
    {
        return new DistanceServo
        {
            LocalOffsetA = LocalOffsetA,
            LocalOffsetB = LocalOffsetB,
            TargetDistance = TargetDistance,
            ServoSettings = new ServoSettings
            {
                MaximumSpeed = ServoMaximumSpeed,
                BaseSpeed = ServoBaseSpeed,
                MaximumForce = ServoMaximumForce
            },
            SpringSettings = new SpringSettings
            {
                Frequency = SpringFrequency,
                DampingRatio = SpringDampingRatio
            }
        };
    }
}
