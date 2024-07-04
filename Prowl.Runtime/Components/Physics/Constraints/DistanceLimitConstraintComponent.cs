using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.Ruler}  Distance Limit")]
public sealed class DistanceLimitConstraintComponent : TwoBodyConstraintComponent<DistanceLimit>
{
    [SerializeField, HideInInspector] private Vector3 _localOffsetA;
    [SerializeField, HideInInspector] private Vector3 _localOffsetB;
    [SerializeField, HideInInspector] private float _minimumDistance;
    [SerializeField, HideInInspector] private float _maximumDistance;

    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

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
    public float MinimumAngle {
        get { return _minimumDistance; }
        set {
            _minimumDistance = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MaximumAngle {
        get { return _maximumDistance; }
        set {
            _maximumDistance = value;
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

    internal override DistanceLimit CreateConstraint()
    {
        return new DistanceLimit {
            LocalOffsetA = _localOffsetA,
            LocalOffsetB = _localOffsetB,
            MinimumDistance = _minimumDistance,
            MaximumDistance = _maximumDistance,
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}