// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Physics Showcase
//
// A row of stations, each built around one part of the physics system:
//   1  Bodies               every collider shape, friction, bounce, and collision events on both sides
//   2  Stacking             towers, a pyramid and a wall to knock down
//   3  Mesh colliders       a concave bowl with convex rocks and balls rolling in it
//   4  Joints               hinge door, piston, universal joint, chain and a rope bridge
//   5  Motors and limits    a windmill and an elevator built from single constraints, and a limited arm
//   6  Queries              raycast, sphere cast and overlap, drawn live
//   7  Triggers             gates that light up while bodies pass through them
//
// The character controller and the wheels have samples of their own, ControllerShowcase and
// VehicleShowcase.
//
// Controls:
//   1 to 7      Jump to a station
//   WASD, Q/E   Fly, hold Right Mouse to look, Shift to go faster
//   Left Mouse  Drag any body around
//   F           Throw a ball from the camera
//   F1          Hide the HUD
//

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace PhysicsShowcase;

internal class Program
{
    static void Main(string[] args)
    {
        new PhysicsShowcaseGame().Run("Physics Showcase", 1600, 900);
    }
}

public sealed class PhysicsShowcaseGame : StationGame
{
    private const float StationSpacing = 40f;
    private const int StationCount = 7;
    private const int QueriesStation = 5;

    private static readonly Color Orange = new(1f, 0.32f, 0.04f, 1f);
    private static readonly Color Teal = new(0.02f, 0.4f, 0.35f, 1f);
    private static readonly Color Blue = new(0.03f, 0.12f, 0.6f, 1f);
    private static readonly Color Red = new(0.6f, 0.03f, 0.03f, 1f);
    private static readonly Color Yellow = new(0.7f, 0.5f, 0.02f, 1f);
    private static readonly Color Green = new(0.05f, 0.45f, 0.05f, 1f);
    private static readonly Color Purple = new(0.3f, 0.05f, 0.5f, 1f);
    private static readonly Color Wood = new(0.22f, 0.1f, 0.03f, 1f);
    private static readonly Color[] Palette = [Orange, Teal, Blue, Red, Yellow, Green, Purple];

    private readonly Random _rng = new(7);
    private Material _dark = null!;
    private Material _stone = null!;

    private float _throwSpeed = 22f;
    private float _ballMass = 3f;
    private bool _showColliders;
    private bool _slowMotion;
    private readonly Queue<GameObject> _thrown = new();

    private static Float3 StationCenter(int index) => new(index * StationSpacing, 0f, 0f);

    protected override string ExtraKeys => "Left Mouse  drag    F  throw a ball";

    protected override void Build()
    {
        _dark = Lit(new Color(0.03f, 0.032f, 0.04f, 1f), 0f, 0.7f);
        _stone = Lit(new Color(0.09f, 0.09f, 0.1f, 1f), 0f, 0.85f);

        var sun = new GameObject("Sun");
        DirectionalLight light = sun.AddComponent<DirectionalLight>();
        light.ShadowDistance = 70f;
        light.Intensity = 0.6f;
        sun.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        Add(sun);

        float length = StationSpacing * StationCount + 40f;
        Add(Block("Floor", new Float3(length, 1f, 80f), Floor(length, 80f), new Float3(StationSpacing * (StationCount - 1) * 0.5f, -0.5f, 0f)));

        BuildBodies(StationCenter(0));
        BuildStacking(StationCenter(1));
        BuildMeshColliders(StationCenter(2));
        BuildJoints(StationCenter(3));
        BuildMotors(StationCenter(4));
        BuildQueries(StationCenter(5));
        BuildTriggers(StationCenter(6));
    }

    public override string Stats => CurrentStation switch
    {
        0 => $"{_bodies.Count} bodies    {_binFloor.Touching} touching the bin floor",
        1 => $"{_stack.Count} blocks    {_thrown.Count} balls thrown",
        2 => $"{_bowlBodies.Count} bodies in the bowl",
        3 => $"Door {_door.CurrentAngleDegrees:0} degrees    Piston {_piston.CurrentDistance:0.00} m",
        4 => $"Windmill {_windmill.AngularVelocity.Z:0.0} rad/s    Elevator at {_elevator.Transform.Position.Y:0.0} m    Elbow {_elbow.CurrentAngleDegrees:0} degrees",
        5 => _queries.Status,
        6 => _gateStatus(),
        _ => string.Empty,
    };

    protected override void Tick()
    {
        if (Input.GetKeyDown(KeyCode.F))
            ThrowBall();
    }

    protected override void OnStationChanged(int index)
    {
        DrawGizmos = _showColliders || index == QueriesStation;
    }

    public override void DrawSceneControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Physics");
        Slider(paper, font, "Gravity", -SampleScene.Physics.Gravity.Y, 0f, 20f, v => SampleScene.Physics.Gravity = new Float3(0f, -v, 0f), "0.0");
        Toggle(paper, font, "Slow motion", _slowMotion, v => { _slowMotion = v; Time.TimeScale = v ? 0.25f : 1f; });
        Toggle(paper, font, "Show colliders and joints", _showColliders, v => { _showColliders = v; DrawGizmos = v || CurrentStation == QueriesStation; });

