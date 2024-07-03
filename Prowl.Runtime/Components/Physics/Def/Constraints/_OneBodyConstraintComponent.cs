using BepuPhysics.Constraints;

namespace Prowl.Runtime.Components.NewPhysics;

public abstract class OneBodyConstraintComponent<T> : ConstraintComponent<T> where T : unmanaged, IConstraintDescription<T>, IOneBodyConstraintDescription<T>
{
    [ShowInInspector]
    public Rigidbody? A
    {
        get => this[0];
        set => this[0] = value;
    }

    public OneBodyConstraintComponent() : base(1){ }
}