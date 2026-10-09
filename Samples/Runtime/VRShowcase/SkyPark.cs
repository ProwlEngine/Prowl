// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace VRShowcase;

/// <summary>The gadget table, the sky tower with its zip line, and a console of buttons.</summary>
public sealed partial class VRShowcaseGame
{
    private DirectionalLight _sunLight = null!;
    private float _ambientStrength;

    private void BuildSkyPark()
    {
        Material metal = Lit(new Color(0.5f, 0.52f, 0.55f, 1f), 0.9f, 0.35f);
        Material red = Lit(new Color(0.6f, 0.05f, 0.04f, 1f), 0.2f, 0.4f);
        Material dark = Lit(new Color(0.05f, 0.05f, 0.06f, 1f), 0.4f, 0.5f);
        Material white = Lit(new Color(0.85f, 0.85f, 0.82f, 1f), 0f, 0.6f);
        Material flame = Unlit(new Color(1f, 0.55f, 0.1f, 1f));

        // The gadget table, everything laid out along it.
        Float3 table = new(12f, 0f, -6f);
        const float top = 0.86f;
        Add(Block("Gadget Table", new Float3(4.6f, 0.06f, 0.9f), _wood, table + new Float3(0f, top - 0.03f, 0f)));
        foreach (float x in new[] { -2.2f, 2.2f })
            foreach (float z in new[] { -0.38f, 0.38f })
                Add(Block("Table Leg", new Float3(0.06f, top - 0.06f, 0.06f), _wood, table + new Float3(x, (top - 0.06f) * 0.5f, z)));
        Float3 On(float x, float height) => table + new Float3(x, top + height, 0f);

        Rocket(On(-2f, 0.05f), red, metal, flame);
        Rocket(On(-1.75f, 0.05f), red, metal, flame);
        Wing(On(-1.3f, 0.09f), white, dark);
        Wing(On(-0.6f, 0.09f), white, dark);
        Torch(On(0f, 0.05f), dark, white);
        Torch(On(0.25f, 0.05f), dark, white);
        for (int i = 0; i < 3; i++) BombAt(On(0.7f + i * 0.17f, 0.08f), flame);
        ZipHandle(On(1.5f, 0.03f), metal, dark);
        Crowbar(On(1.95f, 0.03f) + new Float3(0f, 0f, -0.25f), red, metal);

        var balloons = new List<Balloons>
        {
            BalloonBunch(table + new Float3(-2.9f, 1.2f, 0.4f), Hsv(0.95f, 0.8f, 0.6f)),
            BalloonBunch(table + new Float3(-2.9f, 1.2f, -0.4f), Hsv(0.55f, 0.8f, 0.6f)),
        };
        Float3 balloonPost = table + new Float3(-2.9f, 0f, 1f);
        Add(Block("Balloon Post", new Float3(0.2f, 1f, 0.2f), _stone, balloonPost + new Float3(0f, 0.5f, 0f)));
        PushButton respawn = PanelButton(balloonPost + new Float3(0f, 1f, 0f), Quaternion.FromEuler(-90f, 0f, 0f), Lit(Hsv(0.95f, 0.8f, 0.6f), 0f, 0.4f));
        respawn.Pressed += () => balloons.ForEach(b => b.Respawn());

        DayNightLever(table + new Float3(2.8f, 0f, 0.6f), metal, red);

        BuildSkyTower(new Float3(16f, 0f, -14f), metal, dark);
        BuildConsole(new Float3(9.5f, 0f, -13f), dark);
    }

    /// <summary>A gadget as one rigidbody of boxes laid out in its own space, the hand holding it at the origin.</summary>
    private Grabbable Gadget(string name, Float3 position, Quaternion rotation, float mass, (Float3 Size, Float3 Center, Material Material)[] parts, params GrabPoint[] grips)
    {
        var go = new GameObject(name);
        go.Transform.Position = position;
        go.Transform.Rotation = rotation;
        Float3 weighted = Float3.Zero;
        float total = 0f;
        foreach ((Float3 size, Float3 center, Material material) in parts)
        {
            GameObject part = Model("Part", Mesh.CreateCube(size), material, Float3.Zero);
            part.SetParent(go);
            part.Transform.LocalPosition = center;
            part.Transform.LocalRotation = Quaternion.Identity;
            part.AddComponent<BoxCollider>().Size = size;
            float volume = size.X * size.Y * size.Z;
            weighted += center * volume;
            total += volume;
        }

        var body = go.AddComponent<Rigidbody3D>();
        body.Mass = mass;
        body.CenterOfMassOverride = weighted / total;
        var grabbable = go.AddComponent<Grabbable>();
        grabbable.Points.AddRange(grips);
        grabbable.Size = SocketSize.Medium;
        Add(go);
        return grabbable;
    }

