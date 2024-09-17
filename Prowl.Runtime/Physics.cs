// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using BepuPhysics;
using BepuPhysics.Collidables;

using BepuUtilities;
using BepuUtilities.Memory;

using Prowl.Runtime.Contacts;
using Prowl.Runtime.Controller;
using Prowl.Runtime.Raycast;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

// Bepu Implementation based on: https://github.com/Nicogo1705/Stride.BepuPhysics

[FilePath("PhysicsSettings.projsetting", FilePathAttribute.Location.Setting)]
public class PhysicsSetting : ScriptableSingleton<PhysicsSetting>
{
    public Vector3 Gravity = new Vector3(0, -9.81f, 0);
    public readonly int Iterations = 8;
    public readonly int Substep = 1;
    public readonly int TargetFrameRate = 50;
    public readonly bool UseMultithreading = true;
    public readonly bool EnhancedDeterminism = false;
    public readonly bool AutoSyncTransforms = true;

}


public static class Physics
{
    public static bool IsReady => isInitialized && Application.IsPlaying;

    public static Simulation? Sim { get; private set; }
    public static BufferPool? Pool { get; private set; }
    public static ThreadDispatcher? Dispatcher { get; private set; }

    public static CharacterControllersManager? Characters { get; private set; }

    private static double timer = 0;


    private static bool isInitialized = false;

    internal static CollidableProperty<PhysicsMaterial> CollidableMaterials { get; private set; }
    internal static ContactEventsManager ContactEvents { get; private set; }

    internal static List<Rigidbody?> Bodies { get; } = new();
    internal static List<Staticbody?> Statics { get; } = new();

    public static PhysicsBody GetContainer(CollidableReference collidable)
    {
        if (collidable.Mobility == CollidableMobility.Static)
        {
            return GetContainer(collidable.StaticHandle);
        }
        return GetContainer(collidable.BodyHandle);
    }

    public static Rigidbody GetContainer(BodyHandle handle)
    {
        return Bodies[handle.Value];
    }

    public static Staticbody GetContainer(StaticHandle handle)
    {
        return Statics[handle.Value];
    }

    public static void Initialize()
    {
        if (isInitialized)
            return;

        //Any IThreadDispatcher implementation can be used for multithreading. Here, we use the BepuUtilities.ThreadDispatcher implementation.
        Dispatcher = new ThreadDispatcher(Environment.ProcessorCount);
        Pool = new BufferPool();
        Characters = new CharacterControllersManager(Pool);
        CollidableMaterials = new CollidableProperty<PhysicsMaterial>();
        ContactEvents = new ContactEventsManager(Dispatcher, Pool);

        var narrow = new BepuNarrowPhaseCallbacks() { CollidableMaterials = CollidableMaterials, ContactEvents = ContactEvents };
        var pose = new BepuPoseIntegratorCallbacks();
        pose.Gravity = PhysicsSetting.Instance.Gravity;
        var desc = new SolveDescription(PhysicsSetting.Instance.Iterations, PhysicsSetting.Instance.Substep);
        Sim = Simulation.Create(Pool, narrow, pose, desc);
        Sim.Deterministic = PhysicsSetting.Instance.EnhancedDeterminism;

        CollidableMaterials.Initialize(Sim);
        ContactEvents.Initialize();

        isInitialized = true;
    }

