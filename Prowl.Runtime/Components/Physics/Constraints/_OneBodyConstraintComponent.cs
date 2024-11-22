// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;

public abstract class OneBodyConstraintComponent<T> : ConstraintComponent<T> where T : unmanaged, IConstraintDescription<T>, IOneBodyConstraintDescription<T>
{
    [ShowInInspector]
    public Rigidbody? A
    {
        get => this[0];
        set => this[0] = value;
    }

    public OneBodyConstraintComponent() : base(1) { }
}
