using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.LinesLeaning}  One Body Linear Servo")]
public sealed class OneBodyLinearServoConstraintComponent : OneBodyConstraintComponent<OneBodyLinearServo>
{
    [SerializeField, HideInInspector] private Vector3 _localOffset;
    [SerializeField, HideInInspector] private Vector3 _target;

    [SerializeField, HideInInspector] private float _servoMaximumSpeed = 100;
    [SerializeField, HideInInspector] private float _servoBaseSpeed = 1;
    [SerializeField, HideInInspector] private float _servoMaximumForce = 1000;

    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public Vector3 LocalOffset
    {
        get => _localOffset;
        set
        {
            _localOffset = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 Target
    {
        get
        {
            return _target;
        }
        set
        {
            _target = value;
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

    internal override OneBodyLinearServo CreateConstraint()
    {
        return new OneBodyLinearServo
        {
            LocalOffset = LocalOffset,
            Target = Target,
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
