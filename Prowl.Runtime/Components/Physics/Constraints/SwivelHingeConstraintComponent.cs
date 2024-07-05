using BepuPhysics.Constraints;

namespace Prowl.Runtime;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.U}  Swing Hinge")]
public sealed class SwivelHingeConstraintComponent : TwoBodyConstraintComponent<SwivelHinge>
{
    [SerializeField, HideInInspector] private Vector3 _localOffsetA;
    [SerializeField, HideInInspector] private Vector3 _localSwivelAxisA;
    [SerializeField, HideInInspector] private Vector3 _localOffsetB;
    [SerializeField, HideInInspector] private Vector3 _localHingeAxisB;

    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public Vector3 LocalOffsetA
    {
        get
        {
            return _localOffsetA;
        }
        set
        {
            _localOffsetA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalSwivelAxisA
    {
        get
        {
            return _localSwivelAxisA;
        }
        set
        {
            _localSwivelAxisA = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalOffsetB
    {
        get
        {
            return _localOffsetB;
        }
        set
        {
            _localOffsetB = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Vector3 LocalHingeAxisB
    {
        get
        {
            return _localHingeAxisB;
        }
        set
        {
            _localHingeAxisB = value;
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

    internal override SwivelHinge CreateConstraint()
    {
        return new SwivelHinge {
            LocalOffsetA = LocalOffsetA,
            LocalSwivelAxisA = LocalSwivelAxisA,
            LocalOffsetB = LocalOffsetB,
            LocalHingeAxisB = LocalHingeAxisB,
            SpringSettings = new SpringSettings(SpringFrequency, SpringDampingRatio)
        };
    }
}