    /// <summary>A visual piece with no collider, for flames, balloons and the like.</summary>
    private static GameObject Decoration(GameObject parent, string name, Mesh mesh, Material material, Float3 local)
    {
        GameObject go = Model(name, mesh, material, Float3.Zero);
        go.SetParent(parent);
        go.Transform.LocalPosition = local;
        go.Transform.LocalRotation = Quaternion.Identity;
        return go;
    }

    private static readonly Quaternion LyingDown = Quaternion.FromEuler(90f, 0f, 0f);

    private void Rocket(Float3 at, Material body, Material metal, Material flame)
    {
        Grabbable rocket = Gadget("Rocket", at, Quaternion.Identity, 1.2f,
        [
            (new Float3(0.07f, 0.07f, 0.4f), new Float3(0f, 0f, 0.02f), body),
            (new Float3(0.09f, 0.09f, 0.06f), new Float3(0f, 0f, -0.2f), metal),
        ], GrabPoint.At(Float3.Zero));
        // The flame points back from the nozzle and stretches with the throttle.
        GameObject fire = Decoration(rocket.GameObject, "Flame", Mesh.CreateCube(new Float3(0.06f, 0.06f, 0.3f)), flame, new Float3(0f, 0f, -0.38f));
        var thruster = rocket.AddComponent<Thruster>();
        thruster.Flame = fire.Transform;
        fire.Transform.LocalScale = Float3.Zero;
    }

    /// <summary>
    /// A paddle held flat in the fist, thumb up, which pushes off the air when swept down. One for each hand to flap.
    /// It lies paddle down with the handle up, to be picked up from above.
    /// </summary>
    private void Wing(Float3 at, Material skin, Material frame)
    {
        Grabbable wing = Gadget("Wing", at, Quaternion.AxisAngle(Float3.UnitZ, MathF.PI), 1f,
        [
            (new Float3(0.03f, 0.1f, 0.03f), Float3.Zero, frame),
            (new Float3(0.8f, 0.02f, 0.55f), new Float3(0f, 0.07f, 0.08f), skin),
        ], GrabPoint.At(Float3.Zero));
        wing.Size = SocketSize.Large;
        var surface = wing.AddComponent<AeroSurface>();
        surface.Center = new Float3(0f, 0.07f, 0.08f);
        surface.Area = 0.44f;
        surface.Strength = 640f;
        surface.Drag = 0.3f;
    }

    private void Torch(Float3 at, Material body, Material lens)
    {
        Grabbable torch = Gadget("Flashlight", at, Quaternion.Identity, 0.4f,
        [
            (new Float3(0.045f, 0.045f, 0.22f), new Float3(0f, 0f, 0.03f), body),
            (new Float3(0.06f, 0.06f, 0.03f), new Float3(0f, 0f, 0.15f), lens),
        ], GrabPoint.At(Float3.Zero));
        torch.Size = SocketSize.Small;
        var lamp = new GameObject("Lamp");
        lamp.SetParent(torch.GameObject);
        lamp.Transform.LocalPosition = new Float3(0f, 0f, 0.17f);
        lamp.Transform.LocalRotation = Quaternion.Identity;
        var light = lamp.AddComponent<SpotLight>();
        light.Range = 30f;
        light.SpotAngle = 40f;
        light.InnerSpotAngle = 25f;
        light.Intensity = 6f;
        light.Color = new Color(1f, 0.95f, 0.85f, 1f);
        light.Enabled = false;
        torch.AddComponent<Flashlight>().Lamp = light;
    }

    private void BombAt(Float3 at, Material glow)
    {
        const float radius = 0.07f;
        Rigidbody3D body = Body("Bomb", Mesh.CreateSphere(radius, 10, 14), Lit(new Color(0.08f, 0.08f, 0.09f, 1f), 0.5f, 0.4f), at, Quaternion.Identity, 0.6f, null);
        body.GameObject.AddComponent<SphereCollider>().Radius = radius;
        Grab(body, radius);
        body.GetComponent<Grabbable>()!.Size = SocketSize.Small;
        Decoration(body.GameObject, "Fuse", Mesh.CreateCube(new Float3(0.015f, 0.04f, 0.015f)), glow, new Float3(0f, radius + 0.015f, 0f));

        var blast = Model("Blast", Mesh.CreateSphere(0.5f, 12, 16), Unlit(new Color(1f, 0.6f, 0.15f, 1f)), at);
        blast.Transform.LocalScale = Float3.Zero;
        Add(blast);
        var bomb = body.AddComponent<Bomb>();
        bomb.Player = _rig.Body;
        bomb.Visual = body.GetComponent<MeshRenderer>()!;
        bomb.Blast = blast.Transform;
    }

