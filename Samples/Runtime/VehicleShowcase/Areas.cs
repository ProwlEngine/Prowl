// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace VehicleShowcase;

/// <summary>
/// The world the vehicles drive around. North of the car park is the asphalt circuit, with a drift pad, a bowling
/// lane and a seesaw inside it; south is the dirt rally track. West are the rough ground, the hill climb and the ice
/// rink; east are the jumps and the moving platforms.
/// </summary>
public sealed partial class VehicleShowcaseGame
{
    private readonly List<LapTimer> _lapTimers = new();

    private void BuildWorld()
    {
        Add(Block("Floor", new Float3(1000f, 1f, 800f), Floor(1000f, 800f), new Float3(0f, -0.5f, 100f)));

        BuildCarPark(Float3.Zero);
        BuildCircuit(new Float3(0f, 0f, 50f));
        BuildDirtTrack(new Float3(0f, 0f, -150f));
        BuildDriftPad(new Float3(-60f, 0f, 150f));
        BuildBowling(new Float3(60f, 0f, 130f));
        BuildSeesaw(new Float3(0f, 0f, 205f));
        BuildRoughGround(new Float3(-320f, 0f, 0f));
        BuildHillClimb(new Float3(-320f, 0f, 140f));
        BuildIceRink(new Float3(-260f, 0f, -170f));
        BuildJumps(new Float3(320f, 0f, -40f));
        BuildTurntable(new Float3(235f, 0f, -100f));
        BuildFerry(new Float3(275f, 0f, -170f));
        BuildLift(new Float3(215f, 0f, -200f));
    }

    // ----------------------------------------------------------------
    //  Building blocks
    // ----------------------------------------------------------------

    private GameObject Static(string name, Float3 size, Float3 position, Float3? euler = null, Material? material = null)
        => Add(Block(name, size, material ?? _dark, position, euler));

    private float Range(float min, float max) => min + _rng.NextSingle() * (max - min);

    private Color RandomColor() => Palette[_rng.Next(Palette.Length)];

    private Rigidbody3D Crate(Float3 position, Float3 size, Color color, float mass = 1f)
    {
        GameObject go = Model("Crate", Mesh.CreateCube(size), Lit(color, 0f, 0.5f), position);
        var rb = go.AddComponent<Rigidbody3D>();
        rb.Mass = mass;
        go.AddComponent<BoxCollider>().Size = size;
        Add(go);
        return rb;
    }

    /// <summary>
    /// A solid ramp running along Z from <paramref name="z0"/> to <paramref name="z1"/>, its top rising from
    /// <paramref name="h0"/> to <paramref name="h1"/> above <paramref name="baseY"/>, with a mesh collider to drive on.
    /// </summary>
    private GameObject Wedge(string name, float x, float z0, float z1, float h0, float h1, float width, Material material, float baseY = 0f)
    {
        float half = width * 0.5f;
        Float3 P(float side, float z, float h) => new(x + side * half, baseY + h, z);
        float length = MathF.Abs(z1 - z0);
        var builder = new MeshBuilder();
        Float3 slope = Float3.Normalize(new Float3(0f, z1 - z0, -(h1 - h0)));
        if (slope.Y < 0f) slope = -slope;
        builder.Quad(P(-1, z0, h0), P(1, z0, h0), P(1, z1, h1), P(-1, z1, h1), new(0f, 0f), new(width * 0.5f, 0f), new(width * 0.5f, length * 0.5f), new(0f, length * 0.5f), slope);
        builder.Quad(P(-1, z0, 0f), P(1, z0, 0f), P(1, z1, 0f), P(-1, z1, 0f), default, default, default, default, -Float3.UnitY);
        foreach (float side in new[] { -1f, 1f })
            builder.Quad(P(side, z0, 0f), P(side, z1, 0f), P(side, z1, h1), P(side, z0, h0), new(0f, 0f), new(length * 0.5f, 0f), new(length * 0.5f, h1 * 0.5f), new(0f, h0 * 0.5f), new Float3(side, 0f, 0f));
        float end0 = MathF.Sign(z0 - z1);
        builder.Quad(P(-1, z0, 0f), P(1, z0, 0f), P(1, z0, h0), P(-1, z0, h0), default, default, default, default, new Float3(0f, 0f, end0));
        builder.Quad(P(-1, z1, 0f), P(1, z1, 0f), P(1, z1, h1), P(-1, z1, h1), default, default, default, default, new Float3(0f, 0f, -end0));

        Mesh mesh = builder.Build();
        GameObject go = Model(name, mesh, material, Float3.Zero);
        go.AddComponent<MeshCollider>().Mesh = mesh;
        return Add(go);
    }