        Header(paper, font, "Thrown balls", 1);
        Slider(paper, font, "Speed", _throwSpeed, 5f, 50f, v => _throwSpeed = v, "0");
        Slider(paper, font, "Mass", _ballMass, 0.5f, 30f, v => _ballMass = v, "0.0");
        Button(paper, font, "Clear thrown balls", ClearThrown);
    }

    public override bool HasStationControls => CurrentStation != QueriesStation;

    public override void DrawControls(Paper paper, FontFile font)
    {
        switch (CurrentStation)
        {
            case 0: BodiesControls(paper, font); break;
            case 1: StackingControls(paper, font); break;
            case 2: MeshControls(paper, font); break;
            case 3: JointControls(paper, font); break;
            case 4: MotorControls(paper, font); break;
            case 6: TriggerControls(paper, font); break;
        }
    }

    // ----------------------------------------------------------------
    //  Shared helpers
    // ----------------------------------------------------------------

    /// <summary>A dynamic body with its own material so it can flash on impact.</summary>
    private Rigidbody3D Body(string name, Mesh mesh, Color color, Float3 position, Quaternion rotation, float mass = 1f)
    {
        GameObject go = Model(name, mesh, Lit(color, 0f, 0.5f).Emissive(color, 0f), position);
        go.Transform.Rotation = rotation;
        var rb = go.AddComponent<Rigidbody3D>();
        rb.Mass = mass;
        go.AddComponent<ImpactFlash>();
        return rb;
    }

    private Rigidbody3D Crate(Float3 position, Float3 size, Color color, float mass = 1f, Quaternion? rotation = null)
    {
        Rigidbody3D rb = Body("Crate", Mesh.CreateCube(size), color, position, rotation ?? Quaternion.Identity, mass);
        rb.GameObject.AddComponent<BoxCollider>().Size = size;
        Add(rb.GameObject);
        return rb;
    }

    private Rigidbody3D Ball(Float3 position, float radius, Color color, float mass = 1f)
    {
        Rigidbody3D rb = Body("Ball", Mesh.CreateSphere(radius, 12, 18), color, position, Quaternion.Identity, mass);
        rb.GameObject.AddComponent<SphereCollider>().Radius = radius;
        Add(rb.GameObject);
        return rb;
    }

    private GameObject Static(string name, Float3 size, Float3 position, Float3? euler = null, Material? material = null)
        => Add(Block(name, size, material ?? _dark, position, euler));

    private Quaternion RandomRotation() => Quaternion.FromEuler(new Float3(_rng.NextSingle() * 360f, _rng.NextSingle() * 360f, _rng.NextSingle() * 360f));

    private float Range(float min, float max) => min + _rng.NextSingle() * (max - min);

    private Color RandomColor() => Palette[_rng.Next(Palette.Length)];

    private void ThrowBall()
    {
        Float3 forward = CameraObject.Transform.Forward;
        Rigidbody3D ball = Ball(CameraObject.Transform.Position + forward, 0.3f, new Color(0.8f, 0.8f, 0.85f, 1f), _ballMass);
        ball.LinearVelocity = forward * _throwSpeed;

        _thrown.Enqueue(ball.GameObject);
        while (_thrown.Count > 40)
            _thrown.Dequeue().Destroy();
    }

    private void ClearThrown()
    {
        while (_thrown.Count > 0)
            _thrown.Dequeue().Destroy();
    }

    private static void DestroyAll(List<GameObject> objects)
    {
        foreach (GameObject go in objects)
            if (go.IsValid()) go.Destroy();
        objects.Clear();
    }

    /// <summary>A procedural lumpy rock, used with a convex MeshCollider.</summary>
    private static Mesh RockMesh(float radius, int seed)
    {
        Mesh mesh = Mesh.CreateSphere(radius, 6, 8);
        Float3[] vertices = mesh.Vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            Float3 v = vertices[i];
            float n = Noise(v.X * 3.1f + seed * 7.3f + v.Y * 1.7f, v.Z * 3.1f + v.Y * 2.3f, 64);
            vertices[i] = v * (0.75f + n * 0.45f);
        }
        mesh.Vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // ----------------------------------------------------------------
    //  1  Bodies
    // ----------------------------------------------------------------

    private Float3 _bodiesCenter;
    private readonly List<GameObject> _bodies = new();
    private ContactCounter _binFloor = null!;
    private float _restitution = 0.1f;
    private readonly List<GameObject> _rampCrates = new();
    private GameObject _ramp = null!;

    private enum Shape { Box, Sphere, Capsule, Cylinder, Cone, Rock }

    private void BuildBodies(Float3 c)
    {
        AddStation("Bodies", "Every collider shape falls into the bin. Each body flashes when it lands hard, from its OnCollisionBegin, and the bin floor is static geometry that counts what rests on it, since both sides of a contact now get the events. On the ramp the three crates differ only in friction.", c, new Float3(2f, 7f, -14f), 1f);

        _bodiesCenter = c;
        Float3 bin = c + new Float3(-3f, 0f, 0f);
        Static("Bin Wall", new Float3(7f, 1.2f, 0.3f), bin + new Float3(0f, 0.6f, 3.5f));
        Static("Bin Wall", new Float3(7f, 1.2f, 0.3f), bin + new Float3(0f, 0.6f, -3.5f));
        Static("Bin Wall", new Float3(0.3f, 1.2f, 7.3f), bin + new Float3(3.5f, 0.6f, 0f));
        Static("Bin Wall", new Float3(0.3f, 1.2f, 7.3f), bin + new Float3(-3.5f, 0.6f, 0f));

        GameObject plate = Static("Bin Floor", new Float3(6.7f, 0.1f, 6.7f), bin + new Float3(0f, 0.05f, 0f), material: Lit(new Color(0.05f, 0.05f, 0.06f, 1f), 0f, 0.6f).Emissive(Orange, 0f));
        _binFloor = plate.AddComponent<ContactCounter>();

        foreach (Shape shape in Enum.GetValues<Shape>())
            for (int i = 0; i < 2; i++)
                DropShape(shape);

        _ramp = Static("Ramp", new Float3(9f, 0.3f, 4.5f), c + new Float3(7f, 1.6f, 0f), new Float3(0f, 0f, 18f), _stone);
        ResetRamp();
    }

    private void DropShape(Shape shape)
    {
        Float3 position = _bodiesCenter + new Float3(-3f + Range(-2f, 2f), Range(4f, 8f), Range(-2f, 2f));
        Color color = RandomColor();
        Rigidbody3D rb;

        switch (shape)
        {
            case Shape.Box:
                rb = Body("Box", Mesh.CreateCube(new Float3(0.8f)), color, position, RandomRotation());
                rb.GameObject.AddComponent<BoxCollider>().Size = new Float3(0.8f);
                break;
            case Shape.Sphere:
                rb = Body("Sphere", Mesh.CreateSphere(0.45f, 12, 18), color, position, RandomRotation());
                rb.GameObject.AddComponent<SphereCollider>().Radius = 0.45f;
                break;
            case Shape.Capsule:
                rb = Body("Capsule", Mesh.CreateCapsule(0.3f, 1.3f), color, position, RandomRotation());
                var capsule = rb.GameObject.AddComponent<CapsuleCollider>();
                capsule.Radius = 0.3f;
                capsule.Height = 1.3f;
                break;
            case Shape.Cylinder:
                rb = Body("Cylinder", Mesh.CreateCylinder(0.4f, 0.8f, 20), color, position, RandomRotation());
                var cylinder = rb.GameObject.AddComponent<CylinderCollider>();
                cylinder.Radius = 0.4f;
                cylinder.Height = 0.8f;
                break;
            case Shape.Cone:
                // The cone's mesh is centered on its middle while the collider is centered on its mass, a
                // quarter of the height above the base, so the visual sits on a child.
                var cone = new GameObject("Cone");
                cone.Transform.Position = position;
                cone.Transform.Rotation = RandomRotation();
                rb = cone.AddComponent<Rigidbody3D>();
                var coneCollider = cone.AddComponent<ConeCollider>();
                coneCollider.Radius = 0.5f;
                coneCollider.Height = 1f;
                GameObject visual = Model("Visual", Mesh.CreateCone(0.5f, 1f, 20), Lit(color, 0f, 0.5f).Emissive(color, 0f), Float3.Zero);
                visual.SetParent(cone);
                visual.Transform.LocalPosition = new Float3(0f, 0.25f, 0f);
                visual.Transform.LocalRotation = Quaternion.Identity;
                cone.AddComponent<ImpactFlash>();
                break;
            default:
                Mesh rock = RockMesh(0.55f, _rng.Next(1000));
                rb = Body("Rock", rock, color, position, RandomRotation(), 2f);
                var rockCollider = rb.GameObject.AddComponent<MeshCollider>();
                rockCollider.Convex = true;
                rockCollider.Mesh = rock;
                break;
        }

        rb.Restitution = _restitution;
        Add(rb.GameObject);
        _bodies.Add(rb.GameObject);
    }

    private void ResetRamp()
    {
        DestroyAll(_rampCrates);

        // Whichever end of the ramp is higher is the top, so the crates start there.
        Transform ramp = _ramp.Transform;
        float end = ramp.TransformPoint(new Float3(1f, 0f, 0f)).Y > ramp.Position.Y ? 3.8f : -3.8f;

        (float friction, Color color)[] lanes = [(0f, Green), (0.15f, Yellow), (0.5f, Red)];
        for (int i = 0; i < lanes.Length; i++)
        {
            Float3 position = ramp.TransformPoint(new Float3(end, 0.6f, -1.4f + i * 1.4f));
            Rigidbody3D crate = Crate(position, new Float3(0.8f), lanes[i].color, 1f, ramp.Rotation);
            crate.Friction = lanes[i].friction;
            _rampCrates.Add(crate.GameObject);
        }
    }

    private void BodiesControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Drop into the bin");
        foreach (Shape shape in Enum.GetValues<Shape>())
            Button(paper, font, shape.ToString(), () => DropShape(shape));
        Slider(paper, font, "Bounce of new bodies", _restitution, 0f, 1f, v => _restitution = v);
        Button(paper, font, "Empty the bin", () => DestroyAll(_bodies));

        Header(paper, font, "Ramp", 1);
        Label(paper, font, "Green has no friction, yellow a little, red a lot.");
        Button(paper, font, "Reset the ramp", ResetRamp);
    }

    // ----------------------------------------------------------------
    //  2  Stacking
    // ----------------------------------------------------------------

    private Float3 _stackCenter;
    private readonly List<GameObject> _stack = new();

    private void BuildStacking(Float3 c)
    {
        AddStation("Stacking", "A tower of alternating planks, a pyramid and a brick wall, all resting and asleep until something hits them. Press F to throw a ball from the camera, and change its speed and mass in the panel on the left.", c, new Float3(0f, 5f, -16f), 2.5f);
        _stackCenter = c;
        BuildStacks();
    }

    private void BuildStacks()
    {
        DestroyAll(_stack);
        Float3 c = _stackCenter;

        Float3 plank = new(2.4f, 0.4f, 0.75f);
        for (int layer = 0; layer < 12; layer++)
        {
            bool turned = layer % 2 == 1;
            for (int i = 0; i < 3; i++)
            {
                float offset = (i - 1) * 0.8f;
                Float3 position = c + new Float3(-7f, 0.2f + layer * 0.4f, 0f) + (turned ? new Float3(offset, 0f, 0f) : new Float3(0f, 0f, offset));
                Quaternion rotation = turned ? Quaternion.FromEuler(new Float3(0f, 90f, 0f)) : Quaternion.Identity;
                _stack.Add(Crate(position, plank, layer % 2 == 0 ? Wood : new Color(0.3f, 0.15f, 0.05f, 1f), 1f, rotation).GameObject);
            }
        }

        const int Base = 7;
        for (int row = 0; row < Base; row++)
            for (int i = 0; i < Base - row; i++)
                _stack.Add(Crate(c + new Float3((i - (Base - row - 1) * 0.5f) * 0.82f, 0.4f + row * 0.8f, 0f), new Float3(0.8f), Palette[row % Palette.Length]).GameObject);

        for (int row = 0; row < 8; row++)
            for (int i = 0; i < (row % 2 == 0 ? 8 : 7); i++)
            {
                float shift = row % 2 == 0 ? 0f : 0.5f;
                _stack.Add(Crate(c + new Float3(5f + (i + shift) * 1.0f, 0.25f + row * 0.5f, 0f), new Float3(0.98f, 0.48f, 0.5f), row % 2 == 0 ? Red : new Color(0.45f, 0.06f, 0.03f, 1f), 0.5f).GameObject);
            }
    }

    private void StackingControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Stacks");
        Button(paper, font, "Rebuild everything", BuildStacks);
        Label(paper, font, "Bodies fall asleep once they stop moving, so a still stack costs almost nothing until a ball wakes it.");
    }

    // ----------------------------------------------------------------
    //  3  Mesh colliders
    // ----------------------------------------------------------------

    private Float3 _bowlCenter;
    private readonly List<GameObject> _bowlBodies = new();

    private void BuildMeshColliders(Float3 c)
    {
        AddStation("Mesh colliders", "The bowl is one concave MeshCollider made from a generated mesh. Static mesh colliders can be any shape. The rocks are convex MeshColliders, which is what a moving body needs: their hull is wrapped around the mesh.", c, new Float3(0f, 10f, -16f), 0f);
        _bowlCenter = c;

        Mesh bowl = BowlMesh(8f, 3.5f, 40);
        GameObject go = Model("Bowl", bowl, Lit(Color.White, 0f, 0.8f).With("_MainTex", Load<Texture2D>("Textures/Bowl Grid")).Tiled(6f, 6f), c + new Float3(0f, 0.01f, 0f));
        go.AddComponent<MeshCollider>().Mesh = bowl;
        Add(go);

        for (int i = 0; i < 6; i++) DropRock();
        for (int i = 0; i < 6; i++) DropBowlBall();
    }

    /// <summary>A square grid bent into a bowl, rising with the square of the distance from the middle.</summary>
    private static Mesh BowlMesh(float radius, float depth, int cells)
    {
        int stride = cells + 1;
        var vertices = new Float3[stride * stride];
        var uvs = new Float2[stride * stride];
        for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                float u = x / (float)cells, v = z / (float)cells;
                float px = (u - 0.5f) * radius * 2f, pz = (v - 0.5f) * radius * 2f;
                float r = MathF.Min(MathF.Sqrt(px * px + pz * pz) / radius, 1f);
                vertices[z * stride + x] = new Float3(px, depth * r * r, pz);
                uvs[z * stride + x] = new Float2(u, v);
            }

        var indices = new uint[cells * cells * 6];
        int k = 0;
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                uint a = (uint)(z * stride + x), b = a + 1, d = a + (uint)stride, e = d + 1;
                indices[k++] = a; indices[k++] = e; indices[k++] = b;
                indices[k++] = a; indices[k++] = d; indices[k++] = e;
            }

        var mesh = new Mesh { Vertices = vertices, UV = uvs, Indices = indices };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private void DropRock()
    {
        Mesh rock = RockMesh(Range(0.4f, 0.7f), _rng.Next(1000));
        Rigidbody3D rb = Body("Rock", rock, new Color(0.12f, 0.11f, 0.1f, 1f), _bowlCenter + new Float3(Range(-4f, 4f), Range(5f, 8f), Range(-4f, 4f)), RandomRotation(), 3f);
        var collider = rb.GameObject.AddComponent<MeshCollider>();
        collider.Convex = true;
        collider.Mesh = rock;
        Add(rb.GameObject);
        _bowlBodies.Add(rb.GameObject);
    }

    private void DropBowlBall()
    {
        Rigidbody3D rb = Ball(_bowlCenter + new Float3(Range(-6f, 6f), Range(5f, 7f), Range(-6f, 6f)), 0.35f, RandomColor());
        _bowlBodies.Add(rb.GameObject);
    }

    private void MeshControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Drop into the bowl");
        Button(paper, font, "Rock", DropRock);
        Button(paper, font, "Ball", DropBowlBall);
        Button(paper, font, "Empty the bowl", () => DestroyAll(_bowlBodies));
    }

    // ----------------------------------------------------------------
    //  4  Joints
    // ----------------------------------------------------------------

    private HingeJoint _door = null!;
    private PrismaticJoint _piston = null!;
    private Rigidbody3D _paddle = null!;
    private Rigidbody3D _chainEnd = null!;
    private Float3 _bridgeCenter;

    private void BuildJoints(Float3 c)
    {
        AddStation("Joints", "From the left: a door on a HingeJoint with limits, a piston on a motorised PrismaticJoint, a paddle on a UniversalJoint that swings any way but never twists, a chain of BallSockets, and a rope bridge of planks hinged to each other, hung with some slack.", c, new Float3(1f, 6f, -16f), 2.5f);

        // Door, hinged on its left edge to the world.
        Static("Door Post", new Float3(0.3f, 2.6f, 0.3f), c + new Float3(-12.95f, 1.3f, 0f));
        Rigidbody3D door = Crate(c + new Float3(-12f, 1.2f, 0f), new Float3(1.4f, 2.2f, 0.12f), Wood, 10f);
        door.AngularDamping = 0.2f;
        _door = door.GameObject.AddComponent<HingeJoint>();
        _door.Anchor = new Float3(-0.7f, 0f, 0f);
        _door.Axis = Float3.UnitY;
        _door.MinAngle = -110f;
        _door.MaxAngle = 110f;

        // Piston, pushing a crate up and down.
        Static("Piston Base", new Float3(1f, 0.4f, 1f), c + new Float3(-7.5f, 0.2f, 0f));
        Rigidbody3D head = Crate(c + new Float3(-7.5f, 0.6f, 0f), new Float3(0.9f, 0.3f, 0.9f), new Color(0.15f, 0.15f, 0.17f, 1f), 2f);
        _piston = head.GameObject.AddComponent<PrismaticJoint>();
        _piston.Axis = Float3.UnitY;
        _piston.MinDistance = 0f;
        _piston.MaxDistance = 2.5f;
        _piston.HasMotor = true;
        _piston.MotorMaxForce = 400f;
        head.GameObject.AddComponent<PistonDriver>().Joint = _piston;
        Crate(c + new Float3(-7.5f, 1.2f, 0f), new Float3(0.6f), Orange);

        // Gantry for the paddle and the chain.
        Static("Gantry", new Float3(6f, 0.3f, 0.3f), c + new Float3(-1.5f, 6f, 0f));
        Static("Gantry Post", new Float3(0.3f, 6f, 0.3f), c + new Float3(-4.35f, 3f, 0f));
        Static("Gantry Post", new Float3(0.3f, 6f, 0.3f), c + new Float3(1.35f, 3f, 0f));

        _paddle = Crate(c + new Float3(-3f, 4.9f, 0f), new Float3(0.9f, 1.8f, 0.1f), Teal, 2f);
        var universal = _paddle.GameObject.AddComponent<UniversalJoint>();
        universal.Anchor = new Float3(0f, 0.9f, 0f);
        universal.Axis1 = Float3.UnitX;
        universal.Axis2 = Float3.UnitZ;

        Rigidbody3D previous = null!;
        for (int i = 0; i < 12; i++)
        {
            Rigidbody3D link = Body("Link", Mesh.CreateCapsule(0.09f, 0.42f, 8, 2), new Color(0.3f, 0.3f, 0.32f, 1f), c + new Float3(0f, 5.6f - i * 0.42f, 0f), Quaternion.Identity, 0.3f);
            var collider = link.GameObject.AddComponent<CapsuleCollider>();
            collider.Radius = 0.09f;
            collider.Height = 0.42f;
            var socket = link.GameObject.AddComponent<BallSocketConstraint>();
            socket.Anchor = new Float3(0f, 0.21f, 0f);
            if (i > 0) socket.ConnectedBody = previous;
            Add(link.GameObject);
            previous = link;
        }
        _chainEnd = previous;

        // Rope bridge between two posts.
        _bridgeCenter = c + new Float3(8.5f, 0f, 0f);
        const int Planks = 10;
        const float Pitch = 0.85f;
        float start = 8.5f - Planks * Pitch * 0.5f;
        Static("Bridge Post", new Float3(0.4f, 2.4f, 1.6f), c + new Float3(start - 0.25f, 1.2f, 0f));
        Static("Bridge Post", new Float3(0.4f, 2.4f, 1.6f), c + new Float3(start + Planks * Pitch + 0.25f, 1.2f, 0f));

        // The planks are laid along a sagging curve, so the bridge hangs slack instead of being pulled
        // straight, which no chain of joints can hold still against gravity.
        const float Sag = 0.7f;
        Float3 Knot(int j)
        {
            float u = 2f * j / Planks - 1f;
            return c + new Float3(start + j * Pitch, 2.3f - Sag * (1f - u * u), 0f);
        }

        Rigidbody3D lastPlank = null!;
        for (int i = 0; i < Planks; i++)
        {
            Float3 a = Knot(i), b = Knot(i + 1);
            float span = Float3.Distance(a, b);
            Quaternion rotation = Quaternion.FromToRotation(Float3.UnitX, (b - a) / span);
            Rigidbody3D plank = Crate((a + b) * 0.5f, new Float3(span - 0.06f, 0.1f, 1.2f), i % 2 == 0 ? Wood : new Color(0.3f, 0.14f, 0.04f, 1f), 2f, rotation);
            plank.LinearDamping = 0.05f;
            plank.AngularDamping = 0.05f;

            var hinge = plank.GameObject.AddComponent<HingeJoint>();
            hinge.Anchor = new Float3(-span * 0.5f, 0f, 0f);
            hinge.Axis = Float3.UnitZ;
            if (lastPlank.IsValid()) hinge.ConnectedBody = lastPlank;

            if (i == Planks - 1)
            {
                var end = plank.GameObject.AddComponent<HingeJoint>();
                end.Anchor = new Float3(span * 0.5f, 0f, 0f);
                end.Axis = Float3.UnitZ;
            }

            lastPlank = plank;
        }
    }

    private void JointControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Door");
        Button(paper, font, "Kick the door", () => _door.GameObject.GetComponent<Rigidbody3D>()!.ApplyImpulse(new Float3(0f, 0f, 30f), _door.Transform.TransformPoint(new Float3(0.6f, 0f, 0f))));
        Toggle(paper, font, "Limit the swing", _door.MinAngle > -180f, v => { _door.MinAngle = v ? -110f : -180f; _door.MaxAngle = v ? 110f : 180f; });

        Header(paper, font, "Piston", 1);
        PistonDriver driver = _piston.GameObject.GetComponent<PistonDriver>()!;
        Slider(paper, font, "Speed", driver.Speed, 0f, 4f, v => driver.Speed = v, "0.0");

        Header(paper, font, "Paddle and chain", 2);
        Button(paper, font, "Push the paddle", () => _paddle.ApplyImpulse(new Float3(4f, 0f, 4f), _paddle.Transform.TransformPoint(new Float3(0.4f, -0.9f, 0f))));
        Button(paper, font, "Swing the chain", () => _chainEnd.ApplyImpulse(new Float3(3f, 0f, 2f)));

        Header(paper, font, "Bridge", 3);
        Button(paper, font, "Drop crates on the bridge", () =>
        {
            for (int i = 0; i < 4; i++)
                Crate(_bridgeCenter + new Float3(Range(-3f, 3f), Range(4f, 6f), Range(-0.3f, 0.3f)), new Float3(0.5f), RandomColor(), 2f, RandomRotation());
        });
    }

    // ----------------------------------------------------------------
    //  5  Motors and limits
    // ----------------------------------------------------------------

    private Rigidbody3D _windmill = null!;
    private AngularMotorConstraint _windmillMotor = null!;
    private Rigidbody3D _elevator = null!;
    private ElevatorDriver _elevatorDriver = null!;
    private HingeJoint _elbow = null!;
    private Rigidbody3D _forearm = null!;

    private void BuildMotors(Float3 c)
    {
        AddStation("Motors and limits", "None of these use a joint component. The windmill is a BallSocket, a HingeAngle and an AngularMotor. The elevator is a PointOnLine, a FixedAngle and a LinearMotor. The arm hangs from a BallSocket with a ConeLimit and a TwistAngle at the shoulder, and its elbow only bends one way.", c, new Float3(0f, 5f, -15f), 2.5f);

        // Windmill: hub plus four blades as child colliders of one body.
        Static("Windmill Tower", new Float3(0.6f, 4.4f, 0.6f), c + new Float3(-7f, 2.2f, 0.4f), material: _stone);
        _windmill = Body("Rotor", Mesh.CreateCylinder(0.35f, 0.4f, 16), new Color(0.2f, 0.2f, 0.22f, 1f), c + new Float3(-7f, 4.6f, -0.2f), Quaternion.FromEuler(new Float3(90f, 0f, 0f)), 20f);
        var hub = _windmill.GameObject.AddComponent<CylinderCollider>();
        hub.Radius = 0.35f;
        hub.Height = 0.4f;
        for (int i = 0; i < 4; i++)
        {
            GameObject blade = Model("Blade", Mesh.CreateCube(new Float3(0.5f, 0.08f, 2.4f)), Lit(new Color(0.6f, 0.6f, 0.58f, 1f), 0f, 0.7f), Float3.Zero);
            blade.SetParent(_windmill.GameObject);
            blade.Transform.LocalRotation = Quaternion.FromEuler(new Float3(0f, i * 90f, 0f));
            blade.Transform.LocalPosition = blade.Transform.LocalRotation * new Float3(0f, 0f, 1.5f);
            blade.AddComponent<BoxCollider>().Size = new Float3(0.5f, 0.08f, 2.4f);
        }
        _windmill.GameObject.AddComponent<BallSocketConstraint>();
        _windmill.GameObject.AddComponent<HingeAngleConstraint>().HingeAxis = Float3.UnitY;
        _windmillMotor = _windmill.GameObject.AddComponent<AngularMotorConstraint>();
        _windmillMotor.Axis1 = Float3.UnitY;
        _windmillMotor.Axis2 = _windmill.Transform.Up;
        _windmillMotor.TargetVelocity = 1.5f;
        _windmillMotor.MaximumForce = 4000f;
        Add(_windmill.GameObject);

        // Elevator: a platform kept level and on a vertical line, driven by a motor.
        Static("Elevator Shaft", new Float3(0.3f, 6f, 0.3f), c + new Float3(-1.6f, 3f, 1.6f), material: _stone);
        Static("Elevator Shaft", new Float3(0.3f, 6f, 0.3f), c + new Float3(1.6f, 3f, 1.6f), material: _stone);
        _elevator = Crate(c + new Float3(0f, 0.25f, 0f), new Float3(2.6f, 0.2f, 2.6f), new Color(0.12f, 0.12f, 0.14f, 1f), 30f);
        _elevator.AffectedByGravity = false;
        var line = _elevator.GameObject.AddComponent<PointOnLineConstraint>();
        line.LineAxis = Float3.UnitY;
        line.Anchor2 = _elevator.Transform.Position;
        _elevator.GameObject.AddComponent<FixedAngleConstraint>();
        var lift = _elevator.GameObject.AddComponent<LinearMotorConstraint>();
        lift.Axis1 = Float3.UnitY;
        lift.Axis2 = Float3.UnitY;
        lift.MaximumForce = 5000f;
        _elevatorDriver = _elevator.GameObject.AddComponent<ElevatorDriver>();
        _elevatorDriver.Motor = lift;
        _elevatorDriver.Bottom = 0.25f;
        _elevatorDriver.Top = 5f;
        for (int i = 0; i < 3; i++)
            Crate(c + new Float3(-0.7f + i * 0.7f, 0.7f, 0f), new Float3(0.5f), Palette[i]);

        // Arm hanging from a shoulder.
        Static("Shoulder", new Float3(0.5f, 0.5f, 0.5f), c + new Float3(7f, 4.5f, 0f), material: _stone);
        Rigidbody3D upper = Body("Upper Arm", Mesh.CreateCapsule(0.16f, 1.1f), Blue, c + new Float3(7f, 3.6f, 0f), Quaternion.Identity, 2f);
        var upperCollider = upper.GameObject.AddComponent<CapsuleCollider>();
        upperCollider.Radius = 0.16f;
        upperCollider.Height = 1.1f;
        upper.GameObject.AddComponent<BallSocketConstraint>().Anchor = new Float3(0f, 0.65f, 0f);
        var cone = upper.GameObject.AddComponent<ConeLimitConstraint>();
        cone.Axis = Float3.UnitY;
        cone.MaxAngle = 50f;
        var twist = upper.GameObject.AddComponent<TwistAngleConstraint>();
        twist.Axis1 = Float3.UnitY;
        twist.Axis2 = Float3.UnitY;
        twist.MinAngle = -20f;
        twist.MaxAngle = 20f;
        Add(upper.GameObject);

        _forearm = Body("Forearm", Mesh.CreateCapsule(0.13f, 1f), Teal, c + new Float3(7f, 2.5f, 0f), Quaternion.Identity, 1.5f);
        var forearmCollider = _forearm.GameObject.AddComponent<CapsuleCollider>();
        forearmCollider.Radius = 0.13f;
        forearmCollider.Height = 1f;
        _elbow = _forearm.GameObject.AddComponent<HingeJoint>();
        _elbow.ConnectedBody = upper;
        _elbow.Anchor = new Float3(0f, 0.55f, 0f);
        _elbow.Axis = Float3.UnitX;
        _elbow.MinAngle = -135f;
        _elbow.MaxAngle = 0f;
        Add(_forearm.GameObject);
    }

    private void MotorControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Windmill");
        Slider(paper, font, "Target speed", _windmillMotor.TargetVelocity, -6f, 6f, v => _windmillMotor.TargetVelocity = v, "0.0");
        Slider(paper, font, "Motor strength", _windmillMotor.MaximumForce, 0f, 8000f, v => _windmillMotor.MaximumForce = v, "0");

        Header(paper, font, "Elevator", 1);
        Slider(paper, font, "Speed", _elevatorDriver.Speed, 0f, 4f, v => _elevatorDriver.Speed = v, "0.0");
        Button(paper, font, "Load crates", () =>
        {
            Float3 top = _elevator.Transform.Position;
            for (int i = 0; i < 3; i++)
                Crate(top + new Float3(Range(-0.8f, 0.8f), 1f + i * 0.6f, Range(-0.8f, 0.8f)), new Float3(0.5f), RandomColor());
        });

        Header(paper, font, "Arm", 2);
        Button(paper, font, "Shove the arm", () => _forearm.ApplyImpulse(new Float3(0f, 2f, 6f), _forearm.Transform.TransformPoint(new Float3(0f, -0.5f, 0f))));
    }

    // ----------------------------------------------------------------
    //  6  Queries
    // ----------------------------------------------------------------

    private QueryProbes _queries = null!;

    private void BuildQueries(Float3 c)
    {
        AddStation("Queries", "The turret sweeps a raycast around the pillars and draws the hit point and surface normal. Along the lane a sphere cast reports how far a ball could roll before the sliding block gets in its way. On the right an overlap sphere pulses and lists every body inside it.", c, new Float3(0f, 9f, -13f), 0f);

        var probes = new GameObject("Query Probes");
        _queries = probes.AddComponent<QueryProbes>();
        _queries.Turret = c + new Float3(-7f, 1f, 0f);
        _queries.LaneStart = c + new Float3(-1f, 0.6f, -4f);
        _queries.Zone = c + new Float3(7f, 1f, 0f);
        Add(probes);

        Add(Model("Turret", Mesh.CreateCylinder(0.35f, 1f, 16), Lit(Orange).Emissive(Orange, 2f), c + new Float3(-7f, 0.5f, 0f)));
        for (int i = 0; i < 7; i++)
        {
            float angle = i * MathF.PI * 2f / 7f;
            float radius = i % 2 == 0 ? 4f : 5.5f;
            Float3 position = c + new Float3(-7f + MathF.Sin(angle) * radius, 1.25f, MathF.Cos(angle) * radius);
            GameObject pillar = Model("Pillar", Mesh.CreateCylinder(0.4f, 2.5f, 16), _stone, position);
            var collider = pillar.AddComponent<CylinderCollider>();
            collider.Radius = 0.4f;
            collider.Height = 2.5f;
            Add(pillar);
        }

        // The sliding block is kinematic and driven by its Transform alone.
        Static("Lane", new Float3(0.2f, 0.4f, 12f), c + new Float3(-2.2f, 0.2f, 2f), material: _stone);
        Static("Lane", new Float3(0.2f, 0.4f, 12f), c + new Float3(0.2f, 0.2f, 2f), material: _stone);
        Rigidbody3D slider = Crate(c + new Float3(-1f, 0.6f, 3f), new Float3(1.2f), Red);
        slider.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        var swing = slider.GameObject.AddComponent<Oscillate>();
        swing.Origin = slider.Transform.Position;
        swing.Offset = new Float3(2.5f, 0f, 0f);

        for (int i = 0; i < 8; i++)
            Crate(c + new Float3(7f + Range(-3f, 3f), 0.4f, Range(-3f, 3f)), new Float3(0.6f), RandomColor(), 1f, Quaternion.FromEuler(new Float3(0f, Range(0f, 90f), 0f)));
    }

    // ----------------------------------------------------------------
    //  7  Triggers
    // ----------------------------------------------------------------

    private readonly List<TriggerGate> _gates = new();
    private Float3 _releasePoint;
    private readonly List<GameObject> _triggerBalls = new();

    private Func<string> _gateStatus = () => string.Empty;

    private void BuildTriggers(Float3 c)
    {
        AddStation("Triggers", "Each gate holds a TriggerVolume. It is not solid, it only reports. The gate glows while anything is inside it and counts every OnTriggerEnter. The pit at the end is a trigger too, and sends each ball back to the top from inside its own callback.", c, new Float3(-2f, 6f, -14f), 1f);

        GameObject ramp = Static("Trigger Ramp", new Float3(16f, 0.3f, 3f), c + new Float3(-2f, 2f, 0f), new Float3(0f, 0f, -12f), _stone);
        Static("Rail", new Float3(16f, 0.6f, 0.2f), c + new Float3(-2f, 2.3f, 1.6f), new Float3(0f, 0f, -12f), _stone);
        Static("Rail", new Float3(16f, 0.6f, 0.2f), c + new Float3(-2f, 2.3f, -1.6f), new Float3(0f, 0f, -12f), _stone);

        Transform rampTransform = ramp.Transform;
        float top = rampTransform.TransformPoint(new Float3(1f, 0f, 0f)).Y > rampTransform.Position.Y ? 7f : -7f;
        _releasePoint = rampTransform.TransformPoint(new Float3(top, 1f, 0f));

        Color[] colors = [Teal, Yellow, Purple];
        for (int i = 0; i < 3; i++)
        {
            Float3 at = rampTransform.TransformPoint(new Float3(-top * 0.6f + top * 0.6f * i, 0f, 0f));
            _gates.Add(BuildGate(at, colors[i], rampTransform.Rotation));
        }

        // Runout and the pit that recycles balls.
        Float3 low = rampTransform.TransformPoint(new Float3(-top, 0f, 0f));
        float direction = MathF.Sign(low.X - rampTransform.Position.X);
        var pit = new GameObject("Recycler");
        pit.Transform.Position = new Float3(low.X + direction * 3f, 0.6f, c.Z);
        var volume = pit.AddComponent<TriggerVolume>();
        volume.Size = new Float3(2f, 1.2f, 4f);
        pit.AddComponent<Recycler>().Target = () => _releasePoint + new Float3(0f, 0f, Range(-0.8f, 0.8f));
        Add(pit);
        Add(Model("Pit", Plane(2f, 4f), Lit(new Color(0.02f, 0.02f, 0.03f, 1f)).Emissive(Orange, 1.5f), pit.Transform.Position - new Float3(0f, 0.59f, 0f)));
        Static("Backstop", new Float3(0.3f, 1.5f, 4f), pit.Transform.Position + new Float3(direction * 1.3f, 0.15f, 0f));

        for (int i = 0; i < 6; i++) ReleaseBall();

        _gateStatus = () => string.Join("    ", _gates.Select((g, i) => $"Gate {i + 1}: {g.Count} entered, {g.Inside} inside"));
    }

    private GameObject GateFrame(string name, Float3 size, Float3 position, Quaternion rotation, Material material)
    {
        GameObject go = Model(name, Mesh.CreateCube(size), material, position);
        go.Transform.Rotation = rotation;
        return Add(go);
    }

    private TriggerGate BuildGate(Float3 at, Color color, Quaternion rotation)
    {
        Material glow = Lit(color, 0f, 0.4f).Emissive(color, 0f);
        Float3 up = rotation * Float3.UnitY;
        GateFrame("Gate Post", new Float3(0.2f, 2f, 0.2f), at + up * 1f + new Float3(0f, 0f, 1.9f), rotation, glow);
        GateFrame("Gate Post", new Float3(0.2f, 2f, 0.2f), at + up * 1f + new Float3(0f, 0f, -1.9f), rotation, glow);
        GateFrame("Gate Lintel", new Float3(0.2f, 0.2f, 4f), at + up * 2f, rotation, glow);

        var gate = new GameObject("Gate");
        gate.Transform.Position = at + up * 0.9f;
        gate.Transform.Rotation = rotation;
        var volume = gate.AddComponent<TriggerVolume>();
        volume.Size = new Float3(0.6f, 1.6f, 3.4f);
        TriggerGate counter = gate.AddComponent<TriggerGate>();
        counter.Glow = glow;
        Add(gate);
        return counter;
    }

    private void ReleaseBall()
    {
        Rigidbody3D ball = Ball(_releasePoint + new Float3(0f, Range(0f, 1f), Range(-0.8f, 0.8f)), 0.3f, RandomColor());
        _triggerBalls.Add(ball.GameObject);
    }

    private void TriggerControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Balls");
        Button(paper, font, "Release another ball", ReleaseBall);
        Button(paper, font, "Remove all balls", () => DestroyAll(_triggerBalls));
        Button(paper, font, "Reset the counters", () => { foreach (TriggerGate gate in _gates) gate.Count = 0; });
    }
}