    public static void Update()
    {
        if (!isInitialized)
            return;

        timer += Time.deltaTime;
        int count = 0;
        while (timer >= Time.fixedDeltaTime && count++ < 10)
        {
            SceneManager.PhysicsUpdate();
            if (PhysicsSetting.Instance.UseMultithreading)
                Sim.Timestep((float)Time.fixedDeltaTime, Dispatcher);
            else
                Sim.Timestep((float)Time.fixedDeltaTime);
            timer -= Time.fixedDeltaTime;
            ContactEvents.Flush(); //Fire event handler stuff.

            foreach (var body in Bodies)
            {
                if (body == null) continue;

                if (PhysicsSetting.Instance.AutoSyncTransforms)
                    body.SyncTransform();

                body.PreviousPose = body.CurrentPose;
                if (body.BodyReference is { } bRef)
                    body.CurrentPose = bRef.Pose;
            }

            foreach (var body in Statics)
            {
                if (body == null) continue;

                if (PhysicsSetting.Instance.AutoSyncTransforms)
                    body.SyncTransform();

                body.PreviousPose = body.CurrentPose;
                if (body.StaticReference is { } sRef)
                    body.CurrentPose = sRef.Pose;
            }
        }

        InterpolateTransforms();
    }

    private static void InterpolateTransforms()
    {
        // Find the interpolation factor, a value [0,1] which represents the ratio of the current time relative to the previous and the next physics step,
        // a value of 0.5 means that we're halfway to the next physics update, just have to wait for the same amount of time.
        var interpolationFactor = (float)(timer / Time.fixedDeltaTime);
        interpolationFactor = MathF.Min(interpolationFactor, 1f);
        foreach (var body in Bodies)
        {
            if (body == null) continue;

            if (body.InterpolationMode == InterpolationMode.Extrapolated)
                interpolationFactor += 1f;

            var interpolatedPosition = Vector3.Lerp(body.PreviousPose.Position, body.CurrentPose.Position, interpolationFactor);
            // We may be able to get away with just a Lerp instead of Slerp, not sure if it needs to be normalized though at which point it may not be that much faster
            var interpolatedRotation = Quaternion.Slerp(body.PreviousPose.Orientation, body.CurrentPose.Orientation, interpolationFactor);

            var prevVersion = body.Transform.version;
            body.Transform.rotation = interpolatedRotation;
            body.Transform.position = interpolatedPosition - Vector3.Transform(body.CenterOfMass, interpolatedRotation);
            // Physics doesnt (for the time being, may change) update the transform version
            body.Transform.version = prevVersion;
        }
    }

    public static void Dispose()
    {
        if (!isInitialized)
            return;

        CollidableMaterials.Dispose();
        ContactEvents.Dispose();
        Bodies.Clear();
        Statics.Clear();

        Characters.Dispose();
        Characters = null;
        Sim.Dispose();
        Sim = null;
        Dispatcher.Dispose();
        Dispatcher = null;
        Pool.Clear();
        Pool = null;

        isInitialized = false;
    }

    #region Public API

