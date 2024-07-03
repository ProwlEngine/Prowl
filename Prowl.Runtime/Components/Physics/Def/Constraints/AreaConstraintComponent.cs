using BepuPhysics.Constraints;

namespace Prowl.Runtime.Components.NewPhysics;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.Square}  Area Constraint")]
public sealed class AreaConstraintComponent : ThreeBodyConstraintComponent<AreaConstraint>
{
    [SerializeField, HideInInspector] private float _targetScaledArea;

    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public float TargetScaledArea
    {
        get
        {
            return _targetScaledArea;
        }
        set
        {
            _targetScaledArea = value;
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

    internal override AreaConstraint CreateConstraint()
    {
        return new() {
            TargetScaledArea = _targetScaledArea,
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}