/// <summary>Glows briefly when its body lands hard, scaled by the impulse of the contact.</summary>
public sealed class ImpactFlash : Component
{
    private Material? _material;
    private float _mass = 1f;
    private float _glow;

    public override void OnEnable()
    {
        MeshRenderer? renderer = GetComponentInChildren<MeshRenderer>();
        _material = renderer.IsValid() ? renderer.Material : null;
        Rigidbody3D? rb = GetComponent<Rigidbody3D>();
        _mass = rb.IsValid() ? rb.Mass : 1f;
    }

    public override void OnCollisionBegin(Collision collision)
    {
        float change = collision.ImpulseMagnitude / _mass;
        _glow = MathF.Max(_glow, Math.Clamp((change - 1f) / 5f, 0f, 1f));
    }

    public override void Update()
    {
        if (_material == null || _glow <= 0f) return;
        _glow = MathF.Max(0f, _glow - Time.DeltaTime * 2.5f);
        _material.SetFloat("_EmissionIntensity", _glow * 6f);
    }
}

/// <summary>Counts what is touching a static collider, lit by how much rests on it.</summary>
public sealed class ContactCounter : Component
{
    public int Touching;
    private Material? _material;

    public override void OnEnable()
    {
        MeshRenderer? renderer = GetComponent<MeshRenderer>();
        _material = renderer.IsValid() ? renderer.Material : null;
    }

