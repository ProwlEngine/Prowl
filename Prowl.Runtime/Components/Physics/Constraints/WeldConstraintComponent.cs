// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;

[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.LocationPin}  Weld Constraint")]
public sealed class WeldConstraintComponent : TwoBodyConstraintComponent<Weld>
{
    [SerializeField, HideInInspector] private Vector3 _localOffset;
    [SerializeField, HideInInspector] private Quaternion _localOrientation = Quaternion.identity;
    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;


    [ShowInInspector]
    public Vector3 LocalOffset
    {
        get
        {
            return _localOffset;
        }
        set
        {
            _localOffset = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public Quaternion LocalOrientation
    {
        get
        {
            return _localOrientation;
        }
        set
        {
            _localOrientation = value;
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

    internal override Weld CreateConstraint()
    {
        return new Weld
        {
            LocalOffset = _localOffset,
            LocalOrientation = _localOrientation,
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}
