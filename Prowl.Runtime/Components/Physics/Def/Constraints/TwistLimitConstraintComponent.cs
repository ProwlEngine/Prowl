using BepuPhysics.Constraints;

namespace Prowl.Runtime.Components.NewPhysics;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.GroupArrowsRotate}  Twist Limit")]
public sealed class TwistLimitConstraintComponent : TwoBodyConstraintComponent<TwistLimit>
{
    [SerializeField, HideInInspector] private Quaternion _localBasisA;
    [SerializeField, HideInInspector] private Quaternion _localBasisB;

    [SerializeField, HideInInspector] private float _minimumAngle = 0;
    [SerializeField, HideInInspector] private float _maximumAngle = 0;
    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public Quaternion LocalBasisA {
        get {
            return _localBasisA;
        }
        set {
            _localBasisA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Quaternion LocalBasisB {
        get {
            return _localBasisB;
        }
        set {
            _localBasisB = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MinimumAngle
    {
        get { return _minimumAngle; }
        set
        {
            _minimumAngle = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MaximumAngle
    {
        get { return _maximumAngle; }
        set
        {
            _maximumAngle = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float SpringFrequency {
        get {
            return _springFrequency;
        }
        set {
            _springFrequency = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float SpringDampingRatio {
        get {
            return _springDampingRatio;
        }
        set {
            _springDampingRatio = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    internal override TwistLimit CreateConstraint()
    {
        return new TwistLimit {
            LocalBasisA = _localBasisA,
            LocalBasisB = _localBasisB,
            MinimumAngle = _minimumAngle,
            MaximumAngle = _maximumAngle,
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}