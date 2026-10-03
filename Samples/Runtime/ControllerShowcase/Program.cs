// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Controller Showcase
//
// One open world to walk around, full of everything a character controller has to cope with. Pieces
// are grouped by theme into neighbourhoods scattered around the spawn, and placed with a fixed seed so
// the world is the same every run:
//   Stairs and slopes    step heights either side of the step limit, ramps over stairs, a mesh staircase,
//                        a fan of slopes, a crest, a trough, a valley too steep to stand in, spiral towers
//   Ceilings and gaps    slanted ceilings, a pitched roof, a crawl tunnel, doorways and bars
//   Traps                corners, wedges, pockets and a funnel that press from several sides
//   Curves               quarter pipes, arched tunnels, domes and logs
//   Rough ground         a jagged field, rolling bumps and a dense stone field
//   Moving things        platforms, lifts, a crush lift, spinning discs and sweepers, log rollers,
//                        push blocks, a spinning column and a seesaw
//   Play                 crates to shove and climb
//   Edges and jumps      hop blocks, gaps, ledges and beams
//
// Controls:
//   WASD        Run, relative to the camera
//   Space       Jump, hold for a higher jump
//   C           Crouch
//   Right Mouse Orbit the camera, the wheel zooms
//   Left Mouse  Drag any body around
//   F1          Hide the HUD
//

using System.Globalization;

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace ControllerShowcase;

internal class Program
{
    static void Main(string[] args)
    {
        new ControllerShowcaseGame().Run("Controller Showcase", 1600, 900);
    }
}

public sealed class ControllerShowcaseGame : StationGame
{
    private static readonly Color Orange = new(1f, 0.32f, 0.04f, 1f);
    private static readonly Color Red = new(0.55f, 0.03f, 0.03f, 1f);
    private static readonly Color Yellow = new(0.7f, 0.5f, 0.02f, 1f);

    private readonly Random _rng = new(5);
    private readonly Float3 _spawn = new(0f, 0.05f, 0f);
    private ChaseCamera _chase = null!;
    private CharacterController _character = null!;
    private CharacterInput _input = null!;

    private Material _stairsMat = null!, _towerMat = null!, _ceilingMat = null!, _trapMat = null!, _curveMat = null!;
    private Material _roughMat = null!, _movingMat = null!, _edgeMat = null!, _plainMat = null!, _dark = null!, _travelMat = null!;

    // ----------------------------------------------------------------
    //  Layout
    // ----------------------------------------------------------------

    /// <summary>One placed piece of the world, so the HUD can say what you are standing next to.</summary>
    private sealed record Landmark(string Name, string About, Float3 Position, float Radius);

    private readonly List<Landmark> _landmarks = new();

    // Pieces are built in their own space, around their own origin, and this is where that space sits.
    private Float3 _origin;
    private Quaternion _turn = Quaternion.Identity;

    private Float3 W(Float3 local) => _origin + _turn * local;
    private Quaternion R(Float3 euler) => _turn * Quaternion.FromEuler(euler);

    /// <summary>The ground a group covers, in its own space, before it is turned and placed.</summary>
    private readonly record struct Footprint(float MinX, float MaxX, float MinZ, float MaxZ)
    {
        public Float3 Center => new((MinX + MaxX) * 0.5f, 0f, (MinZ + MaxZ) * 0.5f);
        public float HalfX => (MaxX - MinX) * 0.5f;
        public float HalfZ => (MaxZ - MinZ) * 0.5f;
    }

    private sealed record Group(string Name, float Bearing, Footprint Area, Action Build, Placement? Fixed = null);

    /// <summary>A footprint turned and set down in the world, for overlap tests while packing.</summary>
    private readonly record struct Placed(Float3 Center, Float3 AxisX, Float3 AxisZ, float HalfX, float HalfZ)
    {
        public static Placed Of(Footprint area, Float3 origin, Quaternion turn, float grow)
            => new(origin + turn * area.Center, turn * Float3.UnitX, turn * Float3.UnitZ, area.HalfX + grow, area.HalfZ + grow);

        // Two rectangles are apart when some edge direction of either one separates them.
        public bool Overlaps(in Placed other)
        {
            foreach (Float3 axis in new[] { AxisX, AxisZ, other.AxisX, other.AxisZ })
            {
                float distance = MathF.Abs(Float3.Dot(other.Center - Center, axis));
                float reach = Reach(axis) + other.Reach(axis);
                if (distance > reach) return false;
            }
            return true;
        }

        private float Reach(Float3 axis) => HalfX * MathF.Abs(Float3.Dot(AxisX, axis)) + HalfZ * MathF.Abs(Float3.Dot(AxisZ, axis));
    }

    protected override string MoveKeys => "WASD  run    Space  jump    C  crouch    Right Mouse  orbit";

    public override string Stats
    {
        get
        {
            string ground = _character.IsGrounded ? $"grounded on a {_character.GroundSlopeAngle:0} degree surface" : "in the air";
            Float3 v = _character.Velocity;
            float speed = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
            string state = $"{ground}    {speed:0.0} m/s    height {_character.Transform.Position.Y:0.00} m    touching {_character.Collisions}    {(_input.Crouched ? "crouched" : "standing")}";

            Landmark? near = Nearest();
            return near == null ? state : $"{near.Name}: {near.About}\n{state}";
        }
    }

    private Landmark? Nearest()
    {
        Float3 at = _character.Transform.Position;
        Landmark? best = null;
        float bestDistance = float.MaxValue;
        foreach (Landmark landmark in _landmarks)
        {
            float dx = landmark.Position.X - at.X, dz = landmark.Position.Z - at.Z;
            float distance = MathF.Sqrt(dx * dx + dz * dz) - landmark.Radius;
            if (distance < bestDistance) { bestDistance = distance; best = landmark; }
        }
        return bestDistance < 6f ? best : null;
    }

    protected override void Build()
    {
        _stairsMat = GridMaterial(new Color(0.12f, 0.1f, 0.05f, 1f), new Color(0.35f, 0.25f, 0.08f, 1f));
        _towerMat = GridMaterial(new Color(0.06f, 0.1f, 0.16f, 1f), new Color(0.12f, 0.25f, 0.4f, 1f));
        _ceilingMat = GridMaterial(new Color(0.12f, 0.06f, 0.12f, 1f), new Color(0.3f, 0.12f, 0.3f, 1f));
        _trapMat = GridMaterial(new Color(0.14f, 0.05f, 0.04f, 1f), new Color(0.4f, 0.12f, 0.08f, 1f));
        _curveMat = GridMaterial(new Color(0.04f, 0.12f, 0.1f, 1f), new Color(0.08f, 0.32f, 0.26f, 1f));
        _roughMat = GridMaterial(new Color(0.07f, 0.1f, 0.04f, 1f), new Color(0.16f, 0.24f, 0.08f, 1f));
        _movingMat = GridMaterial(new Color(0.14f, 0.11f, 0.02f, 1f), new Color(0.4f, 0.3f, 0.04f, 1f));
        _edgeMat = GridMaterial(new Color(0.1f, 0.1f, 0.12f, 1f), new Color(0.28f, 0.28f, 0.34f, 1f));
        _plainMat = GridMaterial(new Color(0.16f, 0.17f, 0.19f, 1f), new Color(0.3f, 0.32f, 0.36f, 1f));
        _dark = Lit(new Color(0.03f, 0.032f, 0.04f, 1f), 0f, 0.7f);
        _travelMat = GridMaterial(new Color(0.08f, 0.05f, 0.14f, 1f), new Color(0.22f, 0.12f, 0.4f, 1f));

        AddStation("Controller playground", "One open world full of everything a character controller has to cope with: stairs and slopes, low ceilings and tight gaps, traps that press from several sides, curves, rough ground, moving platforms, jump pads, teleporters, and gravity that pulls sideways, upward or toward a little planet. Walk up to anything and the line below says what it is testing.", Float3.Zero, new Float3(0f, 5f, -12f), 1f);

        var sun = new GameObject("Sun");
        DirectionalLight light = sun.AddComponent<DirectionalLight>();
        light.ShadowDistance = 70f;
        light.Intensity = 0.6f;
        sun.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        Add(sun);

        _chase = CameraObject.AddComponent<ChaseCamera>();
        CameraObject.GetComponent<PhysicsGrabber>()!.Enabled = false;
        _chase.Distance = 7f;

        _groupList = Groups();
        DefineParts();
        BuildWorld();
        BuildSpawnPad();

        const float Size = 240f;
        Add(Block("Ground", new Float3(Size, 1f, Size), GridMaterial(new Color(0.16f, 0.17f, 0.19f, 1f), new Color(0.3f, 0.32f, 0.36f, 1f), Size / 4f, Size / 4f), new Float3(0f, -0.5f, 0f)));

        BuildPlayer(_spawn);
        _chase.Target = _character.Transform;
    }

    protected override void OnStationChanged(int index)
    {
        CameraObject.GetComponent<FlyCamera>()!.Enabled = false;
        _input.Stand();
        _character.Teleport(_spawn);
        _chase.Yaw = 0f;
    }

