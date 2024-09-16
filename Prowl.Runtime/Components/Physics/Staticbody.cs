// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics;
using BepuPhysics.Collidables;

using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Cubes}  Staticbody")]
public class Staticbody : PhysicsBody
{
    [SerializeField, HideInInspector] private ContinuousDetection _continuous = ContinuousDetection.Discrete;

    /// <summary> Can be null when it isn't part of a simulation yet/anymore </summary>
    internal StaticReference? StaticReference { get; private set; }

    internal RigidPose PreviousPose, CurrentPose;

    private uint _transformVersion = 1;

    public Vector3 Position
    {
        get => StaticReference?.Pose.Position ?? default;
        set
        {
            if (StaticReference is { } bodyRef)
                bodyRef.Pose.Position = value;
        }
    }

    public Quaternion Orientation
    {
        get => StaticReference?.Pose.Orientation ?? Quaternion.identity;
        set
        {
            if (StaticReference is { } bodyRef)
                bodyRef.Pose.Orientation = value;
        }
    }

    [ShowInInspector]
    public ContinuousDetection ContinuousDetection
    {
        get => StaticReference?.Continuity ?? default;
        set
        {
            if (StaticReference is { } bodyRef)
                bodyRef.Continuity = value;
        }
    }

    public override void OnEnable()
    {
        base.OnEnable();
        _transformVersion = Transform.version;
    }

    protected override ref PhysicsMaterial MaterialProperties => ref Physics.CollidableMaterials[StaticReference!.Value.Handle];
    protected internal override RigidPose? Pose => StaticReference?.Pose;

    protected override void AttachInner(RigidPose containerPose, BodyInertia shapeInertia, TypedIndex shapeIndex)
    {
        Debug.Assert(Physics.IsReady);

        var sDescription = new StaticDescription(containerPose, shapeIndex);

        if (StaticReference is { } sRef)
        {
            sRef.ApplyDescription(sDescription);
        }
        else
        {
            var sHandle = Physics.Sim.Statics.Add(sDescription);
            StaticReference = Physics.Sim.Statics[sHandle];

            while (Physics.Statics.Count <= sHandle.Value) // There may be more than one add if soft physics inserted a couple of bodies
                Physics.Statics.Add(null);
            Physics.Statics[sHandle.Value] = this;

            Physics.CollidableMaterials.Allocate(sHandle) = new();
        }
    }

    protected override void DetachInner()
    {
        Debug.Assert(Physics.IsReady);

        if (StaticReference == null)
            return;

        Physics.Sim.Statics.Remove(StaticReference.Value.Handle);
        Physics.Statics[StaticReference.Value.Handle.Value] = null;
        StaticReference = null;
    }

    public void SyncTransform()
    {
        if (Transform.version != _transformVersion)
        {
            Position = Transform.position;
            Orientation = Transform.rotation;
            _transformVersion = Transform.version;
        }
    }

    protected override void RegisterContactHandler()
    {
        if (Physics.IsReady && ContactEventHandler is not null && StaticReference is { } sRef)
            Physics.ContactEvents.Register(sRef.Handle, ContactEventHandler);
    }

    protected override void UnregisterContactHandler()
    {
        if (Physics.IsReady && StaticReference is { } sRef)
            Physics.ContactEvents.Unregister(sRef.Handle);
    }

    protected override bool IsContactHandlerRegistered()
    {
        if (Physics.IsReady && StaticReference is { } sRef)
            return Physics.ContactEvents.IsListener(sRef.Handle);
        return false;
    }
}
