// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.Utils;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for Prowl's integration with the Jitter2 physics engine. These are not testing Jitter2 itself
/// (gravity, the solver, etc. are assumed correct) but the wiring Prowl puts on top: colliders building
/// and registering the right shapes, the Rigidbody3D component creating/removing/syncing its body,
/// trigger volumes raising events, layer assignment, and collision filtering.
/// </summary>
public class PhysicsTests : RuntimeTestBase
{
    // The Mass setter must validate the incoming value (not the backing field); zero/negative mass
    // would produce a NaN inverse mass in the solver.
    [Fact]
    public void Rigidbody3D_Mass_RejectsZeroAndNegative()
    {
        var rb = new Rigidbody3D();
        Assert.Throws<ArgumentException>(() => rb.Mass = 0f);
        Assert.Throws<ArgumentException>(() => rb.Mass = -5f);
    }

    public override void Dispose()
    {
        // CollisionMatrix is global static state. Boolean32Matrix is a struct wrapping a uint[], so a
        // plain copy would alias the live array; reset to the engine default (all layers collide) instead.
        CollisionMatrix.s_collisionMatrix = new Boolean32Matrix(true);
        base.Dispose();
    }

    private Scene CreatePhysicsScene()
    {
        var scene = CreateScene(enable: true);
        scene.Physics.UseMultithreading = false; // deterministic stepping
        return scene;
    }

    private Rigidbody3D AddDynamicBox(Scene scene, Float3 position, bool gravity = true, int layer = 0)
    {
        var go = CreateGameObject("DynamicBox");
        go.Transform.Position = position;
        go.LayerIndex = layer;
        var rb = go.AddComponent<Rigidbody3D>();
        rb.AffectedByGravity = gravity;
        go.AddComponent<BoxCollider>();
        scene.Add(go);
        return rb;
    }

    private GameObject AddStaticBox(Scene scene, Float3 position, Float3 size, int layer = 0)
    {
        var go = CreateGameObject("StaticBox");
        go.Transform.Position = position;
        go.LayerIndex = layer;
        go.AddComponent<BoxCollider>().Size = size;
        scene.Add(go);
        return go;
    }

    private static LayerMask OnlyLayer(int index)
    {
        var mask = new LayerMask();
        mask.SetLayer(index);
        return mask;
    }

    // ---------------------------------------------------------------------
    // Rigidbody3D <-> body lifecycle and transform sync
    // ---------------------------------------------------------------------

    [Fact]
    public void Rigidbody_CreatesQueryableBody_OnEnable()
    {
        var scene = CreatePhysicsScene();
        AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        StepPhysics(scene);

        Assert.True(scene.Physics.CheckSphere(new Float3(0, 0, 0), 0.4f));
    }

    // With AutoSyncTransforms on (default), a Transform edit is pushed into the body so a query made
    // right after sees the new pose.
    [Fact]
    public void MovingRigidbodyTransform_IsSeenByQueries_WhenAutoSyncOn()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        StepPhysics(scene);
        Assert.True(scene.Physics.CheckSphere(new Float3(0, 0, 0), 0.4f));

        rb.Transform.Position = new Float3(100, 0, 0);

