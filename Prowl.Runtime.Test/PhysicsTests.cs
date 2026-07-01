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

    private TriggerVolume AddBoxTrigger(Scene scene, Float3 position, Float3 size)
    {
        var go = CreateGameObject("Trigger");
        go.Transform.Position = position;
        var trigger = go.AddComponent<TriggerVolume>();
        trigger.Shape = TriggerShape.Box;
        trigger.Size = size;
        scene.Add(go);
        return trigger;
    }

    [Fact]
    public void Trigger_Entered_FiresForRigidbodyInside()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        var rb = AddDynamicBox(scene, Float3.Zero, gravity: false);

        var entered = new List<Rigidbody3D>();
        trigger.Entered += entered.Add;

        StepPhysics(scene);

        Assert.Contains(rb, entered);
    }

    [Fact]
    public void Trigger_Staying_FiresOnSubsequentSteps()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        var rb = AddDynamicBox(scene, Float3.Zero, gravity: false);

        int staying = 0;
        trigger.Staying += _ => staying++;

        StepPhysics(scene);   // Entered
        StepPhysics(scene);   // Staying
        StepPhysics(scene);   // Staying

        Assert.Equal(2, staying);
    }

    [Fact]
    public void Trigger_Exited_FiresWhenBodyLeaves()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(2, 2, 2));
        var rb = AddDynamicBox(scene, Float3.Zero, gravity: false);

        var exited = new List<Rigidbody3D>();
        trigger.Exited += exited.Add;

        StepPhysics(scene); // Entered
        rb.MovePosition(new Float3(50, 0, 0));
        StepPhysics(scene); // Exited

        Assert.Contains(rb, exited);
    }

    [Fact]
    public void Trigger_IgnoresStaticColliders()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        AddStaticBox(scene, Float3.Zero, new Float3(1, 1, 1)); // no Rigidbody3D

        bool fired = false;
        trigger.Entered += _ => fired = true;

        StepPhysics(scene);

        Assert.False(fired);
        Assert.Empty(trigger.Overlapping);
    }

    [Fact]
    public void Trigger_LayerMask_FiltersBodies()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        trigger.LayerMask = OnlyLayer(5);
        AddDynamicBox(scene, Float3.Zero, gravity: false, layer: 3); // not in the mask

        bool fired = false;
        trigger.Entered += _ => fired = true;

        StepPhysics(scene);

        Assert.False(fired);
    }

    [Fact]
    public void Trigger_FiresExit_OnDisable()
    {
        var scene = CreatePhysicsScene();
        var trigger = AddBoxTrigger(scene, Float3.Zero, new Float3(4, 4, 4));
        var rb = AddDynamicBox(scene, Float3.Zero, gravity: false);

        var exited = new List<Rigidbody3D>();
        trigger.Exited += exited.Add;

        StepPhysics(scene); // Entered, now occupant
        trigger.Enabled = false;

        Assert.Contains(rb, exited);
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

        PhysicsWorld.IgnoreCollisionBetween(top, floorRb);
        try
        {
            Tick(scene, 180);

            Assert.True(top.Transform.Position.Y < 0,
                $"Ignored pair should not collide; body was at y={top.Transform.Position.Y}");
        }
        finally
        {
            PhysicsWorld.EnableCollisionBetween(top, floorRb);
        }
    }
}