    /// <summary>A bar for both hands with a hook on top, to hang from a zip line.</summary>
    private void ZipHandle(Float3 at, Material metal, Material grip)
    {
        Grabbable handle = Gadget("Zip Handle", at, LyingDown, 1f,
        [
            (new Float3(0.45f, 0.035f, 0.035f), Float3.Zero, grip),
            (new Float3(0.025f, 0.2f, 0.025f), new Float3(0f, 0.11f, 0f), metal),
            (new Float3(0.025f, 0.025f, 0.08f), new Float3(0f, 0.22f, 0.03f), metal),
            (new Float3(0.025f, 0.05f, 0.025f), new Float3(0f, 0.2f, 0.065f), metal),
        ], GrabPoint.Line(new Float3(-0.2f, 0f, 0f), new Float3(0.2f, 0f, 0f)));
        handle.AddComponent<ZipHook>().Hook = new Float3(0f, 0.2f, 0.03f);
    }

    /// <summary>A crowbar: a weapon, and its hooked end rides a zip line just like a handle.</summary>
    private void Crowbar(Float3 at, Material paint, Material metal)
    {
        Grabbable bar = Gadget("Crowbar", at, LyingDown * Quaternion.FromEuler(0f, 0f, 90f), 2.5f,
        [
            (new Float3(0.028f, 0.75f, 0.028f), new Float3(0f, 0.3f, 0f), paint),
            (new Float3(0.028f, 0.028f, 0.09f), new Float3(0f, 0.69f, 0.035f), paint),
            (new Float3(0.028f, 0.06f, 0.028f), new Float3(0f, 0.65f, 0.075f), metal),
            (new Float3(0.028f, 0.03f, 0.06f), new Float3(0f, -0.08f, 0.02f), metal),
        ], GrabPoint.Line(new Float3(0f, -0.02f, 0f), new Float3(0f, 0.55f, 0f)));
        bar.Size = SocketSize.Large;
        bar.GripPivot = 0.6f;
        bar.AddComponent<MeleeWeapon>();
        bar.AddComponent<ZipHook>().Hook = new Float3(0f, 0.665f, 0.035f);
    }

    private Balloons BalloonBunch(Float3 at, Color color)
    {
        Grabbable bunch = Gadget("Balloons", at, Quaternion.Identity, 0.3f,
        [
            (new Float3(0.03f, 0.25f, 0.03f), Float3.Zero, Lit(new Color(0.8f, 0.8f, 0.8f, 1f), 0f, 0.6f)),
        ], GrabPoint.Line(new Float3(0f, -0.1f, 0f), new Float3(0f, 0.1f, 0f)));
        Material rubber = Lit(color, 0f, 0.25f);
        Float3[] spots = [new(0f, 0.75f, 0f), new(0.18f, 0.62f, 0.06f), new(-0.16f, 0.66f, -0.08f), new(0.04f, 0.6f, 0.2f)];
        foreach (Float3 spot in spots)
        {
            Decoration(bunch.GameObject, "Balloon", Mesh.CreateSphere(0.16f, 10, 14), rubber, spot);
            Float3 low = new(0f, 0.12f, 0f);
            GameObject line = Decoration(bunch.GameObject, "String", Mesh.CreateCube(new Float3(0.004f, Float3.Distance(low, spot), 0.004f)), _dark, (low + spot) * 0.5f);
            line.Transform.LocalRotation = Quaternion.FromToRotation(Float3.UnitY, Float3.Normalize(spot - low));
        }
        bunch.Body.AffectedByGravity = false;
        return bunch.AddComponent<Balloons>();
    }

    // ----------------------------------------------------------------
    //  Controls: things the hand grips or pushes and works for real
    // ----------------------------------------------------------------