    protected override void Tick()
    {
        // Anything that falls out of the world comes back to the spawn.
        if (_character.Transform.Position.Y < -20f)
        {
            _input.Stand();
            _character.Teleport(_spawn);
        }
    }

    /// <summary>
    /// The world's groups. Inside a group every piece has a hand placed spot and facing; only where the
    /// groups themselves sit is left to <see cref="Lay"/>. Offsets are in the group's own space, with
    /// its open side toward -Z, which is turned to face the spawn.
    /// </summary>
    private Group[] Groups() =>
    [
        new("Stairs", 0f, new(-28.5f, 18.5f, -11f, 17f), () =>
        {
            Sub("Stair lanes", "every lane climbs 4 m, in steps from 0.1 m to 0.5 m either side of the step limit; the first, third and fifth lanes have an invisible ramp over the steps", 15f, new(0f, 0f, -5f), 0f, StairLanes);
            Sub("Slope fan", "ramps from 10 to 70 degrees, walkable up to the slope limit and slid down past it", 9f, new(-20f, 0f, -8f), 0f, SlopeFan);
        }),
        new("Slope shapes", 45f, new(-10f, 11f, -8f, 18.6f), () =>
        {
            Sub("Crest and trough", "a ridge to walk over and a dip to walk through, where the ground turns sharply under the feet", 8f, new(-4f, 0f, 0f), 0f, CrestAndTrough);
            Sub("Steep valley", "two slopes too steep to stand on, meeting in a crease the controller must not jitter in", 5f, new(8f, 0f, 0f), 0f, Valley);
        }),
        new("Towers", 80f, new(-9f, 10f, -3f, 3f), () =>
        {
            Sub("Spiral tower", "eight flights of thin steps around a column, with open air under every step; jump off the top onto a plate only 5 cm thick, which a fast fall must not pass through", 5f, new(-6f, 0f, 0f), 0f, () => Tower(1f));
        }),
        new("Ceilings and gaps", 125f, new(-15.3f, 13.1f, -6.5f, 8.2f), () =>
        {
            Sub("Doorway wall", "openings 1.3, 1.85 and 2.2 m high, then 0.7, 0.9 and 1.2 m wide, either side of the character's size", 9f, new(0f, 0f, 8f), 0f, DoorwayWall);
            Sub("Bars", "bars at shin height to step over, knee height to jump, and duck height to crouch under", 4f, new(1f, 0f, -2f), 0f, Bars);
            Sub("Crawl tunnel", "too low to stand in, so only crouching gets through, and standing waits until there is room", 5f, new(11.5f, 0f, -2f), 0f, CrawlTunnel);
        }),
        new("Traps", 165f, new(-14f, 14f, -4.2f, 4.2f), () =>
        {
            Sub("Open box", "a box to run into, pressing from three sides and above", 3f, new(-12f, 0f, 0f), 0f, OpenBox);
            Sub("Narrowing V", "two walls narrowing to a point", 3f, new(-6f, 0f, 0f), 0f, NarrowingV);
            Sub("Ceiling wedge", "a ceiling sloping down to meet the floor", 3f, new(0f, 0f, 0f), 0f, CeilingWedge);
            Sub("Leaning pocket", "walls that lean in overhead and close toward the back", 3f, new(6f, 0f, 0f), 0f, LeaningPocket);
            Sub("Funnel", "walls narrowing to a gap the character cannot fit through", 4f, new(12f, 0f, 0f), 0f, Funnel);
        }),
        new("Curves", 205f, new(-11.5f, 12f, -11f, 14.2f), () =>
        {
            Sub("Quarter pipes", "floors that curve up into walls, from a tight 1 m radius to a gentle 4.5 m one", 7f, new(-4f, 0f, -9f), 0f, QuarterPipes);
            Sub("Arched tunnel", "a tunnel whose walls curve over into the ceiling", 2f, new(-8f, 0f, 3f), 0f, () => Arch(1.6f));
            Sub("Arched tunnel", "a wider tunnel whose curved walls start walkable and turn steep", 3f, new(-1f, 0f, 3f), 0f, () => Arch(2.6f));
            Sub("Logs", "logs lying on the ground, from a curb the size of a step to a hump", 6f, new(9f, 0f, -5f), 0f, Logs);
            Sub("Dome", "a low dome to walk over", 1.5f, new(-8f, 0f, 12f), 0f, () => DomeAt(1.2f));
            Sub("Dome", "a dome whose sides turn too steep near the ground", 2.5f, new(-1f, 0f, 12f), 0f, () => DomeAt(2.2f));
            Sub("Dome", "a big dome to climb", 3.5f, new(8f, 0f, 9f), 0f, () => DomeAt(3.2f));
        }),
        new("Rough ground", 245f, new(-12.5f, 12.5f, -10f, 16f), () =>
        {
            Sub("Jagged field", "sharp random peaks with steep faces in every direction", 7f, new(-6.5f, 0f, -4f), 0f, JaggedField);
            Sub("Rolling bumps", "smooth bumps whose slope changes every step", 7f, new(6.5f, 0f, -4f), 0f, RollingBumps);
            Sub("Stone field", "a dense field of small stones of different heights, low ones to walk over and taller ones to step up or catch on", 6.5f, new(0f, 0f, 9.5f), 0f, Pebbles);
        }),
        new("Moving things", 290f, new(-14.3f, 14f, -17.3f, 17.4f), () =>
        {
            Sub("Sliding platform", "a platform gliding back and forth", 5f, new(-13f, 0f, -12f), 0f, SlidingPlatform);
            Sub("Lift", "a lift up to a ledge", 3f, new(-6f, 0f, -12f), 0f, LiftToLedge);
            Sub("Spinning disc", "a turntable", 3.5f, new(-10f, 0f, 1f), 0f, () => Disc(0f));
            Sub("Tilted disc", "a tilted turntable, so the floor under the feet rises and falls as it turns", 3.5f, new(-1.5f, 0f, 1f), 0f, () => Disc(10f));
            Sub("Sweepers", "panels sweeping round at waist height", 5f, new(9f, 0f, 1f), 0f, Sweepers);
            Sub("Log rollers", "logs turning on their long axis", 4f, new(-8f, 0f, 14f), 0f, LogRollers);
        }),
        new("Edges and jumps", 330f, new(-15.5f, 12.3f, -13f, 12.8f), () =>
        {
            Sub("Hop blocks", "blocks of rising height to jump up", 6f, new(-2f, 0f, -12f), 0f, HopBlocks);
            Sub("Beams", "beams 1, 0.5 and 0.25 m wide to balance along", 8f, new(-14f, 0f, 2f), 0f, Beams);
        }),
        new("Gravity and travel", 0f, new(-33f, 25f, -8f, 18f), () =>
        {
            Sub("Jump pads", "pads that throw you up onto towers 2, 4 and 7 m tall", 8f, new(-12f, 0f, -2f), 0f, JumpPads);
            Sub("Teleporters", "step onto a ring to come out at its partner, here the ground and the top of a 9 m column", 3f, new(2f, 0f, -4f), 0f, Teleporters);
            Sub("Planet", "a small world with its own gravity pulling toward its middle; the orange pad throws you up into its pull and the ring on top brings you back", 9f, new(14f, 0f, 6f), 0f, Planet);
            Sub("Flip room", "a room whose gravity pulls toward the ceiling, so walking in drops you onto it", 6f, new(-2f, 0f, 11f), 0f, FlipRoom);
            Sub("Wall walk", "a corridor whose gravity pulls toward its wall, so you walk along the wall", 7f, new(-28f, 0f, 8f), 0f, WallWalk);
        }, new Placement(0f, 48f, 0f, 0f)),
    ];

    /// <summary>
    /// Sets each group down whole at the free spot nearest the spawn along its bearing, turned so its
    /// open side faces the spawn, packed against the groups placed before it.
    /// </summary>
    private Dictionary<string, Placement> Lay(Group[] groups)
    {
        const float Gap = 1f, Clearing = 5f;
        var placed = new List<Placed> { new(Float3.Zero, Float3.UnitX, Float3.UnitZ, Clearing, Clearing) };
        var result = new Dictionary<string, Placement>();

        foreach (Group group in groups)
        {
            // A group with a fixed spot skips the search, which draws no random numbers, so the groups built after it come out the same.
            if (group.Fixed is Placement fixedAt)
            {
                placed.Add(Placed.Of(group.Area, fixedAt.Origin, fixedAt.Turn, Gap * 0.5f));
                result[group.Name] = fixedAt;
                continue;
            }

            float bearing = group.Bearing * MathF.PI / 180f;
            Float3 direction = new(MathF.Sin(bearing), 0f, MathF.Cos(bearing));

            Float3 best = Float3.Zero;
            Quaternion bestTurn = Quaternion.Identity;
            float bestScore = float.MaxValue;
            for (float reach = 4f; bestScore == float.MaxValue; reach += 4f)
            {
                for (int attempt = 0; attempt < 500; attempt++)
                {
                    float angle = bearing + Range(-0.5f, 0.5f), distance = Range(Clearing, Clearing + reach * 3f);
                    Float3 candidate = new Float3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * distance;
                    Quaternion turn = Quaternion.FromEuler(new Float3(0f, MathF.Atan2(-candidate.X, -candidate.Z) * 180f / MathF.PI, 0f));
                    Placed footprint = Placed.Of(group.Area, candidate, turn, Gap * 0.5f);

                    bool clear = true;
                    foreach (Placed other in placed)
                        if (footprint.Overlaps(other)) { clear = false; break; }
                    if (!clear) continue;

                    // Nearest the spawn wins, with a nudge toward the group's own bearing.
                    float score = Float3.Length(footprint.Center) + Float3.Length(candidate / distance - direction) * 4f;
                    if (score < bestScore) { bestScore = score; best = candidate; bestTurn = turn; }
                }
            }

            placed.Add(Placed.Of(group.Area, best, bestTurn, Gap * 0.5f));
            result[group.Name] = new Placement(best.X, best.Z, 0f, MathF.Atan2(-best.X, -best.Z) * 180f / MathF.PI);
        }

        return result;
    }

