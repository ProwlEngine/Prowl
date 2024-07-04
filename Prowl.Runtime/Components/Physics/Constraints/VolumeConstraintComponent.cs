using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.Square}  Volume Constraint")]
public sealed class VolumeConstraintComponent : FourBodyConstraintComponent<VolumeConstraint>
{
    [SerializeField, HideInInspector] private float _targetScaledVolume = 35;
    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    public float TargetScaledVolume
    {
        get { return _targetScaledVolume; }
        set
        {
            _targetScaledVolume = value;
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

    internal override VolumeConstraint CreateConstraint()
    {
        return new VolumeConstraint() {
            TargetScaledVolume = _targetScaledVolume,
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}