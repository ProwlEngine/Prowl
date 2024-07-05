using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.Circle}  Ball Socket Motor")]
public sealed class BallSocketMotorConstraintComponent : TwoBodyConstraintComponent<BallSocketMotor>
{
    [SerializeField, HideInInspector] private Vector3 _localOffsetB;
    [SerializeField, HideInInspector] private Vector3 _targetVelocityLocalA;

    [SerializeField, HideInInspector] private float _motorSoftness = 0;
    [SerializeField, HideInInspector] private float _motorMaximumForce = 1000;

    [ShowInInspector]
    public Vector3 LocalOffsetB {
        get => _localOffsetB;
        set {
            _localOffsetB = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 TargetVelocityLocalA
    {
        get
        {
            return _targetVelocityLocalA;
        }
        set
        {
            _targetVelocityLocalA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MotorSoftness {
        get {
            return _motorSoftness;
        }
        set {
            _motorSoftness = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MotorMaximumForce {
        get {
            return _motorMaximumForce;
        }
        set {
            _motorMaximumForce = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    internal override BallSocketMotor CreateConstraint()
    {
        return new BallSocketMotor {
            LocalOffsetB = _localOffsetB,
            TargetVelocityLocalA = _targetVelocityLocalA,
            Settings = new MotorSettings(_motorMaximumForce, _motorSoftness)
        };
    }
}