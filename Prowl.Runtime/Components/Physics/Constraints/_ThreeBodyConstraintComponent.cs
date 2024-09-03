using BepuPhysics.Constraints;

namespace Prowl.Runtime;

public abstract class ThreeBodyConstraintComponent<T> : ConstraintComponent<T> where T : unmanaged, IConstraintDescription<T>, IThreeBodyConstraintDescription<T>
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

    [ShowInInspector]
    public Rigidbody? C
    {
        get => this[2];
        set => this[2] = value;
    }

    public ThreeBodyConstraintComponent() : base(3) { }
}
