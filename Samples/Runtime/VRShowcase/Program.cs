// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// VR Showcase
//
// A playground for moving and handling things in VR with a fully simulated body (VRBody.cs): a rolling ball,
// a leg and a torso joined by joints, so the player is pushed by the world, rides what it stands on and can
// never be squeezed into anything. Put the headset on before starting, with SteamVR or the Meta app running.
// Without a headset it runs on the monitor with mouse and keyboard.
//
//   Spawn        a table of things to pick up, juggle and throw at a stack of crates, and a menu panel to point at
//   Stairs       steps and a ramp up to a balcony
//   Islands      pillars over a sunken pool with a ferry between two of them
//   Jumps        stepping stones with gaps to jump across
//   Crawlway     a passage too low to walk through, crouch to get under it
//   Armory       a weapon rack, a knife table, hanging dummies, a target board, a hay bale and melons
//   Range        a pistol, an automatic, spare magazines and a bow, with plates, cans and bales down range
//   Climbing     a wall of holds and a ladder up to a platform, and an elevator back down
//   Turntable    a spinning platform that turns whoever rides it
//   Sky Park     a gadget table (rockets, wings, web shooters, flashlights, a gravity gun, bombs, a zip handle,
//                a crowbar, a backpack, balloons and a day and night lever), a tower with a ladder, an elevator,
//                gliders and a zip line down the park, poles to swing from, a trampoline, a launch pad,
//                a bowling lane and dominoes
//
// Headset controls:
//   Left stick           Walk, click it to sprint
//   Right stick          Snap or smooth turn, hold down to crouch, up to stand and up again to jump
//                        (with teleporting on, forward aims a teleport and clicking the stick jumps)
//   Grip                 Pick up and throw, hold on to climb, let go near a slot or rack to put it there
//   Trigger              Fires a gun, presses UI. On a held shaft it lets the shaft slide through the hand
//   A or X               Drops the magazine of the gun in that hand
//   Guns                 Push a magazine into the grip, grip the slide with the other hand and pull it to chamber
//   Bow                  Hold it in one hand, grip the string with the other, draw and let go
//   Gadgets              The trigger throttles a rocket, fires a web (squeeze fully to reel in), switches a
//                        flashlight and lifts with the gravity gun (squeeze fully to fling). Sweep wings down to
//                        flap, hold a glider overhead and jump. Hold a zip handle or crowbar to a cable to ride it.
//                        Let things go at the backpack's mouth to put them in, grip the mouth to take one out
//   Left menu            Back to the spawn
//   Walking and ducking in the room moves the body with you
//
// Without a headset:
//   WASD  walk    Shift  sprint    Space  jump    C  crouch    Right Mouse  look    R  respawn    F1  hide the HUD
//

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Samples;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace VRShowcase;

internal class Program
{
    static void Main(string[] args)
    {
        new VRShowcaseGame().Run("VR Showcase", 1600, 900);
    }
}

public sealed partial class VRShowcaseGame : StationGame
{
    private static readonly Color Orange = new(1f, 0.32f, 0.04f, 1f);
    private static readonly Color Teal = new(0.02f, 0.35f, 0.3f, 1f);
    private static readonly Color Water = new(0.01f, 0.06f, 0.12f, 1f);

    private VRRig _rig = null!;
    private Material _dark = null!, _stone = null!, _wood = null!, _accent = null!;
    private readonly Random _rng = new(7);

    protected override string MoveKeys => XR.IsRunning
        ? "Left stick  walk    Right stick  turn, down crouch, up stand or jump    Grip  grab or climb    Trigger  fire, press or slide    Left menu  respawn"
        : "WASD  walk    Shift  sprint    Space  jump    C  crouch    Right Mouse  look    R  respawn";

