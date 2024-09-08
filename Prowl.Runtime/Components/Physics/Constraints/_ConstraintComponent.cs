// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Constraints;

namespace Prowl.Runtime;

public abstract class ConstraintComponent<T> : ConstraintComponentBase where T : unmanaged, IConstraintDescription<T>
{
    /// <summary>
    /// ContainerData is the bridge to Bepu.
    /// Set through the processor when it calls <see cref="CreateProcessorData"/>.
    /// </summary>
    internal ConstraintData<T>? ConstraintData { get; set; }

    internal override void RemoveDataRef()
    {
        ConstraintData = null;
    }

    internal override ConstraintDataBase? UntypedConstraintData => ConstraintData;

    internal abstract T CreateConstraint();

    internal override ConstraintDataBase CreateProcessorData() => ConstraintData = new(this);
    protected ConstraintComponent(int bodies) : base(bodies) { }
}
