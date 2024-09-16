// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using BepuPhysics;
using BepuPhysics.Constraints;

namespace Prowl.Runtime;

public abstract class ConstraintComponentBase : MonoBehaviour
{
    [SerializeField, HideInInspector] private readonly Rigidbody?[] _bodies;

    public ReadOnlySpan<Rigidbody?> Bodies => _bodies;

    protected ConstraintComponentBase(int bodies) => _bodies = new Rigidbody?[bodies];

    protected Rigidbody? this[int i]
    {
        get => _bodies[i];
        set
        {
            _bodies[i] = value;
            UntypedConstraintData?.RebuildConstraint();
        }
    }

    public override void OnEnable()
    {
        if (UntypedConstraintData == null)
            CreateProcessorData();
        //UntypedConstraintData.RebuildConstraint();
    }

    public override void OnDisable()
    {
        UntypedConstraintData?.DestroyConstraint();
        RemoveDataRef();
    }

    public override void LateUpdate()
    {
        //timer += Time.deltaTimeF;
        //if(timer > 0.1)
        {
            if (UntypedConstraintData?.Exist == false)
                UntypedConstraintData.RebuildConstraint();
        }
    }

    internal abstract void RemoveDataRef();

    internal abstract ConstraintDataBase? UntypedConstraintData { get; }

    internal abstract ConstraintDataBase CreateProcessorData();
}

internal abstract class ConstraintDataBase
{
    public abstract bool Exist { get; }

    internal abstract void RebuildConstraint();
    internal abstract void DestroyConstraint();
    internal abstract void TryUpdateDescription();
}

internal sealed class ConstraintData<T> : ConstraintDataBase where T : unmanaged, IConstraintDescription<T>
{
    private readonly ConstraintComponent<T> _constraintComponent;
    private ConstraintHandle _cHandle = new(-1);
    private bool _exist = false;

    public override bool Exist => _exist;

    public ConstraintData(ConstraintComponent<T> constraintComponent)
    {
        _constraintComponent = constraintComponent;
    }

    internal override void RebuildConstraint()
    {
        DestroyConstraint();

        if (!_constraintComponent.Enabled && Physics.IsReady)
            return;

        foreach (var container in _constraintComponent.Bodies)
        {
            if (container is null || container.BodyReference.HasValue == false)
                return; // need to wait for a body to be attached or instanced
        }

        Span<BodyHandle> bodies = stackalloc BodyHandle[_constraintComponent.Bodies.Length];
        int count = 0;

        foreach (var component in _constraintComponent.Bodies)
            bodies[count++] = component.BodyReference!.Value.Handle;

        Span<BodyHandle> validBodies = bodies[..count];

        _cHandle = Physics.Sim.Solver.Add(validBodies, _constraintComponent.CreateConstraint());

        _exist = true;
    }

    internal override void DestroyConstraint()
    {
        if (_cHandle.Value != -1 && Physics.IsReady && Physics.Sim.Solver.ConstraintExists(_cHandle))
        {
            Physics.Sim.Solver.Remove(_cHandle);
            _cHandle = new(-1);
        }

        _exist = false;
    }

    internal override void TryUpdateDescription()
    {
        if (Physics.IsReady && _cHandle.Value != -1 && Physics.Sim.Solver.ConstraintExists(_cHandle))
        {
            Physics.Sim.Solver.ApplyDescription(_cHandle, _constraintComponent.CreateConstraint());
        }
    }
}