    public override string Stats
    {
        get
        {
            string headset = XR.IsRunning
                ? $"{XR.HeadsetName} through {XR.RuntimeName}, {XR.EyeTextureSize.X}x{XR.EyeTextureSize.Y} per eye, {(XR.IsFocused ? "focused" : XR.IsVisible ? "visible" : "waiting for the headset")}"
                : "No headset, running on the monitor";
            VRBody body = _rig.Body;
            Float3 v = body.Torso.LinearVelocity;
            float speed = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
            string state = $"{(body.IsGrounded ? "grounded" : "in the air")}{(body.IsBraced ? ", held up by the hands" : "")}    {speed:0.0} m/s    eyes {body.EyeHeight:0.00} m up, aiming for {body.TargetEyeHeight:0.00}, stick crouch {_rig.StickCrouch:0.00}";
            return $"{headset}\n{state}";
        }
    }

    protected override void Build()
    {
        // The station camera is replaced by the rig's head.
        CameraObject.Enabled = false;

        AddStation("VR Playground",
            "Walk, turn, jump, duck, grab and fight with a fully simulated body and hands. The table at the spawn, the stairs to the left, the islands, the stepping stones behind you and the crawlway past the stairs are for moving. To the right is the armory: weapons on a rack, knives on a table, dummies to hit and stab, a target board, hay and melons. Past it is the sky park: gadgets to fly, swing and glide with, a tower with a zip line, a trampoline, a launch pad, bowling and dominoes.",
            Float3.Zero);

        _dark = Lit(new Color(0.03f, 0.032f, 0.04f, 1f), 0f, 0.7f);
        _stone = Lit(new Color(0.09f, 0.09f, 0.1f, 1f), 0f, 0.85f);
        _wood = Lit(new Color(0.16f, 0.08f, 0.035f, 1f), 0f, 0.6f);
        _accent = Lit(Orange, 0f, 0.45f);

        var sun = new GameObject("Sun");
        DirectionalLight light = sun.AddComponent<DirectionalLight>();
        light.ShadowDistance = 40f;
        light.Intensity = 0.6f;
        sun.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        Add(sun);
        _sunLight = light;

        Add(Block("Floor", new Float3(80f, 1f, 80f), Floor(80f, 80f), new Float3(0f, -0.5f, 0f)));
        Add(Model("Spawn Pad", Plane(1.6f, 1.6f), _accent, new Float3(0f, 0.005f, 0f)));

        BuildTable(new Float3(0f, 0f, 1.6f));
        BuildCrateStack(new Float3(0f, 0f, 7f));
        BuildStairs(new Float3(-6f, 0f, 2f));
        BuildCrawlway(new Float3(-6f, 0f, 12f));
        BuildIslands(new Float3(9f, 0f, 4f));
        BuildSteppingStones(new Float3(0f, 0f, -5f));
        BuildArmory();
        BuildClimbingWall(new Float3(-12f, 0f, 1.25f));
        BuildFerry(new Float3(9f, 0f, 4f));
        BuildTurntable(new Float3(4.5f, 0f, 6f));

        _rig = BuildRig(Float3.Zero);
        BuildMenu(new Float3(-1.6f, 1.25f, 1.2f), -53f);
        BuildSkyPark();

        // Started here, before the scene loads, so the first frame already goes to the headset.
        XR.Start();
    }

    // ----------------------------------------------------------------
    //  The player
    // ----------------------------------------------------------------