    private Material Textured(string texture, Color tint, float roughness, float tiling)
        => Lit(tint, 0f, roughness).With("_MainTex", Load<Texture2D>(texture)).Tiled(tiling, tiling);

    private static Material Grid(Color tint) => Lit(tint, 0f, 0.85f).With("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid));

    // ----------------------------------------------------------------
    //  Car park
    // ----------------------------------------------------------------

    private void BuildCarPark(Float3 c)
    {
        for (int i = 0; i < 6; i++)
            Static("Bump", new Float3(6f, 0.2f, 0.4f), c + new Float3(-12f, 0.1f, 4f + i * 1.6f), material: _stone);
        Static("Kicker", new Float3(5f, 0.4f, 6f), c + new Float3(14f, 0.7f, 8f), new Float3(-12f, 0f, 0f), _stone);
        for (int i = 0; i < 12; i++)
            Crate(c + new Float3(Range(-16f, -6f), 0.3f, Range(16f, 28f)), new Float3(0.6f), RandomColor());
        for (int i = 0; i < 5; i++)
            Crate(c + new Float3(16f, 0.5f + i * 1.0f, 22f), new Float3(1f), Palette[i], 3f);
    }

    // ----------------------------------------------------------------
    //  Race tracks
    // ----------------------------------------------------------------

    /// <summary>
    /// A closed loop of road following <paramref name="path"/> from t = 0 to 2 pi, starting at t = 0, with a start
    /// line and a TriggerVolume that times laps. Returns the road so a surface can be put on it.
    /// </summary>
    private GameObject Loop(string name, Func<float, Float3> path, float width, int segments, Material road, bool kerbs, Func<float, float>? bumps = null)
    {
        Float3 Side(float t)
        {
            Float3 tangent = path(t + 0.001f) - path(t - 0.001f);
            tangent.Y = 0f;
            return -Float3.Normalize(Float3.Cross(tangent, Float3.UnitY));
        }

        var surface = new MeshBuilder();
        var skirt = new MeshBuilder();
        var red = new MeshBuilder();
        var white = new MeshBuilder();
        var lines = new MeshBuilder();
        float travelled = 0f;
        Float3 lift = new(0f, 0.02f, 0f);

        for (int i = 0; i < segments; i++)
        {
            float t0 = i * MathF.PI * 2f / segments, t1 = (i + 1) * MathF.PI * 2f / segments;
            Float3 p0 = path(t0), p1 = path(t1);
            Float3 s0 = Side(t0), s1 = Side(t1);
            float length = Float3.Distance(p0, p1);
            Float3 b0 = new(0f, bumps?.Invoke(travelled) ?? 0f, 0f), b1 = new(0f, bumps?.Invoke(travelled + length) ?? 0f, 0f);

            Float3 l0 = p0 - s0 * width * 0.5f + b0, r0 = p0 + s0 * width * 0.5f + b0;
            Float3 l1 = p1 - s1 * width * 0.5f + b1, r1 = p1 + s1 * width * 0.5f + b1;
            surface.Quad(l0, r0, r1, l1, new Float2(0f, travelled / width), new Float2(1f, travelled / width), new Float2(1f, (travelled + length) / width), new Float2(0f, (travelled + length) / width), Float3.UnitY);
            travelled += length;

            skirt.Quad(l0, l1, new Float3(l1.X, -0.1f, l1.Z), new Float3(l0.X, -0.1f, l0.Z), default, default, default, default, -s0);
            skirt.Quad(r0, r1, new Float3(r1.X, -0.1f, r1.Z), new Float3(r0.X, -0.1f, r0.Z), default, default, default, default, s0);

            if (!kerbs) continue;
            MeshBuilder kerb = (i / 2) % 2 == 0 ? red : white;
            kerb.Quad(l0 + lift, l0 + s0 * 0.8f + lift, l1 + s1 * 0.8f + lift, l1 + lift, default, default, default, default, Float3.UnitY);
            kerb.Quad(r0 - s0 * 0.8f + lift, r0 + lift, r1 + lift, r1 - s1 * 0.8f + lift, default, default, default, default, Float3.UnitY);
            if ((i / 3) % 2 == 0)
                lines.Quad(p0 - s0 * 0.12f + lift + b0, p0 + s0 * 0.12f + lift + b0, p1 + s1 * 0.12f + lift + b1, p1 - s1 * 0.12f + lift + b1, default, default, default, default, Float3.UnitY);
        }

        Mesh roadMesh = surface.Build();
        GameObject track = Model(name, roadMesh, road, Float3.Zero);
        track.AddComponent<MeshCollider>().Mesh = roadMesh;
        Add(track);
        Add(Model(name + " Sides", skirt.Build(), _dark, Float3.Zero));
        if (kerbs)
        {
            Material paint = Lit(new Color(0.7f, 0.7f, 0.7f, 1f), 0f, 0.6f);
            Add(Model("Kerbs", red.Build(), Lit(new Color(0.6f, 0.02f, 0.02f, 1f), 0f, 0.6f), Float3.Zero));
            Add(Model("Kerbs", white.Build(), paint, Float3.Zero));
            Add(Model("Lines", lines.Build(), paint, Float3.Zero));
        }

        // The start line, with a gantry over it and a gate that times each lap.
        const float LineT = 0.02f;
        Float3 start = path(LineT) + new Float3(0f, bumps?.Invoke(0f) ?? 0f, 0f);
        Quaternion lineRotation = Quaternion.LookRotation(Float3.Normalize(path(LineT + 0.01f) - path(LineT)), Float3.UnitY);
        GameObject line = Model("Start Line", Plane(width, 1.5f), Lit(Color.White, 0f, 0.7f).With("_MainTex", Load<Texture2D>("Textures/Checker")).Tiled(width * 0.5f, 1f), start + new Float3(0f, 0.03f, 0f));
        line.Transform.Rotation = lineRotation;
        Add(line);

        var gate = new GameObject(name + " Lap Gate");
        gate.Transform.Position = start + new Float3(0f, 1.5f, 0f);
        gate.Transform.Rotation = lineRotation;
        gate.AddComponent<TriggerVolume>().Size = new Float3(width, 3f, 1f);
        LapTimer timer = gate.AddComponent<LapTimer>();
        timer.Track = name;
        _lapTimers.Add(timer);
        Add(gate);

        foreach (float side in new[] { -1f, 1f })
            Static("Gantry Post", new Float3(0.4f, 5f, 0.4f), start + Side(LineT) * (width * 0.5f + 0.6f) * side + new Float3(0f, 2.5f, 0f));
        GameObject banner = Model("Gantry", Mesh.CreateCube(new Float3(width + 1.6f, 0.8f, 0.4f)), Lit(new Color(0.02f, 0.02f, 0.03f, 1f)).Emissive(Orange, 1.2f), start + new Float3(0f, 5.2f, 0f));
        banner.Transform.Rotation = lineRotation;
        Add(banner);
        return track;
    }

    // A long asphalt circuit around a wobbly ellipse, rising into a hill on the far side. The start is at its
    // southern end, nearest the car park.
    private void BuildCircuit(Float3 start)
    {
        static Float3 Point(float t)
        {
            float r = 1f + 0.16f * MathF.Sin(3f * t) + 0.07f * MathF.Cos(5f * t + 1f);
            float lift = 0.5f + 0.5f * MathF.Sin(t);
            return new Float3(MathF.Cos(t) * 150f * r, 0.03f + 9f * lift * lift, MathF.Sin(t) * 96f * r);
        }

        const float StartT = MathF.PI * 1.5f;
        Float3 origin = start - new Float3(Point(StartT).X, 0f, Point(StartT).Z);
        Material asphalt = Textured("Textures/Asphalt", Color.White, 0.85f, 3f);
        Loop("Circuit", t => origin + Point(StartT + 0.2f + t), 12f, 520, asphalt, kerbs: true);

        // The approach road from the car park up to the circuit.
        var approach = new MeshBuilder();
        Float3 a0 = new(-4f, 0.03f, 4f), a1 = new(4f, 0.03f, 4f);
        Float3 entry = origin + Point(StartT);
        approach.Quad(a0, a1, new Float3(a1.X, 0.03f, entry.Z - 6f), new Float3(a0.X, 0.03f, entry.Z - 6f), new Float2(0f, 0f), new Float2(1f, 0f), new Float2(1f, 6f), new Float2(0f, 6f), Float3.UnitY);
        Add(Model("Approach", approach.Build(), asphalt, Float3.Zero));
    }

    // A narrower dirt track winding south of the car park, its surface rising and falling in bumps, with a little
    // less grip than asphalt.
    private void BuildDirtTrack(Float3 center)
    {
        static Float3 Point(float t)
        {
            float r = 1f + 0.22f * MathF.Sin(2f * t + 0.5f) + 0.12f * MathF.Sin(5f * t) + 0.05f * MathF.Cos(9f * t);
            return new Float3(MathF.Cos(t) * 125f * r, 0.05f, MathF.Sin(t) * 68f * r);
        }

        float Bumps(float along) => (Noise(along / 7f, 0.5f, 4096) - 0.5f) * 0.7f + (Noise(along / 2.3f, 3.5f, 4096) - 0.5f) * 0.15f + 0.3f;
        Material dirt = Textured("Textures/Dirt", Color.White, 0.95f, 1f);
        GameObject track = Loop("Dirt Track", t => center + Point(MathF.PI * 0.5f + t), 10f, 600, dirt, kerbs: false, Bumps);
        track.AddComponent<WheelSurface>().Grip = 0.8f;
        track.AddComponent<TyreSurface>().Spray = TyreSprayKind.Dust;
    }

    // ----------------------------------------------------------------
    //  Inside the circuit
    // ----------------------------------------------------------------

    // A wide pad with a ring painted on it, for drifting circles round the pole in the middle.
    private void BuildDriftPad(Float3 c)
    {
        Material asphalt = Textured("Textures/Asphalt", Color.White, 0.85f, 6f);
        Add(Model("Drift Pad", Mesh.CreateCube(new Float3(56f, 0.06f, 56f)), asphalt, c + new Float3(0f, 0.03f, 0f)));
        Add(Model("Drift Ring", Mesh.CreateCylinder(16f, 0.02f, 64), Lit(new Color(0.75f, 0.75f, 0.7f, 1f), 0f, 0.6f), c + new Float3(0f, 0.065f, 0f)));
        Add(Model("Drift Ring Inside", Mesh.CreateCylinder(15.6f, 0.02f, 64), asphalt, c + new Float3(0f, 0.07f, 0f)));
        Static("Drift Pole", new Float3(0.5f, 3f, 0.5f), c + new Float3(0f, 1.5f, 0f), material: Lit(Orange).Emissive(Orange, 1.5f));
    }

    // A lane with ten pins at the end of it.
    private void BuildBowling(Float3 c)
    {
        Add(Model("Bowling Lane", Mesh.CreateCube(new Float3(8f, 0.06f, 60f)), Textured("Textures/Asphalt", new Color(0.6f, 0.45f, 0.3f, 1f), 0.5f, 4f), c + new Float3(0f, 0.03f, 0f)));
        Material pin = Lit(new Color(0.85f, 0.85f, 0.82f, 1f), 0f, 0.3f);
        Material stripe = Lit(new Color(0.6f, 0.02f, 0.02f, 1f), 0f, 0.4f);
        for (int row = 0; row < 4; row++)
            for (int i = 0; i <= row; i++)
            {
                Float3 at = c + new Float3((i - row * 0.5f) * 1.1f, 0.75f, 24f + row * 1.0f);
                GameObject go = Model("Pin", Mesh.CreateCylinder(0.25f, 1.5f, 12), pin, at);
                Part(go, Model("Stripe", Mesh.CreateCylinder(0.26f, 0.15f, 12), stripe, Float3.Zero), new Float3(0f, 0.45f, 0f));
                var rb = go.AddComponent<Rigidbody3D>();
                var collider = go.AddComponent<CylinderCollider>();
                collider.Radius = 0.25f;
                collider.Height = 1.5f;
                Add(go);
                rb.Mass = 6f;
            }
    }

    // A long plank on a hinge: drive up one end and it tips you down the other.
    private void BuildSeesaw(Float3 c)
    {
        Static("Seesaw Pivot", new Float3(4f, 0.8f, 0.8f), c + new Float3(0f, 0.5f, 0f), new Float3(45f, 0f, 0f), _stone);
        GameObject plank = Model("Seesaw", Mesh.CreateCube(new Float3(5f, 0.3f, 18f)), Grid(new Color(0.5f, 0.35f, 0.15f, 1f)), c + new Float3(0f, 1.25f, 0f));
        var rb = plank.AddComponent<Rigidbody3D>();
        plank.AddComponent<BoxCollider>().Size = new Float3(5f, 0.3f, 18f);
        Add(plank);
        rb.Mass = 500f;
        var hinge = plank.AddComponent<HingeJoint>();
        hinge.Anchor = Float3.Zero;
        hinge.Axis = Float3.UnitX;
        hinge.MinAngle = -8f;
        hinge.MaxAngle = 8f;
    }

    // ----------------------------------------------------------------
    //  West
    // ----------------------------------------------------------------

    private void BuildRoughGround(Float3 c)
    {
        const int Cells = 60;
        const float Size = 90f;
        float Height(float u, float v)
        {
            float edge = MathF.Min(MathF.Min(u, 1f - u), MathF.Min(v, 1f - v));
            float fade = Math.Clamp(edge * 8f, 0f, 1f);
            return Fractal(u, v, 4, 4) * 5f * fade * fade;
        }

        var ground = new MeshBuilder();
        for (int z = 0; z < Cells; z++)
            for (int x = 0; x < Cells; x++)
            {
                Float3 P(int i, int j) => new((i / (float)Cells - 0.5f) * Size, Height(i / (float)Cells, j / (float)Cells) + 0.02f, (j / (float)Cells - 0.5f) * Size);
                Float2 U(int i, int j) => new(i * Size / Cells * 0.5f, j * Size / Cells * 0.5f);
                ground.Quad(P(x, z), P(x + 1, z), P(x + 1, z + 1), P(x, z + 1), U(x, z), U(x + 1, z), U(x + 1, z + 1), U(x, z + 1), Float3.UnitY);
            }

        Mesh mesh = ground.Build();
        GameObject go = Model("Rough Ground", mesh, Grid(new Color(0.2f, 0.32f, 0.12f, 1f)), c);
        go.AddComponent<MeshCollider>().Mesh = mesh;
        go.AddComponent<TyreSurface>().Spray = TyreSprayKind.Dust;
        Add(go);
    }

    // Slopes of rising steepness side by side, each up to a plateau and back down gently. The steepest needs a
    // run up, and only some vehicles make it.
    private void BuildHillClimb(Float3 c)
    {
        const float Height = 7f;
        float[] angles = [10f, 20f, 30f, 45f];
        for (int i = 0; i < angles.Length; i++)
        {
            float x = c.X + (i - 1.5f) * 13f;
            float climb = Height / MathF.Tan(angles[i] * MathF.PI / 180f);
            Material material = Grid(Palette[(i + 1) % Palette.Length] * 0.8f);
            Wedge($"Climb {angles[i]}", x, c.Z, c.Z + climb, 0f, Height, 10f, material);
            Wedge("Hill Top", x, c.Z + climb, c.Z + climb + 14f, Height, Height, 10f, material);
            Wedge("Hill Descent", x, c.Z + climb + 14f, c.Z + climb + 14f + Height / MathF.Tan(12f * MathF.PI / 180f), Height, 0f, 10f, material);
        }
    }

    // A rink of ice with low walls round it and a slalom of cones to knock over.
    private void BuildIceRink(Float3 c)
    {
        const float Width = 60f, Length = 90f;
        Material ice = Lit(Color.White, 0.1f, 0.08f).With("_MainTex", Load<Texture2D>("Textures/Ice")).Tiled(6f, 9f);
        GameObject rink = Static("Ice", new Float3(Width, 0.1f, Length), c + new Float3(0f, 0.05f, 0f), material: ice);
        rink.AddComponent<WheelSurface>().Grip = 0.12f;
        rink.AddComponent<TyreSurface>().Spray = TyreSprayKind.Chips;

        Material wall = Lit(new Color(0.6f, 0.65f, 0.7f, 1f), 0f, 0.5f);
        foreach (float side in new[] { -1f, 1f })
        {
            Static("Rink Wall", new Float3(0.4f, 0.5f, Length), c + new Float3(side * Width * 0.5f, 0.25f, 0f), material: wall);
            Static("Rink Wall", new Float3(Width * 0.35f, 0.5f, 0.4f), c + new Float3(side * Width * 0.325f, 0.25f, Length * 0.5f), material: wall);
            Static("Rink Wall", new Float3(Width * 0.35f, 0.5f, 0.4f), c + new Float3(side * Width * 0.325f, 0.25f, -Length * 0.5f), material: wall);
        }

        Material cone = Lit(Orange, 0f, 0.5f);
        for (int i = 0; i < 8; i++)
        {
            GameObject go = Model("Cone", Mesh.CreateCone(0.35f, 0.9f, 12), cone, c + new Float3((i % 2 == 0 ? -1f : 1f) * 4f, 0.55f, -32f + i * 9f));
            var rb = go.AddComponent<Rigidbody3D>();
            var collider = go.AddComponent<ConeCollider>();
            collider.Radius = 0.35f;
            collider.Height = 0.9f;
            Add(go);
            rb.Mass = 3f;
        }
    }

    // ----------------------------------------------------------------
    //  East
    // ----------------------------------------------------------------

    // A long straight of jumps getting bigger, each with a long run up, a gap to clear and a sloped landing, then a
    // tabletop. Hold Shift on the run up to clear the big ones.
    private void BuildJumps(Float3 c)
    {
        Material ramp = Grid(new Color(0.45f, 0.45f, 0.5f, 1f));
        Material landing = Grid(new Color(0.3f, 0.32f, 0.36f, 1f));
        float z = c.Z;
        foreach ((float angle, float gap) in new[] { (10f, 14f), (14f, 22f), (18f, 32f) })
        {
            float rise = 12f * MathF.Sin(angle * MathF.PI / 180f);
            float run = 12f * MathF.Cos(angle * MathF.PI / 180f);
            Wedge("Ramp", c.X, z, z + run, 0f, rise, 10f, ramp);
            z += run + gap;
            Wedge("Landing", c.X, z, z + rise / MathF.Tan(angle * 0.6f * MathF.PI / 180f), rise * 0.8f, 0f, 12f, landing);
            z += rise / MathF.Tan(angle * 0.6f * MathF.PI / 180f) + 70f;
        }

        // Tabletop: up, a long flat top to land on, and down.
        const float Table = 3f;
        Wedge("Table Ramp", c.X, z, z + 14f, 0f, Table, 10f, ramp);
        Wedge("Table", c.X, z + 14f, z + 44f, Table, Table, 10f, landing);
        Wedge("Table Descent", c.X, z + 44f, z + 64f, Table, 0f, 10f, ramp);
    }

    // A round platform turning slowly. Drive on and park to be carried round.
    private void BuildTurntable(Float3 c)
    {
        GameObject table = Model("Turntable", Mesh.CreateCylinder(10f, 0.3f, 48), Grid(new Color(0.2f, 0.4f, 0.5f, 1f)), c + new Float3(0f, 0.15f, 0f));
        var body = table.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        var collider = table.AddComponent<CylinderCollider>();
        collider.Radius = 10f;
        collider.Height = 0.3f;
        table.AddComponent<Spinner>().DegreesPerSecond = 25f;
        Part(table, Model("Turntable Stripe", Mesh.CreateCube(new Float3(1f, 0.02f, 19f)), Lit(Orange).Emissive(Orange, 0.8f), Float3.Zero), new Float3(0f, 0.16f, 0f));
        Add(table);
    }

    // Two raised docks with a ferry shuttling between them across the gap. Wait on a dock for the ferry, drive on
    // and it carries you over.
    private void BuildFerry(Float3 c)
    {
        const float Deck = 2f, Gap = 46f, DockLength = 14f;
        Material dock = Grid(new Color(0.4f, 0.3f, 0.2f, 1f));
        foreach (float side in new[] { -1f, 1f })
        {
            float inner = c.Z + side * Gap * 0.5f;
            float outer = inner + side * DockLength;
            Wedge("Dock", c.X, inner, outer, Deck, Deck, 12f, dock);
            Wedge("Dock Ramp", c.X, outer, outer + side * 14f, Deck, 0f, 12f, dock);
        }

        GameObject ferry = Model("Ferry", Mesh.CreateCube(new Float3(10f, 0.4f, 12f)), Grid(new Color(0.55f, 0.1f, 0.05f, 1f)), new Float3(c.X, Deck - 0.2f, c.Z + Gap * 0.5f - 6.2f));
        var body = ferry.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        ferry.AddComponent<BoxCollider>().Size = new Float3(10f, 0.4f, 12f);
        var mover = ferry.AddComponent<MovingPlatform>();
        mover.Travel = new Float3(0f, 0f, -(Gap - 12.4f));
        mover.Speed = 4f;
        mover.Wait = 5f;
        Add(ferry);
    }

    // A lift that carries a vehicle up to a high deck, with a long ramp back down.
    private void BuildLift(Float3 c)
    {
        const float High = 8f;
        Material deck = Grid(new Color(0.35f, 0.35f, 0.4f, 1f));
        Wedge("High Deck", c.X, c.Z - 4f, c.Z - 16f, High, High, 12f, deck);
        Wedge("High Deck Ramp", c.X, c.Z - 16f, c.Z - 76f, High, 0f, 10f, deck);

        GameObject lift = Model("Lift", Mesh.CreateCube(new Float3(9f, 0.4f, 8f)), Grid(new Color(0.7f, 0.55f, 0.05f, 1f)), new Float3(c.X, 0.2f, c.Z));
        var body = lift.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        lift.AddComponent<BoxCollider>().Size = new Float3(9f, 0.4f, 8f);
        var mover = lift.AddComponent<MovingPlatform>();
        mover.Travel = new Float3(0f, High, 0f);
        mover.Speed = 2f;
        mover.Wait = 4f;
        Add(lift);
    }
}
