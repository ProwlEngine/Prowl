using BepuPhysics.Constraints;

namespace Prowl.Runtime.Components.NewPhysics;

public abstract class TwoBodyConstraintComponent<T> : ConstraintComponent<T> where T : unmanaged, IConstraintDescription<T>, ITwoBodyConstraintDescription<T>
{
    [ShowInInspector]
    public Rigidbody? A
    {
        get => this[0];
        set => this[0] = value;
    }

    [ShowInInspector]
    public Rigidbody? B
    {
        get => this[1];
        set => this[1] = value;
    }

    public TwoBodyConstraintComponent() : base(2) { }
}