    private VRRig BuildRig(Float3 position)
    {
        VRBody body = VRBody.Create(position, 1.65f);
        Add(body.GameObject);

        var rig = new GameObject("VR Rig");
        rig.Transform.Position = position;

        var origin = new GameObject("Tracking Origin");
        origin.SetParent(rig);
        origin.Transform.LocalPosition = Float3.Zero;

        var head = new GameObject("Head") { Tag = "Main Camera" };
        head.SetParent(origin);
        head.Transform.LocalPosition = new Float3(0f, 1.65f, 0f);
        var camera = head.AddComponent<Camera>();
        camera.HDR = true;
        camera.NearClipPlane = 0.05f;
        camera.FarClipPlane = 300f;
        camera.Effects =
        [
            new BloomEffect { Intensity = 0.4f, Threshold = 1.0f },
            new TonemapperEffect(),
            new FXAAEffect(),
        ];
        head.AddComponent<TrackedPoseDriver>().Node = XRNode.Head;
        head.AddComponent<AudioListener>();

        // The hands are rigidbodies of their own, so they live at the root of the scene rather than under the rig.
        PhysicsHand Hand(XRHand side)
        {
            PhysicsHand hand = PhysicsHand.Create(side, origin.Transform, body, new Color(0.55f, 0.36f, 0.25f, 1f));
            Add(hand.GameObject);
            Add(hand.Ghost);
            return hand;
        }

        var vrRig = rig.AddComponent<VRRig>();
        vrRig.Body = body;
        vrRig.Origin = origin.Transform;
        vrRig.Head = head.Transform;
        vrRig.LeftHand = Hand(XRHand.Left);
        vrRig.RightHand = Hand(XRHand.Right);
        Add(rig);

        var pointer = new GameObject("UI Pointer");
        var uiPointer = pointer.AddComponent<UIPointer>();
        uiPointer.LeftHand = vrRig.LeftHand;
        uiPointer.RightHand = vrRig.RightHand;
        Add(pointer);

        return vrRig;
    }

    // ----------------------------------------------------------------
    //  Things to pick up
    // ----------------------------------------------------------------

    private void BuildTable(Float3 at)
    {
        const float height = 0.85f;
        Add(Block("Table Top", new Float3(1.6f, 0.05f, 0.8f), _wood, at + new Float3(0f, height - 0.025f, 0f)));
        foreach (float x in new[] { -0.72f, 0.72f })
            foreach (float z in new[] { -0.32f, 0.32f })
                Add(Block("Table Leg", new Float3(0.06f, height - 0.05f, 0.06f), _wood, at + new Float3(x, (height - 0.05f) * 0.5f, z)));

        float top = height + 0.01f;
        for (int i = 0; i < 4; i++)
            Cube(at + new Float3(-0.6f + i * 0.17f, top + 0.06f, -0.15f), 0.12f, Hsv(i * 0.17f, 0.8f, 0.4f));
        for (int i = 0; i < 3; i++)
            Ball(at + new Float3(0.15f + i * 0.17f, top + 0.07f, -0.15f), 0.07f, Hsv(0.55f + i * 0.1f, 0.8f, 0.4f));
    }

    private void BuildCrateStack(Float3 at)
    {
        Add(Block("Plinth", new Float3(1.6f, 0.9f, 1f), _stone, at + new Float3(0f, 0.45f, 0f)));

        const float size = 0.22f;
        for (int row = 0; row < 4; row++)
            for (int i = 0; i < 4 - row; i++)
            {
                float x = (i - (3 - row) * 0.5f) * (size + 0.01f);
                Cube(at + new Float3(x, 0.9f + size * 0.5f + row * size, 0f), size, Sample.Lerp(Orange, Teal, row / 3f));
            }
    }

    private void Cube(Float3 position, float size, Color color)
        => Grab(Body("Cube", Mesh.CreateCube(new Float3(size)), Lit(color, 0f, 0.5f), position, Quaternion.Identity, 0.4f, new Float3(size)), size * 0.6f);

    private void Ball(Float3 position, float radius, Color color)
    {
        Rigidbody3D body = Body("Ball", Mesh.CreateSphere(radius, 12, 18), Lit(color, 0f, 0.3f), position, Quaternion.Identity, 0.3f, null);
        body.GameObject.AddComponent<SphereCollider>().Radius = radius;
        Grab(body, radius);
    }

    private Rigidbody3D Body(string name, Mesh mesh, Material material, Float3 position, Quaternion rotation, float mass, Float3? box)
    {
        GameObject go = Model(name, mesh, material, position);
        go.Transform.Rotation = rotation;
        var body = go.AddComponent<Rigidbody3D>();
        body.Mass = mass;
        body.EnableSpeculativeContacts = true;
        if (box.HasValue) go.AddComponent<BoxCollider>().Size = box.Value;
        Add(go);
        return body;
    }

