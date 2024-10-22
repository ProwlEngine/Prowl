// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using BepuPhysics;
using BepuPhysics.Collidables;

using BepuUtilities.Memory;

using Prowl.Runtime.Contacts;

namespace Prowl.Runtime;

public abstract class PhysicsBody : MonoBehaviour
{
    [SerializeField, HideInInspector] private float _springFrequency = 30;
    [SerializeField, HideInInspector] private float _springDampingRatio = 3;
    [SerializeField, HideInInspector] private float _frictionCoefficient = 1f;
    [SerializeField, HideInInspector] private float _maximumRecoveryVelocity = 1000;

    public enum BodyType { Small, Big }
    public BodyType Type;
    private IContactEventHandler? _trigger;

    protected TypedIndex ShapeIndex { get; private set; }

    public IContactEventHandler? ContactEventHandler
    {
        get
        {
            return _trigger;
        }
        set
        {
            if (IsContactHandlerRegistered())
                UnregisterContactHandler();

            _trigger = value;
            RegisterContactHandler();
            TryUpdateMaterialProperties();
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
            TryUpdateMaterialProperties();
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
            TryUpdateMaterialProperties();
        }
    }

    [ShowInInspector]
    public float FrictionCoefficient
    {
        get => _frictionCoefficient;
        set
        {
            _frictionCoefficient = value;
            TryUpdateMaterialProperties();
        }
    }

    public float MaximumRecoveryVelocity
    {
        get => _maximumRecoveryVelocity;
        set
        {
            _maximumRecoveryVelocity = value;
            TryUpdateMaterialProperties();
        }
    }

    public Vector3 CenterOfMass { get; private set; }

    public override void OnEnable() => ReAttach();
    public override void OnDisable() => Detach();

#warning TODO: Work on making ReAttach more Seamless, Ideally you should be able to change the scale of a collider, the mass or something else and have it be completely seamlessly updated

    internal void ReAttach()
    {
        Debug.Assert(Physics.IsReady, "Physics is not ready, cannot reattach");

        Detach();

        if (!TryGetShape(out var index, out var centerOfMass, out var shapeInertia))
            return;

        ShapeIndex = index;
        CenterOfMass = centerOfMass;

        AttachInner(new(Transform.position + Transform.rotation * CenterOfMass, Transform.rotation), shapeInertia, ShapeIndex);

        if (ContactEventHandler != null && !IsContactHandlerRegistered())
            RegisterContactHandler();

        TryUpdateMaterialProperties();
    }

    internal void Detach()
    {
        if (!Physics.IsReady) return; // Dispose can call this so dont throw an error

        CenterOfMass = new();

        if (IsContactHandlerRegistered())
            UnregisterContactHandler();

        if (ShapeIndex.Exists)
        {
            Physics.Sim.Shapes.RemoveAndDispose(ShapeIndex, Physics.Sim.BufferPool);
            ShapeIndex = default;
        }

        DetachInner();
    }

    bool TryGetShape(out TypedIndex index, out Vector3 centerOfMass, out BodyInertia inertia)
    {
        List<Collider> colliders = GetComponentsInChildren<Collider>().ToList();

        if (colliders.Count == 0)
        {
            index = default;
            centerOfMass = default;
            inertia = default;
            return false;
        }

        var compoundBuilder = new CompoundBuilder(Physics.Sim.BufferPool, Physics.Sim.Shapes, colliders.Count);
        try
        {
            foreach (var collider in colliders)
            {
                if (collider.Container != null && collider.Container != this)
                {
                    Debug.LogError("Collider is already attached to another container! Do you have a rigidbody as a child of another rigidbody?");
                    throw new InvalidOperationException("Collider is already attached to another container.");
                }

                Vector3 localTranslation = Vector3.zero;
                Quaternion localRotation = Quaternion.identity;
                if (collider.Transform != Transform)
                {
                    localTranslation = collider.Transform.localPosition;
                    localRotation = collider.Transform.localRotation;
                }

                var compoundChildLocalPose = new RigidPose(localTranslation, localRotation);
                collider.AddToCompoundBuilder(Physics.Sim.BufferPool, ref compoundBuilder, compoundChildLocalPose);
                collider.Container = this;
            }

            compoundBuilder.BuildDynamicCompound(out Buffer<CompoundChild> compoundChildren, out inertia, out System.Numerics.Vector3 shapeCenter);
            centerOfMass = shapeCenter;
            if (Type == BodyType.Small)
                index = Physics.Sim.Shapes.Add(new Compound(compoundChildren));
            else
                index = Physics.Sim.Shapes.Add(new BigCompound(compoundChildren, Physics.Sim.Shapes, Physics.Sim.BufferPool));
        }
        finally
        {
            compoundBuilder.Dispose();
        }

        return true;
    }

    protected void TryUpdateMaterialProperties()
    {
        if (!Physics.IsReady)
            return;

        ref var mat = ref MaterialProperties;

        mat.SpringSettings = new(SpringFrequency, SpringDampingRatio);
        mat.FrictionCoefficient = FrictionCoefficient;
        mat.MaximumRecoveryVelocity = MaximumRecoveryVelocity;
        mat.IsTrigger = ContactEventHandler != null && ContactEventHandler.NoContactResponse;
    }

    #region Abstract

    protected abstract ref PhysicsMaterial MaterialProperties { get; }
    protected internal abstract RigidPose? Pose { get; }

    /// <summary>
    /// Called every time the container is added to a simulation
    /// </summary>
    /// <remarks>
    /// May occur when certain larger changes are made to the object, <see cref="Simulation"/> is the one this object is being added to
    /// </remarks>
    protected abstract void AttachInner(RigidPose containerPose, BodyInertia shapeInertia, TypedIndex shapeIndex);
    /// <summary>
    /// Called every time the container is removed from the simulation
    /// </summary>
    /// <remarks>
    /// May occur right before <see cref="AttachInner"/> when certain larger changes are made to the object, <see cref="Simulation"/> is the one this object was on prior to detaching
    /// </remarks>
    protected abstract void DetachInner();
    protected abstract void RegisterContactHandler();
    protected abstract void UnregisterContactHandler();
    protected abstract bool IsContactHandlerRegistered();

    #endregion
}
