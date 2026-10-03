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

    private static void SetField(Rigidbody3D rb, string name, float value)
        => typeof(Rigidbody3D).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(rb, value);

    // The inspector writes the fields directly, skipping the setters' checks, then calls OnValidate.
    [Fact]
    public void Rigidbody3D_OutOfRangeFields_AreClampedOnValidate()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, Float3.Zero);

        SetField(rb, "angularDamping", 1.5f);
        SetField(rb, "linearDamping", -1f);
        SetField(rb, "restitution", 3f);
        SetField(rb, "friction", -2f);
        SetField(rb, "deactivationTime", -1f);
        SetField(rb, "linearSleepThreshold", -1f);
        SetField(rb, "mass", 0f);
        rb.OnValidate();

        Assert.Equal(1f, rb.AngularDamping);
        Assert.Equal(0f, rb.LinearDamping);
        Assert.Equal(1f, rb.Restitution);
        Assert.Equal(0f, rb.Friction);
        Assert.Equal(0f, rb.DeactivationTime);
        Assert.Equal(0f, rb.LinearSleepThreshold);
        Assert.True(rb.Mass > 0f);
        Assert.Equal((0f, 1f), ((float)rb.Native!.Damping.linear, (float)rb.Native.Damping.angular));
    }

    [Fact]
    public void Rigidbody3D_SleepThresholds_ReachTheMatchingAxis()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, Float3.Zero);

        SetField(rb, "linearSleepThreshold", 0.25f);
        SetField(rb, "angularSleepThreshold", 0.75f);
        rb.OnValidate();
        Assert.Equal(0.25f, (float)rb.Native!.DeactivationThreshold.linear, 4);
        Assert.Equal(0.75f, (float)rb.Native.DeactivationThreshold.angular, 4);

        rb.LinearSleepThreshold = 0.5f;
        rb.AngularSleepThreshold = 1.5f;
        Assert.Equal(0.5f, (float)rb.Native.DeactivationThreshold.linear, 4);
        Assert.Equal(1.5f, (float)rb.Native.DeactivationThreshold.angular, 4);
    }

    public override void Dispose()
    {
        // CollisionMatrix is global static state, so put it back to the engine default between tests.
        CollisionMatrix.Reset();
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
        var mask = LayerMask.Nothing;
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

    [Theory]
    [InlineData(50f)]
    [InlineData(3000f)]
    public void Raycast_OnAPlacedBox_IsExactWhateverItsSize(float size)
    {
        var scene = CreatePhysicsScene();
        var box = CreateGameObject("PlacedBox");
        box.Transform.Position = new Float3(0, -1, 0);
        box.Transform.Rotation = Quaternion.FromEuler(new Float3(10f, 0f, 5f));
        box.Transform.LocalScale = new Float3(1f, 2f, 1f);
        box.AddComponent<BoxCollider>().Size = new Float3(size, 1, size);
        scene.Add(box);
        StepPhysics(scene, 2);

        Float3 up = box.Transform.Rotation * Float3.UnitY;
        Float3 top = box.Transform.Position + up;
        var random = new System.Random(3);
        for (int i = 0; i < 200; i++)
        {
            var origin = new Float3(random.NextSingle() * 20f - 10f, 3f, random.NextSingle() * 20f - 10f);
            var down = new Float3(0f, -1f, 0f);
            Assert.True(scene.Physics.Raycast(origin, down, 10f, out RaycastHit hit));

            float expected = Float3.Dot(top - origin, up) / Float3.Dot(down, up);
            Assert.InRange(hit.Distance, expected - 1e-3f, expected + 1e-3f);
            Assert.True(Float3.Dot(hit.Normal, up) > 0.9999f, $"normal {hit.Normal} should be the top face's {up}");
        }
    }

    [Fact]
    public void Raycast_StartingInsideAPlacedBox_HasNoNaNNormal()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(3, 0, 0), new Float3(2, 2, 2));
        StepPhysics(scene, 2);

        Assert.True(scene.Physics.Raycast(new Float3(3, 0, 0), new Float3(0, -1, 0), 10f, out RaycastHit hit));
        Assert.False(float.IsNaN(hit.Normal.X) || float.IsNaN(hit.Normal.Y) || float.IsNaN(hit.Normal.Z), $"normal {hit.Normal}");
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

    private sealed class DisableOnEnter : MonoBehaviour
    {
        public override void OnTriggerEnter(Rigidbody3D other) => other.Enabled = false;
    }

    [Fact]
    public void Trigger_HandlersMayRemoveBodies()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        trigger.GameObject.AddComponent<DisableOnEnter>();
        var a = AddDynamicBox(scene, new Float3(-1, 0, 0), gravity: false);
        var b = AddDynamicBox(scene, new Float3(1, 0, 0), gravity: false);

        int errors = 0;
        void Count(string message, DebugStackTrace? trace, LogSeverity severity) { if (severity == LogSeverity.Error) errors++; }
        Debug.OnLog += Count;
        try { StepPhysics(scene, 3); }
        finally { Debug.OnLog -= Count; }

        Assert.Equal(0, errors);
        Assert.False(a.Enabled);
        Assert.False(b.Enabled);
    }

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
        public readonly List<Collision> Stays = [];
        public readonly List<Collision> Ends = [];

        public override void OnCollisionBegin(Collision collision) => Begins.Add(collision);
        public override void OnCollisionStay(Collision collision) => Stays.Add(collision);
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

    private GameObject AddStaticRigidbodyFloor(Scene scene, out Rigidbody3D floorBody)
    {
        var floor = CreateGameObject("Floor");
        floor.Transform.Position = new Float3(0, -0.5f, 0);
        floorBody = floor.AddComponent<Rigidbody3D>();
        floorBody.MotionType = Jitter2.Dynamics.MotionType.Static;
        floor.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
        scene.Add(floor);
        return floor;
    }

    [Fact]
    public void Collision_BothSidesGetTheirOwnNormalAndTheRealContactPoint()
    {
        var scene = CreatePhysicsScene();
        GameObject floor = AddStaticRigidbodyFloor(scene, out Rigidbody3D floorBody);
        var floorRecorder = floor.AddComponent<CollisionRecorder>();

        var box = AddDynamicBox(scene, new Float3(3, 1, 0.5f), gravity: true);
        var boxRecorder = box.GameObject.AddComponent<CollisionRecorder>();

        Tick(scene, 120);

        Collision boxHit = Assert.Single(boxRecorder.Begins);
        Collision floorHit = Assert.Single(floorRecorder.Begins);

        Assert.True(boxHit.Normal.Y > 0.99f, $"box normal {boxHit.Normal}");
        Assert.True(floorHit.Normal.Y < -0.99f, $"floor normal {floorHit.Normal}");
        Assert.InRange(boxHit.Point.Y, -0.05f, 0.05f);
        Assert.InRange(boxHit.Point.X, 2.5f, 3.5f);
        Assert.InRange(boxHit.Point.Z, 0.0f, 1.0f);
        Assert.True(boxHit.ImpulseMagnitude > 0.0f, "the impulse is read after the solver ran");

        Assert.Same(floorBody, boxHit.Rigidbody);
        Assert.Same(box, floorHit.Rigidbody);
        Assert.Same(box.GameObject, floorHit.GameObject);
        Assert.Same(box.GameObject, boxHit.ThisCollider.GameObject);
        Assert.Same(floor, floorHit.ThisCollider.GameObject);
    }

    [Fact]
    public void Collision_StaticColliderAndChildColliderObjectsReceiveEvents()
    {
        var scene = CreatePhysicsScene();
        GameObject floor = AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));
        var floorRecorder = floor.AddComponent<CollisionRecorder>();

        var root = CreateGameObject("Root");
        root.Transform.Position = new Float3(0, 2, 0);
        var rb = root.AddComponent<Rigidbody3D>();
        var rootRecorder = root.AddComponent<CollisionRecorder>();
        var child = CreateGameObject("Child");
        child.SetParent(root);
        child.Transform.LocalPosition = new Float3(0, -1, 0);
        var childCollider = child.AddComponent<BoxCollider>();
        var childRecorder = child.AddComponent<CollisionRecorder>();
        scene.Add(root);

        Tick(scene, 120);

        Collision floorHit = Assert.Single(floorRecorder.Begins);
        Assert.Same(rb, floorHit.Rigidbody);
        Assert.Same(childCollider, floorHit.Collider);
        Assert.Same(root, floorHit.GameObject);

        Assert.Same(childCollider, Assert.Single(childRecorder.Begins).ThisCollider);
        Assert.Same(childCollider, Assert.Single(rootRecorder.Begins).ThisCollider);
    }

    [Fact]
    public void Collision_StayFiresWhileTouching_ThenEnd()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        var rb = AddDynamicBox(scene, new Float3(0, 1, 0), gravity: true);
        rb.DeactivationTime = 1000f;
        var recorder = rb.GameObject.AddComponent<CollisionRecorder>();

        Tick(scene, 60);
        Assert.Single(recorder.Begins);
        Assert.True(recorder.Stays.Count > 30, $"stays {recorder.Stays.Count}");
        Assert.True(recorder.Stays[^1].Normal.Y > 0.99f);

        rb.AffectedByGravity = false;
        rb.LinearVelocity = new Float3(0, 40, 0);
        Tick(scene, 30);

        Assert.Single(recorder.Ends);
        Assert.Single(recorder.Begins);
    }

    private sealed class DisableOnBegin : MonoBehaviour
    {
        public override void OnCollisionBegin(Collision collision) => GetComponent<Rigidbody3D>().Enabled = false;
    }

    [Fact]
    public void Collision_ChangingPhysicsInsideACallback_DoesNotBreakTheStep()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        var lower = AddDynamicBox(scene, new Float3(0, 3, 0), gravity: false);
        lower.GameObject.AddComponent<DisableOnBegin>();

        var upper = AddDynamicBox(scene, new Float3(0, 6, 0), gravity: true);
        var upperRecorder = upper.GameObject.AddComponent<CollisionRecorder>();

        int errors = 0;
        void Count(string message, DebugStackTrace? trace, LogSeverity severity) { if (severity == LogSeverity.Error) errors++; }
        Debug.OnLog += Count;
        try { Tick(scene, 120); }
        finally { Debug.OnLog -= Count; }

        Assert.Equal(0, errors);
        Assert.False(lower.Enabled);
        Assert.True(upperRecorder.Begins.Count >= 1);
        Assert.Equal(upperRecorder.Begins.Count, upperRecorder.Ends.Count + 1);
    }

    [Fact]
    public void Collision_EndFires_WhenTheOtherBodyIsDisabledOrDestroyed()
    {
        var scene = CreatePhysicsScene();
        GameObject floor = AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));
        var floorRecorder = floor.AddComponent<CollisionRecorder>();

        var a = AddDynamicBox(scene, new Float3(-3, 1, 0), gravity: true);
        var b = AddDynamicBox(scene, new Float3(3, 1, 0), gravity: true);
        Tick(scene, 60);
        Assert.Equal(2, floorRecorder.Begins.Count);

        a.Enabled = false;
        b.GameObject.Destroy();
        Tick(scene, 2);

        Assert.Equal(2, floorRecorder.Ends.Count);
    }

    [Fact]
    public void Collision_MovingAStaticCollider_DoesNotRestartTheContact()
    {
        var scene = CreatePhysicsScene();
        GameObject floor = AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        var rb = AddDynamicBox(scene, new Float3(0, 1, 0), gravity: true);
        var recorder = rb.GameObject.AddComponent<CollisionRecorder>();
        Tick(scene, 60);
        Assert.Single(recorder.Begins);

        for (int i = 0; i < 30; i++)
        {
            floor.Transform.Position += new Float3(0.001f, 0, 0);
            Tick(scene, 1);
        }

        Assert.Single(recorder.Begins);
        Assert.Empty(recorder.Ends);
    }

    // A convex MeshCollider used to hand the raw mesh triangles to ConvexHullShape, which needs points
    // already on a valid hull. A lumpy, non-convex mesh broke its support walk and fell through floors.
    [Fact]
    public void ConvexMeshCollider_OfANonConvexMesh_RestsOnTheFloor()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(40, 1, 40));

        var rocks = new List<Rigidbody3D>();
        for (int i = 0; i < 6; i++)
        {
            Mesh mesh = Mesh.CreateSphere(0.55f, 6, 8);
            Float3[] v = mesh.Vertices;
            for (int k = 0; k < v.Length; k++)
                v[k] *= 0.75f + 0.45f * (MathF.Sin(v[k].X * 13 + i) * MathF.Cos(v[k].Z * 11 + v[k].Y * 7) + 1) * 0.5f;
            mesh.Vertices = v;
            mesh.RecalculateBounds();

            var go = CreateGameObject("Rock");
            go.Transform.Position = new Float3(i * 2, 3, 0);
            go.Transform.Rotation = Quaternion.FromEuler(new Float3(i * 40, i * 70, i * 20));
            rocks.Add(go.AddComponent<Rigidbody3D>());
            var collider = go.AddComponent<MeshCollider>();
            collider.Convex = true;
            collider.Mesh = mesh;
            scene.Add(go);
        }

        Tick(scene, 240);

        foreach (Rigidbody3D rock in rocks)
            Assert.True(rock.Transform.Position.Y > 0.2f, $"a rock sank to {rock.Transform.Position}");
    }

    // ---------------------------------------------------------------------
    // Constraint lifecycle
    // ---------------------------------------------------------------------

    [Fact]
    public void Constraint_ConnectedBodyEnabledLater_IsBoundToIt()
    {
        var scene = CreatePhysicsScene();

        var anchorGo = CreateGameObject("Anchor");
        var anchor = anchorGo.AddComponent<Rigidbody3D>();
        anchor.MotionType = Jitter2.Dynamics.MotionType.Static;
        var socket = anchorGo.AddComponent<BallSocketConstraint>();

        var bobGo = CreateGameObject("Bob");
        bobGo.Transform.Position = new Float3(1, 0, 0);
        var bob = bobGo.AddComponent<Rigidbody3D>();
        bobGo.AddComponent<SphereCollider>();
        socket.ConnectedBody = bob;

        scene.Add(anchorGo);
        scene.Add(bobGo);
        Tick(scene, 120);

        Assert.True(Float3.Length(bob.Transform.Position) < 1.2f, $"bob fell to {bob.Transform.Position}");
    }

    [Fact]
    public void Constraint_SurvivesItsRigidbodyBeingDisabledAndEnabled()
    {
        var scene = CreatePhysicsScene();

        var anchorGo = CreateGameObject("Anchor");
        var anchor = anchorGo.AddComponent<Rigidbody3D>();
        anchor.MotionType = Jitter2.Dynamics.MotionType.Static;
        scene.Add(anchorGo);

        var armGo = CreateGameObject("Arm");
        armGo.Transform.Position = new Float3(1, 0, 0);
        var arm = armGo.AddComponent<Rigidbody3D>();
        armGo.AddComponent<SphereCollider>();
        var joint = armGo.AddComponent<HingeJoint>();
        joint.ConnectedBody = anchor;
        scene.Add(armGo);
        Tick(scene, 2);

        arm.Enabled = false;
        Assert.False(joint.Active);
        arm.Enabled = true;
        Assert.True(joint.Active);

        Tick(scene, 120);
        Assert.True(Float3.Length(arm.Transform.Position) < 1.5f, $"arm fell to {arm.Transform.Position}");

        anchor.Enabled = false;
        anchor.Enabled = true;
        Assert.True(joint.Active, "re-enabling the connected body rebinds too");
    }

    [Fact]
    public void ConstraintReadouts_AreSafeAfterTheirBodyIsDisabled()
    {
        var scene = CreatePhysicsScene();

        var anchorGo = CreateGameObject("Anchor");
        var anchor = anchorGo.AddComponent<Rigidbody3D>();
        anchor.MotionType = Jitter2.Dynamics.MotionType.Static;
        scene.Add(anchorGo);

        var armGo = CreateGameObject("Arm");
        armGo.Transform.Position = new Float3(1, 0, 0);
        var arm = armGo.AddComponent<Rigidbody3D>();
        var socket = armGo.AddComponent<BallSocketConstraint>();
        var distance = armGo.AddComponent<DistanceLimitConstraint>();
        var cone = armGo.AddComponent<ConeLimitConstraint>();
        var hingeAngle = armGo.AddComponent<HingeAngleConstraint>();
        var twist = armGo.AddComponent<TwistAngleConstraint>();
        var fixedAngle = armGo.AddComponent<FixedAngleConstraint>();
        var line = armGo.AddComponent<PointOnLineConstraint>();
        var plane = armGo.AddComponent<PointOnPlaneConstraint>();
        var linearMotor = armGo.AddComponent<LinearMotorConstraint>();
        var angularMotor = armGo.AddComponent<AngularMotorConstraint>();
        var hinge = armGo.AddComponent<HingeJoint>();
        foreach (PhysicsConstraint c in armGo.GetComponents<PhysicsConstraint>()) c.ConnectedBody = anchor;
        scene.Add(armGo);
        Tick(scene, 2);

        arm.Enabled = false;

        _ = socket.Impulse;
        _ = distance.CurrentDistance;
        _ = distance.Impulse;
        _ = cone.Angle;
        _ = cone.Impulse;
        _ = hingeAngle.Angle;
        _ = hingeAngle.Impulse;
        _ = twist.Angle;
        _ = twist.Impulse;
        _ = fixedAngle.Impulse;
        _ = line.Distance;
        _ = line.Impulse;
        _ = plane.Impulse;
        _ = linearMotor.Impulse;
        _ = angularMotor.LocalAxis1;
        _ = angularMotor.LocalAxis2;
        Assert.False(hinge.Active);
    }

    [Fact]
    public void DistanceLimit_HoldsTheTargetByDefault_AndAllowsItsRange()
    {
        var scene = CreatePhysicsScene();

        var anchorGo = CreateGameObject("Anchor");
        var anchor = anchorGo.AddComponent<Rigidbody3D>();
        anchor.MotionType = Jitter2.Dynamics.MotionType.Static;
        scene.Add(anchorGo);

        Rigidbody3D AddArm(float x, out DistanceLimitConstraint limit)
        {
            var go = CreateGameObject("Arm");
            go.Transform.Position = new Float3(x, 0, 5);
            var rb = go.AddComponent<Rigidbody3D>();
            go.AddComponent<SphereCollider>().Radius = 0.1f;
            limit = go.AddComponent<DistanceLimitConstraint>();
            limit.ConnectedBody = anchor;
            limit.ConnectedAnchor = new Float3(0, 0, 5);
            return rb;
        }

        AddArm(1, out DistanceLimitConstraint rod);
        AddArm(-1, out DistanceLimitConstraint rope);
        rope.MinDistance = 0;
        rope.MaxDistance = 3;
        foreach (GameObject go in new[] { rod.GameObject, rope.GameObject }) scene.Add(go);

        Tick(scene, 180);

        Assert.Equal(1.0, rod.CurrentDistance, 1);
        Assert.InRange(rope.CurrentDistance, 2.5f, 3.1f);
    }

    [Fact]
    public void PointOnPlane_HoldsThePointOnThePlaneByDefault()
    {
        var scene = CreatePhysicsScene();

        var go = CreateGameObject("Slider");
        go.Transform.Position = new Float3(0, 5, 0);
        var rb = go.AddComponent<Rigidbody3D>();
        go.AddComponent<SphereCollider>();
        var plane = go.AddComponent<PointOnPlaneConstraint>();
        plane.Anchor2 = new Float3(0, 5, 0);
        scene.Add(go);

        Tick(scene, 120);

        Assert.Equal(5.0, rb.Transform.Position.Y, 1);
    }

    // ---------------------------------------------------------------------
    // Rigidbody wiring
    // ---------------------------------------------------------------------

    private sealed class MoveOnce : MonoBehaviour
    {
        public Float3 Target;
        private bool _moved;

        public override void Update()
        {
            if (_moved) return;
            _moved = true;
            Transform.Position = Target;
        }
    }

    [Fact]
    public void Rigidbody_TransformEditsSurvive_WhateverTheComponentOrder()
    {
        var scene = CreatePhysicsScene();

        var go = CreateGameObject("Moved");
        var mover = go.AddComponent<MoveOnce>();
        mover.Target = new Float3(10, 0, 0);
        var rb = go.AddComponent<Rigidbody3D>();
        rb.AffectedByGravity = false;
        go.AddComponent<BoxCollider>();
        scene.Add(go);

        Tick(scene, 3);

        Assert.Equal(10.0, rb.Transform.Position.X, 3);
        Assert.Equal(10.0, rb.Native!.Position.X, 3);
    }

    [Fact]
    public void Rigidbody_LayerChange_ReachesQueries()
    {
        var scene = CreatePhysicsScene();
        var rb = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        Tick(scene, 1);

        rb.GameObject.LayerIndex = 3;

        Assert.True(scene.Physics.Raycast(new Float3(0, 5, 0), new Float3(0, -1, 0), 10f, OnlyLayer(3)));
        Assert.False(scene.Physics.Raycast(new Float3(0, 5, 0), new Float3(0, -1, 0), 10f, OnlyLayer(0)));
    }

    [Fact]
    public void IgnoreCollisionBetween_SeparatesBodiesAlreadyTouching()
    {
        var scene = CreatePhysicsScene();
        AddStaticRigidbodyFloor(scene, out Rigidbody3D floorBody);
        var rb = AddDynamicBox(scene, new Float3(0, 0.5f, 0), gravity: true);
        Tick(scene, 60);
        Assert.True(rb.Transform.Position.Y > 0.4f);

        scene.Physics.IgnoreCollisionBetween(rb, floorBody);
        Tick(scene, 60);

        Assert.True(rb.Transform.Position.Y < -1f, $"box stayed at y={rb.Transform.Position.Y}");
    }

    [Fact]
    public void CollisionMatrixChange_SeparatesBodiesAlreadyTouching()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20), layer: 1);
        var rb = AddDynamicBox(scene, new Float3(0, 0.5f, 0), gravity: true, layer: 2);
        Tick(scene, 60);
        Assert.True(rb.Transform.Position.Y > 0.4f);

        CollisionMatrix.SetLayerCollision(1, 2, false);
        Tick(scene, 60);

        Assert.True(rb.Transform.Position.Y < -1f, $"box stayed at y={rb.Transform.Position.Y}");
    }

    [Fact]
    public void ZeroFriction_SlidesFreelyOverStaticGeometry()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(200, 1, 200));
        var rb = AddDynamicBox(scene, new Float3(0, 0.5f, 0), gravity: true);
        rb.Friction = 0f;
        Tick(scene, 30);

        rb.LinearVelocity = new Float3(5, 0, 0);
        Tick(scene, 60);

        Assert.True(rb.LinearVelocity.X > 4.9f, $"puck slowed to {rb.LinearVelocity.X}");
    }

    [Fact]
    public void StaticRigidbody_IgnoresVelocityAndImpulses()
    {
        var scene = CreatePhysicsScene();
        AddStaticRigidbodyFloor(scene, out Rigidbody3D floorBody);
        Tick(scene, 1);

        floorBody.LinearVelocity = new Float3(1, 0, 0);
        floorBody.AngularVelocity = new Float3(1, 0, 0);
        floorBody.AddForce(new Float3(1, 0, 0), ForceMode.Impulse);
        floorBody.AddForce(new Float3(1, 0, 0), ForceMode.VelocityChange);
        floorBody.AddTorque(new Float3(1, 0, 0), ForceMode.Impulse);
        floorBody.ApplyImpulse(new Float3(1, 0, 0));
        floorBody.ApplyImpulse(new Float3(1, 0, 0), Float3.Zero);
        floorBody.ApplyAngularImpulse(new Float3(1, 0, 0));

        Assert.Equal(Float3.Zero, floorBody.LinearVelocity);
    }

    [Fact]
    public void AddTorque_Acceleration_IsInWorldSpace_ForARotatedBody()
    {
        var scene = CreatePhysicsScene();

        var go = CreateGameObject("Plank");
        go.Transform.Rotation = Quaternion.FromEuler(new Float3(0, 0, 90));
        var rb = go.AddComponent<Rigidbody3D>();
        rb.AffectedByGravity = false;
        go.AddComponent<BoxCollider>().Size = new Float3(4, 1, 1);
        scene.Add(go);
        StepPhysics(scene, 1);

        rb.AddTorque(new Float3(0, 6, 0), ForceMode.Acceleration);
        StepPhysics(scene, 2); // Jitter integrates queued forces on the step after they are added

        Float3 spin = rb.AngularVelocity;
        Assert.Equal(6.0 * Time.FixedDeltaTime, spin.Y, 2);
        Assert.Equal(0.0, spin.X, 2);
        Assert.Equal(0.0, spin.Z, 2);
    }

    [Fact]
    public void LogOnce_ReportsOnce_CountsTheRest_AndResetsOnPlayModeChange()
    {
        Debug.ClearReportedOnce();

        int logged = 0;
        void Count(string message, DebugStackTrace? trace, LogSeverity severity) => logged++;

        Debug.OnLog += Count;
        try
        {
            for (int i = 0; i < 100; i++)
                Debug.LogWarningOnce("Test.Spam", "expensive thing happened");

            Assert.Equal(1, logged);
            Assert.Equal(100, Debug.GetReportCount("Test.Spam"));

            // A distinct id is its own budget.
            Debug.LogWarningOnce("Test.Other", "something else");
            Assert.Equal(2, logged);

            // Toggling play mode is a fresh run, so the condition gets to report again.
            bool wasPlaying = Application.IsPlaying;
            Application.IsPlaying = !wasPlaying;
            Application.IsPlaying = wasPlaying;

            Assert.Equal(0, Debug.GetReportCount("Test.Spam"));
            Debug.LogWarningOnce("Test.Spam", "expensive thing happened");
            Assert.Equal(3, logged);
        }
        finally
        {
            Debug.OnLog -= Count;
            Debug.ClearReportedOnce();
        }
    }

    // CenterOfMass used to return the body origin, which is only the centre of mass when the shapes
    // happen to be centred on it.
    [Fact]
    public void CenterOfMass_FollowsOffsetColliders()
    {
        var scene = CreatePhysicsScene();

        var go = CreateGameObject("Offset");
        var rb = go.AddComponent<Rigidbody3D>();
        rb.AffectedByGravity = false;
        go.AddComponent<BoxCollider>().Center = new Float3(4, 0, 0);
        scene.Add(go);
        StepPhysics(scene, 2);

        Assert.Equal(4.0, rb.CenterOfMass.X, 2);
        Assert.Equal(0.0, rb.Transform.Position.X, 2); // the origin is still where it was
    }

    [Fact]
    public void CenterOfMass_OfACompound_IsMassWeightedBetweenTheShapes()
    {
        var scene = CreatePhysicsScene();

        var go = CreateGameObject("Compound");
        var rb = go.AddComponent<Rigidbody3D>();
        rb.AffectedByGravity = false;

        // Equal boxes either side of the origin average back to it.
        go.AddComponent<BoxCollider>().Center = new Float3(-2, 0, 0);
        go.AddComponent<BoxCollider>().Center = new Float3(2, 0, 0);
        scene.Add(go);
        StepPhysics(scene, 2);

        Assert.Equal(0.0, rb.CenterOfMass.X, 2);
    }

    // Removing a body removes its constraints, which zeroes their handles while the component still
    // holds the managed object. Jitter's constraint properties are views onto unmanaged memory reached
    // through that handle, so a null check alone let writes land on freed memory.
    [Fact]
    public void ConstraintProperties_AreSafeAfterTheirBodyIsDisabled()
    {
        var scene = CreatePhysicsScene();

        var anchorGo = CreateGameObject("Anchor");
        var anchor = anchorGo.AddComponent<Rigidbody3D>();
        anchor.MotionType = Jitter2.Dynamics.MotionType.Static;
        scene.Add(anchorGo);

        var armGo = CreateGameObject("Arm");
        armGo.Transform.Position = new Float3(1, 0, 0);
        var arm = armGo.AddComponent<Rigidbody3D>();
        var joint = armGo.AddComponent<DistanceLimitConstraint>();
        joint.ConnectedBody = anchor;
        scene.Add(armGo);
        StepPhysics(scene, 2);

        // Disabling the rigidbody tears the constraint out from under the still-enabled component.
        arm.Enabled = false;
        StepPhysics(scene, 1);

        joint.TargetDistance = 3f;
        joint.Softness = 0.5f;
        joint.Anchor = new Float3(0, 1, 0);
        Assert.False(joint.Active);
    }

    // A joint is several Jitter constraints, and PhysicsConstraint.Active used to route through a
    // single-constraint accessor that joints return null from, so Active always read false and its
    // setter was a no-op.
    [Fact]
    public void Joint_Active_ReflectsAndControlsAllOfItsConstraints()
    {
        var scene = CreatePhysicsScene();

        var anchorGo = CreateGameObject("Anchor");
        var anchor = anchorGo.AddComponent<Rigidbody3D>();
        anchor.MotionType = Jitter2.Dynamics.MotionType.Static;
        scene.Add(anchorGo);

        var armGo = CreateGameObject("Arm");
        armGo.Transform.Position = new Float3(1, 0, 0);
        var arm = armGo.AddComponent<Rigidbody3D>();
        var joint = armGo.AddComponent<HingeJoint>();
        joint.ConnectedBody = anchor;
        scene.Add(armGo);
        StepPhysics(scene, 2);

        Assert.True(joint.Active, "a freshly created joint should report itself active");

        joint.Active = false;
        Assert.False(joint.Active, "disabling should reach every constraint the joint owns");

        joint.Active = true;
        Assert.True(joint.Active);
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

    /// <summary>
    /// A capsule resting exactly on a box is touching it. Casting along the top of the box does not
    /// hit it, and casting down into it hits it facing up. Both used to come back as a hit whose normal
    /// was the cast reversed, which reads a floor underfoot as a wall straight ahead.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.005f)]
    public void ACastFromRestingOnASurfaceOnlyHitsItWhenMovingIntoIt(float gap)
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        Float3 bottom = new(0, 0.3f + gap, 0), top = new(0, 1.5f + gap, 0);
        Assert.False(scene.Physics.CapsuleCast(bottom, top, 0.3f, new Float3(1, 0, 0), 0.5f, out _));
        Assert.False(scene.Physics.CapsuleCast(bottom, top, 0.3f, new Float3(0, 1, 0), 0.5f, out _));

        Assert.True(scene.Physics.CapsuleCast(bottom, top, 0.3f, new Float3(0, -1, 0), 0.5f, out ShapeCastHit down));
        Assert.True(down.Normal.Y > 0.9f, $"the floor was reported facing {down.Normal}");
    }
    // ---------------------------------------------------------------------
    // Constraints between bodies
    // ---------------------------------------------------------------------

    // A twist limit leaves translation free, so the only thing that can push the boxes apart is contact.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constraint_CollideConnected_DecidesWhetherJoinedBodiesCollide(bool collide)
    {
        var scene = CreatePhysicsScene();
        Rigidbody3D a = AddDynamicBox(scene, new Float3(0, 0, 0), gravity: false);
        Rigidbody3D b = AddDynamicBox(scene, new Float3(0.5f, 0, 0), gravity: false);
        var twist = a.GameObject.AddComponent<TwistAngleConstraint>();
        twist.ConnectedBody = b;
        twist.CollideConnected = collide;
        StepPhysics(scene, 30);
        Update(scene);

        float gap = b.Transform.Position.X - a.Transform.Position.X;
        Assert.Equal(collide, gap > 0.9f);
    }

    // Jitter measures a hinge from the connected body's side. Prowl reverses that, so the angle, its
    // limits and the motor all mean this body turning about its own +axis.
    [Theory]
    [InlineData(1f, true)]
    [InlineData(-1f, false)]
    public void HingeJoint_APositiveTurnOfItsBody_IsAPositiveAngle(float spin, bool allowed)
    {
        var scene = CreatePhysicsScene();
        Rigidbody3D body = AddDynamicBox(scene, Float3.Zero, gravity: false);
        var hinge = body.GameObject.AddComponent<HingeJoint>();
        hinge.Axis = Float3.UnitX;
        hinge.MinAngle = 0f;
        hinge.MaxAngle = 90f;
        StepPhysics(scene, 1);

        body.AngularVelocity = new Float3(spin * 2f, 0f, 0f);
        StepPhysics(scene, 20);

        Assert.Equal(allowed, hinge.CurrentAngleDegrees > 10f);
    }

    private Rigidbody3D AddFloatingBody(Scene scene)
    {
        var go = CreateGameObject("Driven");
        go.Transform.Position = new Float3(0, 5, 0);
        var rb = go.AddComponent<Rigidbody3D>();
        rb.AffectedByGravity = false;
        go.AddComponent<BoxCollider>();
        return rb;
    }

    // A positive motor moves its own body along its +axis, for every kind of motor.
    [Fact]
    public void LinearMotor_PositiveVelocity_MovesItsBodyAlongTheAxis()
    {
        var scene = CreatePhysicsScene();
        Rigidbody3D body = AddFloatingBody(scene);
        var motor = body.GameObject.AddComponent<LinearMotorConstraint>();
        motor.Axis1 = Float3.UnitY;
        motor.Axis2 = Float3.UnitY;
        motor.MaximumForce = 1000f;
        motor.TargetVelocity = 1f;
        scene.Add(body.GameObject);

        Tick(scene, 30);

        Assert.Equal(1.0, body.LinearVelocity.Y, 1);
    }

    [Fact]
    public void AngularMotor_PositiveVelocity_TurnsItsBodyAboutTheAxis()
    {
        var scene = CreatePhysicsScene();
        Rigidbody3D body = AddFloatingBody(scene);
        body.GameObject.AddComponent<BallSocketConstraint>();
        var motor = body.GameObject.AddComponent<AngularMotorConstraint>();
        motor.Axis1 = Float3.UnitY;
        motor.Axis2 = Float3.UnitY;
        motor.MaximumForce = 1000f;
        motor.TargetVelocity = 1f;
        scene.Add(body.GameObject);

        Tick(scene, 30);

        Assert.Equal(1.0, body.AngularVelocity.Y, 1);
    }

    [Fact]
    public void HingeJointMotor_PositiveVelocity_TurnsItsBodyAboutTheAxis_AndReadsAPositiveAngle()
    {
        var scene = CreatePhysicsScene();
        Rigidbody3D body = AddFloatingBody(scene);
        var hinge = body.GameObject.AddComponent<HingeJoint>();
        hinge.Axis = Float3.UnitY;
        hinge.HasMotor = true;
        hinge.MotorMaxForce = 1000f;
        hinge.MotorTargetVelocity = 1f;
        scene.Add(body.GameObject);

        Tick(scene, 30);

        Assert.Equal(1.0, body.AngularVelocity.Y, 1);
        Assert.True(hinge.CurrentAngleDegrees > 10f, $"angle {hinge.CurrentAngleDegrees}");
    }

    [Fact]
    public void PrismaticJointMotor_PositiveVelocity_MovesItsBodyAlongTheAxis_AndReadsAPositiveDistance()
    {
        var scene = CreatePhysicsScene();
        Rigidbody3D body = AddFloatingBody(scene);
        var slider = body.GameObject.AddComponent<PrismaticJoint>();
        slider.Axis = Float3.UnitY;
        slider.MinDistance = -5f;
        slider.MaxDistance = 5f;
        slider.HasMotor = true;
        slider.MotorMaxForce = 1000f;
        slider.MotorTargetVelocity = 1f;
        scene.Add(body.GameObject);

        Tick(scene, 30);

        Assert.Equal(1.0, body.LinearVelocity.Y, 1);
        Assert.True(slider.CurrentDistance > 0.3f, $"distance {slider.CurrentDistance}");
    }

    // Jitter leaves a sleeping body asleep when a motor on it changes, so an elevator that paused at the
    // top long enough to sleep never came back down.
    [Fact]
    public void ChangingAMotor_WakesASleepingBody()
    {
        var scene = CreatePhysicsScene();
        Rigidbody3D body = AddFloatingBody(scene);
        body.DeactivationTime = 0.1f;
        var motor = body.GameObject.AddComponent<LinearMotorConstraint>();
        motor.Axis1 = Float3.UnitY;
        motor.Axis2 = Float3.UnitY;
        motor.MaximumForce = 1000f;
        scene.Add(body.GameObject);
        Tick(scene, 60);
        Assert.False(body.IsActive);

        motor.TargetVelocity = 1f;
        Tick(scene, 10);

        Assert.True(body.LinearVelocity.Y > 0.5f, $"velocity {body.LinearVelocity}");
    }

    [Fact]
    public void PrismaticJoint_Limits_AreMeasuredAlongItsOwnAxis()
    {
        var scene = CreatePhysicsScene();
        Rigidbody3D body = AddFloatingBody(scene);
        body.AffectedByGravity = true;
        var slider = body.GameObject.AddComponent<PrismaticJoint>();
        slider.Axis = Float3.UnitY;
        slider.MinDistance = -1f;
        slider.MaxDistance = 0f;
        scene.Add(body.GameObject);

        Tick(scene, 120);

        Assert.Equal(4.0, body.Transform.Position.Y, 1);
    }
}