    private static void Grab(Rigidbody3D body, float reach) => body.GameObject.AddComponent<Grabbable>().Reach = reach;

    // ----------------------------------------------------------------
    //  Places to go
    // ----------------------------------------------------------------

    private void BuildStairs(Float3 at)
    {
        const int steps = 10;
        const float rise = 0.15f, run = 0.35f, width = 2f;
        for (int i = 0; i < steps; i++)
        {
            float height = rise * (i + 1);
            Add(Block("Step", new Float3(width, height, run), _stone, at + new Float3(0f, height * 0.5f, i * run)));
        }

        float top = rise * steps;
        float landingZ = steps * run + 1.5f;
        Add(Block("Balcony", new Float3(width + 3f, top, 3f), _stone, at + new Float3(-1.5f, top * 0.5f, landingZ)));
        Add(Block("Balcony Rail", new Float3(width + 3f, 1f, 0.1f), _dark, at + new Float3(-1.5f, top + 0.5f, landingZ + 1.45f)));

        // A ramp beside the stairs for walking up without steps.
        float rampLength = steps * run;
        float angle = MathF.Atan2(top, rampLength) * Maths.Rad2Deg;
        float slope = MathF.Sqrt(top * top + rampLength * rampLength);
        Add(Block("Ramp", new Float3(1.6f, 0.2f, slope), _accent, at + new Float3(-2.4f, top * 0.5f - 0.1f, rampLength * 0.5f - run * 0.5f), new Float3(-angle, 0f, 0f)));
    }

    private void BuildCrawlway(Float3 at)
    {
        const float length = 4f, clearance = 1.1f;
        Add(Block("Crawlway Left", new Float3(0.3f, clearance + 0.3f, length), _dark, at + new Float3(-0.8f, (clearance + 0.3f) * 0.5f, 0f)));
        Add(Block("Crawlway Right", new Float3(0.3f, clearance + 0.3f, length), _dark, at + new Float3(0.8f, (clearance + 0.3f) * 0.5f, 0f)));
        Add(Block("Crawlway Roof", new Float3(1.9f, 0.3f, length), _accent, at + new Float3(0f, clearance + 0.15f, 0f)));
    }

    private void BuildIslands(Float3 at)
    {
        Add(Block("Pool", new Float3(14f, 0.1f, 14f), Lit(Water, 0f, 0.05f), at + new Float3(4f, 0.02f, 4f)));

        (Float3 offset, float height)[] pillars =
        [
            (new Float3(0f, 0f, 0f), 0.6f),
            (new Float3(4f, 0f, 1.5f), 1.6f),
            (new Float3(7.5f, 0f, 5f), 2.8f),
            (new Float3(4f, 0f, 8f), 4f),
            (new Float3(0.5f, 0f, 6f), 1.2f),
        ];
        foreach ((Float3 offset, float height) in pillars)
        {
            Add(Block("Island", new Float3(2.2f, height, 2.2f), _stone, at + offset + new Float3(0f, height * 0.5f, 0f)));
            Add(Model("Island Top", Plane(1.8f, 1.8f), Lit(Teal, 0f, 0.6f), at + offset + new Float3(0f, height + 0.005f, 0f)));
        }
    }

    private void BuildSteppingStones(Float3 at)
    {
        Float3 cursor = at;
        for (int i = 0; i < 9; i++)
        {
            float gap = 0.6f + _rng.NextSingle() * 0.7f;
            float height = 0.25f + i * 0.12f;
            cursor += new Float3((_rng.NextSingle() - 0.5f) * 1.2f, 0f, -gap - 1f);
            Add(Block("Stepping Stone", new Float3(1f, height, 1f), i % 2 == 0 ? _accent : _stone, cursor + new Float3(0f, height * 0.5f, 0f)));
        }
    }

