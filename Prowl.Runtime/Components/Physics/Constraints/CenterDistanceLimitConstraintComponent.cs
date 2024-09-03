using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.ArrowsToDot}  Center Distance Limit")]
public sealed class CenterDistanceLimitConstraintComponent : TwoBodyConstraintComponent<CenterDistanceLimit>
{
    [SerializeField, HideInInspector] private float _minimumDistance = 0;
    [SerializeField, HideInInspector] private float _maximumDistance = 0;
    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public float MinimumDistance
    {
        get { return _minimumDistance; }
        set
        {
            _minimumDistance = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MaximumDistance
    {
        get { return _maximumDistance; }
        set
        {
            _maximumDistance = value;
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

    internal override CenterDistanceLimit CreateConstraint()
    {
        return new CenterDistanceLimit
        {
            MinimumDistance = _minimumDistance,
            MaximumDistance = _maximumDistance,
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}