    /// <summary>A button on a panel facing the rotation's +Z. The cap sits proud of the panel and sinks in when pushed.</summary>
    private PushButton PanelButton(Float3 at, Quaternion rotation, Material cap)
    {
        AddTurned(Model("Button Ring", Mesh.CreateCube(new Float3(0.08f, 0.08f, 0.01f)), _dark, at + rotation * new Float3(0f, 0f, 0.005f)), rotation);
        GameObject go = Model("Button", Mesh.CreateCube(new Float3(0.055f, 0.055f, 0.03f)), cap, at + rotation * new Float3(0f, 0f, 0.035f));
        go.Transform.Rotation = rotation;
        go.AddComponent<BoxCollider>().Size = new Float3(0.055f, 0.055f, 0.03f);
        var body = go.AddComponent<Rigidbody3D>();
        body.Mass = 0.1f;
        body.AffectedByGravity = false;
        var slide = go.AddComponent<PrismaticJoint>();
        slide.Axis = Float3.UnitZ;
        slide.Pinned = true;
        slide.MinDistance = -0.018f;
        slide.MaxDistance = 0f;
        slide.HasMotor = true;
        slide.MotorMaxForce = 6f;
        var button = go.AddComponent<PushButton>();
        button.Slide = slide;
        button.Travel = 0.018f;
        Add(go);
        return button;
    }

    private void AddTurned(GameObject go, Quaternion rotation)
    {
        go.Transform.Rotation = rotation;
        Add(go);
    }

    /// <summary>A lamp that lights when told to, in its own colour.</summary>
    private Action<bool> Lamp(Float3 at, Color color)
    {
        Material off = Lit(new Color(color.R * 0.15f, color.G * 0.15f, color.B * 0.15f, 1f), 0f, 0.3f);
        Material on = Lit(color, 0f, 0.3f).Emissive(color, 3f);
        GameObject lamp = Model("Lamp", Mesh.CreateSphere(0.05f, 10, 14), off, at);
        Add(lamp);
        MeshRenderer renderer = lamp.GetComponent<MeshRenderer>()!;
        return lit => renderer.Material = lit ? on : off;
    }

    /// <summary>A console of buttons, each flipping a lamp on the board above it.</summary>
    private void BuildConsole(Float3 at, Material dark)
    {
        // The desk faces +Z, its top tipped up toward whoever stands in front.
        Add(Block("Console", new Float3(1.2f, 0.9f, 0.7f), _stone, at + new Float3(0f, 0.45f, 0f)));
        Quaternion panel = Quaternion.FromEuler(-60f, 0f, 0f);
        Float3 face = at + new Float3(0f, 1.0f, 0.05f);
        GameObject plate = Block("Console Panel", new Float3(1.2f, 0.5f, 0.06f), dark, face);
        plate.Transform.Rotation = panel;
        Add(plate);

        Float3 board = at + new Float3(0f, 1.7f, -0.3f);
        Add(Block("Lamp Board", new Float3(1.2f, 0.4f, 0.1f), _dark, board));
        Color[] colors = [Hsv(0f, 0.9f, 1f), Hsv(0.33f, 0.9f, 1f), Hsv(0.6f, 0.9f, 1f), Hsv(0.15f, 0.9f, 1f)];
        for (int i = 0; i < colors.Length; i++)
        {
            float x = -0.36f + i * 0.24f;
            Action<bool> light = Lamp(board + new Float3(x, 0f, 0.08f), colors[i]);
            bool lit = false;
            PanelButton(face + panel * new Float3(x, 0f, 0.03f), panel, Lit(colors[i], 0f, 0.4f)).Pressed += () => light(lit = !lit);
        }
    }

    private void DayNightLever(Float3 at, Material metal, Material knob)
    {
        Add(Block("Lever Post", new Float3(0.2f, 1f, 0.2f), _stone, at + new Float3(0f, 0.5f, 0f)));
        var pivot = new GameObject("Day Night Lever");
        pivot.Transform.Position = at + new Float3(0f, 1.02f, 0f);
        pivot.Transform.Rotation = Quaternion.FromEuler(0f, 90f, 0f);
        var arm = new GameObject("Arm");
        arm.SetParent(pivot);
        arm.Transform.LocalPosition = Float3.Zero;
        Decoration(arm, "Shaft", Mesh.CreateCube(new Float3(0.03f, 0.35f, 0.03f)), metal, new Float3(0f, 0.175f, 0f));
        Decoration(arm, "Knob", Mesh.CreateSphere(0.045f, 10, 14), knob, new Float3(0f, 0.35f, 0f));
        var lever = pivot.AddComponent<Lever>();
        lever.Arm = arm.Transform;
        lever.Changed += SetTimeOfDay;
        Add(pivot);
        lever.SetValue(1f);
    }