    public override void OnCollisionBegin(Collision collision) => Touching++;
    public override void OnCollisionEnd(Collision collision) => Touching--;

    public override void Update()
    {
        if (_material != null) _material.SetFloat("_EmissionIntensity", MathF.Min(Touching * 0.05f, 1f));
    }
}

/// <summary>Flips a prismatic motor up and down at its limits.</summary>
public sealed class PistonDriver : Component
{
    public PrismaticJoint Joint = null!;
    public float Speed = 1.5f;
    private bool _up = true;

    public override void FixedUpdate()
    {
        float distance = Joint.CurrentDistance;
        if (_up && distance > 2.3f) _up = false;
        else if (!_up && distance < 0.1f) _up = true;
        Joint.MotorTargetVelocity = _up ? Speed : -Speed;
    }
}

/// <summary>Runs a linear motor between two heights.</summary>
public sealed class ElevatorDriver : Component
{
    public LinearMotorConstraint Motor = null!;
    public float Bottom, Top;
    public float Speed = 1.2f;
    private bool _up = true;
    private float _wait;

    public override void FixedUpdate()
    {
        float y = Transform.Position.Y;
        if (_wait > 0f)
        {
            _wait -= Time.FixedDeltaTime;
            Motor.TargetVelocity = 0f;
            return;
        }

        if (_up && y > Top) { _up = false; _wait = 1.5f; }
        else if (!_up && y < Bottom) { _up = true; _wait = 1.5f; }
        Motor.TargetVelocity = _up ? Speed : -Speed;
    }
}