    // ----------------------------------------------------------------
    //  Parts and their placements
    // ----------------------------------------------------------------

    /// <summary>Where a part sits: its origin on the ground, how high it is lifted, and its facing in degrees.</summary>
    private record struct Placement(float X, float Z, float Height, float Yaw)
    {
        public readonly Float3 Origin => new(X, Height, Z);
        public readonly Quaternion Turn => Quaternion.FromEuler(new Float3(0f, Yaw, 0f));
    }

    /// <summary>One part of a group as the group's code describes it, before it is placed or built.</summary>
    private sealed record PartDef(string Key, string Name, string About, float Radius, string Group, Float3 Offset, float Yaw, Action Build);

    private Group[] _groupList = [];
    private readonly List<PartDef> _defs = new();
    private List<GameObject>? _collecting;
    private string? _definingGroup;

    /// <summary>Called from a group's code: records one part, at its offset and facing within the group.</summary>
    private void Sub(string name, string about, float radius, Float3 offset, float yaw, Action build)
    {
        string key = $"{_definingGroup}/{name}";
        int count = 1;
        while (_defs.Exists(d => d.Key == key)) key = $"{_definingGroup}/{name} {++count}";
        _defs.Add(new PartDef(key, name, about, radius, _definingGroup!, offset, yaw, build));
    }

    private void DefineParts()
    {
        _defs.Clear();
        foreach (Group group in _groupList)
        {
            _definingGroup = group.Name;
            group.Build();
        }
        _definingGroup = null;
    }

    // Every object a part makes passes through here, so each one gets a stable name to save edits under.
    private new GameObject Add(GameObject go)
    {
        _collecting?.Add(go);
        return base.Add(go);
    }

    /// <summary>Builds every part where its group's automatic layout puts it, then moves each object to where Layout.txt places it.</summary>
    private void BuildWorld()
    {
        Dictionary<string, Placement> groups = Lay(_groupList);
        var built = new List<List<GameObject>>();
        foreach (PartDef def in _defs)
        {
            Placement group = groups[def.Group];
            _origin = group.Origin + group.Turn * def.Offset;
            _turn = Quaternion.FromEuler(new Float3(0f, group.Yaw + def.Yaw, 0f));

            var objects = new List<GameObject>();
            _collecting = objects;
            def.Build();
            _collecting = null;

            for (int i = 0; i < objects.Count; i++)
                Register($"{def.Key}#{i}", objects[i]);
            _landmarks.Add(new Landmark(def.Name, def.About, _origin, def.Radius));
            built.Add(objects);
        }

        _origin = Float3.Zero;
        _turn = Quaternion.Identity;

        foreach ((string key, Pose pose) in LoadLayout())
            if (_byKey.TryGetValue(key, out GameObject? go))
                SetPose(go, pose);

        // Layout.txt can move a part well away from where its code put it, so each landmark goes to the middle of what its part built.
        for (int i = 0; i < _landmarks.Count; i++)
        {
            if (built[i].Count == 0) continue;
            Float3 middle = Float3.Zero;
            foreach (GameObject go in built[i]) middle += go.Transform.Position;
            _landmarks[i] = _landmarks[i] with { Position = middle / built[i].Count };
        }
    }

    // ----------------------------------------------------------------
    //  Hand placed layout
    // ----------------------------------------------------------------

    private readonly record struct Pose(Float3 Position, Quaternion Rotation, Float3 Scale);

    private readonly Dictionary<string, GameObject> _byKey = new();
    private readonly Dictionary<PingPongMover, (Float3 From, Float3 To)> _moverEnds = new();

    private static string LayoutPath => Path.Combine(AppContext.BaseDirectory, "Layout.txt");

    private void Register(string key, GameObject go)
    {
        _byKey[key] = go;

        // A mover's ends are kept in the object's own space, so placing the object takes its path along.
        Quaternion inverse = Quaternion.Inverse(go.Transform.Rotation);
        foreach (PingPongMover mover in go.GetComponents<PingPongMover>())
            _moverEnds[mover] = (inverse * (mover.From - go.Transform.Position), inverse * (mover.To - go.Transform.Position));
    }

    private void SetPose(GameObject go, Pose pose)
    {
        go.Transform.Position = pose.Position;
        go.Transform.Rotation = pose.Rotation;
        go.Transform.LocalScale = pose.Scale;

        foreach (PingPongMover mover in go.GetComponents<PingPongMover>())
            if (_moverEnds.TryGetValue(mover, out var ends))
            {
                mover.From = go.Transform.Position + go.Transform.Rotation * ends.From;
                mover.To = go.Transform.Position + go.Transform.Rotation * ends.To;
            }
    }

