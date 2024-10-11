// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Icons.FontAwesome6.HillRockslide}  Physics/{Icons.FontAwesome6.Joint}  Constraints/{Icons.FontAwesome6.GroupArrowsRotate}  Twist Servo")]
public sealed class TwistServoConstraintComponent : TwoBodyConstraintComponent<TwistServo>
{
    [SerializeField, HideInInspector] private Quaternion _localBasisA;
    [SerializeField, HideInInspector] private Quaternion _localBasisB;

    [SerializeField, HideInInspector] private float _targetAngle;
    [SerializeField, HideInInspector] private float _servoMaximumSpeed = 10;
    [SerializeField, HideInInspector] private float _servoBaseSpeed = 1;
    [SerializeField, HideInInspector] private float _servoMaximumForce = 1000;

    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public Quaternion LocalBasisA
    {
        get
        {
            return _localBasisA;
        }
        set
        {
            _localBasisA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Quaternion LocalBasisB
    {
        get
        {
            return _localBasisB;
        }
        set
        {
            _localBasisB = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float TargetAngle
    {
        get { return _targetAngle; }
        set
        {
            _targetAngle = value;
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

    internal override TwistServo CreateConstraint()
    {
        return new TwistServo
        {
            LocalBasisA = _localBasisA,
            LocalBasisB = _localBasisB,
            TargetAngle = _targetAngle,
            ServoSettings = new ServoSettings
            {
                MaximumSpeed = _servoMaximumSpeed,
                BaseSpeed = _servoBaseSpeed,
                MaximumForce = _servoMaximumForce
            },
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}