    // ----------------------------------------------------------------
    //  Climbing and riding
    // ----------------------------------------------------------------

    /// <summary>
    /// A wall facing +X with holds to climb, a ladder beside it and a platform on top, with an elevator on the far side
    /// to ride back down. The top edge of the wall is a hold too, to pull up and over.
    /// </summary>
    private void BuildClimbingWall(Float3 at)
    {
        const float height = 4f, width = 5f, depth = 2.6f;
        Add(Block("Climbing Wall", new Float3(0.4f, height, width), _stone, at + new Float3(-0.2f, height * 0.5f, 0f)));
        Add(Block("Wall Top", new Float3(depth, 0.2f, width), _stone, at + new Float3(-depth * 0.5f, height - 0.1f, 0f)));
        Add(Block("Wall Rail", new Float3(0.08f, 1f, width), _dark, at + new Float3(-depth + 0.04f, height + 0.5f, 0f)));
        Hold(at + new Float3(0.03f, height - 0.03f, 0f), new Float3(0.1f, 0.06f, width));

        // Holds climb the face in a zigzag, close enough to reach from one to the next.
        for (int i = 0; i < 9; i++)
        {
            float y = 0.9f + i * 0.36f;
            float z = 0.6f + (i % 2 == 0 ? -0.25f : 0.25f) + MathF.Sin(i * 1.3f) * 0.15f;
            Hold(at + new Float3(0.05f, y, z), new Float3(0.1f, 0.06f, 0.18f));
        }

        // The ladder runs up the left part of the face.
        Float3 ladder = at + new Float3(0.12f, 0f, -1.6f);
        Add(Hidden(Block("Ladder Rail", new Float3(0.05f, height + 0.6f, 0.05f), _wood, ladder + new Float3(0f, (height + 0.6f) * 0.5f, -0.28f))));
        Add(Hidden(Block("Ladder Rail", new Float3(0.05f, height + 0.6f, 0.05f), _wood, ladder + new Float3(0f, (height + 0.6f) * 0.5f, 0.28f))));
        for (float y = 0.3f; y < height + 0.5f; y += 0.3f)
            Add(Hidden(Block("Rung", new Float3(0.04f, 0.04f, 0.52f), _wood, ladder + new Float3(0f, y, 0f))));
        Prop("Ladder", ladder, Quaternion.Identity);

        Platform("Elevator", new Float3(1.6f, 0.2f, 1.6f), at + new Float3(-depth - 0.85f, 0.1f, 0f), at + new Float3(-depth - 0.85f, height - 0.1f, 0f), 1f, 2.5f);
    }

    private void Hold(Float3 position, Float3 size, Material? material = null)
    {
        GameObject hold = Block("Hold", size, material ?? _accent, position);
        Add(hold);
    }

    /// <summary>A kinematic platform that moves between two points, pausing at each end, for the body and anything on it to ride.</summary>
    private void Platform(string name, Float3 size, Float3 from, Float3 to, float speed, float pause)
    {
        GameObject platform = Block(name, size, Lit(Teal, 0f, 0.5f), from);
        var body = platform.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        body.EnableSpeculativeContacts = true;
        body.Friction = 1f;
        var mover = platform.AddComponent<MovingPlatform>();
        mover.From = from;
        mover.To = to;
        mover.Speed = speed;
        mover.Pause = pause;
        Add(platform);
    }

    /// <summary>A ferry from the lowest pillar's top over the water to the next one, rising as it goes.</summary>
    private void BuildFerry(Float3 islands)
        => Platform("Ferry", new Float3(1.4f, 0.2f, 1.4f), islands + new Float3(0f, 0.5f, 1.9f), islands + new Float3(0.5f, 1.1f, 4.1f), 0.6f, 2f);

    private void BuildTurntable(Float3 at)
    {
        GameObject table = Block("Turntable", new Float3(3f, 0.16f, 3f), Lit(Orange, 0f, 0.5f), at + new Float3(0f, 0.08f, 0f));
        var body = table.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        body.EnableSpeculativeContacts = true;
        body.Friction = 1f;
        table.AddComponent<Spinner>().DegreesPerSecond = 20f;
        Add(table);
    }