    /// <summary>Reads the "object | position x y z | rotation x y z w | scale x y z" lines of Layout.txt.</summary>
    private static Dictionary<string, Pose> LoadLayout()
    {
        var result = new Dictionary<string, Pose>();
        if (!File.Exists(LayoutPath)) return result;

        foreach (string line in File.ReadAllLines(LayoutPath))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            string[] fields = line.Split('|');
            if (fields.Length != 11) continue;
            float F(int i) => float.Parse(fields[i].Trim(), CultureInfo.InvariantCulture);
            result[fields[0].Trim()] = new Pose(new Float3(F(1), F(2), F(3)), new Quaternion(F(4), F(5), F(6), F(7)), new Float3(F(8), F(9), F(10)));
        }
        return result;
    }

    private void BuildSpawnPad()
    {
        Mesh pad = Mesh.CreateCylinder(3f, 0.06f, 40);
        Add(Model("Spawn Pad", pad, Lit(new Color(0.05f, 0.05f, 0.06f, 1f)).Emissive(Orange, 0.08f), new Float3(0f, 0.03f, 0f)));
    }

    private static Material GridMaterial(Color background, Color line, float tileX = 1f, float tileY = 1f)
    {
        Material material = Lit(Color.White, 0f, 0.85f);
        material.SetTexture("_MainTex", Grid(background, line));
        if (tileX != 1f || tileY != 1f) material.SetVector("_Tiling", new Float2(tileX, tileY));
        return material;
    }

    private float Range(float min, float max) => min + _rng.NextSingle() * (max - min);

    // ----------------------------------------------------------------
    //  Building blocks, all in the current piece's own space
    // ----------------------------------------------------------------

    private GameObject Place(GameObject go, Float3 local, Float3? euler = null)
    {
        go.Transform.Position = W(local);
        go.Transform.Rotation = R(euler ?? Float3.Zero);
        return Add(go);
    }

    private GameObject Box(Float3 size, Float3 local, Material material, Float3? euler = null, string name = "Box")
        => Place(Block(name, size, material, Float3.Zero), local, euler);

    /// <summary>A collider with nothing drawn, for the invisible ramps laid over stairs.</summary>
    private void InvisibleBox(Float3 size, Float3 local, Float3 euler)
    {
        var go = new GameObject("Hidden Ramp");
        go.AddComponent<BoxCollider>().Size = size;
        Place(go, local, euler);
    }

    private GameObject MeshPiece(string name, Mesh mesh, Material material, Float3 local, Float3? euler = null)
    {
        GameObject go = Model(name, mesh, material, Float3.Zero);
        go.AddComponent<MeshCollider>().Mesh = mesh;
        return Place(go, local, euler);
    }

    /// <summary>A plate tilted up toward +Z by <paramref name="degrees"/>, with its low edge on the ground at <paramref name="foot"/>.</summary>
    private void Ramp(Float3 foot, float width, float length, float degrees, Material material, float yaw = 0f)
    {
        float a = degrees * MathF.PI / 180f;
        Quaternion spin = Quaternion.FromEuler(new Float3(0f, yaw, 0f));
        Float3 center = foot + spin * new Float3(0f, MathF.Sin(a) * length * 0.5f, MathF.Cos(a) * length * 0.5f);
        Box(new Float3(width, 0.3f, length), center, material, new Float3(-degrees, yaw, 0f), "Ramp");
    }

    /// <summary>Solid steps from the ground at <paramref name="foot"/> up to <paramref name="rise"/>, running toward +Z.</summary>
    private void Stairs(Float3 foot, float width, float rise, float run, float stepHeight, Material material)
    {
        int count = Math.Max(1, (int)MathF.Round(rise / stepHeight));
        float depth = run / count;
        for (int i = 0; i < count; i++)
        {
            float height = (i + 1) * stepHeight;
            Box(new Float3(width, height, depth), foot + new Float3(0f, height * 0.5f, (i + 0.5f) * depth), material, name: "Step");
        }
    }

    /// <summary>A kinematic body, moved by the scripts below through its velocity so what rides it is carried along.</summary>
    private Rigidbody3D Kinematic(string name, Mesh mesh, Action<GameObject> addCollider, Material material, Float3 local, Float3? euler = null)
    {
        GameObject go = Model(name, mesh, material, Float3.Zero);
        var rb = go.AddComponent<Rigidbody3D>();
        rb.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        rb.AffectedByGravity = false;
        addCollider(go);
        Place(go, local, euler);
        return rb;
    }

    private Rigidbody3D KinematicBox(string name, Float3 size, Material material, Float3 local)
        => Kinematic(name, Mesh.CreateCube(size), go => go.AddComponent<BoxCollider>().Size = size, material, local);

    private Rigidbody3D KinematicCylinder(string name, float radius, float height, Material material, Float3 local, Float3? euler = null)
        => Kinematic(name, Mesh.CreateCylinder(radius, height, 32), go => { var c = go.AddComponent<CylinderCollider>(); c.Radius = radius; c.Height = height; }, material, local, euler);

    private void Mover(Rigidbody3D body, Float3 fromLocal, Float3 toLocal, float speed)
    {
        var mover = body.GameObject.AddComponent<PingPongMover>();
        mover.From = W(fromLocal);
        mover.To = W(toLocal);
        mover.Speed = speed;
    }

    private static void Spin(Rigidbody3D body, float degreesPerSecond)
        => body.GameObject.AddComponent<Spinner>().DegreesPerSecond = degreesPerSecond;

    // ----------------------------------------------------------------
    //  Stairs and slopes
    // ----------------------------------------------------------------

    private void StairLanes()
    {
        float[] steps = [0.1f, 0.1f, 0.2f, 0.2f, 0.25f, 0.3f, 0.35f, 0.5f, 0f];
        bool[] overlay = [true, false, true, false, true, false, false, false, false];
        float slope = MathF.Atan2(4f, 8f) * 180f / MathF.PI;
        for (int i = 0; i < steps.Length; i++)
        {
            Float3 foot = new((i - 4) * 3f, 0f, -6f);
            if (steps[i] > 0f) Stairs(foot, 2.6f, 4f, 8f, steps[i], _stairsMat);
            else Ramp(foot, 2.6f, MathF.Sqrt(80f), slope, _stairsMat);

            if (overlay[i]) InvisibleBox(new Float3(2.6f, 0.05f, MathF.Sqrt(80f)), foot + new Float3(0f, 2f, 4f), new Float3(-slope, 0f, 0f));
        }
        Box(new Float3(27f, 4f, 4f), new Float3(0f, 2f, 4f), _stairsMat, name: "Top Landing");
        Stairs(new Float3(0f, 0f, 14f) , 6f, 4f, 8f, 0.25f, _stairsMat);
        Box(new Float3(6f, 4f, 2f), new Float3(0f, 2f, 7f), _stairsMat, name: "Back Landing");
    }

    private void SlopeFan()
    {
        for (int i = 0; i < 7; i++)
        {
            float yaw = -60f + i * 20f;
            Quaternion spin = Quaternion.FromEuler(new Float3(0f, yaw, 0f));
            Ramp(spin * new Float3(0f, 0f, 2.5f), 2f, 6f, 10f + i * 10f, i % 2 == 0 ? _stairsMat : _edgeMat, yaw);
        }
    }

    private void CrestAndTrough()
    {
        float s25 = MathF.Sin(25f * MathF.PI / 180f), c25 = MathF.Cos(25f * MathF.PI / 180f);
        Ramp(new Float3(-4f, 0f, -4f), 4f, 4f, 25f, _stairsMat);
        Box(new Float3(4f, 0.3f, 4f), new Float3(-4f, 2f * s25, -4f + 6f * c25), _edgeMat, new Float3(25f, 0f, 0f), "Crest Down");

        float slope = 2f / s25;
        Float3 trough = new(4f, 0f, -8f);
        Stairs(trough, 4f, 2f, 4f, 0.25f, _edgeMat);
        Box(new Float3(4f, 2f, 3f), trough + new Float3(0f, 1f, 5.5f), _edgeMat, name: "Trough Landing");
        Box(new Float3(4f, 0.3f, slope), trough + new Float3(0f, 1f, 7f + c25 * slope * 0.5f), _stairsMat, new Float3(25f, 0f, 0f), "Trough Down");
        Box(new Float3(4f, 0.3f, slope), trough + new Float3(0f, 1f, 7f + c25 * slope * 1.5f), _edgeMat, new Float3(-25f, 0f, 0f), "Trough Up");
        Box(new Float3(4f, 2f, 3f), trough + new Float3(0f, 1f, 7f + c25 * slope * 2f + 1.5f), _edgeMat, name: "Trough Landing");
    }

    private void Valley()
    {
        float s62 = MathF.Sin(62f * MathF.PI / 180f), c62 = MathF.Cos(62f * MathF.PI / 180f);
        Box(new Float3(4f, 0.3f, 8f), new Float3(-c62 * 2f, s62 * 2f, 0f), _trapMat, new Float3(0f, 0f, -62f), "Valley Side");
        Box(new Float3(4f, 0.3f, 8f), new Float3(c62 * 2f, s62 * 2f, 0f), _trapMat, new Float3(0f, 0f, 62f), "Valley Side");
    }

    private void Tower(float winding)
    {
        const int Levels = 8;
        Box(new Float3(2f, Levels, 2f), new Float3(0f, Levels * 0.5f, 0f), _dark, name: "Column");

        // The path is a 2 m wide ring around the column. A landing sits on each corner of the ring and a
        // flight of four steps climbs the 2 m between one corner and the next.
        Float3[] corners = [new(-2f, 0f, -2f), new(2f, 0f, -2f), new(2f, 0f, 2f), new(-2f, 0f, 2f)];
        int corner = 0;
        for (int level = 0; level < Levels; level++)
        {
            int next = winding > 0f ? (corner + 1) % 4 : (corner + 3) % 4;
            Float3 from = corners[corner], dir = (corners[next] - from) * 0.25f;
            bool alongX = MathF.Abs(dir.X) > 0.5f;

            for (int step = 0; step < 4; step++)
            {
                float top = level + (step + 1) * 0.25f;
                Box(alongX ? new Float3(0.5f, 0.25f, 2f) : new Float3(2f, 0.25f, 0.5f), from + dir * (1.25f + step * 0.5f) + new Float3(0f, top - 0.125f, 0f), _towerMat, name: "Tower Step");
            }

            Box(new Float3(2f, 0.25f, 2f), corners[next] + new Float3(0f, level + 1f - 0.125f, 0f), _edgeMat, name: "Tower Landing");
            corner = next;
        }

        if (winding > 0f)
        {
            Box(new Float3(5f, 0.05f, 5f), new Float3(6f, 0.5f, 0f), Lit(Orange, 0f, 0.5f), name: "Thin Plate");
            Box(new Float3(0.2f, 0.5f, 0.2f), new Float3(6f, 0.25f, 0f), _dark, name: "Plate Post");
        }
    }

    // ----------------------------------------------------------------
    //  Ceilings and gaps
    // ----------------------------------------------------------------

    private void CrawlTunnel()
    {
        for (int i = 0; i < 6; i++)
        {
            float z = -3.75f + i * 1.5f;
            Box(new Float3(0.4f, 1.3f, 1.5f), new Float3(-1.4f, 0.65f, z), _dark, name: "Tunnel Wall");
            Box(new Float3(0.4f, 1.3f, 1.5f), new Float3(1.4f, 0.65f, z), _dark, name: "Tunnel Wall");
            Box(new Float3(3.2f, 0.3f, 1.5f), new Float3(0f, 1.45f, z), _ceilingMat, name: "Tunnel Roof");
        }
    }

    private void DoorwayWall()
    {
        (float Width, float Height)[] doors = [(1.4f, 1.3f), (1.4f, 1.85f), (1.4f, 2.2f), (0.7f, 2.4f), (0.9f, 2.4f), (1.2f, 2.4f)];
        float total = 1f;
        foreach ((float width, float _) in doors) total += width + 1.6f;

        float x = -total * 0.5f;
        Box(new Float3(1f, 3f, 0.4f), new Float3(x + 0.5f, 1.5f, 0f), _ceilingMat, name: "Wall");
        x += 1f;
        foreach ((float width, float height) in doors)
        {
            Box(new Float3(width, 3f - height, 0.4f), new Float3(x + width * 0.5f, height + (3f - height) * 0.5f, 0f), _dark, name: "Lintel");
            x += width;
            Box(new Float3(1.6f, 3f, 0.4f), new Float3(x + 0.8f, 1.5f, 0f), _ceilingMat, name: "Wall");
            x += 1.6f;
        }
    }

    private void Bars()
    {
        float[] heights = [0.15f, 0.35f, 1.6f];
        for (int i = 0; i < heights.Length; i++)
            Box(new Float3(3f, 0.08f, 0.08f), new Float3(0f, heights[i], -2.5f + i * 2.5f), Lit(Yellow, 0f, 0.4f), name: "Bar");
        foreach (float x in new[] { -1.5f, 1.5f })
            Box(new Float3(0.15f, 1.8f, 7f), new Float3(x, 0.9f, 0f), _dark, name: "Bar Post");
    }

    // ----------------------------------------------------------------
    //  Traps
    // ----------------------------------------------------------------

    private void OpenBox()
    {
        Box(new Float3(4f, 0.2f, 4f), new Float3(0f, 3.9f, 0f), _trapMat, name: "Box Roof");
        Box(new Float3(0.2f, 4f, 4f), new Float3(-2f, 2f, 0f), _trapMat, name: "Box Wall");
        Box(new Float3(0.2f, 4f, 4f), new Float3(2f, 2f, 0f), _trapMat, name: "Box Wall");
        Box(new Float3(4f, 4f, 0.2f), new Float3(0f, 2f, 2f), _trapMat, name: "Box Wall");
    }

    private void NarrowingV()
    {
        Box(new Float3(0.2f, 3f, 5f), new Float3(-1f, 1.5f, 0f), _trapMat, new Float3(0f, 22f, 0f), "V Wall");
        Box(new Float3(0.2f, 3f, 5f), new Float3(1f, 1.5f, 0f), _trapMat, new Float3(0f, -22f, 0f), "V Wall");
    }

    private void CeilingWedge()
    {
        Box(new Float3(3f, 0.2f, 6f), new Float3(0f, 1.3f, 0f), _trapMat, new Float3(25f, 0f, 0f), "Wedge Roof");
        Box(new Float3(0.2f, 2.6f, 6f), new Float3(-1.6f, 1.3f, 0f), _dark, name: "Wedge Side");
        Box(new Float3(0.2f, 2.6f, 6f), new Float3(1.6f, 1.3f, 0f), _dark, name: "Wedge Side");
    }

    private void LeaningPocket()
    {
        Box(new Float3(0.2f, 4f, 4f), new Float3(-1.2f, 2f, 0f), _trapMat, new Float3(0f, 30f, -20f), "Pocket");
        Box(new Float3(0.2f, 4f, 4f), new Float3(1.2f, 2f, 0f), _trapMat, new Float3(0f, -30f, 20f), "Pocket");
    }

    private void Funnel()
    {
        Box(new Float3(0.2f, 3f, 8.2f), new Float3(-1.15f, 1.5f, 0f), _trapMat, new Float3(0f, 12f, 0f), "Funnel");
        Box(new Float3(0.2f, 3f, 8.2f), new Float3(1.15f, 1.5f, 0f), _trapMat, new Float3(0f, -12f, 0f), "Funnel");
    }

    // ----------------------------------------------------------------
    //  Curves
    // ----------------------------------------------------------------

    private void QuarterPipes()
    {
        float[] radii = [1f, 2f, 3f, 4.5f];
        float x = -6f;
        foreach (float radius in radii)
        {
            MeshPiece("Quarter Pipe", QuarterPipe(radius, 3f, 16), _curveMat, new Float3(x, 0f, -2f));
            Box(new Float3(3f, radius + 0.5f, 0.3f), new Float3(x, (radius + 0.5f) * 0.5f, radius - 2f + 0.15f), _dark, name: "Pipe Back");
            x += 3.5f;
        }
    }

    private void Arch(float radius) => MeshPiece("Arch", ArchTunnel(radius, 6f, 20), _curveMat, new Float3(0f, 0f, -3f));

    private void DomeAt(float radius) => MeshPiece("Dome", Dome(radius, 18), _curveMat, new Float3(0f, -radius * 0.35f, 0f));

    private void Logs()
    {
        float[] radii = [0.3f, 0.6f, 1f, 1.5f];
        float z = -5f;
        foreach (float radius in radii)
        {
            GameObject log = Model("Log", Mesh.CreateCylinder(radius, 6f, 24), _curveMat, Float3.Zero);
            var collider = log.AddComponent<CylinderCollider>();
            collider.Radius = radius;
            collider.Height = 6f;
            Place(log, new Float3(0f, radius * 0.6f, z + radius), new Float3(0f, 0f, 90f));
            z += radius * 2f + 1.2f;
        }
    }

    /// <summary>A floor that curves up into a wall, the inside of a quarter cylinder, facing its centre.</summary>
    private static Mesh QuarterPipe(float radius, float width, int segments)
    {
        var builder = new MeshBuilder();
        Float3 Point(int s, float x)
        {
            float a = s * MathF.PI * 0.5f / segments;
            return new Float3(x, radius - radius * MathF.Cos(a), radius * MathF.Sin(a));
        }

        for (int s = 0; s < segments; s++)
        {
            Float3 a = Point(s, -width * 0.5f), b = Point(s, width * 0.5f), d = Point(s + 1, -width * 0.5f), e = Point(s + 1, width * 0.5f);
            Float3 facing = new Float3(0f, radius, 0f) - (a + e) * 0.5f;
            builder.Quad(a, b, e, d, new Float2(0f, s), new Float2(width, s), new Float2(width, s + 1), new Float2(0f, s + 1), facing);
        }
        return builder.Build();
    }

    /// <summary>A half cylinder shell standing on the floor, so its walls curve over into the ceiling, seen from both sides.</summary>
    private static Mesh ArchTunnel(float radius, float length, int segments)
    {
        var builder = new MeshBuilder();
        Float3 Point(int s, float z)
        {
            float a = s * MathF.PI / segments;
            return new Float3(radius * MathF.Cos(a), radius * MathF.Sin(a), z);
        }

        for (int s = 0; s < segments; s++)
        {
            Float3 a = Point(s, 0f), b = Point(s, length), d = Point(s + 1, 0f), e = Point(s + 1, length);
            Float3 facing = -(a + e) * 0.5f;
            facing.Z = 0f;
            builder.Quad(a, b, e, d, new Float2(s, 0f), new Float2(s, length), new Float2(s + 1, length), new Float2(s + 1, 0f), facing);
            builder.Quad(a, b, e, d, new Float2(s, 0f), new Float2(s, length), new Float2(s + 1, length), new Float2(s + 1, 0f), -facing);
        }
        return builder.Build();
    }

    /// <summary>The top half of a sphere, facing out.</summary>
    private static Mesh Dome(float radius, int segments)
    {
        var builder = new MeshBuilder();
        Float3 Point(int ring, int slice)
        {
            float pitch = ring * MathF.PI * 0.5f / segments, yaw = slice * MathF.PI * 2f / segments;
            return new Float3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Sin(yaw)) * radius;
        }

        for (int ring = 0; ring < segments; ring++)
            for (int slice = 0; slice < segments; slice++)
            {
                Float3 a = Point(ring, slice), b = Point(ring, slice + 1), d = Point(ring + 1, slice), e = Point(ring + 1, slice + 1);
                builder.Quad(a, b, e, d, new Float2(slice, ring), new Float2(slice + 1, ring), new Float2(slice + 1, ring + 1), new Float2(slice, ring + 1), (a + e) * 0.5f);
            }
        return builder.Build();
    }

    // ----------------------------------------------------------------
    //  Rough ground
    // ----------------------------------------------------------------

    private void JaggedField() => MeshPiece("Jagged Field", Heightfield(12f, 12, (u, v) => _rng.NextSingle() * 1.2f * Edge(u, v)), _roughMat, new Float3(0f, 0.02f, 0f));

    private void RollingBumps() => MeshPiece("Rolling Bumps", Heightfield(12f, 32, (u, v) => (MathF.Sin(u * 18f) * MathF.Cos(v * 15f) * 0.5f + 0.5f) * 1.2f * Edge(u, v)), _roughMat, new Float3(0f, 0.02f, 0f));

    private void Pebbles()
    {
        for (int i = 0; i < 320; i++)
        {
            float width = Range(0.08f, 0.35f), height = Range(0.05f, 0.45f);
            float angle = Range(0f, MathF.PI * 2f), distance = MathF.Sqrt(Range(0f, 1f)) * 6.5f;
            Box(new Float3(width, height, width * Range(0.7f, 1.3f)), new Float3(MathF.Sin(angle) * distance, height * 0.5f, MathF.Cos(angle) * distance), _dark, new Float3(Range(-8f, 8f), Range(0f, 90f), Range(-8f, 8f)), "Pebble");
        }
    }

    private static float Edge(float u, float v)
    {
        float edge = MathF.Min(MathF.Min(u, 1f - u), MathF.Min(v, 1f - v));
        return Math.Clamp(edge * 6f, 0f, 1f);
    }

    private static Mesh Heightfield(float size, int cells, Func<float, float, float> height)
    {
        var heights = new float[cells + 1, cells + 1];
        for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
                heights[x, z] = height(x / (float)cells, z / (float)cells);

        var builder = new MeshBuilder();
        Float3 P(int x, int z) => new((x / (float)cells - 0.5f) * size, heights[x, z], (z / (float)cells - 0.5f) * size);
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
                builder.Quad(P(x, z), P(x + 1, z), P(x + 1, z + 1), P(x, z + 1), new Float2(x, z), new Float2(x + 1, z), new Float2(x + 1, z + 1), new Float2(x, z + 1), Float3.UnitY);
        return builder.Build();
    }

    /// <summary>A staircase as one closed triangle mesh: treads, risers, sides and back.</summary>
    private static Mesh MeshStairs(float width, int steps, float rise, float run)
    {
        var builder = new MeshBuilder();
        float hw = width * 0.5f;
        for (int i = 0; i < steps; i++)
        {
            float y0 = i * rise, y1 = (i + 1) * rise, z0 = i * run, z1 = (i + 1) * run;
            builder.Quad(new(-hw, y0, z0), new(hw, y0, z0), new(hw, y1, z0), new(-hw, y1, z0), default, default, default, default, -Float3.UnitZ);
            builder.Quad(new(-hw, y1, z0), new(hw, y1, z0), new(hw, y1, z1), new(-hw, y1, z1), default, default, default, default, Float3.UnitY);
            builder.Quad(new(-hw, 0f, z0), new(-hw, y1, z0), new(-hw, y1, z1), new(-hw, 0f, z1), default, default, default, default, -Float3.UnitX);
            builder.Quad(new(hw, 0f, z0), new(hw, y1, z0), new(hw, y1, z1), new(hw, 0f, z1), default, default, default, default, Float3.UnitX);
        }
        float top = steps * rise, back = steps * run;
        builder.Quad(new(-hw, 0f, back), new(hw, 0f, back), new(hw, top, back), new(-hw, top, back), default, default, default, default, Float3.UnitZ);
        return builder.Build();
    }

    // ----------------------------------------------------------------
    //  Moving things
    // ----------------------------------------------------------------

    private void SlidingPlatform()
    {
        Rigidbody3D platform = KinematicBox("Sliding Platform", new Float3(2.5f, 0.2f, 2.5f), _movingMat, new Float3(0f, 0.6f, -4f));
        Mover(platform, new Float3(0f, 0.6f, -4f), new Float3(0f, 0.6f, 4f), 3f);
    }

    private void LiftToLedge()
    {
        Box(new Float3(3f, 4f, 3f), new Float3(1.5f, 2f, 0f), _dark, name: "Ledge");
        Rigidbody3D lift = KinematicBox("Lift", new Float3(2.5f, 0.2f, 2.5f), _movingMat, new Float3(-1.25f, 0.1f, 0f));
        Mover(lift, new Float3(-1.25f, 0.1f, 0f), new Float3(-1.25f, 3.9f, 0f), 1.5f);
    }

    private void Disc(float tilt)
        => Spin(KinematicCylinder(tilt > 0f ? "Tilted Disc" : "Spinning Disc", 3.5f, 0.25f, _movingMat, new Float3(0f, tilt > 0f ? 0.8f : 0.13f, 0f), new Float3(tilt, 0f, 0f)), tilt > 0f ? 45f : 60f);

    private void Sweepers()
    {
        foreach (float z in new[] { -2.6f, 2.6f })
            Spin(Kinematic("Sweeper", Mesh.CreateCube(new Float3(0.15f, 1.6f, 5f)), go => go.AddComponent<BoxCollider>().Size = new Float3(0.15f, 1.6f, 5f), Lit(Red, 0f, 0.5f), new Float3(0f, 0.85f, z)), z > 0f ? 40f : -40f);
    }

    private void LogRollers()
    {
        foreach (float z in new[] { -2.2f, 2.2f })
            Spin(KinematicCylinder("Log Roller", 1.2f, 6f, _movingMat, new Float3(0f, 0.4f, z), new Float3(0f, 0f, 90f)), 50f);
    }

    // ----------------------------------------------------------------
    //  Edges and jumps
    // ----------------------------------------------------------------

    private void HopBlocks()
    {
        float[] heights = [0.5f, 1f, 1.5f, 2f, 2.5f];
        for (int i = 0; i < heights.Length; i++)
            Box(new Float3(2f, heights[i], 2f), new Float3((i - 2) * 2.6f, heights[i] * 0.5f, 0f), i % 2 == 0 ? _plainMat : _edgeMat, name: "Hop");
    }

    private void Beams()
    {
        float[] widths = [1f, 0.5f, 0.25f];
        Box(new Float3(3f, 2f, 3f), new Float3(0f, 1f, -6.5f), _edgeMat, name: "Beam Start");
        Ramp(new Float3(0f, 0f, -12.1f), 3f, 4.6f, 26f, _edgeMat);
        Box(new Float3(3f, 2f, 3f), new Float3(0f, 1f, 6.5f), _edgeMat, name: "Beam End");
        for (int i = 0; i < widths.Length; i++)
            Box(new Float3(widths[i], 0.2f, 10f), new Float3(-1f + i, 1.9f, 0f), _dark, name: "Beam");
    }

    // ----------------------------------------------------------------
    //  Gravity and travel
    // ----------------------------------------------------------------

    private const float PadGravity = 24f;

    private Material Glow(Color color) => Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(color, 2f);

    private JumpPad Pad(Float3 local, Float3 launchLocal, Color color)
    {
        GameObject go = Model("Jump Pad", Mesh.CreateCylinder(1.2f, 0.08f, 32), Glow(color), Float3.Zero);
        JumpPad pad = go.AddComponent<JumpPad>();
        pad.Launch = _turn * launchLocal;
        Place(go, local + new Float3(0f, 0.04f, 0f));
        return pad;
    }

    private Teleporter Gate(Float3 local, Float3? euler = null)
    {
        GameObject go = Model("Teleporter", Mesh.CreateCylinder(1f, 0.06f, 32), Glow(new Color(0.55f, 0.2f, 1f, 1f)), Float3.Zero);
        Teleporter gate = go.AddComponent<Teleporter>();
        Place(go, local, euler);
        return gate;
    }

    private GameObject Zone(Float3 local, Action<GravityZone> setUp)
    {
        var go = new GameObject("Gravity Zone");
        setUp(go.AddComponent<GravityZone>());
        return Place(go, local);
    }

    /// <summary>The speed that throws something from the ground onto a top <paramref name="height"/> up and <paramref name="ahead"/> along, clearing it by a metre.</summary>
    private static Float3 Throw(float height, float ahead)
    {
        float up = MathF.Sqrt(2f * PadGravity * (height + 1f));
        float flight = up / PadGravity + MathF.Sqrt(2f * 1f / PadGravity);
        return new Float3(0f, up, ahead / flight);
    }

    private void JumpPads()
    {
        float[] heights = [2f, 4f, 7f];
        for (int i = 0; i < heights.Length; i++)
        {
            float x = (i - 1) * 5f;
            Box(new Float3(3f, heights[i], 3f), new Float3(x, heights[i] * 0.5f, 4f), _travelMat, name: "Pad Tower");
            Pad(new Float3(x, 0f, -1f), Throw(heights[i], 5f), new Color(0.2f, 1f, 0.5f, 1f));
        }
    }

    private void Teleporters()
    {
        Box(new Float3(2.6f, 9f, 2.6f), new Float3(0f, 4.5f, 6f), _travelMat, name: "Gate Column");
        Teleporter low = Gate(new Float3(0f, 0.03f, 0f));
        Teleporter high = Gate(new Float3(0f, 9.03f, 6f));
        low.Exit = high;
        high.Exit = low;
    }

    private void Planet()
    {
        const float Radius = 6f;
        Float3 center = new(0f, 20f, 0f);

        GameObject planet = Model("Planet", Mesh.CreateSphere(Radius, 32, 48), _travelMat, Float3.Zero);
        planet.AddComponent<SphereCollider>().Radius = Radius;
        Place(planet, center);

        Zone(center, zone =>
        {
            zone.TowardCenter = true;
            zone.Radius = Radius + 8f;
            zone.Strength = PadGravity;
            zone.Priority = 1;
        });

        // Blocks and steps standing out of the surface, each upright to the planet.
        var random = new Random(7);
        for (int i = 0; i < 14; i++)
        {
            Float3 direction = Float3.Normalize(new Float3(random.NextSingle() * 2f - 1f, random.NextSingle() * 2f - 1f, random.NextSingle() * 2f - 1f));
            if (direction.Y > 0.85f) continue;
            float height = 0.3f + random.NextSingle() * 1.2f;
            Quaternion stand = Quaternion.FromToRotation(Float3.UnitY, direction);
            GameObject block = Block("Planet Block", new Float3(1.4f, height, 1.4f), i % 2 == 0 ? _plainMat : _edgeMat, Float3.Zero);
            block.Transform.Position = W(center + direction * (Radius + height * 0.5f - 0.1f));
            block.Transform.Rotation = _turn * stand;
            Add(block);
        }

        Pad(Float3.Zero, new Float3(0f, 23f, 0f), new Color(1f, 0.6f, 0.1f, 1f));

        Teleporter top = Gate(center + new Float3(0f, Radius + 0.02f, 0f));
        Teleporter ground = Gate(new Float3(3.5f, 0.03f, -3.5f));
        top.Exit = ground;
        ground.Exit = top;
    }

    private void FlipRoom()
    {
        const float Size = 8f, High = 5f, Wall = 0.4f, Door = 2.4f;
        float outer = Size + Wall * 2f;
        Box(new Float3(outer, Wall, outer), new Float3(0f, High + Wall * 0.5f, 0f), _travelMat, name: "Flip Ceiling");
        Box(new Float3(outer, High, Wall), new Float3(0f, High * 0.5f, Size * 0.5f + Wall * 0.5f), _travelMat, name: "Flip Wall");
        foreach (float side in new[] { -1f, 1f })
        {
            Box(new Float3(Wall, High, outer), new Float3(side * (Size * 0.5f + Wall * 0.5f), High * 0.5f, 0f), _travelMat, name: "Flip Wall");
            float segment = (outer - Door) * 0.5f;
            Box(new Float3(segment, High, Wall), new Float3(side * (Door * 0.5f + segment * 0.5f), High * 0.5f, -(Size * 0.5f + Wall * 0.5f)), _travelMat, name: "Flip Wall");
        }

        // Things to stand on once the ceiling is the floor.
        Box(new Float3(2f, 0.6f, 2f), new Float3(-2f, High - 0.3f, 1.5f), _edgeMat, name: "Ceiling Block");
        Box(new Float3(2f, 1.2f, 2f), new Float3(2f, High - 0.6f, 2f), _plainMat, name: "Ceiling Block");

        Zone(new Float3(0f, High * 0.5f, 0f), zone =>
        {
            zone.Size = new Float3(Size - 0.4f, High, Size - 0.4f);
            zone.Direction = new Float3(0f, 1f, 0f);
            zone.Strength = PadGravity;
            zone.Priority = 1;
        });
    }

    private void WallWalk()
    {
        const float Length = 16f, High = 8f;
        Box(new Float3(0.6f, High, Length), new Float3(-3.3f, High * 0.5f, 0f), _travelMat, name: "Walking Wall");
        for (int i = 0; i < 4; i++)
            Box(new Float3(0.8f, 1.6f, 1.6f), new Float3(-2.6f, 2f + i * 1.4f, -4.5f + i * 3f), _edgeMat, name: "Wall Ledge");

        Zone(new Float3(-0.5f, High * 0.5f, 0f), zone =>
        {
            zone.Size = new Float3(5f, High, Length - 2f);
            zone.Direction = new Float3(-1f, 0f, 0f);
            zone.Strength = PadGravity;
            zone.Priority = 1;
        });

        Place(Model("Zone Edge", Mesh.CreateCube(new Float3(0.1f, 0.02f, Length - 2f)), Glow(new Color(0.3f, 0.6f, 1f, 1f)), Float3.Zero), new Float3(2f, 0.01f, 0f));
    }

    // ----------------------------------------------------------------
    //  Player
    // ----------------------------------------------------------------

    private void BuildPlayer(Float3 position)
    {
        var player = new GameObject("Player");
        player.Transform.Position = position;

        var model = new GameObject("Model");
        model.SetParent(player);
        model.Transform.LocalPosition = Float3.Zero;
        void Part(Mesh mesh, Material material, Float3 local)
        {
            GameObject part = Model("Part", mesh, material, Float3.Zero);
            part.SetParent(model);
            part.Transform.LocalPosition = local;
            part.Transform.LocalRotation = Quaternion.Identity;
        }
        Material suit = Lit(Orange, 0f, 0.45f);
        Part(Mesh.CreateCapsule(0.35f, 1.25f), suit, new Float3(0f, 0.68f, 0f));
        Part(Mesh.CreateSphere(0.27f, 12, 18), suit, new Float3(0f, 1.52f, 0f));
        Part(Mesh.CreateCube(new Float3(0.38f, 0.14f, 0.12f)), Lit(new Color(0.01f, 0.02f, 0.04f, 1f), 0.8f, 0.1f).Emissive(new Color(0.2f, 0.7f, 1f, 1f), 1.5f), new Float3(0f, 1.55f, 0.22f));
        Part(Mesh.CreateCube(new Float3(0.45f, 0.55f, 0.22f)), Lit(new Color(0.05f, 0.05f, 0.06f, 1f), 0f, 0.6f), new Float3(0f, 0.95f, -0.3f));

        _character = player.AddComponent<CharacterController>();
        _character.Shape = CharacterController.ColliderShape.Capsule;
        _character.Radius = 0.38f;
        _character.Height = 1.8f;
        _character.StepSize = 0.3f;
        _input = player.AddComponent<CharacterInput>();
        _input.Model = model.Transform;
        _input.View = _chase;
        Add(player);
    }

    public override void DrawControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Movement");
        Slider(paper, font, "Run speed", _input.Speed, 1f, 12f, v => _input.Speed = v, "0.0");
        Slider(paper, font, "Jump height", _input.JumpHeight, 0.2f, 3f, v => _input.JumpHeight = v, "0.0");
        Slider(paper, font, "Gravity", _input.Gravity, 5f, 50f, v => _input.Gravity = v, "0");
        Slider(paper, font, "Air control", _input.AirControl, 0f, 1f, v => _input.AirControl = v);

        Header(paper, font, "Controller", 1);
        Slider(paper, font, "Steepest walkable slope", _character.MaxSlopeAngle, 10f, 80f, v => _character.MaxSlopeAngle = v, "0");
        Slider(paper, font, "Step height", _character.StepSize, 0f, 0.6f, v => _character.StepSize = v);
        Slider(paper, font, "Snap down distance", _character.SnapDownDistance, 0f, 1f, v => _character.SnapDownDistance = v);
        Cycle(paper, font, "Shape", _character.Shape, v => _character.Shape = v);
        Button(paper, font, "Back to the spawn", () => { _input.Stand(); _character.Teleport(_spawn); });
    }
}

