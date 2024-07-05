using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.LinesLeaning}  Linear Axis Motor")]
public sealed class LinearAxisMotorConstraintComponent : TwoBodyConstraintComponent<LinearAxisMotor>
{
    [SerializeField, HideInInspector] private Vector3 _localOffsetA;
    [SerializeField, HideInInspector] private Vector3 _localOffsetB;
    [SerializeField, HideInInspector] private Vector3 _localAxis;
    [SerializeField, HideInInspector] private float _targetVelocity;
    [SerializeField, HideInInspector] private float _motorSoftness = 0;
    [SerializeField, HideInInspector] private float _motorMaximumForce = 1000;

    [ShowInInspector]
    public Vector3 LocalOffsetA {
        get => _localOffsetA;
        set {
            _localOffsetA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalOffsetB {
        get => _localOffsetB;
        set {
            _localOffsetB = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalAxis {
        get => _localAxis;
        set {
            _localAxis = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float TargetVelocity
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

    internal override LinearAxisMotor CreateConstraint()
    {
        return new LinearAxisMotor {
            LocalOffsetA = _localOffsetA,
            LocalOffsetB = _localOffsetB,
            LocalAxis = _localAxis,
            TargetVelocity = _targetVelocity,
            Settings = new MotorSettings(_motorMaximumForce, _motorSoftness)
        };
    }
}