    /// <summary>Sets the sun from 0, the middle of the night, through dusk at about a third, to 1, the middle of the day.</summary>
    private void SetTimeOfDay(float value)
    {
        if (_sunLight.IsNotValid()) return;
        float elevation = -25f + 75f * value;
        _sunLight.Transform.LocalEulerAngles = new Float3(elevation, 30f, 0f);
        float day = Saturate((elevation + 2f) / 20f);
        _sunLight.Intensity = 0.6f * day;
        _sunLight.Color = Lerp(new Color(1f, 0.45f, 0.2f, 1f), Color.White, Saturate(elevation / 30f));
        Scene? scene = _sunLight.GameObject.Scene;
        if (scene.IsNotValid()) return;
        if (_ambientStrength <= 0f) _ambientStrength = scene.Ambient.Strength;
        scene.Ambient.Strength = _ambientStrength * (0.06f + 0.94f * day);
    }

    /// <summary>
    /// A tower to climb by ladder or ride up by elevator, with a zip line from its top down to the far side of the
    /// park and an open edge to fly from.
    /// </summary>
    private void BuildSkyTower(Float3 at, Material metal, Material dark)
    {
        const float height = 9f, size = 3f;
        Add(Block("Sky Tower", new Float3(size, height, size), _stone, at + new Float3(0f, height * 0.5f, 0f)));
        Add(Model("Tower Top", Plane(size - 0.4f, size - 0.4f), _accent, at + new Float3(0f, height + 0.005f, 0f)));
        Add(Block("Tower Rail", new Float3(0.08f, 1f, size), dark, at + new Float3(-size * 0.5f + 0.04f, height + 0.5f, 0f)));
        Add(Block("Tower Rail", new Float3(size, 1f, 0.08f), dark, at + new Float3(0f, height + 0.5f, size * 0.5f - 0.04f)));

        // A ladder up the south face and an elevator on the east.
        Float3 ladder = at + new Float3(0f, 0f, -size * 0.5f - 0.12f);
        foreach (float x in new[] { -0.28f, 0.28f })
            Add(Block("Ladder Rail", new Float3(0.05f, height + 0.8f, 0.05f), _wood, ladder + new Float3(x, (height + 0.8f) * 0.5f, 0f)));
        for (float y = 0.3f; y < height + 0.7f; y += 0.3f)
            Add(Block("Rung", new Float3(0.52f, 0.04f, 0.04f), _wood, ladder + new Float3(0f, y, 0f)));
        Platform("Tower Elevator", new Float3(1.6f, 0.2f, 1.6f), at + new Float3(size * 0.5f + 0.85f, 0.1f, 0f), at + new Float3(size * 0.5f + 0.85f, height - 0.1f, 0f), 1.5f, 2.5f);

        // The zip line runs from a post on the tower's south east corner to a post far down the park.
        Float3 high = at + new Float3(size * 0.5f - 0.15f, height + 2.4f, -size * 0.5f + 0.15f);
        Float3 low = new(34f, 2.7f, -30f);
        Add(Block("Zip Post", new Float3(0.12f, 2.6f, 0.12f), metal, high + new Float3(0f, -1.2f, 0f) + Float3.Normalize(high - low) * 0.15f));
        Add(Block("Zip Post", new Float3(0.15f, low.Y + 0.4f, 0.15f), metal, new Float3(low.X, (low.Y + 0.4f) * 0.5f, low.Z) + Float3.Normalize(low - high) * 0.2f));
        Add(Block("Landing Mat", new Float3(4f, 0.15f, 4f), Lit(Teal, 0f, 0.8f), low + Float3.Normalize(new Float3(high.X - low.X, 0f, high.Z - low.Z)) * 2.5f + new Float3(0f, -low.Y + 0.075f, 0f)));

        var cable = new GameObject("Zip Line");
        cable.Transform.Position = high;
        var anchor = cable.AddComponent<Rigidbody3D>();
        anchor.MotionType = Jitter2.Dynamics.MotionType.Static;
        cable.AddComponent<SphereCollider>().Radius = 0.01f;
        var zip = cable.AddComponent<ZipLine>();
        zip.Top = high;
        zip.Bottom = low;
        var line = cable.AddComponent<LineRenderer>();
        line.Material = new Material(Shader.LoadDefault(DefaultShader.Line));
        line.StartWidth = line.EndWidth = 0.02f;
        line.StartColor = line.EndColor = new Color(0.2f, 0.2f, 0.22f, 1f);
        line.Points.Add(Float3.Zero);
        line.Points.Add(low - high);
        Add(cable);

        // A spare handle waits on the tower top.
        ZipHandle(at + new Float3(0.6f, height + 0.05f, -0.6f), metal, dark);
    }
}