/// <summary>Drives a kinematic body back and forth between two points through its velocity, pausing at each end.</summary>
public sealed class PingPongMover : MonoBehaviour
{
    public Float3 From, To;
    public float Speed = 3f;
    public float Pause = 0.6f;

    private Rigidbody3D _body = null!;
    private bool _forward = true;
    private float _wait;

    public override void OnEnable() => _body = GetComponent<Rigidbody3D>()!;

    public override void FixedUpdate()
    {
        float dt = Time.FixedDeltaTime;
        if (_wait > 0f)
        {
            _wait -= dt;
            _body.LinearVelocity = Float3.Zero;
            return;
        }

        Float3 target = _forward ? To : From;
        Float3 offset = target - _body.Position;
        float distance = Float3.Length(offset);
        if (distance <= Speed * dt)
        {
            _body.LinearVelocity = offset / dt;
            _forward = !_forward;
            _wait = Pause;
            return;
        }

        _body.LinearVelocity = offset / distance * Speed;
    }
}

/// <summary>Spins a kinematic body at a steady rate about its own up axis.</summary>
public sealed class Spinner : MonoBehaviour
{
    public float DegreesPerSecond = 45f;

    private Rigidbody3D _body = null!;

    public override void OnEnable() => _body = GetComponent<Rigidbody3D>()!;

