// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Persistent keeps them alive between steps for lower latency at idle CPU cost.
/// </summary>
public enum PhysicsThreadModel
{
    Regular,
    Persistent
}

public class PhysicsWorld
{
    /// <summary>
    /// Stops two rigidbodies colliding with each other, on top of whatever the layer matrix says. The
    /// pair is scoped to this world and is dropped when the world is cleared.
    /// </summary>
    public void IgnoreCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB) => _layerFilter.IgnoreCollisionBetween(bodyA, bodyB);

    /// <summary>Undoes <see cref="IgnoreCollisionBetween"/> for a pair.</summary>
    public void EnableCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB) => _layerFilter.EnableCollisionBetween(bodyA, bodyB);

    /// <summary>Forgets every pair passed to <see cref="IgnoreCollisionBetween"/>.</summary>
    public void ClearIgnoredCollisions() => _layerFilter.ClearIgnoredCollisions();

    /// <summary>
    /// Bake (or fetch the cached) physics representation of a mesh. The bake is stored directly on
    /// the <see cref="Mesh"/> instance (<see cref="Mesh.BakedPhysics"/>), so its lifetime is tied to
    /// the mesh rather than a separate global cache. The result is shared by every collider that uses
    /// the mesh, so the bake happens once instead of per MeshCollider instance, and is rebuilt
    /// automatically when the mesh's <see cref="Mesh.Version"/> changes.
    /// <para/>
    /// Thread-safe. The bake is pure CPU work, so this may be called from worker threads to pre-bake
    /// meshes off the main thread (independent meshes bake in parallel; each mesh locks on itself).
    /// </summary>
    public static BakedPhysicsMesh BakeMesh(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        lock (mesh)
        {
            var cached = mesh.BakedPhysics;
            if (cached != null && cached.Version == mesh.Version)
                return cached;
        }

        // Bake outside the lock so independent meshes bake concurrently. A rare double-bake of the
        // same mesh is harmless (the bake is idempotent; last write wins).
        BakedPhysicsMesh baked = BakedPhysicsMesh.Build(mesh);

        lock (mesh)
        {
            var cached = mesh.BakedPhysics;
            if (cached != null && cached.Version == mesh.Version)
                return cached; // another thread baked the current version meanwhile
            mesh.BakedPhysics = baked;
            return baked;
        }
    }

    public World World { get; private set; }

    /// <summary>
    /// Static rigidbodies indexed by layer. Each layer has its own static rigidbody to ensure collision filtering works correctly.
    /// Orphan colliders (colliders without a Rigidbody3D component) will attach to the static rigidbody for their layer.
    /// </summary>
    private Dictionary<int, Jitter2.Dynamics.RigidBody> _staticRigidbodiesByLayer = new();

    /// <summary>
    /// Composite filter that chains multiple broad phase filters together.
    /// </summary>
    private CompositeBroadPhaseFilter _compositeBroadPhaseFilter;

    private readonly LayerFilter _layerFilter = new();

    // Registered terrain providers for shape casts. The grid placement is read from the provider per
    // query rather than captured here, so terrain can be moved and scaled after registration.
    internal readonly Dictionary<TerrainHeightmapProxy, ITerrainHeightProvider> _terrainProxies = [];

    private Float3 _gravity = new(0, -9.81f, 0);
    private int _solverIterations = 8;
    private int _relaxIterations = 4;
    private int _substep = 2;
    private float _speculativeRelaxationFactor = 0.9f;

    /// <summary>Acceleration applied to every body with <see cref="Rigidbody3D.AffectedByGravity"/>.</summary>
    public Float3 Gravity
    {
        get => _gravity;
        set
        {
            if (!IsFinite(value))
            {
                Debug.LogError($"[Physics] Gravity must be finite, ignoring {value}.");
                return;
            }

            _gravity = value;
        }
    }

    /// <summary>Constraint solver iterations per substep. At least 1.</summary>
    public int SolverIterations
    {
        get => _solverIterations;
        set => _solverIterations = Maths.Max(1, value);
    }

    /// <summary>Velocity relaxation iterations per substep. At least 0.</summary>
    public int RelaxIterations
    {
        get => _relaxIterations;
        set => _relaxIterations = Maths.Max(0, value);
    }

    /// <summary>How many substeps each fixed step is divided into. At least 1.</summary>
    public int Substep
    {
        get => _substep;
        set => _substep = Maths.Max(1, value);
    }

    public bool AllowSleep = true;
    public bool UseMultithreading = true;
    /// <summary>
    /// Whether Transform edits are pushed into the physics system immediately (before physics queries),
    /// or only batched right before the FixedUpdate step. Matches Unity's Physics.autoSyncTransforms:
    /// when true, a query right after moving a Transform sees the new pose; when false, call
    /// <see cref="SyncTransforms"/> manually (the pre-step sync still happens every FixedUpdate either way).
    /// This only concerns the transform -> body direction; the body -> transform readback after a step
    /// is separate and always runs for dynamic/kinematic bodies.
    /// </summary>
    public bool AutoSyncTransforms = true;

    // Rigidbodies that may need their Transform pushed into the physics world (transform -> body).
    private readonly HashSet<Rigidbody3D> _syncBodies = [];

    // Colliders without a Rigidbody3D all share one static body per layer, so a hit's body cannot say
    // which GameObject was struck. This maps every shape back to the Collider that created it. The key
    // is weak, so an entry that no Detach reached (a shape bulk-removed during a rebuild, say) dies
    // with the shape instead of pinning it and its collider for the lifetime of the world.
    private readonly ConditionalWeakTable<RigidBodyShape, Collider> _shapeOwners = new();

    // Collision events name their shapes by id rather than by reference (the contact data is freed
    // before the event is raised), so the same mapping is kept by ShapeId. Written and cleared in
    // lockstep with the table above; a stale entry can only return a destroyed collider, which the
    // lookup filters out.
    private readonly Dictionary<ulong, Collider> _shapeOwnersById = [];

    private readonly List<IDynamicTreeProxy> _queryProxies = [];
    private readonly List<ShapeCastHit> _queryHits = [];
    private readonly SphereShape _querySphere = new(0.5f);
    private readonly BoxShape _queryBox = new(1.0f, 1.0f, 1.0f);
    private readonly CapsuleShape _queryCapsule = new(0.5f, 1.0f);
    private readonly CylinderShape _queryCylinder = new(1.0f, 0.5f);
    private readonly ConeShape _queryCone = new(0.5f, 1.0f);

    private SphereShape QuerySphere(float radius)
    {
        _querySphere.Radius = radius;
        return _querySphere;
    }

    private BoxShape QueryBox(Float3 size)
    {
        _queryBox.Size = new JVector(size.X, size.Y, size.Z);
        return _queryBox;
    }

    private CapsuleShape QueryCapsule(float radius, float length)
    {
        _queryCapsule.Radius = radius;
        _queryCapsule.Length = length;
        return _queryCapsule;
    }

    private CylinderShape QueryCylinder(float radius, float height)
    {
        _queryCylinder.Radius = radius;
        _queryCylinder.Height = height;
        return _queryCylinder;
    }

    private ConeShape QueryCone(float radius, float height)
    {
        _queryCone.Radius = radius;
        _queryCone.Height = height;
        return _queryCone;
    }

    internal void RegisterBody(Rigidbody3D body) => _syncBodies.Add(body);
    internal void UnregisterBody(Rigidbody3D body) => _syncBodies.Remove(body);

    internal void RegisterShapeOwner(RigidBodyShape shape, Collider collider)
    {
        _shapeOwners.AddOrUpdate(shape, collider);
        _shapeOwnersById[shape.ShapeId] = collider;
    }

    internal void UnregisterShapeOwner(RigidBodyShape shape)
    {
        _shapeOwners.Remove(shape);
        _shapeOwnersById.Remove(shape.ShapeId);
    }

    /// <summary>
    /// The <see cref="Collider"/> that created the given shape, or null if the shape is not tracked.
    /// </summary>
    public Collider GetShapeOwner(RigidBodyShape shape)
    {
        if (shape != null && _shapeOwners.TryGetValue(shape, out Collider collider) && collider.IsValid())
            return collider;
        return null;
    }

    /// <summary>
    /// The <see cref="Collider"/> that created the shape with the given id, or null if it is not tracked.
    /// </summary>
    public Collider GetShapeOwner(ulong shapeId)
    {
        if (_shapeOwnersById.TryGetValue(shapeId, out Collider collider) && collider.IsValid())
            return collider;
        return null;
    }

    /// <summary>
    /// The Transform of the terrain owning the given proxy, or null if the proxy is not registered
    /// terrain. Terrain has no shape or body, so this is the only way a hit can name its GameObject.
    /// </summary>
    internal Transform GetTerrainTransform(IDynamicTreeProxy proxy)
    {
        MonoBehaviour owner = GetTerrainOwner(proxy);
        return owner.IsValid() && owner.GameObject.IsValid() ? owner.GameObject.Transform : null;
    }

    /// <summary>The component that registered the given terrain proxy, or null if it is not terrain.</summary>
    private MonoBehaviour GetTerrainOwner(IDynamicTreeProxy proxy)
    {
        if (proxy is TerrainHeightmapProxy terrain &&
            _terrainProxies.TryGetValue(terrain, out ITerrainHeightProvider provider) &&
            provider is MonoBehaviour owner && owner.IsValid())
            return owner;

        return null;
    }

    /// <summary>Whether a terrain proxy sits on a layer the mask accepts. Terrain has no body to carry
    /// layer data, so it is taken from the GameObject the terrain component lives on. The per-collider
    /// exclusions do not apply: terrain is neither a rigidbody nor a Collider.</summary>
    private bool TerrainAccepted(IDynamicTreeProxy proxy, in QueryFilter filter)
    {
        MonoBehaviour owner = GetTerrainOwner(proxy);
        return owner.IsValid() && owner.GameObject.IsValid() && filter.LayerMask.HasLayer(owner.GameObject.LayerIndex);
    }

    /// <summary>
    /// Whether a query may report this shape. One place so every cast, overlap and raycast applies the
    /// same rules, including the exclusions a bare layer mask cannot express.
    /// </summary>
    private bool Accepts(RigidBodyShape shape, in QueryFilter filter)
    {
        // A body with no user data has no layer to test, so a filter can only exclude it.
        if (shape.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData userData) return false;
        if (!filter.LayerMask.HasLayer(userData.Layer)) return false;

        // The owner lookup is only worth doing when something is actually excluded.
        if (!filter.HasExclusions) return true;

        if (filter.IgnoreRigidbody.IsValid() && userData.Rigidbody == filter.IgnoreRigidbody) return false;
        if (filter.IgnoreCollider.IsValid() && GetShapeOwner(shape) == filter.IgnoreCollider) return false;

        // Static colliders share one body per layer, so excluding a rigidbody has to also drop the
        // static shapes belonging to colliders parented under it.
        if (filter.IgnoreRigidbody.IsValid() && userData.Rigidbody.IsNotValid())
        {
            Collider owner = GetShapeOwner(shape);
            if (owner.IsValid() && owner.GetComponentInParent<Rigidbody3D>() == filter.IgnoreRigidbody) return false;
        }

        return true;
    }

    private bool AcceptsProxy(IDynamicTreeProxy proxy, in QueryFilter filter)
    {
        if (proxy is RigidBodyShape shape) return Accepts(shape, filter);
        return TerrainAccepted(proxy, filter);
    }

    /// <summary>
    /// The Transform a query hit should report: the rigidbody's when there is one, otherwise the
    /// collider's own.
    /// </summary>
    internal static Transform ResolveHitTransform(Rigidbody3D rigidbody, Collider collider)
    {
        if (rigidbody.IsValid() && rigidbody.GameObject.IsValid()) return rigidbody.GameObject.Transform;
        if (collider.IsValid() && collider.GameObject.IsValid()) return collider.GameObject.Transform;
        return null;
    }

    /// <summary>
    /// Pushes any changed Transforms into their physics bodies (transform -> body). Runs automatically
    /// before each simulation step and, when <see cref="AutoSyncTransforms"/> is on, before queries.
    /// Call it manually to make Transform edits visible to queries when auto-sync is off. Only bodies
    /// whose Transform actually changed since the last sync are updated, so it is cheap when idle.
    /// </summary>
    public void SyncTransforms()
    {
        foreach (var body in _syncBodies)
            if (body.IsValid()) body.SyncTransformToBody();
    }

    /// <summary>
    /// When true, uses Jitter2's deterministic island-based solver instead of the regular parallel solver.
    /// Slower but produces identical results across runs required for networked physics, replays, or lockstep simulation.
    /// </summary>
    public bool EnhancedDeterminism = false;

    /// <summary>
    /// Thread model for the physics step. Persistent keeps worker threads alive between steps
    /// (lower latency, higher idle CPU). Regular spins them up per step (higher latency, lower idle CPU).
    /// </summary>
    public PhysicsThreadModel ThreadModel = PhysicsThreadModel.Regular;

    /// <summary>
    /// Generates extra contact points along the perimeter of contact patches for improved stability
    /// on flat surfaces. Default true. Disable for slight perf gain at the cost of stability.
    /// </summary>
    public bool EnableAuxiliaryContactPoints = true;

    /// <summary>
    /// Persists contact manifolds across frames (warm-starts the solver). Default true. Disable for
    /// slight memory savings; significantly hurts stack stability if turned off.
    /// </summary>
    public bool PersistentContactManifold = true;

    /// <summary>
    /// Damping factor (0..1) applied to speculative contact correction. Lower = softer prediction.
    /// Default 0.9.
    /// </summary>
    public float SpeculativeRelaxationFactor
    {
        get => _speculativeRelaxationFactor;
        set => _speculativeRelaxationFactor = Maths.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>
    /// Event triggered before each physics step.
    /// </summary>
    public event Action<float> PreStep;

    /// <summary>
    /// Event triggered after each physics step.
    /// </summary>
    public event Action<float> PostStep;

    /// <summary>
    /// Event triggered before each physics substep, with the substep duration (FixedDeltaTime / Substep).
    /// Use this for sub-stepped force/impulse models (e.g. vehicle tyres) that need the body's
    /// re-integrated velocity each substep.
    /// </summary>
    public event Action<float> PreSubStep;

    public PhysicsWorld()
    {
        World = new World();

        World.DynamicTree.Filter = World.DefaultDynamicTreeFilter;

        // Set up composite broad phase filter
        _compositeBroadPhaseFilter = new CompositeBroadPhaseFilter();
        _compositeBroadPhaseFilter.AddFilter(_layerFilter);
        World.BroadPhaseFilter = _compositeBroadPhaseFilter;

        World.NarrowPhaseFilter = new TriangleEdgeCollisionFilter();

        // Hook up physics step events
        InitQueryDelegates();

        World.PreStep += OnPreStep;
        World.PostStep += OnPostStep;
        World.PreSubStep += OnPreSubStep;
    }

    /// <summary>
    /// Gets or creates a static rigidbody for the specified layer.
    /// Each layer has its own static rigidbody to ensure collision filtering works correctly.
    /// </summary>
    public Jitter2.Dynamics.RigidBody GetOrCreateStaticRigidBody(int layer)
    {
        if (_staticRigidbodiesByLayer.TryGetValue(layer, out var staticBody))
        {
            return staticBody;
        }

        // Create a new static rigidbody for this layer
        staticBody = World.CreateRigidBody();
        staticBody.MotionType = MotionType.Static;
        staticBody.Tag = new Rigidbody3D.RigidBodyUserData()
        {
            Rigidbody = null, // No Rigidbody3D component associated with this
            InstanceID = layer, // This is just used to sort for collision filtering, it just needs to be a consistent value
            Layer = layer
        };

        _staticRigidbodiesByLayer[layer] = staticBody;
        return staticBody;
    }

    private void OnPreStep(float deltaTime) => InvokeStepEvent(PreStep, deltaTime, nameof(PreStep));
    private void OnPreSubStep(float deltaTime) => InvokeStepEvent(PreSubStep, deltaTime, nameof(PreSubStep));
    private void OnPostStep(float deltaTime) => InvokeStepEvent(PostStep, deltaTime, nameof(PostStep));

    // Step callbacks fire inside Jitter's World.Step. A throwing subscriber (e.g. a wheel raycasting
    // against a disposed body) must not unwind through the solver and crash. Isolate each subscriber
    // so one failure neither aborts the step nor blocks the other subscribers.
    private static void InvokeStepEvent(Action<float> evt, float deltaTime, string stage)
    {
        if (evt == null) return;
        foreach (Delegate d in evt.GetInvocationList())
        {
            try { ((Action<float>)d)(deltaTime); }
            catch (Exception ex)
            {
                // Fires every step while the subscriber stays broken, so key it on the subscriber.
                Debug.LogErrorOnce($"Physics.StepCallback.{d.Method.DeclaringType?.Name}.{d.Method.Name}",
                    $"[Physics] {stage} subscriber {d.Method.DeclaringType?.Name}.{d.Method.Name} threw and was skipped: {ex.Message}");
            }
        }
    }

    public void Clear()
    {
        World?.Clear();

        // Clear the static rigidbodies dictionary - they will be recreated as needed
        _staticRigidbodiesByLayer.Clear();

        // World.Clear removed every body, so the per-body registries hold nothing worth visiting.
        // The components re-register when they recreate their bodies.
        _syncBodies.Clear();
        _shapeOwners.Clear();
        _shapeOwnersById.Clear();
        _layerFilter.ClearIgnoredCollisions();

        // World.Clear drops every dynamic tree proxy, terrain included, so the terrain filters would be
        // left chained onto the broad phase testing against proxies that no longer exist. Reset the
        // chain to the layer filter, which is all a fresh world starts with.
        _terrainProxies.Clear();
        _compositeBroadPhaseFilter.ClearFilters();
        _compositeBroadPhaseFilter.AddFilter(_layerFilter);
    }

    public void Update()
    {
        // Configure world settings
        World.AllowDeactivation = AllowSleep;

        World.SubstepCount = Substep;
        World.SolverIterations = (SolverIterations, RelaxIterations);

        World.Gravity = new JVector(Gravity.X, Gravity.Y, Gravity.Z);

        World.SolveMode = EnhancedDeterminism ? SolveMode.Deterministic : SolveMode.Regular;
        World.ThreadModel = ThreadModel == PhysicsThreadModel.Persistent
            ? World.ThreadModelType.Persistent
            : World.ThreadModelType.Regular;

        World.EnableAuxiliaryContactPoints = EnableAuxiliaryContactPoints;
        World.PersistentContactManifold = PersistentContactManifold;
        World.SpeculativeRelaxationFactor = SpeculativeRelaxationFactor;

        // Push any user Transform edits into the bodies before stepping (always - this is the
        // "sync prior to the physics step" that happens regardless of AutoSyncTransforms).
        SyncTransforms();

        World.Step(Time.FixedDeltaTime, UseMultithreading);

        // Record the pose each body just landed on, so interpolated bodies have two steps to render
        // between. Driven from here rather than a per-body event to keep it one pass with no delegates.
        foreach (var body in _syncBodies)
            if (body.IsValid()) body.CapturePose();
    }

    /// <summary>
    /// Casts a ray and reports whether it hit anything.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction) => Raycast(origin, direction, float.MaxValue, QueryFilter.Default);

    /// <inheritdoc cref="Raycast(Float3, Float3)"/>
    public bool Raycast(Float3 origin, Float3 direction, float maxDistance) => Raycast(origin, direction, maxDistance, QueryFilter.Default);

    /// <summary>
    /// Casts a ray and reports whether it hit anything, restricted by <paramref name="filter"/>.
    /// A <see cref="LayerMask"/> converts implicitly, so a layer-only call site reads unchanged.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction, float maxDistance, QueryFilter filter)
    {
        if (!BeginRayQuery(ref origin, ref direction, maxDistance, filter, nameof(Raycast))) return false;

        return World.DynamicTree.RayCast(ToJ(origin), ToJ(direction), maxDistance,
            _acceptProxyDelegate, PostFilter, out _, out _, out _);
    }

    /// <summary>
    /// Casts a ray and returns the closest hit.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction, out RaycastHit hitInfo) =>
        Raycast(origin, direction, out hitInfo, float.MaxValue, QueryFilter.Default);

    /// <inheritdoc cref="Raycast(Float3, Float3, out RaycastHit)"/>
    public bool Raycast(Float3 origin, Float3 direction, float maxDistance, out RaycastHit hitInfo) =>
        Raycast(origin, direction, out hitInfo, maxDistance, QueryFilter.Default);

    /// <summary>
    /// Casts a ray and returns the closest hit, restricted by <paramref name="filter"/>.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction, out RaycastHit hitInfo, float maxDistance, QueryFilter filter)
    {
        hitInfo = new RaycastHit();
        if (!BeginRayQuery(ref origin, ref direction, maxDistance, filter, nameof(Raycast))) return false;

        bool hit = World.DynamicTree.RayCast(ToJ(origin), ToJ(direction), maxDistance,
            _acceptProxyDelegate, PostFilter,
            out IDynamicTreeProxy shape, out JVector normal, out float lambda);

        if (hit)
        {
            var result = new DynamicTree.RayCastResult { Entity = shape, Lambda = lambda, Normal = normal };
            hitInfo.SetFromJitterResult(this, result, origin, direction);
        }

        return hit;
    }

    /// <summary>
    /// Casts a ray and collects every hit along it, nearest first.
    /// </summary>
    /// <returns>Number of hits written to <paramref name="hits"/>.</returns>
    public int RaycastAll(Float3 origin, Float3 direction, float maxDistance, List<RaycastHit> hits) =>
        RaycastAll(origin, direction, maxDistance, hits, QueryFilter.Default);

    /// <inheritdoc cref="RaycastAll(Float3, Float3, float, List{RaycastHit})"/>
    public int RaycastAll(Float3 origin, Float3 direction, float maxDistance, List<RaycastHit> hits, QueryFilter filter)
    {
        hits.Clear();
        if (!BeginRayQuery(ref origin, ref direction, maxDistance, filter, nameof(RaycastAll))) return 0;

        _rayHitSink = hits;
        _rayOrigin = origin;
        _rayDirection = direction;
        try
        {
            World.DynamicTree.RayCast(ToJ(origin), ToJ(direction), maxDistance,
                _acceptProxyDelegate, _collectRayHitDelegate, out _, out _, out _);
        }
        finally
        {
            _rayHitSink = null;
        }

        hits.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return hits.Count;
    }

    /// <summary>
    /// Casts a ray between two points and returns the closest hit. The usual way to ask "is there
    /// anything between these two things".
    /// </summary>
    public bool Linecast(Float3 from, Float3 to, out RaycastHit hitInfo) =>
        Linecast(from, to, out hitInfo, QueryFilter.Default);

    /// <inheritdoc cref="Linecast(Float3, Float3, out RaycastHit)"/>
    public bool Linecast(Float3 from, Float3 to, out RaycastHit hitInfo, QueryFilter filter)
    {
        hitInfo = new RaycastHit();
        Float3 delta = to - from;
        float distance = Float3.Length(delta);
        if (distance <= 0.0f) return false;

        return Raycast(from, delta, out hitInfo, distance, filter);
    }

    /// <inheritdoc cref="Linecast(Float3, Float3, out RaycastHit)"/>
    public bool Linecast(Float3 from, Float3 to) => Linecast(from, to, out _, QueryFilter.Default);

    /// <inheritdoc cref="Linecast(Float3, Float3, out RaycastHit)"/>
    public bool Linecast(Float3 from, Float3 to, QueryFilter filter) => Linecast(from, to, out _, filter);

    /// <summary>
    /// Shared prologue for the ray queries: validates the inputs, normalizes the direction, publishes
    /// the filter for the shared callbacks and syncs pending Transform edits into the bodies.
    /// </summary>
    private bool BeginRayQuery(ref Float3 origin, ref Float3 direction, float maxDistance, in QueryFilter filter, string query)
    {
        if (!ValidateQuery(origin, direction, maxDistance, query)) return false;

        direction = Float3.Normalize(direction);
        if (Float3.LengthSquared(direction) <= 0.0f) return false;

        _activeFilter = filter;
        if (AutoSyncTransforms) SyncTransforms(); // eager transform->body sync so the query sees recent Transform edits
        return true;
    }

    private static JVector ToJ(Float3 v) => new(v.X, v.Y, v.Z);


    // The tree's filter callbacks are delegates, so binding a filter per call would allocate a closure
    // every query. Queries are main-thread and non-reentrant, so the in-flight filter and ray-hit sink
    // live in fields and the delegates are built once.
    private QueryFilter _activeFilter = QueryFilter.Default;
    private List<RaycastHit> _rayHitSink;
    private Float3 _rayOrigin, _rayDirection;
    private DynamicTree.RayCastFilterPre _acceptProxyDelegate;
    private DynamicTree.RayCastFilterPost _collectRayHitDelegate;

    private void InitQueryDelegates()
    {
        _acceptProxyDelegate = proxy => AcceptsProxy(proxy, _activeFilter);
        _collectRayHitDelegate = CollectRayHit;
    }

    // Collects every hit rather than the nearest: returning false tells the traversal this hit was
    // filtered out, so it keeps descending instead of narrowing its search to the closest so far.
    private bool CollectRayHit(DynamicTree.RayCastResult result)
    {
        var hit = new RaycastHit();
        hit.SetFromJitterResult(this, result, _rayOrigin, _rayDirection);
        if (hit.Hit) _rayHitSink.Add(hit);
        return false;
    }

    private static bool IsFinite(Float3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// Rejects query inputs that would take NaN or infinity into the solver. Jitter's support maps use
    /// <c>Math.Sign</c> on the search direction, which throws outright on NaN, so a single poisoned
    /// caller would otherwise crash the frame from deep inside the narrow phase. Reporting no hit and
    /// naming the query gives the caller something to act on instead.
    /// </summary>
    private static bool ValidateQuery(Float3 origin, Float3 direction, float maxDistance, string query)
    {
        if (!IsFinite(origin) || !IsFinite(direction) || !float.IsFinite(maxDistance))
        {
            // A poisoned caller feeds this every frame, so report the query once rather than per call.
            Debug.LogErrorOnce($"Physics.NonFiniteQuery.{query}", $"[Physics] {query} was given a non-finite origin ({origin}), direction ({direction}) or distance ({maxDistance}) and was skipped.");
            return false;
        }

        return true;
    }

    /// <summary>As <see cref="ValidateQuery(Float3, Float3, float, string)"/>, for queries with no sweep.</summary>
    private static bool ValidateQuery(Float3 position, string query)
    {
        if (!IsFinite(position))
        {
            Debug.LogErrorOnce($"Physics.NonFiniteQuery.{query}", $"[Physics] {query} was given a non-finite position ({position}) and was skipped.");
            return false;
        }

        return true;
    }

    private static bool PostFilter(DynamicTree.RayCastResult result)
    {
        return true;
    }

    #region Shape Casting

    /// <summary>
    /// Generic shape cast that returns all hits along the sweep path.
    /// </summary>
    /// <param name="shape">The shape to cast.</param>
    /// <param name="orientation">The orientation of the casting shape.</param>
    /// <param name="origin">Starting position of the shape.</param>
    /// <param name="direction">Direction to cast the shape.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <param name="filter">Layer mask for filtering.</param>
    /// <returns>Number of hits found.</returns>
    public int ShapeCastAll(RigidBodyShape shape, Quaternion orientation, Float3 origin, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter)
    {
        hits.Clear();
        if (!ValidateQuery(origin, direction, maxDistance, nameof(ShapeCastAll))) return 0;

        if (AutoSyncTransforms) SyncTransforms(); // eager transform->body sync so the query sees recent Transform edits
        direction = Float3.Normalize(direction);

        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);
        JVector sweep = jDirection * maxDistance;

        // Get all shapes from the dynamic tree that could potentially be hit
        List<IDynamicTreeProxy> potentialShapes = _queryProxies;
        potentialShapes.Clear();

        // Create a bounding box that encompasses the entire sweep
        JBoundingBox sweepBox = new();
        shape.CalculateBoundingBox(new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), jOrigin, out JBoundingBox startBox);
        shape.CalculateBoundingBox(new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), jOrigin + sweep, out JBoundingBox endBox);

        sweepBox.Min = JVector.Min(startBox.Min, endBox.Min);
        sweepBox.Max = JVector.Max(startBox.Max, endBox.Max);

        World.DynamicTree.Query(potentialShapes, in sweepBox);

        var jOrientation = new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W);

        foreach (IDynamicTreeProxy proxy in potentialShapes)
        {
            if (proxy is TerrainHeightmapProxy terrainProxy)
            {
                // Shape cast against terrain heightmap triangles
                if (TerrainAccepted(terrainProxy, filter))
                    SweepAgainstTerrain(shape, jOrientation, jOrigin, sweep, terrainProxy, sweepBox, hits);
                continue;
            }

            if (proxy is not RigidBodyShape targetShape) continue;

            if (!Accepts(targetShape, filter)) continue;
            var userData = targetShape.RigidBody.Tag as Rigidbody3D.RigidBodyUserData;

            Jitter2.Dynamics.RigidBody targetBody = targetShape.RigidBody;

            // Perform sweep test
            bool hit = NarrowPhase.Sweep(
                shape, targetShape,
                jOrientation, targetBody.Data.Orientation,
                jOrigin, targetBody.Data.Position,
                sweep, JVector.Zero,
                out JVector pointA, out JVector pointB, out JVector normal, out float lambda);

            if (hit && lambda >= 0 && lambda <= 1.0)
            {
                float penetration = 0.0f;

                // A zero normal means the shapes already overlapped at the start of the sweep, where
                // Sweep reports lambda 0 and no direction. Recover the direction and the depth from
                // MPR/EPA; the fraction stays 0, the depth belongs in Penetration.
                if (normal.LengthSquared() <= 0)
                {
                    lambda = 0.0f;

                    bool resolved = NarrowPhase.MprEpa(
                        shape, targetShape,
                        jOrientation, targetBody.Data.Orientation,
                        jOrigin, targetBody.Data.Position,
                        out JVector deepestA, out JVector deepestB, out JVector separation, out penetration);

                    if (resolved && separation.LengthSquared() > 0)
                    {
                        pointA = deepestA;
                        pointB = deepestB;
                        normal = JVector.Normalize(separation);
                    }
                    else
                    {
                        // EPA did not converge. Report the sweep direction so the caller is still
                        // blocked, rather than handing back a zero normal it would divide by.
                        penetration = 0.0f;
                        normal = jDirection;
                    }
                }

                Collider owner = GetShapeOwner(targetShape);
                var castHit = new ShapeCastHit
                {
                    Hit = true,
                    Fraction = lambda,
                    Distance = lambda * maxDistance,
                    Penetration = penetration,
                    Normal = -(new Float3(normal.X, normal.Y, normal.Z)),
                    Point = new Float3(pointA.X, pointA.Y, pointA.Z),
                    HitPoint = new Float3(pointB.X, pointB.Y, pointB.Z),
                    Rigidbody = userData.Rigidbody,
                    Shape = targetShape,
                    Collider = owner,
                    Transform = ResolveHitTransform(userData.Rigidbody, owner)
                };
                hits.Add(castHit);
            }
        }

        // Nearest first, so callers can take hits[0] without scanning.
        hits.Sort(static (a, b) => a.Fraction.CompareTo(b.Fraction));
        return hits.Count;
    }

    /// <summary>
    /// Sweep a shape against terrain heightmap triangles within the sweep bounding box.
    /// </summary>
    private void SweepAgainstTerrain(RigidBodyShape shape, JQuaternion jOrientation,
        JVector jOrigin, JVector sweep, TerrainHeightmapProxy terrainProxy,
        JBoundingBox sweepBox, List<ShapeCastHit> hits)
    {
        if (!_terrainProxies.TryGetValue(terrainProxy, out ITerrainHeightProvider hp))
            return;

        JVector origin = hp.Origin;
        float cs = hp.CellSize;
        if (cs <= 0.0f) return;

        // Convert sweep AABB to grid coordinates
        int minX = Maths.Max(0, (int)Maths.Floor((sweepBox.Min.X - origin.X) / cs));
        int minZ = Maths.Max(0, (int)Maths.Floor((sweepBox.Min.Z - origin.Z) / cs));
        int maxX = Maths.Min(hp.Width - 1, (int)Maths.Ceiling((sweepBox.Max.X - origin.X) / cs));
        int maxZ = Maths.Min(hp.Height - 1, (int)Maths.Ceiling((sweepBox.Max.Z - origin.Z) / cs));

        float bestLambda = float.MaxValue;
        JVector bestNormal = JVector.Zero;
        JVector bestPointA = JVector.Zero;
        JVector bestPointB = JVector.Zero;

        for (int x = minX; x < maxX; x++)
        {
            for (int z = minZ; z < maxZ; z++)
            {
                if (!hp.IsValidCell(x, z) || hp.IsCellHole(x, z)) continue;
                if (!hp.TryGetHeight(x, z, out float h00) ||
                    !hp.TryGetHeight(x + 1, z, out float h10) ||
                    !hp.TryGetHeight(x + 1, z + 1, out float h11) ||
                    !hp.TryGetHeight(x, z + 1, out float h01))
                    continue;

                // Two triangles per cell
                for (int tri = 0; tri < 2; tri++)
                {
                    CollisionTriangle triangle;
                    if (tri == 0)
                    {
                        triangle.A = new JVector(x * cs + origin.X, h00, z * cs + origin.Z);
                        triangle.B = new JVector((x + 1) * cs + origin.X, h11, (z + 1) * cs + origin.Z);
                        triangle.C = new JVector((x + 1) * cs + origin.X, h10, z * cs + origin.Z);
                    }
                    else
                    {
                        triangle.A = new JVector(x * cs + origin.X, h00, z * cs + origin.Z);
                        triangle.B = new JVector(x * cs + origin.X, h01, (z + 1) * cs + origin.Z);
                        triangle.C = new JVector((x + 1) * cs + origin.X, h11, (z + 1) * cs + origin.Z);
                    }

                    bool hit = NarrowPhase.Sweep(
                        shape, triangle,
                        jOrientation, JQuaternion.Identity,
                        jOrigin, JVector.Zero,
                        sweep, JVector.Zero,
                        out JVector pA, out JVector pB, out JVector n, out float lambda);

                    if (hit && lambda >= 0 && lambda <= 1.0f && lambda < bestLambda)
                    {
                        // Overlap at t=0 leaves no sweep direction. Substitute the triangle's own
                        // normal, flipped to point from the caster at the triangle so it matches
                        // the sign convention of the sweep normal (negated once when reported).
                        // NormalizeSafe, because a degenerate cell would otherwise hand back NaN.
                        if (n.LengthSquared() <= 0)
                        {
                            n = -JVector.NormalizeSafe((triangle.B - triangle.A) % (triangle.C - triangle.A));
                            if (n.LengthSquared() <= 0) continue;
                        }

                        bestLambda = lambda;
                        bestNormal = n;
                        bestPointA = pA;
                        bestPointB = pB;
                    }
                }
            }
        }

        if (bestLambda < float.MaxValue)
        {
            // The height provider is the TerrainCollider component itself, so it can name the GameObject
            // even though terrain has no RigidBodyShape to look up.
            var terrain = hp as MonoBehaviour;
            hits.Add(new ShapeCastHit
            {
                Hit = true,
                Fraction = bestLambda,
                Distance = bestLambda * sweep.Length(),
                Normal = -(new Float3(bestNormal.X, bestNormal.Y, bestNormal.Z)),
                Point = new Float3(bestPointA.X, bestPointA.Y, bestPointA.Z),
                HitPoint = new Float3(bestPointB.X, bestPointB.Y, bestPointB.Z),
                Rigidbody = null,
                Shape = null,
                Collider = null,
                Transform = terrain.IsValid() && terrain.GameObject.IsValid() ? terrain.GameObject.Transform : null,
            });
        }
    }

    /// <summary>
    /// Generic shape cast that returns all hits with default layer mask.
    /// </summary>
    public int ShapeCastAll(RigidBodyShape shape, Quaternion orientation, Float3 origin, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return ShapeCastAll(shape, orientation, origin, direction, maxDistance, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Generic shape cast that returns only the closest hit.
    /// </summary>
    /// <param name="shape">The shape to cast.</param>
    /// <param name="orientation">The orientation of the casting shape.</param>
    /// <param name="origin">Starting position of the shape.</param>
    /// <param name="direction">Direction to cast the shape.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about the closest hit.</param>
    /// <param name="filter">Layer mask for filtering.</param>
    /// <returns>True if the shape hit something.</returns>
    public bool ShapeCast(RigidBodyShape shape, Quaternion orientation, Float3 origin, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter)
    {
        List<ShapeCastHit> hits = _queryHits;
        int hitCount = ShapeCastAll(shape, orientation, origin, direction, maxDistance, hits, filter);

        if (hitCount > 0)
        {
            hitInfo = hits[0]; // ShapeCastAll returns them nearest first
            return true;
        }

        hitInfo = new ShapeCastHit();
        return false;
    }

    /// <summary>
    /// Generic shape cast that returns only the closest hit with default orientation and layer mask.
    /// </summary>
    public bool ShapeCast(RigidBodyShape shape, Float3 origin, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return ShapeCast(shape, Quaternion.Identity, origin, direction, maxDistance, out hitInfo, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a sphere along a direction and returns the closest hit.
    /// </summary>
    /// <param name="origin">Starting position of the sphere center.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <param name="direction">Direction to cast the sphere.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the sphere hit something.</returns>
    public bool SphereCast(Float3 origin, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return SphereCast(origin, radius, direction, maxDistance, out hitInfo, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a sphere along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool SphereCast(Float3 origin, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter)
    {
        return ShapeCast(QuerySphere(radius), Quaternion.Identity, origin, direction, maxDistance, out hitInfo, filter);
    }

    /// <summary>
    /// Casts a sphere along a direction and returns all hits.
    /// </summary>
    /// <param name="origin">Starting position of the sphere center.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <param name="direction">Direction to cast the sphere.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int SphereCastAll(Float3 origin, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return SphereCastAll(origin, radius, direction, maxDistance, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a sphere along a direction with layer filtering and returns all hits.
    /// </summary>
    public int SphereCastAll(Float3 origin, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter)
    {
        return ShapeCastAll(QuerySphere(radius), Quaternion.Identity, origin, direction, maxDistance, hits, filter);
    }

    /// <summary>
    /// Casts a capsule along a direction and returns the closest hit.
    /// </summary>
    /// <param name="point1">Start point of the capsule's line segment.</param>
    /// <param name="point2">End point of the capsule's line segment.</param>
    /// <param name="radius">Radius of the capsule.</param>
    /// <param name="direction">Direction to cast the capsule.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the capsule hit something.</returns>
    public bool CapsuleCast(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return CapsuleCast(point1, point2, radius, direction, maxDistance, out hitInfo, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a capsule along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool CapsuleCast(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter)
    {
        // Calculate capsule properties
        Float3 capsuleCenter = (point1 + point2) * 0.5f;
        Float3 capsuleAxis = point2 - point1;
        float capsuleLength = Float3.Length(capsuleAxis);

        // Reused capsule shape (aligned along Y-axis)
        CapsuleShape capsule = QueryCapsule(radius, capsuleLength);

        // Calculate orientation to align capsule with the segment
        Quaternion capsuleOrientation = CalculateCapsuleOrientation(capsuleAxis, capsuleLength);

        return ShapeCast(capsule, capsuleOrientation, capsuleCenter, direction, maxDistance, out hitInfo, filter);
    }

    /// <summary>
    /// Casts a capsule along a direction and returns all hits.
    /// </summary>
    /// <param name="point1">Start point of the capsule's line segment.</param>
    /// <param name="point2">End point of the capsule's line segment.</param>
    /// <param name="radius">Radius of the capsule.</param>
    /// <param name="direction">Direction to cast the capsule.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int CapsuleCastAll(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return CapsuleCastAll(point1, point2, radius, direction, maxDistance, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a capsule along a direction with layer filtering and returns all hits.
    /// </summary>
    public int CapsuleCastAll(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter)
    {
        // Calculate capsule properties
        Float3 capsuleCenter = (point1 + point2) * 0.5f;
        Float3 capsuleAxis = point2 - point1;
        float capsuleLength = Float3.Length(capsuleAxis);

        // Reused capsule shape (aligned along Y-axis)
        CapsuleShape capsule = QueryCapsule(radius, capsuleLength);

        // Calculate orientation to align capsule with the segment
        Quaternion capsuleOrientation = CalculateCapsuleOrientation(capsuleAxis, capsuleLength);

        return ShapeCastAll(capsule, capsuleOrientation, capsuleCenter, direction, maxDistance, hits, filter);
    }

    /// <summary>
    /// Helper method to calculate the orientation needed to align a capsule (Y-axis aligned) with a given axis.
    /// </summary>
    private static Quaternion CalculateCapsuleOrientation(Float3 capsuleAxis, float capsuleLength)
    {
        if (capsuleLength <= 1e-6)
            return Quaternion.Identity;

        Float3 normalizedAxis = capsuleAxis / capsuleLength;
        Float3 yAxis = new(0, 1, 0);

        // If axis is aligned with Y, no rotation needed
        if (Maths.Abs(Float3.Dot(normalizedAxis, yAxis) - 1.0) < 1e-6)
        {
            return Quaternion.Identity;
        }
        // If axis is opposite to Y, rotate 180 degrees around X
        else if (Maths.Abs(Float3.Dot(normalizedAxis, yAxis) + 1.0) < 1e-6)
        {
            return Quaternion.AxisAngle(new Float3(1, 0, 0), Maths.PI);
        }
        // Calculate rotation from Y-axis to the capsule axis
        else
        {
            Float3 rotAxis = Float3.Cross(yAxis, normalizedAxis);
            rotAxis = Float3.Normalize(rotAxis);
            float angle = Maths.Acos(Float3.Dot(yAxis, normalizedAxis));
            return Quaternion.AxisAngle(new Float3(rotAxis.X, rotAxis.Y, rotAxis.Z), angle);
        }
    }

    /// <summary>
    /// Casts a box along a direction and returns the closest hit.
    /// </summary>
    /// <param name="origin">Starting position of the box center.</param>
    /// <param name="size">Size of the box (width, height, depth).</param>
    /// <param name="orientation">Orientation of the box.</param>
    /// <param name="direction">Direction to cast the box.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the box hit something.</returns>
    public bool BoxCast(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return BoxCast(origin, size, orientation, direction, maxDistance, out hitInfo, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a box along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool BoxCast(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter)
    {
        return ShapeCast(QueryBox(size), orientation, origin, direction, maxDistance, out hitInfo, filter);
    }

    /// <summary>
    /// Casts a box along a direction and returns all hits.
    /// </summary>
    /// <param name="origin">Starting position of the box center.</param>
    /// <param name="size">Size of the box (width, height, depth).</param>
    /// <param name="orientation">Orientation of the box.</param>
    /// <param name="direction">Direction to cast the box.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int BoxCastAll(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return BoxCastAll(origin, size, orientation, direction, maxDistance, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a box along a direction with layer filtering and returns all hits.
    /// </summary>
    public int BoxCastAll(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter)
    {
        return ShapeCastAll(QueryBox(size), orientation, origin, direction, maxDistance, hits, filter);
    }

    /// <summary>
    /// Casts a cylinder along a direction and returns the closest hit.
    /// </summary>
    /// <param name="origin">Starting position of the cylinder center.</param>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="height">Height of the cylinder.</param>
    /// <param name="orientation">Orientation of the cylinder.</param>
    /// <param name="direction">Direction to cast the cylinder.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the cylinder hit something.</returns>
    public bool CylinderCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return CylinderCast(origin, radius, height, orientation, direction, maxDistance, out hitInfo, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a cylinder along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool CylinderCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter)
    {
        return ShapeCast(QueryCylinder(radius, height), orientation, origin, direction, maxDistance, out hitInfo, filter);
    }

    /// <summary>
    /// Casts a cylinder along a direction and returns all hits.
    /// </summary>
    /// <param name="origin">Starting position of the cylinder center.</param>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="height">Height of the cylinder.</param>
    /// <param name="orientation">Orientation of the cylinder.</param>
    /// <param name="direction">Direction to cast the cylinder.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int CylinderCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return CylinderCastAll(origin, radius, height, orientation, direction, maxDistance, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a cylinder along a direction with layer filtering and returns all hits.
    /// </summary>
    public int CylinderCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter)
    {
        return ShapeCastAll(QueryCylinder(radius, height), orientation, origin, direction, maxDistance, hits, filter);
    }

    /// <summary>
    /// Casts a cone along a direction and returns the closest hit.
    /// </summary>
    /// <param name="origin">Starting position of the cone center.</param>
    /// <param name="radius">Base radius of the cone.</param>
    /// <param name="height">Height of the cone.</param>
    /// <param name="orientation">Orientation of the cone.</param>
    /// <param name="direction">Direction to cast the cone.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hitInfo">Information about what was hit.</param>
    /// <returns>True if the cone hit something.</returns>
    public bool ConeCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo)
    {
        return ConeCast(origin, radius, height, orientation, direction, maxDistance, out hitInfo, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a cone along a direction with layer filtering and returns the closest hit.
    /// </summary>
    public bool ConeCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter)
    {
        return ShapeCast(QueryCone(radius, height), orientation, origin, direction, maxDistance, out hitInfo, filter);
    }

    /// <summary>
    /// Casts a cone along a direction and returns all hits.
    /// </summary>
    /// <param name="origin">Starting position of the cone center.</param>
    /// <param name="radius">Base radius of the cone.</param>
    /// <param name="height">Height of the cone.</param>
    /// <param name="orientation">Orientation of the cone.</param>
    /// <param name="direction">Direction to cast the cone.</param>
    /// <param name="maxDistance">Maximum distance to cast.</param>
    /// <param name="hits">List to populate with all hits found.</param>
    /// <returns>Number of hits found.</returns>
    public int ConeCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits)
    {
        return ConeCastAll(origin, radius, height, orientation, direction, maxDistance, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Casts a cone along a direction with layer filtering and returns all hits.
    /// </summary>
    public int ConeCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter)
    {
        return ShapeCastAll(QueryCone(radius, height), orientation, origin, direction, maxDistance, hits, filter);
    }

    #endregion

    #region Overlap Queries

    /// <summary>
    /// Generic overlap query that returns all colliders overlapping the given shape.
    /// </summary>
    /// <param name="shape">The shape to test for overlaps.</param>
    /// <param name="orientation">The orientation of the shape.</param>
    /// <param name="position">Position of the shape.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <param name="filter">Layer mask for filtering.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int Overlap(RigidBodyShape shape, Quaternion orientation, Float3 position, List<ShapeCastHit> hits, QueryFilter filter)
    {
        hits.Clear();
        if (!ValidateQuery(position, nameof(Overlap))) return 0;

        if (AutoSyncTransforms) SyncTransforms(); // eager transform->body sync (also covers Overlap*/Check* which funnel here)
        var jPosition = new JVector(position.X, position.Y, position.Z);

        // Get all shapes from the dynamic tree that could potentially overlap
        List<IDynamicTreeProxy> potentialShapes = _queryProxies;
        potentialShapes.Clear();

        // Create a bounding box for the shape
        shape.CalculateBoundingBox(new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), jPosition, out JBoundingBox shapeBounds);
        World.DynamicTree.Query(potentialShapes, in shapeBounds);

        foreach (IDynamicTreeProxy proxy in potentialShapes)
        {
            if (proxy is not RigidBodyShape targetShape) continue;

            if (!Accepts(targetShape, filter)) continue;
            var userData = targetShape.RigidBody.Tag as Rigidbody3D.RigidBodyUserData;

            Jitter2.Dynamics.RigidBody targetBody = targetShape.RigidBody;

            // Perform overlap test using sweep with zero distance
            bool overlaps = NarrowPhase.MprEpa(
                shape, targetShape,
                new JQuaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), targetBody.Data.Orientation,
                jPosition, targetBody.Data.Position,
                out JVector pointA, out JVector pointB, out JVector normal, out float penetration);

            if (overlaps && penetration > 0)
            {
                Collider owner = GetShapeOwner(targetShape);
                var hit = new ShapeCastHit
                {
                    Hit = true,
                    Fraction = 0,
                    Penetration = penetration,
                    Normal = -(new Float3(normal.X, normal.Y, normal.Z)),
                    Point = new Float3(pointA.X, pointA.Y, pointA.Z),
                    HitPoint = new Float3(pointB.X, pointB.Y, pointB.Z),
                    Rigidbody = userData.Rigidbody,
                    Shape = targetShape,
                    Collider = owner,
                    Transform = ResolveHitTransform(userData.Rigidbody, owner)
                };
                hits.Add(hit);
            }
        }

        return hits.Count;
    }

    /// <summary>
    /// Generic overlap query with default layer mask.
    /// </summary>
    public int Overlap(RigidBodyShape shape, Quaternion orientation, Float3 position, List<ShapeCastHit> hits)
    {
        return Overlap(shape, orientation, position, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Tests if a sphere overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the sphere.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapSphere(Float3 position, float radius, List<ShapeCastHit> hits)
    {
        return OverlapSphere(position, radius, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Tests if a sphere overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapSphere(Float3 position, float radius, List<ShapeCastHit> hits, QueryFilter filter)
    {
        return Overlap(QuerySphere(radius), Quaternion.Identity, position, hits, filter);
    }

    /// <summary>
    /// Tests if a capsule overlaps with any colliders.
    /// </summary>
    /// <param name="point1">Start point of the capsule's line segment.</param>
    /// <param name="point2">End point of the capsule's line segment.</param>
    /// <param name="radius">Radius of the capsule.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapCapsule(Float3 point1, Float3 point2, float radius, List<ShapeCastHit> hits)
    {
        return OverlapCapsule(point1, point2, radius, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Tests if a capsule overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapCapsule(Float3 point1, Float3 point2, float radius, List<ShapeCastHit> hits, QueryFilter filter)
    {
        // Calculate capsule properties
        Float3 capsuleCenter = (point1 + point2) * 0.5f;
        Float3 capsuleAxis = point2 - point1;
        float capsuleLength = Float3.Length(capsuleAxis);

        // Reused capsule shape (aligned along Y-axis)
        CapsuleShape capsule = QueryCapsule(radius, capsuleLength);

        // Calculate orientation to align capsule with the segment
        Quaternion capsuleOrientation = CalculateCapsuleOrientation(capsuleAxis, capsuleLength);

        return Overlap(capsule, capsuleOrientation, capsuleCenter, hits, filter);
    }

    /// <summary>
    /// Tests if a box overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the box.</param>
    /// <param name="size">Size of the box (width, height, depth).</param>
    /// <param name="orientation">Orientation of the box.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapBox(Float3 position, Float3 size, Quaternion orientation, List<ShapeCastHit> hits)
    {
        return OverlapBox(position, size, orientation, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Tests if a box overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapBox(Float3 position, Float3 size, Quaternion orientation, List<ShapeCastHit> hits, QueryFilter filter)
    {
        return Overlap(QueryBox(size), orientation, position, hits, filter);
    }

    /// <summary>
    /// Tests if a cylinder overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the cylinder.</param>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="height">Height of the cylinder.</param>
    /// <param name="orientation">Orientation of the cylinder.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapCylinder(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits)
    {
        return OverlapCylinder(position, radius, height, orientation, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Tests if a cylinder overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapCylinder(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits, QueryFilter filter)
    {
        return Overlap(QueryCylinder(radius, height), orientation, position, hits, filter);
    }

    /// <summary>
    /// Tests if a cone overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the cone.</param>
    /// <param name="radius">Base radius of the cone.</param>
    /// <param name="height">Height of the cone.</param>
    /// <param name="orientation">Orientation of the cone.</param>
    /// <param name="hits">List to populate with all overlapping colliders.</param>
    /// <returns>Number of overlapping colliders found.</returns>
    public int OverlapCone(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits)
    {
        return OverlapCone(position, radius, height, orientation, hits, QueryFilter.Default);
    }

    /// <summary>
    /// Tests if a cone overlaps with any colliders with layer filtering.
    /// </summary>
    public int OverlapCone(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits, QueryFilter filter)
    {
        return Overlap(QueryCone(radius, height), orientation, position, hits, filter);
    }

    #endregion

    #region Check Queries

    /// <summary>
    /// Checks if a sphere overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the sphere.</param>
    /// <param name="radius">Radius of the sphere.</param>
    /// <returns>True if the sphere overlaps with any collider.</returns>
    public bool CheckSphere(Float3 position, float radius)
    {
        return CheckSphere(position, radius, QueryFilter.Default);
    }

    /// <summary>
    /// Checks if a sphere overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckSphere(Float3 position, float radius, QueryFilter filter)
    {
        List<ShapeCastHit> hits = _queryHits;
        return OverlapSphere(position, radius, hits, filter) > 0;
    }

    /// <summary>
    /// Checks if a capsule overlaps with any colliders.
    /// </summary>
    /// <param name="point1">Start point of the capsule's line segment.</param>
    /// <param name="point2">End point of the capsule's line segment.</param>
    /// <param name="radius">Radius of the capsule.</param>
    /// <returns>True if the capsule overlaps with any collider.</returns>
    public bool CheckCapsule(Float3 point1, Float3 point2, float radius)
    {
        return CheckCapsule(point1, point2, radius, QueryFilter.Default);
    }

    /// <summary>
    /// Checks if a capsule overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckCapsule(Float3 point1, Float3 point2, float radius, QueryFilter filter)
    {
        List<ShapeCastHit> hits = _queryHits;
        return OverlapCapsule(point1, point2, radius, hits, filter) > 0;
    }

    /// <summary>
    /// Checks if a box overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the box.</param>
    /// <param name="size">Size of the box (width, height, depth).</param>
    /// <param name="orientation">Orientation of the box.</param>
    /// <returns>True if the box overlaps with any collider.</returns>
    public bool CheckBox(Float3 position, Float3 size, Quaternion orientation)
    {
        return CheckBox(position, size, orientation, QueryFilter.Default);
    }

    /// <summary>
    /// Checks if a box overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckBox(Float3 position, Float3 size, Quaternion orientation, QueryFilter filter)
    {
        List<ShapeCastHit> hits = _queryHits;
        return OverlapBox(position, size, orientation, hits, filter) > 0;
    }

    /// <summary>
    /// Checks if a cylinder overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the cylinder.</param>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="height">Height of the cylinder.</param>
    /// <param name="orientation">Orientation of the cylinder.</param>
    /// <returns>True if the cylinder overlaps with any collider.</returns>
    public bool CheckCylinder(Float3 position, float radius, float height, Quaternion orientation)
    {
        return CheckCylinder(position, radius, height, orientation, QueryFilter.Default);
    }

    /// <summary>
    /// Checks if a cylinder overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckCylinder(Float3 position, float radius, float height, Quaternion orientation, QueryFilter filter)
    {
        List<ShapeCastHit> hits = _queryHits;
        return OverlapCylinder(position, radius, height, orientation, hits, filter) > 0;
    }

    /// <summary>
    /// Checks if a cone overlaps with any colliders.
    /// </summary>
    /// <param name="position">Center position of the cone.</param>
    /// <param name="radius">Base radius of the cone.</param>
    /// <param name="height">Height of the cone.</param>
    /// <param name="orientation">Orientation of the cone.</param>
    /// <returns>True if the cone overlaps with any collider.</returns>
    public bool CheckCone(Float3 position, float radius, float height, Quaternion orientation)
    {
        return CheckCone(position, radius, height, orientation, QueryFilter.Default);
    }

    /// <summary>
    /// Checks if a cone overlaps with any colliders with layer filtering.
    /// </summary>
    public bool CheckCone(Float3 position, float radius, float height, Quaternion orientation, QueryFilter filter)
    {
        List<ShapeCastHit> hits = _queryHits;
        return OverlapCone(position, radius, height, orientation, hits, filter) > 0;
    }

    #endregion

    #region Terrain Collision

    /// <summary>
    /// Registers a terrain collider with the physics world.
    /// </summary>
    /// <param name="heightmapProxy">The terrain heightmap proxy for raycasting.</param>
    /// <param name="collisionFilter">The terrain collision filter for broad phase collision detection.</param>
    /// <param name="heightProvider">Sampler for terrain heights and the live grid placement.</param>
    public void RegisterTerrain(TerrainHeightmapProxy heightmapProxy, TerrainCollisionFilter collisionFilter,
        ITerrainHeightProvider heightProvider)
    {
        if (heightmapProxy == null || collisionFilter == null)
            return;

        World.DynamicTree.AddProxy(heightmapProxy, false);
        _compositeBroadPhaseFilter.AddFilter(collisionFilter);

        _terrainProxies[heightmapProxy] = heightProvider;
    }

    /// <summary>
    /// Re-fits a registered terrain's broad phase bounds after its transform changed. The grid itself is
    /// sampled live, so only the dynamic tree needs telling.
    /// </summary>
    public void RefreshTerrain(TerrainHeightmapProxy heightmapProxy)
    {
        if (heightmapProxy == null || heightmapProxy.SetIndex == -1)
            return;

        World.DynamicTree.Update(heightmapProxy);
    }

    /// <summary>
    /// Unregisters a terrain collider from the physics world.
    /// </summary>
    /// <param name="heightmapProxy">The terrain heightmap proxy to remove.</param>
    /// <param name="collisionFilter">The terrain collision filter to remove.</param>
    public void UnregisterTerrain(TerrainHeightmapProxy heightmapProxy, TerrainCollisionFilter collisionFilter)
    {
        if (heightmapProxy == null || collisionFilter == null)
            return;

        _terrainProxies.Remove(heightmapProxy);

        if (heightmapProxy.SetIndex != -1)
        {
            World.DynamicTree.RemoveProxy(heightmapProxy);
        }

        // Remove the terrain collision filter from the composite filter
        _compositeBroadPhaseFilter.RemoveFilter(collisionFilter);
    }

    #endregion
}