        Assert.False(scene.Physics.CheckSphere(new Float3(0, 0, 0), 0.4f), "body should no longer be at the origin");
        Assert.True(scene.Physics.CheckSphere(new Float3(100, 0, 0), 0.4f), "body should be at the new position");
    }

    // With AutoSyncTransforms off, queries don't auto-sync; the edit is only visible after a manual
    // SyncTransforms() (or the next FixedUpdate pre-step sync).
    [Fact]
    public void MovingRigidbodyTransform_NeedsManualSync_WhenAutoSyncOff()
    {
        var scene = CreatePhysicsScene();
        scene.Physics.AutoSyncTransforms = false;
        var rb = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        StepPhysics(scene);

        rb.Transform.Position = new Float3(100, 0, 0);
        Assert.True(scene.Physics.CheckSphere(new Float3(0, 0, 0), 0.4f), "query should still see the old pose (no auto-sync)");

        scene.Physics.SyncTransforms();
        Assert.True(scene.Physics.CheckSphere(new Float3(100, 0, 0), 0.4f), "manual sync should push the new pose");
    }

    [Fact]
    public void Rigidbody_RemovesBody_OnDisable()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        StepPhysics(scene);
        Assert.True(scene.Physics.CheckSphere(new Float3(0, 0, 0), 0.4f));

        rb.GameObject.Enabled = false;
        StepPhysics(scene);

        Assert.False(scene.Physics.CheckSphere(new Float3(0, 0, 0), 0.4f));
    }

    [Fact]
    public void Rigidbody_SyncsTransformFromBody_AfterStep()
    {
        // Prowl's Rigidbody3D.Update copies the simulated body pose back onto the Transform.
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, new Float3(0, 10, 0));

        Tick(scene, 30);

        Assert.True(rb.Transform.Position.Y < 10.0,
            $"Transform.Y should have followed the falling body, was {rb.Transform.Position.Y}");
    }

    [Fact]
    public void Rigidbody_MovePosition_TeleportsBody_AndShapeFollows()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        StepPhysics(scene);
        Assert.True(scene.Physics.CheckSphere(new Float3(0, 0, 0), 0.4f));

        rb.MovePosition(new Float3(10, 0, 0));
        Tick(scene);

        Assert.False(scene.Physics.CheckSphere(new Float3(0, 0, 0), 0.4f));
        Assert.True(scene.Physics.CheckSphere(new Float3(10, 0, 0), 0.4f));
        Assert.Equal(10.0, rb.Transform.Position.X, 2);
    }

    [Fact]
    public void Rigidbody_InitialTransformPosition_PlacesBody()
    {
        // AutoSyncTransforms: the body is created at the GameObject's transform position.
        var scene = CreatePhysicsScene();
        AddDynamicBox(scene, new Float3(5, 0, 0), gravity: false);
        StepPhysics(scene);

        Assert.True(scene.Physics.CheckSphere(new Float3(5, 0, 0), 0.4f));
        Assert.False(scene.Physics.CheckSphere(new Float3(0, 0, 0), 0.4f));
    }

    // ---------------------------------------------------------------------
    // Colliders build and register the correct shapes
    // ---------------------------------------------------------------------

    [Fact]
    public void BoxCollider_RegistersShape()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, Float3.Zero, new Float3(2, 2, 2));
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(Float3.Zero, 0.3f));
    }

    [Fact]
    public void SphereCollider_RegistersShape()
    {
        var scene = CreatePhysicsScene();
        var go = CreateGameObject();
        go.AddComponent<SphereCollider>().Radius = 1f;
        scene.Add(go);
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(Float3.Zero, 0.3f));
    }

    [Fact]
    public void CapsuleCollider_RegistersShape()
    {
        var scene = CreatePhysicsScene();
        var go = CreateGameObject();
        go.AddComponent<CapsuleCollider>();
        scene.Add(go);
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(Float3.Zero, 0.2f));
    }

    [Fact]
    public void CylinderCollider_RegistersShape()
    {
        var scene = CreatePhysicsScene();
        var go = CreateGameObject();
        go.AddComponent<CylinderCollider>();
        scene.Add(go);
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(Float3.Zero, 0.2f));
    }

    [Fact]
    public void ConeCollider_RegistersShape()
    {
        var scene = CreatePhysicsScene();
        var go = CreateGameObject();
        go.AddComponent<ConeCollider>();
        scene.Add(go);
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(Float3.Zero, 0.2f));
    }

    [Fact]
    public void BoxCollider_Size_DeterminesExtent()
    {
        var scene = CreatePhysicsScene();
        var go = CreateGameObject();
        go.AddComponent<BoxCollider>().Size = new Float3(1, 1, 1); // half-extents 0.5
        scene.Add(go);
        StepPhysics(scene, 2);

        // Just inside the +X face (0.45), then just outside it (0.65). The outside probe is close to
        // the true 0.5 face so a 2x-extent bug (treating Size as half-extent) would be caught -
        // a box reaching to 1.0 would (wrongly) report the 0.65 probe as inside.
        Assert.True(scene.Physics.CheckSphere(new Float3(0.4f, 0, 0), 0.05f));
        Assert.False(scene.Physics.CheckSphere(new Float3(0.7f, 0, 0), 0.05f));
    }

    [Fact]
    public void Collider_Center_OffsetsShape()
    {
        var scene = CreatePhysicsScene();
        var go = CreateGameObject();
        var box = go.AddComponent<BoxCollider>();
        box.Size = new Float3(0.5f, 0.5f, 0.5f);
        box.Center = new Float3(3, 0, 0);
        scene.Add(go);
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(new Float3(3, 0, 0), 0.1f));
        Assert.False(scene.Physics.CheckSphere(Float3.Zero, 0.1f));
    }

    [Fact]
    public void Collider_WithoutRigidbody_IsStaticAndQueryable()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, 0, 0), new Float3(2, 2, 2));
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.Raycast(new Float3(0, 5, 0), new Float3(0, -1, 0), 10f, out RaycastHit hit));
        Assert.True(hit.Distance > 0);
    }

    [Fact]
    public void Collider_OnRigidbody_MovesWithBody()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, Float3.Zero, gravity: false);
        StepPhysics(scene);

        rb.MovePosition(new Float3(0, 8, 0));
        Tick(scene);

        Assert.False(scene.Physics.CheckSphere(Float3.Zero, 0.4f));
        Assert.True(scene.Physics.CheckSphere(new Float3(0, 8, 0), 0.4f));
    }

    [Fact]
    public void CompoundColliders_BothShapesRegisterOnOneBody()
    {
        var scene = CreatePhysicsScene();
        var go = CreateGameObject();
        var rb = go.AddComponent<Rigidbody3D>();
        rb.AffectedByGravity = false;

        var left = go.AddComponent<BoxCollider>();
        left.Size = new Float3(0.5f, 0.5f, 0.5f);
        left.Center = new Float3(-2, 0, 0);

        var right = go.AddComponent<BoxCollider>();
        right.Size = new Float3(0.5f, 0.5f, 0.5f);
        right.Center = new Float3(2, 0, 0);

        scene.Add(go);
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(new Float3(-2, 0, 0), 0.1f));
        Assert.True(scene.Physics.CheckSphere(new Float3(2, 0, 0), 0.1f));
        Assert.False(scene.Physics.CheckSphere(Float3.Zero, 0.1f)); // gap between the two boxes
    }

    // ---------------------------------------------------------------------
    // MeshCollider
    // ---------------------------------------------------------------------

    [Fact]
    public void MeshCollider_Concave_RegistersTriangleMesh()
    {
        // Concave mesh colliders build per-triangle shapes (no volume), so the meaningful check is that
        // a dynamic body collides with the triangle surface instead of falling through it.
        var scene = CreatePhysicsScene();
        var floor = CreateGameObject("MeshFloor");
        var mc = floor.AddComponent<MeshCollider>();
        mc.Mesh = Mesh.CreateCube(new Float3(20, 1, 20)); // top at y=0.5
        mc.Convex = false;
        scene.Add(floor);

        var body = AddDynamicBox(scene, new Float3(0, 3, 0), gravity: true);

        Tick(scene, 180);

        Assert.True(body.Transform.Position.Y > 0,
            $"Body should rest on the concave mesh, was at y={body.Transform.Position.Y}");
    }

    // Jitter drops degenerate triangles while baking, so the shape count has to come from the baked
    // mesh and not from the source triangle soup - indexing by the soup count runs off the end.
    [Fact]
    public void MeshCollider_Concave_MeshWithDegenerateTriangle_BuildsShapes()
    {
        var mesh = new Mesh
        {
            Vertices = [new Float3(0, 0, 0), new Float3(1, 0, 0), new Float3(0, 0, 1), new Float3(2, 0, 0)]
        };
        mesh.Indices = [0, 1, 2, 0, 0, 3]; // the second triangle has zero area

        var go = CreateGameObject("DegenerateMesh");
        var mc = go.AddComponent<MeshCollider>();
        mc.Mesh = mesh;
        mc.Convex = false;

        var shapes = mc.CreateShapes();

        Assert.NotNull(shapes);
        Assert.Single(shapes);
    }

    [Fact]
    public void MeshCollider_Convex_RegistersHull()
    {
        var scene = CreatePhysicsScene();
        var go = CreateGameObject();
        var mc = go.AddComponent<MeshCollider>();
        mc.Mesh = Mesh.CreateCube(Float3.One);
        mc.Convex = true;
        scene.Add(go);
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(Float3.Zero, 0.2f));
    }

    [Fact]
    public void MeshCollider_Convex_OnDynamicBody_RestsOnFloor()
    {
        // A convex mesh collider has volume, so it can drive a dynamic rigidbody (mass/inertia work).
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, 0, 0), new Float3(20, 1, 20)); // floor top at y=0.5

        var go = CreateGameObject("DynamicMesh");
        go.Transform.Position = new Float3(0, 3, 0);
        var rb = go.AddComponent<Rigidbody3D>();
        var mc = go.AddComponent<MeshCollider>();
        mc.Mesh = Mesh.CreateCube(Float3.One);
        mc.Convex = true;
        scene.Add(go);

        Tick(scene, 180);

        Assert.True(rb.Transform.Position.Y > 0,
            $"Dynamic convex-mesh body should rest on the floor, was at y={rb.Transform.Position.Y}");
    }

    [Fact]
    public void MeshCollider_Concave_OnDynamicBody_UsesBoxInertiaFallback()
    {
        // Concave TriangleShapes have no volume, so the body's inertia falls back to a solid-box
        // approximation from the mesh AABB (instead of a meaningless identity tensor) and must not throw.
        var scene = CreatePhysicsScene();
        var go = CreateGameObject("ConcaveDynamic");
        go.Transform.Position = new Float3(0, 5, 0);
        var rb = go.AddComponent<Rigidbody3D>();
        rb.Mass = 2f;
        var mc = go.AddComponent<MeshCollider>();
        mc.Mesh = Mesh.CreateCube(Float3.One); // unit cube, AABB size 1 on each axis
        mc.Convex = false;
        scene.Add(go);

        Tick(scene, 5);

        // Solid box about its centre for a unit cube of mass 2 is I = (1/12)*2*(1+1) = 1/3 per axis.
        // Jitter's shape AABBs carry a small collision margin so the value lands a touch above 1/3; the
        // important thing is it's the box approximation, not the identity (1.0) tensor it replaced.
        Float3 inertia = rb.InertiaTensor;
        Assert.True(inertia.X is > 0.25f and < 0.45f, $"inertia.X={inertia.X}");
        Assert.True(inertia.Y is > 0.25f and < 0.45f, $"inertia.Y={inertia.Y}");
        Assert.True(inertia.Z is > 0.25f and < 0.45f, $"inertia.Z={inertia.Z}");
    }

    // ---------------------------------------------------------------------
    // Trigger volumes
    // ---------------------------------------------------------------------

    // Records the trigger callbacks now delivered as MonoBehaviour overrides. Lives on the trigger's GameObject.
    private sealed class TriggerRecorder : MonoBehaviour
    {
        public readonly List<Rigidbody3D> Entered = new();
        public readonly List<Rigidbody3D> Exited = new();
        public int StayCount;

        public override void OnTriggerEnter(Rigidbody3D other) => Entered.Add(other);
        public override void OnTriggerStay(Rigidbody3D other) => StayCount++;
        public override void OnTriggerExit(Rigidbody3D other) => Exited.Add(other);
    }

    private TriggerVolume AddBoxTrigger(Scene scene, Float3 position, Float3 size)
    {
        var go = CreateGameObject("Trigger");
        go.Transform.Position = position;
        var trigger = go.AddComponent<TriggerVolume>();
        trigger.Shape = TriggerShape.Box;
        trigger.Size = size;
        go.AddComponent<TriggerRecorder>();
        scene.Add(go);
        return trigger;
    }

    private static TriggerRecorder Recorder(TriggerVolume trigger) => trigger.GetComponent<TriggerRecorder>()!;

    [Fact]
    public void Trigger_Entered_FiresForRigidbodyInside()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        var rb = AddDynamicBox(scene, Float3.Zero, gravity: false);

        StepPhysics(scene);

        Assert.Contains(rb, Recorder(trigger).Entered);
    }

    [Fact]
    public void Trigger_Staying_FiresOnSubsequentSteps()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        var rb = AddDynamicBox(scene, Float3.Zero, gravity: false);

        StepPhysics(scene);   // Entered
        StepPhysics(scene);   // Staying
        StepPhysics(scene);   // Staying

        Assert.Equal(2, Recorder(trigger).StayCount);
    }

    [Fact]
    public void Trigger_Exited_FiresWhenBodyLeaves()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(2, 2, 2));
        var rb = AddDynamicBox(scene, Float3.Zero, gravity: false);

        StepPhysics(scene); // Entered
        rb.MovePosition(new Float3(50, 0, 0));
        StepPhysics(scene); // Exited

        Assert.Contains(rb, Recorder(trigger).Exited);
    }

    [Fact]
    public void Trigger_IgnoresStaticColliders()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        AddStaticBox(scene, Float3.Zero, new Float3(1, 1, 1)); // no Rigidbody3D

        StepPhysics(scene);

        Assert.Empty(Recorder(trigger).Entered);
        Assert.Empty(trigger.Overlapping);
    }

    [Fact]
    public void Trigger_LayerMask_FiltersBodies()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        trigger.LayerMask = OnlyLayer(5);
        AddDynamicBox(scene, Float3.Zero, gravity: false, layer: 3); // not in the mask

        StepPhysics(scene);

        Assert.Empty(Recorder(trigger).Entered);
    }

    [Fact]
    public void Trigger_FiresExit_OnDisable()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        var rb = AddDynamicBox(scene, Float3.Zero, gravity: false);

        StepPhysics(scene); // Entered, now occupant
        trigger.Enabled = false;

        Assert.Contains(rb, Recorder(trigger).Exited);
    }

    // ---------------------------------------------------------------------
    // Layer assignment and collision filtering
    // ---------------------------------------------------------------------

    [Fact]
    public void Raycast_LayerMask_RespectsGameObjectLayer()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, Float3.Zero, new Float3(2, 2, 2), layer: 5);
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.Raycast(new Float3(0, 5, 0), new Float3(0, -1, 0), 10f, OnlyLayer(5)));
        Assert.False(scene.Physics.Raycast(new Float3(0, 5, 0), new Float3(0, -1, 0), 10f, OnlyLayer(3)));
    }

    [Fact]
    public void CollisionMatrix_DisabledLayers_DynamicPassesThroughStatic()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, 0, 0), new Float3(20, 1, 20), layer: 1); // floor, top at y=0.5
        var top = AddDynamicBox(scene, new Float3(0, 3, 0), gravity: true, layer: 2);

        CollisionMatrix.SetLayerCollision(1, 2, false);

        Tick(scene, 180);

        Assert.True(top.Transform.Position.Y < 0,
            $"Body on a non-colliding layer should have passed through the floor, was at y={top.Transform.Position.Y}");
    }

    [Fact]
    public void CollisionMatrix_EnabledLayers_DynamicRestsOnStatic()
    {
        // Control for the previous test: with collision enabled (default) the body rests on the floor.
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, 0, 0), new Float3(20, 1, 20), layer: 1);
        var top = AddDynamicBox(scene, new Float3(0, 3, 0), gravity: true, layer: 2);

        Tick(scene, 180);

        Assert.True(top.Transform.Position.Y > 0.5,
            $"Body should rest on the floor, was at y={top.Transform.Position.Y}");
    }

    [Fact]
    public void IgnoreCollisionBetween_BodiesDoNotCollide()
    {
        var scene = CreatePhysicsScene();

        // Static floor as a Rigidbody3D so it can be referenced in the ignore pair.
        var floorGo = CreateGameObject("Floor");
        var floorRb = floorGo.AddComponent<Rigidbody3D>();
        floorRb.MotionType = Jitter2.Dynamics.MotionType.Static;
        floorGo.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
        scene.Add(floorGo);

        var top = AddDynamicBox(scene, new Float3(0, 3, 0), gravity: true);

        scene.Physics.IgnoreCollisionBetween(top, floorRb);

        Tick(scene, 180);

        Assert.True(top.Transform.Position.Y < 0,
            $"Ignored pair should not collide; body was at y={top.Transform.Position.Y}");
    }

    [Fact]
    public void IgnoredCollisions_AreScopedToTheirOwnWorld()
    {
        var sceneA = CreatePhysicsScene();
        var floorA = CreateGameObject("Floor");
        var floorRbA = floorA.AddComponent<Rigidbody3D>();
        floorRbA.MotionType = Jitter2.Dynamics.MotionType.Static;
        floorA.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
        sceneA.Add(floorA);

        var boxA = AddDynamicBox(sceneA, new Float3(0, 3, 0), gravity: true);
        sceneA.Physics.IgnoreCollisionBetween(boxA, floorRbA);

        // A second world must not inherit the first world's ignore pairs.
        var sceneB = CreatePhysicsScene();
        var floorB = CreateGameObject("Floor");
        var floorRbB = floorB.AddComponent<Rigidbody3D>();
        floorRbB.MotionType = Jitter2.Dynamics.MotionType.Static;
        floorB.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
        sceneB.Add(floorB);

        var boxB = AddDynamicBox(sceneB, new Float3(0, 3, 0), gravity: true);

        Tick(sceneB, 180);

        Assert.True(boxB.Transform.Position.Y > 0,
            $"Pair ignored in another world should still collide here; body was at y={boxB.Transform.Position.Y}");
    }

    /// <summary>Records the collisions it is told about, so the payload can be asserted.</summary>
    private sealed class CollisionRecorder : MonoBehaviour
    {
        public readonly List<Collision> Begins = [];
        public readonly List<Collision> Ends = [];

        public override void OnCollisionBegin(Collision collision) => Begins.Add(collision);
        public override void OnCollisionEnd(Collision collision) => Ends.Add(collision);
    }

    // Static colliders share one body per layer, so the body cannot name what was hit and the contact
    // used to be dropped entirely. It now reports the Collider that owns the shape.
    [Fact]
    public void CollisionBegin_FiresAgainstStaticGeometry_AndNamesTheCollider()
    {
        var scene = CreatePhysicsScene();
        GameObject floor = AddStaticBox(scene, new Float3(0, -1, 0), new Float3(20, 1, 20));

        var rb = AddDynamicBox(scene, new Float3(0, 2, 0), gravity: true);
        var recorder = rb.GameObject.AddComponent<CollisionRecorder>();

        Tick(scene, 180);

        Assert.NotEmpty(recorder.Begins);

        Collision hit = recorder.Begins[0];
        Assert.Null(hit.Rigidbody);                     // static geometry has no rigidbody of its own
        Assert.NotNull(hit.Collider);
        Assert.Same(floor, hit.Collider.GameObject);
        Assert.Same(floor, hit.GameObject);
    }

    [Fact]
    public void CollisionEnd_AgainstStaticGeometry_StillNamesTheCollider()
    {
        var scene = CreatePhysicsScene();
        GameObject floor = AddStaticBox(scene, new Float3(0, -1, 0), new Float3(20, 1, 20));

        var rb = AddDynamicBox(scene, new Float3(0, 2, 0), gravity: true);
        var recorder = rb.GameObject.AddComponent<CollisionRecorder>();

        Tick(scene, 180);
        Assert.NotEmpty(recorder.Begins);

        // Fling it off the floor so the contact breaks.
        rb.AffectedByGravity = false;
        rb.LinearVelocity = new Float3(0, 40, 0);
        Tick(scene, 60);

        Assert.NotEmpty(recorder.Ends);
        Assert.Same(floor, recorder.Ends[0].Collider.GameObject);
    }

    // ---------------------------------------------------------------------
    // Collider shape rebuilds
    // ---------------------------------------------------------------------

    [Fact]
    public void Collider_Center_RebuildsShapes_WhenSetAtRuntime()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, Float3.Zero, new Float3(1, 1, 1));
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(Float3.Zero, 0.2f));

        Collider collider = scene.AllObjects.First(o => o.Name == "StaticBox").GetComponent<BoxCollider>();
        collider.Center = new Float3(10, 0, 0);
        StepPhysics(scene, 2);

        Assert.False(scene.Physics.CheckSphere(Float3.Zero, 0.2f), "the shape should have moved off the origin");
        Assert.True(scene.Physics.CheckSphere(new Float3(10, 0, 0), 0.2f), "the shape should be at the new centre");
    }

    [Fact]
    public void Collider_OnRigidbody_RebuildsWhenMovedRelativeToTheBody()
    {
        var scene = CreatePhysicsScene();

        var bodyGo = CreateGameObject("Body");
        var rb = bodyGo.AddComponent<Rigidbody3D>();
        rb.AffectedByGravity = false;

        var colliderGo = CreateGameObject("Child");
        colliderGo.AddComponent<BoxCollider>();
        colliderGo.SetParent(bodyGo);
        scene.Add(bodyGo);
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.CheckSphere(Float3.Zero, 0.2f));

        colliderGo.Transform.LocalPosition = new Float3(0, 8, 0);
        Tick(scene, 2);

        Assert.True(scene.Physics.CheckSphere(new Float3(0, 8, 0), 0.3f),
            "the shape should have followed the collider's new offset from the body");
    }

    // ---------------------------------------------------------------------
    // Rigidbody3D surface: force modes and axis constraints
    // ---------------------------------------------------------------------

    // Acceleration and VelocityChange ignore mass, so a heavy and a light body must respond identically.
    [Theory]
    [InlineData(ForceMode.Acceleration)]
    [InlineData(ForceMode.VelocityChange)]
    public void MassIndependentForceModes_MoveHeavyAndLightBodiesAlike(ForceMode mode)
    {
        var scene = CreatePhysicsScene();
        var light = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        var heavy = AddDynamicBox(scene, new Float3(10, 0, 0), gravity: false);
        StepPhysics(scene, 1);
        heavy.Mass = 100f;

        light.AddForce(new Float3(0, 0, 5), mode);
        heavy.AddForce(new Float3(0, 0, 5), mode);
        Tick(scene, 20);

        Assert.Equal(light.Transform.Position.Z, heavy.Transform.Position.Z, 2);
    }

    // Force and Impulse scale with mass, so the heavy body must lag behind.
    [Theory]
    [InlineData(ForceMode.Force)]
    [InlineData(ForceMode.Impulse)]
    public void MassDependentForceModes_MoveHeavyBodiesLess(ForceMode mode)
    {
        var scene = CreatePhysicsScene();
        var light = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        var heavy = AddDynamicBox(scene, new Float3(10, 0, 0), gravity: false);
        StepPhysics(scene, 1);
        heavy.Mass = 100f;

        light.AddForce(new Float3(0, 0, 5), mode);
        heavy.AddForce(new Float3(0, 0, 5), mode);
        Tick(scene, 20);

        Assert.True(light.Transform.Position.Z > heavy.Transform.Position.Z * 5f,
            $"light moved {light.Transform.Position.Z}, heavy moved {heavy.Transform.Position.Z}");
    }

    [Fact]
    public void FreezePosition_PinsTheFrozenAxis_AndLeavesOthersFree()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, new Float3(0, 5, 0), gravity: true);
        rb.Constraints = RigidbodyConstraints.FreezePositionY;

        rb.AddForce(new Float3(0, 0, 3), ForceMode.VelocityChange);
        Tick(scene, 60);

        Assert.Equal(5.0, rb.Transform.Position.Y, 2);
        Assert.True(rb.Transform.Position.Z > 0.5f, "the unfrozen axis should still move");
    }

    [Fact]
    public void FreezeRotation_KeepsTheBodyUpright()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        rb.Constraints = RigidbodyConstraints.FreezeRotation;

        rb.AddTorque(new Float3(4, 4, 4), ForceMode.VelocityChange);
        Tick(scene, 60);

        Quaternion rotation = rb.Transform.Rotation;
        Assert.Equal(1.0, Maths.Abs(rotation.W), 3);
    }

    // ---------------------------------------------------------------------
    // Query API: filters, all-hits, linecast
    // ---------------------------------------------------------------------

    [Fact]
    public void RaycastAll_ReturnsEveryHit_NearestFirst()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, 0, 0), new Float3(2, 1, 2));
        AddStaticBox(scene, new Float3(0, -4, 0), new Float3(2, 1, 2));
        AddStaticBox(scene, new Float3(0, -8, 0), new Float3(2, 1, 2));
        StepPhysics(scene, 2);

        var hits = new List<RaycastHit>();
        int count = scene.Physics.RaycastAll(new Float3(0, 5, 0), new Float3(0, -1, 0), 50f, hits);

        Assert.Equal(3, count);
        Assert.True(hits[0].Distance <= hits[1].Distance && hits[1].Distance <= hits[2].Distance,
            "hits should be sorted nearest first");
    }

    [Fact]
    public void QueryFilter_IgnoringRigidbody_SkipsThatBody()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        StepPhysics(scene, 2);

        var from = new Float3(0, 5, 0);
        var dir = new Float3(0, -1, 0);

        Assert.True(scene.Physics.Raycast(from, dir, 50f, out _));
        Assert.False(scene.Physics.Raycast(from, dir, out _, 50f, QueryFilter.Default.Ignoring(rb)),
            "the ignored body should not be reported");
    }

    [Fact]
    public void QueryFilter_IgnoringCollider_SkipsStaticGeometry()
    {
        var scene = CreatePhysicsScene();
        GameObject floor = AddStaticBox(scene, new Float3(0, 0, 0), new Float3(4, 1, 4));
        StepPhysics(scene, 2);

        var from = new Float3(0, 5, 0);
        var dir = new Float3(0, -1, 0);

        Assert.True(scene.Physics.Raycast(from, dir, 50f, out _));

        Collider collider = floor.GetComponent<BoxCollider>();
        Assert.False(scene.Physics.Raycast(from, dir, out _, 50f, QueryFilter.Default.Ignoring(collider)),
            "the ignored collider should not be reported");
    }

    [Fact]
    public void Linecast_HitsOnlyBetweenTheTwoPoints()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, 0, 0), new Float3(2, 1, 2));
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.Linecast(new Float3(0, 5, 0), new Float3(0, -5, 0), out _));
        Assert.False(scene.Physics.Linecast(new Float3(0, 5, 0), new Float3(0, 3, 0), out _),
            "a line stopping short of the box should not hit it");
    }

    [Fact]
    public void ShapeCastAll_ReturnsHitsNearestFirst()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, 0, 0), new Float3(2, 1, 2));
        AddStaticBox(scene, new Float3(0, -6, 0), new Float3(2, 1, 2));
        StepPhysics(scene, 2);

        var hits = new List<ShapeCastHit>();
        int count = scene.Physics.SphereCastAll(new Float3(0, 6, 0), 0.4f, new Float3(0, -1, 0), 50f, hits);

        Assert.Equal(2, count);
        Assert.True(hits[0].Fraction <= hits[1].Fraction, "hits should be sorted nearest first");
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    public void Queries_WithNonFiniteInputs_ReportNoHitInsteadOfThrowing(float x, float y, float z)
    {
        var scene = CreatePhysicsScene();
        AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        StepPhysics(scene, 1);

        var bad = new Float3(x, y, z);
        var hits = new List<ShapeCastHit>();

        Assert.False(scene.Physics.Raycast(bad, new Float3(0, -1, 0), 10f, out _));
        Assert.False(scene.Physics.Raycast(Float3.Zero, bad, 10f, out _));
        Assert.Equal(0, scene.Physics.SphereCastAll(bad, 0.5f, new Float3(0, -1, 0), 10f, hits));
        Assert.False(scene.Physics.SphereCast(Float3.Zero, 0.5f, bad, 10f, out _));
        Assert.Equal(0, scene.Physics.OverlapSphere(bad, 0.5f, hits));
        Assert.False(scene.Physics.CheckSphere(bad, 0.5f));
    }
}