    public override void FixedUpdate()
        => _body.AngularVelocity = _body.Rotation * Float3.UnitY * (DegreesPerSecond * MathF.PI / 180f);
}

public sealed class CharacterInput : MonoBehaviour
{
    public float Speed = 6f;
    public float JumpHeight = 1.4f;
    public float AirControl = 0.35f;

    /// <summary>The pull of gravity outside every <see cref="GravityZone"/>.</summary>
    public float Gravity = 24f;
    public Transform Model = null!;
    public ChaseCamera View = null!;

    private const float Acceleration = 50f;
    private const float CoyoteTime = 0.12f;
    private const float JumpBuffer = 0.12f;
    private const float StandingHeight = 1.8f;
    private const float CrouchHeight = 1.1f;

    private CharacterController _controller = null!;
    private Float3 _velocity;
    private float _sinceGrounded;
    private float _sinceJumpPressed = 1f;
    private float _sinceLaunched = 1f;
    private float _facing;
    private bool _wantsCrouch;
    private Quaternion _body = Quaternion.Identity;
    private Teleporter? _arrivedOn;

    public bool Crouched { get; private set; }

    public override void OnEnable() => _controller = GetComponent<CharacterController>()!;

    /// <summary>Stands back up immediately and forgets any motion, for a teleport.</summary>
    public void Stand()
    {
        _wantsCrouch = false;
        _velocity = Float3.Zero;
        _arrivedOn = null;
        if (_controller.IsNotValid()) return;
        _controller.Up = Float3.UnitY;
        if (Crouched && _controller.TrySetHeight(StandingHeight)) SetCrouched(false);
    }

