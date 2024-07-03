using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using Prowl.Runtime.Components.NewPhysics;
using Prowl.Runtime.Components.NewPhysics.Contacts;
using Prowl.Runtime.Components.NewPhysics.Raycast;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime
{

    // Bepu Implementation based on: https://github.com/Nicogo1705/Stride.BepuPhysics

    struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        /// <summary>
        /// Performs any required initialization logic after the Simulation instance has been constructed.
        /// </summary>
        /// <param name="simulation">Simulation that owns these callbacks.</param>
        public void Initialize(Simulation simulation)
        {
            //Often, the callbacks type is created before the simulation instance is fully constructed, so the simulation will call this function when it's ready.
            //Any logic which depends on the simulation existing can be put here.
        }

        /// <summary>
        /// Chooses whether to allow contact generation to proceed for two overlapping collidables.
        /// </summary>
        /// <returns>True if collision detection should proceed, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        {
            //Before creating a narrow phase pair, the broad phase asks this callback whether to bother with a given pair of objects.
            //This can be used to implement arbitrary forms of collision filtering. See the RagdollDemo or NewtDemo for examples.
            //Here, we'll make sure at least one of the two bodies is dynamic.
            //The engine won't generate static-static pairs, but it will generate kinematic-kinematic pairs.
            //That's useful if you're trying to make some sort of sensor/trigger object, but since kinematic-kinematic pairs
            //can't generate constraints (both bodies have infinite inertia), simple simulations can just ignore such pairs.

            //This function also exposes the speculative margin. It can be validly written to, but that is a very rare use case.
            //Most of the time, you can ignore this function's speculativeMargin parameter entirely.
            return a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
        {
            //This is similar to the top level broad phase callback above. It's called by the narrow phase before generating subpairs between children in parent shapes. 
            //This only gets called in pairs that involve at least one shape type that can contain multiple children, like a Compound.
            return true;
        }

        /// <summary>
        /// Provides a notification that a manifold has been created for a pair. Offers an opportunity to change the manifold's details. 
        /// </summary>
        /// <returns>True if a constraint should be created for the manifold, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            //The IContactManifold parameter includes functions for accessing contact data regardless of what the underlying type of the manifold is.
            //If you want to have direct access to the underlying type, you can use the manifold.Convex property and a cast like Unsafe.As<TManifold, ConvexContactManifold or NonconvexContactManifold>(ref manifold).

            //The engine does not define any per-body material properties. Instead, all material lookup and blending operations are handled by the callbacks.
            //For the purposes of this demo, we'll use the same settings for all pairs.
            //(Note that there's no 'bounciness' or 'coefficient of restitution' property!
            //Bounciness is handled through the contact spring settings instead. Setting See here for more details: https://github.com/bepu/bepuphysics2/issues/3 and check out the BouncinessDemo for some options.)
            pairMaterial.FrictionCoefficient = 1f;
            pairMaterial.MaximumRecoveryVelocity = 2f;
            pairMaterial.SpringSettings = new SpringSettings(30, 1);
            return true;
        }

        /// <summary>
        /// Provides a notification that a manifold has been created between the children of two collidables in a compound-including pair.
        /// Offers an opportunity to change the manifold's details. 
        /// </summary>
        /// <returns>True if this manifold should be considered for constraint generation, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
        {
            return true;
        }

        /// <summary>
        /// Releases any resources held by the callbacks. Called by the owning narrow phase when it is being disposed.
        /// </summary>
        public void Dispose()
        {
        }
    }

    public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        /// <summary>
        /// Performs any required initialization logic after the Simulation instance has been constructed.
        /// </summary>
        /// <param name="simulation">Simulation that owns these callbacks.</param>
        public void Initialize(Simulation simulation)
        {
            //In this demo, we don't need to initialize anything.
            //If you had a simulation with per body gravity stored in a CollidableProperty<T> or something similar, having the simulation provided in a callback can be helpful.
        }

        /// <summary>
        /// Gets how the pose integrator should handle angular velocity integration.
        /// </summary>
        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.ConserveMomentum;

        /// <summary>
        /// Gets whether the integrator should use substepping for unconstrained bodies when using a substepping solver.
        /// If true, unconstrained bodies will be integrated with the same number of substeps as the constrained bodies in the solver.
        /// If false, unconstrained bodies use a single step of length equal to the dt provided to Simulation.Timestep. 
        /// </summary>
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;

        /// <summary>
        /// Gets whether the velocity integration callback should be called for kinematic bodies.
        /// If true, IntegrateVelocity will be called for bundles including kinematic bodies.
        /// If false, kinematic bodies will just continue using whatever velocity they have set.
        /// Most use cases should set this to false.
        /// </summary>
        public readonly bool IntegrateVelocityForKinematics => false;

        public Vector3 Gravity;
        public float LinearDamping;
        public float AngularDamping;

        public PoseIntegratorCallbacks(Vector3 gravity, float linearDamping = .03f, float angularDamping = .03f) : this()
        {
            Gravity = gravity;
            LinearDamping = linearDamping;
            AngularDamping = angularDamping;
        }

        Vector3Wide gravityWideDt;
        Vector<float> linearDampingDt;
        Vector<float> angularDampingDt;

        /// <summary>
        /// Callback invoked ahead of dispatches that may call into <see cref="IntegrateVelocity"/>.
        /// It may be called more than once with different values over a frame. For example, when performing bounding box prediction, velocity is integrated with a full frame time step duration.
        /// During substepped solves, integration is split into substepCount steps, each with fullFrameDuration / substepCount duration.
        /// The final integration pass for unconstrained bodies may be either fullFrameDuration or fullFrameDuration / substepCount, depending on the value of AllowSubstepsForUnconstrainedBodies. 
        /// </summary>
        /// <param name="dt">Current integration time step duration.</param>
        /// <remarks>This is typically used for precomputing anything expensive that will be used across velocity integration.</remarks>
        public void PrepareForIntegration(float dt)
        {
            //No reason to recalculate gravity * dt for every body; just cache it ahead of time.
            //Since these callbacks don't use per-body damping values, we can precalculate everything.
            linearDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - LinearDamping, 0, 1), dt));
            angularDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - AngularDamping, 0, 1), dt));
            gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
        }

        /// <summary>
        /// Callback for a bundle of bodies being integrated.
        /// </summary>
        /// <param name="bodyIndices">Indices of the bodies being integrated in this bundle.</param>
        /// <param name="position">Current body positions.</param>
        /// <param name="orientation">Current body orientations.</param>
        /// <param name="localInertia">Body's current local inertia.</param>
        /// <param name="integrationMask">Mask indicating which lanes are active in the bundle. Active lanes will contain 0xFFFFFFFF, inactive lanes will contain 0.</param>
        /// <param name="workerIndex">Index of the worker thread processing this bundle.</param>
        /// <param name="dt">Durations to integrate the velocity over. Can vary over lanes.</param>
        /// <param name="velocity">Velocity of bodies in the bundle. Any changes to lanes which are not active by the integrationMask will be discarded.</param>
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            //This is a handy spot to implement things like position dependent gravity or per-body damping.
            //This implementation uses a single damping value for all bodies that allows it to be precomputed.
            //We don't have to check for kinematics; IntegrateVelocityForKinematics returns false, so we'll never see them in this callback.
            //Note that these are SIMD operations and "Wide" types. There are Vector<float>.Count lanes of execution being evaluated simultaneously.
            //The types are laid out in array-of-structures-of-arrays (AOSOA) format. That's because this function is frequently called from vectorized contexts within the solver.
            //Transforming to "array of structures" (AOS) format for the callback and then back to AOSOA would involve a lot of overhead, so instead the callback works on the AOSOA representation directly.
            velocity.Linear = (velocity.Linear + gravityWideDt) * linearDampingDt;
            velocity.Angular = velocity.Angular * angularDampingDt;
        }

    }

    [FilePath("PhysicsSettings.projsetting", FilePathAttribute.Location.Setting)]
    public class PhysicsSetting : ScriptableSingleton<PhysicsSetting>
    {
        public Vector3 Gravity = new Vector3(0, -9.81f, 0);
        public int Iterations = 8;
        public int Substep = 1;
        public int TargetFrameRate = 50;
        public bool UseMultithreading = true;
        public bool EnhancedDeterminism = false;
        public bool AutoSyncTransforms = true;

    }


    public static class Physics
    {
        public static bool IsReady => isInitialized && Application.isPlaying;

        public static Simulation? Sim { get; private set; }
        public static BufferPool? Pool { get; private set; }
        public static ThreadDispatcher? Dispatcher { get; private set; }


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
            else
            {
                return GetContainer(collidable.BodyHandle);
            }
            return null;
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

            Pool = new BufferPool();

            var narrow = new NarrowPhaseCallbacks();
            var pose = new PoseIntegratorCallbacks(PhysicsSetting.Instance.Gravity);
            var desc = new SolveDescription(PhysicsSetting.Instance.Iterations, PhysicsSetting.Instance.Substep);
            Sim = Simulation.Create(Pool, narrow, pose, desc);
            Sim.Deterministic = PhysicsSetting.Instance.EnhancedDeterminism;

            //Any IThreadDispatcher implementation can be used for multithreading. Here, we use the BepuUtilities.ThreadDispatcher implementation.
            Dispatcher = new ThreadDispatcher(Environment.ProcessorCount);


            CollidableMaterials = new CollidableProperty<PhysicsMaterial>();
            CollidableMaterials.Initialize(Sim);

            ContactEvents = new ContactEventsManager(Dispatcher, Pool);
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

}