/// <summary>Moves back and forth by writing the Transform, which a kinematic body follows.</summary>
public sealed class Oscillate : Component
{
    public Float3 Origin;
    public Float3 Offset;
    public float Period = 4f;

    public override void Update()
        => Transform.Position = Origin + Offset * MathF.Sin(Time.TimeSinceStartup * MathF.PI * 2f / Period);
}

/// <summary>Runs the three query demos every frame and draws them as gizmos.</summary>
public sealed class QueryProbes : Component
{
    public Float3 Turret;
    public Float3 LaneStart;
    public Float3 Zone;
    public string Status = string.Empty;

    private static readonly Color HitColor = new(0.3f, 1f, 0.4f, 1f);
    private static readonly Color MissColor = new(1f, 0.3f, 0.25f, 1f);
    private static readonly Color NormalColor = new(1f, 0.85f, 0.2f, 1f);

    private Float3 _rayDirection;
    private RaycastHit _rayHit;
    private bool _rayHits;
    private ShapeCastHit _sphereHit;
    private bool _sphereHits;
    private float _zoneRadius;
    private readonly List<ShapeCastHit> _overlaps = new();

    private const float RayLength = 9f;
    private const float LaneLength = 11f;
    private const float BallRadius = 0.4f;

    public override void Update()
    {
        PhysicsWorld physics = GameObject.Scene.Physics;

        float angle = Time.TimeSinceStartup * 0.8f;
        _rayDirection = new Float3(MathF.Sin(angle), 0f, MathF.Cos(angle));
        _rayHits = physics.Raycast(Turret, _rayDirection, RayLength, out _rayHit);

        _sphereHits = physics.SphereCast(LaneStart, BallRadius, Float3.UnitZ, LaneLength, out _sphereHit);

        _zoneRadius = 1.6f + MathF.Sin(Time.TimeSinceStartup * 1.3f) * 0.9f;
        physics.OverlapSphere(Zone, _zoneRadius, _overlaps);
        _overlaps.RemoveAll(hit => hit.Rigidbody.IsNotValid());

        string ray = _rayHits && _rayHit.Transform != null ? $"Ray hit {_rayHit.Transform.GameObject.Name} at {_rayHit.Distance:0.0} m" : "Ray hit nothing";
        string sphere = _sphereHits ? $"sphere cast stopped at {_sphereHit.Distance:0.0} m" : "sphere cast is clear";
        Status = $"{ray}    {sphere}    overlap holds {_overlaps.Count}";
    }