    private void SetCrouched(bool crouched)
    {
        Crouched = crouched;
        Model.LocalScale = new Float3(1f, crouched ? CrouchHeight / StandingHeight : 1f, 1f);
    }

    public override void Update()
    {
        float dt = Time.DeltaTime;
        if (dt <= 0f) return;

        if (Input.GetKeyDown(KeyCode.C)) _wantsCrouch = !_wantsCrouch;
        if (_wantsCrouch && !Crouched && _controller.TrySetHeight(CrouchHeight)) SetCrouched(true);
        else if (!_wantsCrouch && Crouched && _controller.TrySetHeight(StandingHeight)) SetCrouched(false);

        // Gravity comes from whatever zone the middle of the body is in, and up is straight away from it.
        Float3 pull = GravityZone.At(_controller.Center, new Float3(0f, -Gravity, 0f));
        float gravity = Float3.Length(pull);
        if (gravity > 1e-3f) _controller.Up = -pull / gravity;
        Float3 up = _controller.Up;
        View.Up = up;

        Float2 input = Float2.Zero;
        if (Input.GetKey(KeyCode.W)) input.Y += 1f;
        if (Input.GetKey(KeyCode.S)) input.Y -= 1f;
        if (Input.GetKey(KeyCode.D)) input.X += 1f;
        if (Input.GetKey(KeyCode.A)) input.X -= 1f;
        if (Float2.LengthSquared(input) > 1f) input = Float2.Normalize(input);

        Float3 forward = Across(View.Heading, up);
        if (Float3.LengthSquared(forward) < 1e-4f) forward = Across(_body * Float3.UnitZ, up);
        forward = Float3.Normalize(forward);
        Float3 right = Float3.Cross(up, forward);
        Float3 wish = (forward * input.Y + right * input.X) * Speed * (Crouched ? 0.45f : 1f);

        bool grounded = _controller.IsGrounded && _sinceLaunched > 0.2f;
        _sinceGrounded = grounded ? 0f : _sinceGrounded + dt;
        _sinceJumpPressed = Input.GetKeyDown(KeyCode.Space) ? 0f : _sinceJumpPressed + dt;
        _sinceLaunched += dt;

        float rising = Float3.Dot(_velocity, up);
        Float3 across = _velocity - up * rising;

        float control = grounded ? 1f : AirControl;
        Float3 change = wish - across;
        float maxChange = Acceleration * control * dt;
        if (Float3.Length(change) > maxChange) change = Float3.Normalize(change) * maxChange;
        across += change;

        if (grounded && rising <= 0f) rising = -2f;
        else rising -= gravity * dt;

        if (!Crouched && _sinceJumpPressed < JumpBuffer && _sinceGrounded < CoyoteTime && rising <= 0f)
        {
            Float3 carried = _controller.GroundVelocity;
            across += Across(carried, up);
            rising = MathF.Sqrt(2f * gravity * JumpHeight) + MathF.Max(Float3.Dot(carried, up), 0f);
            _sinceJumpPressed = 1f;
            _sinceGrounded = 1f;
        }

        // Letting go of jump early cuts the climb short, for small hops. A pad launch always flies its full arc.
        if (!Input.GetKey(KeyCode.Space) && rising > 0f && _sinceLaunched > 1f) rising -= gravity * dt;

        _velocity = across + up * rising;
        UsePadsAndGates();

        CharacterController.CollisionFlags flags = _controller.Move(_velocity * dt);
        Float3 achieved = _controller.Velocity;
        rising = Float3.Dot(_velocity, up);
        across = _velocity - up * rising;
        if ((flags & CharacterController.CollisionFlags.Above) != 0 && rising > 0f) rising = 0f;
        if ((flags & CharacterController.CollisionFlags.Sides) != 0) across = Across(achieved, up);

        // In the air, a fall something held up stops building speed, and a run up a steep slope keeps the upward speed it gained.
        if (!_controller.IsGrounded) rising = MathF.Max(rising, Float3.Dot(achieved, up));
        _velocity = across + up * rising;

        TurnBody(up, wish, input, dt);
    }