    // ----------------------------------------------------------------
    //  World space menu
    // ----------------------------------------------------------------

    /// <summary>A panel of world space UI to point a hand at: the laser shows on it and the trigger presses. Without a headset the mouse works it.</summary>
    private void BuildMenu(Float3 center, float yaw)
    {
        var events = new GameObject("Event System");
        events.AddComponent<EventSystem>();
        Add(events);

        Float2 size = new(600f, 440f);
        const float scale = 0.0016f;
        Quaternion rotation = Quaternion.FromEuler(0f, yaw, 0f);

        var canvasObject = new GameObject("Menu");
        canvasObject.Transform.Rotation = rotation;
        canvasObject.Transform.LocalScale = new Float3(scale);
        canvasObject.Transform.Position = center - rotation * new Float3(size.X * scale * 0.5f, size.Y * scale * 0.5f, 0f);
        var canvas = canvasObject.AddComponent<GameCanvas>();
        canvas.RenderMode = Prowl.Runtime.UI.RenderMode.WorldSpace;
        canvas.ReferenceResolution = size;

        UIElement(canvasObject, "Background", Float2.Zero, size).AddComponent<UIImage>().Color = new Color(0.05f, 0.06f, 0.08f, 1f);
        MenuLabel(canvasObject, "VR Playground", new Float2(30f, 370f), new Float2(540f, 50f), 36);

        UIToggle smoothTurn = MenuToggle(canvasObject, "Smooth turning", new Float2(30f, 300f), _rig.Turning == TurnMode.Smooth);
        smoothTurn.OnValueChanged += on => _rig.Turning = on ? TurnMode.Smooth : TurnMode.Snap;
        UIToggle teleport = MenuToggle(canvasObject, "Teleporting", new Float2(30f, 250f), _rig.Teleporting);
        teleport.OnValueChanged += on => _rig.Teleporting = on;
        UIToggle handWalk = MenuToggle(canvasObject, "Walk where the left hand points", new Float2(30f, 200f), _rig.MoveRelativeTo == MoveDirection.LeftHand);
        handWalk.OnValueChanged += on => _rig.MoveRelativeTo = on ? MoveDirection.LeftHand : MoveDirection.Head;

        TextComponent speedLabel = MenuLabel(canvasObject, $"Walk speed {_rig.WalkSpeed:0.0} m/s", new Float2(30f, 140f), new Float2(260f, 40f), 24);
        UISlider speed = MenuSlider(canvasObject, new Float2(300f, 148f), new Float2(260f, 24f), 1f, 5f, _rig.WalkSpeed);
        speed.OnValueChanged += v =>
        {
            _rig.WalkSpeed = v;
            speedLabel.Text = $"Walk speed {v:0.0} m/s";
        };

        int presses = 0;
        UIButton counter = MenuButton(canvasObject, "Pressed 0 times", new Float2(30f, 40f), new Float2(260f, 60f), out TextComponent counterLabel);
        counter.OnClick += () => counterLabel.Text = $"Pressed {++presses} times";
        MenuButton(canvasObject, "Back to spawn", new Float2(310f, 40f), new Float2(260f, 60f), out _).OnClick += _rig.Respawn;

        Add(canvasObject);
    }

    /// <summary>A UI element placed by its bottom left corner, in the canvas's design pixels.</summary>
    private static GameObject UIElement(GameObject parent, string name, Float2 corner, Float2 size)
    {
        var go = new GameObject(name);
        go.SetParent(parent, false);
        RectTransform rect = go.EnsureRectTransform();
        rect.AnchorMin = Float2.Zero;
        rect.AnchorMax = Float2.Zero;
        rect.Pivot = Float2.Zero;
        rect.AnchoredPosition = corner;
        rect.SizeDelta = size;
        return go;
    }