    public override void DrawGizmos()
    {
        if (_rayHits)
        {
            Debug.DrawLine(Turret, _rayHit.Point, HitColor);
            Debug.DrawWireSphere(_rayHit.Point, 0.08f, HitColor);
            Debug.DrawArrow(_rayHit.Point, _rayHit.Normal * 0.8f, NormalColor);
        }
        else
        {
            Debug.DrawLine(Turret, Turret + _rayDirection * RayLength, MissColor);
        }

        Float3 end = LaneStart + Float3.UnitZ * (_sphereHits ? _sphereHit.Distance : LaneLength);
        Debug.DrawWireSphere(LaneStart, BallRadius, HitColor);
        Debug.DrawLine(LaneStart, end, _sphereHits ? MissColor : HitColor);
        Debug.DrawWireSphere(end, BallRadius, _sphereHits ? MissColor : HitColor);
        if (_sphereHits) Debug.DrawArrow(_sphereHit.HitPoint, _sphereHit.Normal * 0.8f, NormalColor);

        Debug.DrawWireSphere(Zone, _zoneRadius, _overlaps.Count > 0 ? NormalColor : HitColor, 24);
        foreach (ShapeCastHit hit in _overlaps)
            if (hit.Transform != null)
                Debug.DrawLine(Zone, hit.Transform.Position, NormalColor);
    }
}

/// <summary>Counts trigger entries and glows while anything is inside.</summary>
public sealed class TriggerGate : Component
{
    public Material Glow = null!;
    public int Count;
    public int Inside;
    private float _glow;

    public override void OnTriggerEnter(Rigidbody3D other) { Count++; Inside++; }
    public override void OnTriggerExit(Rigidbody3D other) => Inside = Math.Max(0, Inside - 1);

    public override void Update()
    {
        float target = Inside > 0 ? 4f : 0.2f;
        _glow += (target - _glow) * MathF.Min(1f, Time.DeltaTime * 12f);
        Glow.SetFloat("_EmissionIntensity", _glow);
    }
}

/// <summary>Sends anything that falls in back to the top of the ramp.</summary>
public sealed class Recycler : Component
{
    public Func<Float3> Target = () => Float3.Zero;

    public override void OnTriggerEnter(Rigidbody3D other)
    {
        other.MovePosition(Target());
        other.LinearVelocity = Float3.Zero;
        other.AngularVelocity = Float3.Zero;
    }
}