    private static Float3 Across(Float3 v, Float3 up) => v - up * Float3.Dot(v, up);

    /// <summary>Turns the body to stand along up, smoothly, and to face the way it walks.</summary>
    private void TurnBody(Float3 up, Float3 wish, Float2 input, float dt)
    {
        Quaternion toUp = Quaternion.FromToRotation(_body * Float3.UnitY, up);
        _body = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, toUp, MathF.Min(1f, dt * 12f)) * _body);

        _facing += _controller.GroundYawDelta;
        if (Float2.LengthSquared(input) > 0.01f)
        {
            Float3 local = Quaternion.Inverse(_body) * wish;
            float target = MathF.Atan2(local.X, local.Z) * 180f / MathF.PI;
            float delta = ((target - _facing) % 360f + 540f) % 360f - 180f;
            _facing += delta * MathF.Min(1f, dt * 12f);
        }
        Model.Rotation = _body * Quaternion.FromEuler(new Float3(0f, _facing, 0f));
    }

    /// <summary>Launches off any jump pad it stands on, and steps through any teleporter it walks onto.</summary>
    private void UsePadsAndGates()
    {
        Float3 feet = Transform.Position;

        if (_sinceLaunched > 0.3f)
        {
            foreach (JumpPad pad in JumpPad.All)
            {
                if (!pad.Holds(feet)) continue;
                _velocity = pad.Launch;
                _sinceLaunched = 0f;
                _sinceGrounded = 1f;
                break;
            }
        }

        if (_arrivedOn != null && !_arrivedOn.Holds(feet)) _arrivedOn = null;
        foreach (Teleporter gate in Teleporter.All)
        {
            if (gate == _arrivedOn || gate.Exit == null || !gate.Holds(feet)) continue;
            _controller.Teleport(gate.Exit.Transform.Position + gate.Exit.Transform.Up * 0.05f);
            _arrivedOn = gate.Exit;
            break;
        }
    }
}

/// <summary>
/// A region with its own gravity: a fixed pull across a box, or a pull toward the middle of a sphere
/// for a planet. Where zones overlap the higher <see cref="Priority"/> wins.
/// </summary>
public sealed class GravityZone : MonoBehaviour
{
    public static readonly List<GravityZone> All = new();

    /// <summary>True pulls toward the zone's middle across a sphere of <see cref="Radius"/>, false pulls along <see cref="Direction"/> across a box of <see cref="Size"/>.</summary>
    public bool TowardCenter;
    public float Radius = 10f;
    public Float3 Size = new(10f, 10f, 10f);

    /// <summary>The way the box pulls, in the zone's own space.</summary>
    public Float3 Direction = new(0f, -1f, 0f);
    public float Strength = 24f;
    public int Priority;

    public override void OnEnable() => All.Add(this);

    public override void OnDisable() => All.Remove(this);

    public bool Contains(Float3 point)
    {
        if (TowardCenter) return Float3.Length(point - Transform.Position) <= Radius;

        Float3 local = Quaternion.Inverse(Transform.Rotation) * (point - Transform.Position);
        return MathF.Abs(local.X) <= Size.X * 0.5f && MathF.Abs(local.Y) <= Size.Y * 0.5f && MathF.Abs(local.Z) <= Size.Z * 0.5f;
    }

    public Float3 Pull(Float3 point)
    {
        if (!TowardCenter) return Transform.Rotation * Float3.Normalize(Direction) * Strength;

        Float3 toCenter = Transform.Position - point;
        float distance = Float3.Length(toCenter);
        return distance > 1e-3f ? toCenter / distance * Strength : Float3.Zero;
    }

    /// <summary>The gravity at a point: the highest priority zone holding it, or <paramref name="outside"/>.</summary>
    public static Float3 At(Float3 point, Float3 outside)
    {
        GravityZone? best = null;
        foreach (GravityZone zone in All)
            if (zone.Contains(point) && (best == null || zone.Priority > best.Priority)) best = zone;
        return best == null ? outside : best.Pull(point);
    }
}

/// <summary>A pad that throws whatever stands on it at <see cref="Launch"/>.</summary>
public sealed class JumpPad : MonoBehaviour
{
    public static readonly List<JumpPad> All = new();

    public Float3 Launch;
    public float Radius = 1.2f;

    public override void OnEnable() => All.Add(this);

    public override void OnDisable() => All.Remove(this);

    public bool Holds(Float3 feet)
    {
        Float3 local = Quaternion.Inverse(Transform.Rotation) * (feet - Transform.Position);
        return local.X * local.X + local.Z * local.Z <= Radius * Radius && local.Y > -0.2f && local.Y < 0.5f;
    }
}

/// <summary>One end of a teleporter: stepping onto it puts the walker on <see cref="Exit"/>.</summary>
public sealed class Teleporter : MonoBehaviour
{
    public static readonly List<Teleporter> All = new();

    public Teleporter? Exit;
    public float Radius = 1f;

    public override void OnEnable() => All.Add(this);

    public override void OnDisable() => All.Remove(this);

    public bool Holds(Float3 feet)
    {
        Float3 local = Quaternion.Inverse(Transform.Rotation) * (feet - Transform.Position);
        return local.X * local.X + local.Z * local.Z <= Radius * Radius && local.Y > -0.3f && local.Y < 0.6f;
    }
}