    /// <summary>
    /// Finds the closest intersection between this ray and shapes in the simulation.
    /// </summary>
    /// <returns>True when the given ray intersects with a shape, false otherwise</returns>
    public static bool RayCast(in Vector3 origin, in Vector3 dir, float maxDistance, out HitInfo result, LayerMask? layerMask = null)
    {
        var handler = new RayClosestHitHandler(layerMask);
        Sim.RayCast(origin, dir, maxDistance, ref handler);
        if (handler.HitInformation.HasValue)
        {
            result = handler.HitInformation.Value;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Collect intersections between the given ray and shapes in this simulation. Hits are NOT sorted.
    /// </summary>
    public static void RaycastPenetrating(in Vector3 origin, in Vector3 dir, float maxDistance, HitInfo[] buffer, out Span<HitInfo> hits, LayerMask? collisionMask = null)
    {
        var handler = new RayHitsArrayHandler(buffer, collisionMask);
        Sim.RayCast(origin, dir, maxDistance, ref handler);
        hits = new(buffer, 0, handler.Count);
    }

    /// <summary>
    /// Collect intersections between the given ray and shapes in this simulation. Hits are NOT sorted.
    /// </summary>
    public static void RaycastPenetrating(in Vector3 origin, in Vector3 dir, float maxDistance, ICollection<HitInfo> collection, LayerMask? collisionMask = null)
    {
        var handler = new RayHitsCollectionHandler(collection, collisionMask);
        Sim.RayCast(origin, dir, maxDistance, ref handler);
    }

    /// <summary>
    /// Finds the closest contact between <paramref name="shape"/> and other shapes in the simulation when thrown in <paramref name="velocity"/> direction.
    /// </summary>
    /// <returns>True when the given ray intersects with a shape, false otherwise</returns>
    public static bool SweepCast<TShape>(in TShape shape, in RigidPose pose, in BodyVelocity velocity, float maxDistance, out HitInfo result, LayerMask? collisionMask = null) where TShape : unmanaged, IConvexShape //== collider "RayCast"
    {
        var handler = new RayClosestHitHandler(collisionMask);
        Sim.Sweep(shape, pose, velocity, maxDistance, Pool, ref handler);
        if (handler.HitInformation.HasValue)
        {
            result = handler.HitInformation.Value;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Finds contacts between <paramref name="shape"/> and other shapes in the simulation when thrown in <paramref name="velocity"/> direction.
    /// </summary>
    /// <returns>True when the given ray intersects with a shape, false otherwise</returns>
    public static void SweepCastPenetrating<TShape>(in TShape shape, in RigidPose pose, in BodyVelocity velocity, float maxDistance, HitInfo[] buffer, out Span<HitInfo> contacts, LayerMask? collisionMask = null) where TShape : unmanaged, IConvexShape //== collider "RayCast"
    {
        var handler = new RayHitsArrayHandler(buffer, collisionMask);
        Sim.Sweep(shape, pose, velocity, maxDistance, Pool, ref handler);
        contacts = new(buffer, 0, handler.Count);
    }

    /// <summary>
    /// Finds contacts between <paramref name="shape"/> and other shapes in the simulation when thrown in <paramref name="velocity"/> direction.
    /// </summary>
    /// <returns>True when the given ray intersects with a shape, false otherwise</returns>
    public static void SweepCastPenetrating<TShape>(in TShape shape, in RigidPose pose, in BodyVelocity velocity, float maxDistance, ICollection<HitInfo> collection, LayerMask? collisionMask = null) where TShape : unmanaged, IConvexShape //== collider "RayCast"
    {
        var handler = new RayHitsCollectionHandler(collection, collisionMask);
        Sim.Sweep(shape, pose, velocity, maxDistance, Pool, ref handler);
    }

    /// <summary>
    /// Returns true when this shape overlaps with any physics object in this simulation
    /// </summary>
    /// <returns>True when the given shape overlaps with any physics object in the simulation</returns>
    public static bool Overlap<TShape>(in TShape shape, in RigidPose pose, LayerMask? collisionMask = null) where TShape : unmanaged, IConvexShape
    {
        var handler = new OverlapAnyHandler(collisionMask);
        Sim.Sweep(shape, pose, default, 0f, Pool, ref handler);
        return handler.Any;
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with any physics object in the simulation that overlaps with this shape
    /// </summary>
    public static void Overlap<TShape>(in TShape shape, in RigidPose pose, PhysicsBody[] buffer, out Span<PhysicsBody> overlaps, LayerMask? collisionMask = null) where TShape : unmanaged, IConvexShape
    {
        var handler = new OverlapArrayHandler(buffer, collisionMask);
        Sim.Sweep(shape, pose, default, 0f, Pool, ref handler);
        overlaps = new(buffer, 0, handler.Count);
    }

    /// <summary>
    /// Fills <paramref name="collection"/> with any physics object in the simulation that overlaps with this shape
    /// </summary>
    public static void Overlap<TShape>(in TShape shape, in RigidPose pose, ICollection<PhysicsBody> collection, LayerMask? collisionMask = null) where TShape : unmanaged, IConvexShape
    {
        var handler = new OverlapCollectionHandler(collection, collisionMask);
        Sim.Sweep(shape, pose, default, 0f, Pool, ref handler);
    }

    #endregion

}
