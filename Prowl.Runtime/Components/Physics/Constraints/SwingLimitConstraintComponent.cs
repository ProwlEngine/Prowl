using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.U}  Swing Limit")]
public sealed class SwingLimitConstraintComponent : TwoBodyConstraintComponent<SwingLimit>
{
    [SerializeField, HideInInspector] private Vector3 _axisLocalA;
    [SerializeField, HideInInspector] private Vector3 _axisLocalB;
    [SerializeField, HideInInspector] private float _minimumDot = 0;
    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    public Vector3 AxisLocalA
    {
        get
        {
            return _axisLocalA;
        }
        set
        {
            _axisLocalA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    public Vector3 AxisLocalB
    {
        get
        {
            return _axisLocalB;
        }
        set
        {
            _axisLocalB = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MinimumDot
    {
        get { return _minimumDot; }
        set
        {
            _minimumDot = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float MaximumSwingAngle
    {
        get { return (float)MathD.Acos(MinimumDot); }
        set
        {
            MinimumDot = (float)MathD.Cos(value);
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

    internal override SwingLimit CreateConstraint()
    {
        return new SwingLimit {
            AxisLocalA = AxisLocalA,
            AxisLocalB = AxisLocalB,
            MinimumDot = MinimumDot,
            SpringSettings = new SpringSettings(SpringFrequency, SpringDampingRatio)
        };
    }
}