    private static TextComponent MenuLabel(GameObject parent, string text, Float2 corner, Float2 size, int fontSize, Prowl.Runtime.UI.TextAlignment alignment = Prowl.Runtime.UI.TextAlignment.CenterLeft)
    {
        var label = UIElement(parent, "Label", corner, size).AddComponent<TextComponent>();
        label.Text = text;
        label.Size = fontSize;
        label.Alignment = alignment;
        label.Color = new Color(0.9f, 0.92f, 0.95f, 1f);
        label.RaycastTarget = false;
        return label;
    }

    private static UIToggle MenuToggle(GameObject parent, string text, Float2 corner, bool on)
    {
        GameObject box = UIElement(parent, "Toggle", corner, new Float2(36f, 36f));
        var image = box.AddComponent<UIImage>();
        image.Color = new Color(0.2f, 0.22f, 0.26f, 1f);
        var toggle = box.AddComponent<UIToggle>();

        var mark = UIElement(box, "Check", new Float2(7f, 7f), new Float2(22f, 22f)).AddComponent<UIImage>();
        mark.Color = Orange;
        mark.RaycastTarget = false;

        toggle.TargetGraphic = image;
        toggle.Colors = Tints(new Color(0.2f, 0.22f, 0.26f, 1f));
        toggle.Checkmark = mark;
        toggle.IsOn = on;
        MenuLabel(parent, text, corner + new Float2(50f, -2f), new Float2(480f, 40f), 24);
        return toggle;
    }

    private static UISlider MenuSlider(GameObject parent, Float2 corner, Float2 size, float min, float max, float value)
    {
        GameObject go = UIElement(parent, "Slider", corner, size);
        go.AddComponent<UIImage>().Color = new Color(0.2f, 0.22f, 0.26f, 1f);
        var slider = go.AddComponent<UISlider>();

        GameObject fill = UIElement(go, "Fill", Float2.Zero, Float2.Zero);
        var fillImage = fill.AddComponent<UIImage>();
        fillImage.Color = Orange;
        fillImage.RaycastTarget = false;

        GameObject handle = UIElement(go, "Handle", Float2.Zero, new Float2(22f, 0f));
        var handleImage = handle.AddComponent<UIImage>();
        handleImage.RaycastTarget = false;

        slider.FillRect = fill.RectTransform;
        slider.HandleRect = handle.RectTransform;
        slider.TargetGraphic = handleImage;
        slider.MinValue = min;
        slider.MaxValue = max;
        slider.Value = value;
        return slider;
    }

    /// <summary>How a widget is tinted at rest, under the pointer and while pressed.</summary>
    private static ColorBlock Tints(Color normal)
    {
        ColorBlock colors = ColorBlock.Default;
        colors.NormalColor = normal;
        colors.SelectedColor = normal;
        colors.HighlightedColor = new Color(normal.R + 0.15f, normal.G + 0.15f, normal.B + 0.17f, 1f);
        colors.PressedColor = Orange;
        return colors;
    }

    private static UIButton MenuButton(GameObject parent, string text, Float2 corner, Float2 size, out TextComponent label)
    {
        GameObject go = UIElement(parent, "Button", corner, size);
        var image = go.AddComponent<UIImage>();
        image.Color = new Color(0.25f, 0.27f, 0.32f, 1f);
        var button = go.AddComponent<UIButton>();
        button.TargetGraphic = image;
        button.Colors = Tints(new Color(0.25f, 0.27f, 0.32f, 1f));
        label = MenuLabel(go, text, Float2.Zero, size, 24, Prowl.Runtime.UI.TextAlignment.CenterMiddle);
        return button;
    }

    // ----------------------------------------------------------------
    //  Panel
    // ----------------------------------------------------------------

    public override bool CompactHud => true;

    public override void DrawControls(Paper paper, FontFile font)
    {
        if (XR.IsRunning) Button(paper, font, "Stop XR", XR.Stop);
        else Button(paper, font, "Start XR", () => XR.Start());
    }
}

