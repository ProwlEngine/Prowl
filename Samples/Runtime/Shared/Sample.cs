// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Shared scaffolding for the runtime samples.
//
// StationGame lays a sample out as a row of stations. Number keys or the bar along the bottom
// jump the camera to a station, and the fly camera roams between them. SampleHud draws the
// title, description, a live stat line and the key help with Paper, and samples add their own
// controls to the panel on the right through Sample.Button, Sample.Toggle and Sample.Slider.
//

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;

using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;

using Gradient = Prowl.Vector.Gradient;
using MouseButton = Prowl.Runtime.MouseButton;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Samples;

public abstract class StationGame : Game
{
    public sealed record Station(string Name, string Description, Float3 Center, Float3 View, float LookHeight);

    protected Scene SampleScene = null!;
    protected GameObject CameraObject = null!;
    protected Camera MainCamera = null!;
    protected SampleHud Hud = null!;
    public readonly List<Station> Stations = new();
    public int CurrentStation { get; private set; }

    /// <summary>Key help for moving around, shown after the station keys.</summary>
    protected virtual string MoveKeys => "WASD Q E  fly    Right Mouse  look    Shift  faster";

    /// <summary>Key help for the sample's own controls, shown after the shared ones.</summary>
    protected virtual string ExtraKeys => string.Empty;

    // Capture mode renders every station with time locked to 60 frames a second, saves an image of each and then
    // times a run of frames there, for reference images and performance numbers that repeat run to run
    private const int PerformanceFrames = 300;
    private string? _captureFolder;
    private int _captureFrames = 120;
    private int _captureCounter;
    private bool _captureNow;
    private readonly Stopwatch _frameClock = new();
    private readonly List<double> _frameTimes = [];
    private RenderStats.Frame _captureStats;
    private double _colorPassMs, _shadowPassMs, _postFxMs;
    private StreamWriter? _performance;

    private static readonly KeyCode[] StationKeys =
    [
        KeyCode.Number1, KeyCode.Number2, KeyCode.Number3, KeyCode.Number4, KeyCode.Number5,
        KeyCode.Number6, KeyCode.Number7, KeyCode.Number8, KeyCode.Number9, KeyCode.Number0,
    ];

    public override void Initialize()
    {
        ReadCaptureArguments();

        // The Assets folder beside the exe holds the sample's textures, materials and models as plain files,
        // imported the first time something asks for them by path.
        AssetDatabase.Mount(new SourceAssetBackend(new FolderAssetSource(Path.Combine(AppContext.BaseDirectory, "Assets"))));

        SampleScene = new Scene();

        CameraObject = new GameObject("Main Camera") { Tag = "Main Camera" };
        MainCamera = CameraObject.AddComponent<Camera>();
        MainCamera.HDR = true;
        MainCamera.FarClipPlane = 500f;
        MainCamera.Effects =
        [
            new BloomEffect { Intensity = 0.4f, Threshold = 1.0f },
            new TonemapperEffect(),
            new FXAAEffect(),
        ];
        CameraObject.AddComponent<FlyCamera>();
        CameraObject.AddComponent<PhysicsGrabber>().Game = this;
        SampleScene.Add(CameraObject);

        var hud = new GameObject("HUD");
        Hud = hud.AddComponent<SampleHud>();
        Hud.Game = this;
        SampleScene.Add(hud);
        if (_captureFolder != null) Hud.Visible = false;

        Build();
        if (_captureFolder != null) FixParticleSeeds();

        GoToStation(0);
        Scene.Load(SampleScene);
    }

    /// <summary>Creates the sample's content. Called once, before the scene loads.</summary>
    protected abstract void Build();

    /// <summary>Called every frame before the scene updates.</summary>
    protected virtual void Tick() { }

    /// <summary>Called when the camera jumps to a station.</summary>
    protected virtual void OnStationChanged(int index) { }

    /// <summary>Draws the sample's own controls into the panel on the right. Leave empty for no panel.</summary>
    public virtual void DrawControls(Paper paper, FontFile font) { }

    /// <summary>Draws controls that apply to the whole sample into a panel under the station's own. Leave empty for no panel.</summary>
    public virtual void DrawSceneControls(Paper paper, FontFile font) { }

    public bool HasControls => _hasControls ??= Overrides(nameof(DrawControls));

    /// <summary>Whether the current station has controls to show. Override to hide the panel for stations without any.</summary>
    public virtual bool HasStationControls => HasControls;
    public bool HasSceneControls => _hasSceneControls ??= Overrides(nameof(DrawSceneControls));
    private bool? _hasControls, _hasSceneControls;

    private bool Overrides(string method) => GetType().GetMethod(method)!.DeclaringType != typeof(StationGame);

    /// <summary>The live stat line under the description.</summary>
    public virtual string Stats => string.Empty;

    /// <summary>Shows only the stats, the frame rate and the sample's controls, for a sample played in a headset.</summary>
    public virtual bool CompactHud => false;

    public string KeyHelp
    {
        get
        {
            string range = Stations.Count >= 10 ? "1 to 9, 0" : $"1 to {Stations.Count}";
            string keys = Stations.Count > 1 ? $"{range}  stations    {MoveKeys}" : MoveKeys;
            return string.IsNullOrEmpty(ExtraKeys) ? keys : keys + "    " + ExtraKeys;
        }
    }

    /// <summary>Registers a station. <paramref name="view"/> is where the camera sits relative to its center.</summary>
    protected void AddStation(string name, string description, Float3 center, Float3? view = null, float lookHeight = 1.5f)
        => Stations.Add(new Station(name, description, center, view ?? new Float3(0f, 5f, -12f), lookHeight));

    /// <summary>Adds a root object to the sample scene and returns it.</summary>
    protected GameObject Add(GameObject go)
    {
        SampleScene.Add(go);
        return go;
    }

    public void GoToStation(int index)
    {
        if (Stations.Count == 0) return;
        CurrentStation = Math.Clamp(index, 0, Stations.Count - 1);
        Station station = Stations[CurrentStation];
        CameraObject.Transform.Position = station.Center + station.View;
        CameraObject.Transform.LookAt(station.Center + new Float3(0f, station.LookHeight, 0f));
        OnStationChanged(CurrentStation);
    }

    public override void BeginUpdate()
    {
        for (int i = 0; i < StationKeys.Length && Stations.Count > 1 && i < Stations.Count; i++)
            if (Input.GetKeyDown(StationKeys[i]))
                GoToStation(i);

        Tick();
        AdvanceCapture();
    }

    // Every particle system draws from its own seed, so captures repeat exactly
    private void FixParticleSeeds()
    {
        uint seed = 1;
        foreach (GameObject go in SampleScene.AllObjects)
            foreach (var particles in go.GetComponents<Prowl.Runtime.ParticleSystem.ParticleSystemComponent>())
            {
                particles.AutoRandomSeed = false;
                particles.RandomSeed = seed++;
            }
    }

    public override void BeginRender() => RenderStats.BeginFrame();
    public override void EndRender() => RenderStats.EndFrame();

    public override void AfterGui(Scene? scene)
    {
        if (!_captureNow) return;
        _captureNow = false;

        Station station = Stations[CurrentStation];
        string file = Path.Combine(_captureFolder!, $"{CurrentStation + 1:00} {station.Name}.png");
        SaveScreenshot(file);
        _captureStats = RenderStats.Last;
    }

    private void ReadCaptureArguments()
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "--capture");
        if (at < 0 || at + 1 >= args.Length) return;

        _captureFolder = Path.GetFullPath(args[at + 1]);
        Directory.CreateDirectory(_captureFolder);
        if (at + 2 < args.Length && int.TryParse(args[at + 2], out int frames))
            _captureFrames = frames;

        Time.LockedDeltaTime = 1f / 60f;
        Application.VSync = false;
        Application.TargetFrameRate = 0;

        _performance = new StreamWriter(Path.Combine(_captureFolder, "performance.csv"));
        _performance.WriteLine("station,frames,mean ms,median ms,p95 ms,max ms,fps,draw calls,instanced draw calls,batches,triangles," +
            "renderables drawn,shadow draw calls,shadow passes,shadow triangles,lights,image effects,color pass cpu ms,shadow pass cpu ms,post cpu ms");
    }

    // Each station settles for the capture frame count, is saved, then has a run of frames timed by the wall clock
    private void AdvanceCapture()
    {
        if (_captureFolder == null || Stations.Count == 0) return;

        double elapsed = _frameClock.Elapsed.TotalMilliseconds;
        _frameClock.Restart();

        _captureCounter++;
        if (_captureCounter == _captureFrames)
        {
            _captureNow = true;
            _frameTimes.Clear();
            _colorPassMs = _shadowPassMs = _postFxMs = 0;
            return;
        }
        if (_captureCounter <= _captureFrames + 1) return;

        _frameTimes.Add(elapsed);
        RenderStats.Frame frame = RenderStats.Last;
        _colorPassMs += frame.ColorPassMs;
        _shadowPassMs += frame.ShadowPassMs;
        _postFxMs += frame.PostFxMs;
        if (_frameTimes.Count < PerformanceFrames) return;

        WritePerformance();
        _captureCounter = 0;
        if (CurrentStation + 1 < Stations.Count)
            GoToStation(CurrentStation + 1);
        else
        {
            _performance!.Dispose();
            Quit();
        }
    }

    private void WritePerformance()
    {
        double[] sorted = [.. _frameTimes];
        Array.Sort(sorted);
        double mean = sorted.Average();
        RenderStats.Frame s = _captureStats;
        string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        _performance!.WriteLine(string.Join(",",
            $"\"{CurrentStation + 1:00} {Stations[CurrentStation].Name}\"", sorted.Length, Number(mean), Number(sorted[sorted.Length / 2]),
            Number(sorted[(int)(sorted.Length * 0.95)]), Number(sorted[^1]), Number(1000.0 / mean),
            s.DrawCalls, s.InstancedDrawCalls, s.Batches, s.Triangles, s.RenderablesDrawn,
            s.ShadowDrawCalls, s.ShadowPasses, s.ShadowTriangles, s.Lights, s.ImageEffects,
            Number(_colorPassMs / sorted.Length), Number(_shadowPassMs / sorted.Length), Number(_postFxMs / sorted.Length)));
        _performance.Flush();
    }

    /// <summary>Writes the window's current contents to a PNG file.</summary>
    public static void SaveScreenshot(string file)
    {
        Texture2D shot = Graphics.Screenshot();
        int width = (int)shot.Width, height = (int)shot.Height;
        var pixels = new Color32[width * height];
        shot.GetData(new Memory<Color32>(pixels));
        shot.Dispose();

        // PNG rows are top first and each starts with a filter byte, the texture is bottom first.
        var raw = new byte[height * (width * 3 + 1)];
        for (int y = 0; y < height; y++)
        {
            int row = y * (width * 3 + 1);
            int source = (height - 1 - y) * width;
            for (int x = 0; x < width; x++)
            {
                Color32 c = pixels[source + x];
                raw[row + 1 + x * 3] = c.R;
                raw[row + 2 + x * 3] = c.G;
                raw[row + 3 + x * 3] = c.B;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            zlib.Write(raw);

        using var stream = File.Create(file);
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(stream, "IHDR", header);
        WriteChunk(stream, "IDAT", compressed.ToArray());
        WriteChunk(stream, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var buffer = new byte[4];
        WriteBigEndian(buffer, 0, (uint)data.Length);
        stream.Write(buffer);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        WriteBigEndian(buffer, 0, crc);
        stream.Write(buffer);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}

/// <summary>WASD and Q E to fly, hold Right Mouse to look, Shift to go faster. Gamepad sticks work too.</summary>
public sealed class FlyCamera : Component
{
    public float Speed = 7f;
    public float FastSpeed = 20f;

    private InputActionMap _map = null!;
    private InputAction _move = null!;
    private InputAction _look = null!;
    private InputAction _lookEnable = null!;
    private InputAction _up = null!;
    private InputAction _down = null!;
    private InputAction _sprint = null!;

    public override void OnEnable()
    {
        _map = new InputActionMap("Fly Camera");

        _move = _map.AddAction("Move", InputActionType.Value);
        _move.ExpectedValueType = typeof(Float2);
        _move.AddBinding(new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.W),
            InputBinding.CreateKeyBinding(KeyCode.S),
            InputBinding.CreateKeyBinding(KeyCode.A),
            InputBinding.CreateKeyBinding(KeyCode.D),
            true));
        var leftStick = InputBinding.CreateGamepadAxisBinding(0);
        leftStick.Processors.Add(new DeadzoneProcessor(0.15f));
        _move.AddBinding(leftStick);

        _lookEnable = _map.AddAction("Look Enable", InputActionType.Button);
        _lookEnable.AddBinding(MouseButton.Right);

        _look = _map.AddAction("Look", InputActionType.Value);
        _look.ExpectedValueType = typeof(Float2);
        var mouse = new DualAxisCompositeBinding(
            InputBinding.CreateMouseAxisBinding(0),
            InputBinding.CreateMouseAxisBinding(1));
        mouse.Processors.Add(new ScaleProcessor(0.25f));
        _look.AddBinding(mouse);

        _up = _map.AddAction("Up", InputActionType.Button);
        _up.AddBinding(KeyCode.E);
        _up.AddBinding(GamepadButton.A);
        _down = _map.AddAction("Down", InputActionType.Button);
        _down.AddBinding(KeyCode.Q);
        _down.AddBinding(GamepadButton.B);

        _sprint = _map.AddAction("Sprint", InputActionType.Button);
        _sprint.AddBinding(KeyCode.ShiftLeft);
        _sprint.AddBinding(GamepadButton.LeftStick);

        Input.RegisterActionMap(_map);
        _map.Enable();
    }

    public override void OnDisable()
    {
        _map.Disable();
        Input.UnregisterActionMap(_map);
        if (Input.CursorLocked)
            Input.UnlockCursor();
    }

    public override void Update()
    {
        // The cursor is hidden and held in place while looking around.
        if (_lookEnable.WasPressedThisFrame())
            Input.LockCursor();
        else if (_lookEnable.WasReleasedThisFrame())
            Input.UnlockCursor();

        Float2 move = _move.ReadValue<Float2>();
        float speed = (_sprint.IsPressed() ? FastSpeed : Speed) * Time.UnscaledDeltaTime;
        float upDown = (_up.IsPressed() ? 1f : 0f) - (_down.IsPressed() ? 1f : 0f);
        Transform.Position += Transform.Forward * move.Y * speed + Transform.Right * move.X * speed + Float3.UnitY * upDown * speed;

        Float2 stick = Input.GetGamepadRightStick();
        if (Maths.Abs(stick.X) < 0.15f) stick.X = 0f;
        if (Maths.Abs(stick.Y) < 0.15f) stick.Y = 0f;

        Float2 look = _lookEnable.IsPressed() ? _look.ReadValue<Float2>() : Float2.Zero;
        look += stick * 120f * Time.UnscaledDeltaTime;
        if (look.X != 0f || look.Y != 0f)
        {
            // Read the angles back from Forward, since Euler angles come back wrapped into 0 to 360.
            Float3 forward = Transform.Forward;
            float pitch = -MathF.Asin(Maths.Clamp(forward.Y, -1f, 1f)) * Maths.Rad2Deg;
            float yaw = MathF.Atan2(forward.X, forward.Z) * Maths.Rad2Deg;
            pitch = Maths.Clamp(pitch + look.Y, -89f, 89f);
            Transform.LocalEulerAngles = new Float3(pitch, yaw + look.X, 0f);
        }
    }
}

/// <summary>
/// Follows a target. Holding Right Mouse orbits it, the wheel zooms, and when following a vehicle the
/// camera swings back behind it on its own.
/// </summary>
public sealed class ChaseCamera : Component
{
    public Transform? Target;
    public bool FollowHeading;
    public float Distance = 8f;
    public float Yaw;
    public float Pitch = 18f;

    /// <summary>Which way is up for the target. The orbit turns smoothly to follow it, for a target walking on walls or round a planet.</summary>
    public Float3 Up = Float3.UnitY;

    private float _sinceLook = 10f;
    private Quaternion _frame = Quaternion.Identity;

    /// <summary>The way the camera looks across the ground, in world space, which is what forward means to the player.</summary>
    public Float3 Heading
    {
        get
        {
            float yaw = Yaw * MathF.PI / 180f;
            return _frame * new Float3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
        }
    }

    public override void LateUpdate()
    {
        if (Target == null) return;
        float dt = Time.DeltaTime;

        if (Input.GetMouseButton(1))
        {
            Float2 delta = Input.MouseDelta;
            Yaw += delta.X * 0.25f;
            Pitch = Maths.Clamp(Pitch + delta.Y * 0.25f, -10f, 75f);
            _sinceLook = 0f;
        }
        else _sinceLook += dt;

        Distance = Maths.Clamp(Distance - Input.MouseWheelDelta * 0.8f, 3f, 25f);

        // Carry the orbit's frame round to the target's up a little each frame, so a change of gravity swings the view rather than snapping it.
        Quaternion toUp = Quaternion.FromToRotation(_frame * Float3.UnitY, Up);
        _frame = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, toUp, MathF.Min(1f, dt * 6f)) * _frame);

        if (FollowHeading && _sinceLook > 1.5f)
        {
            Float3 forward = Quaternion.Inverse(_frame) * Target.Forward;
            if (forward.X * forward.X + forward.Z * forward.Z > 1e-4f)
            {
                float heading = MathF.Atan2(forward.X, forward.Z) * 180f / MathF.PI;
                float delta = ((heading - Yaw) % 360f + 540f) % 360f - 180f;
                Yaw += delta * MathF.Min(1f, dt * 3f);
            }
        }

        float yaw = Yaw * MathF.PI / 180f, pitch = Pitch * MathF.PI / 180f;
        Float3 back = _frame * new Float3(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
        Float3 frameUp = _frame * Float3.UnitY;
        Float3 focus = Target.Position + frameUp * 1.3f;
        Float3 goal = focus + back * Distance;

        Transform.Position += (goal - Transform.Position) * MathF.Min(1f, dt * 10f);
        Transform.LookAt(focus, frameUp);
    }
}

/// <summary>Collects quads for a procedural mesh, winding each one to face the given direction.</summary>
public sealed class MeshBuilder
{
    private readonly List<Float3> _vertices = new();
    private readonly List<Float2> _uvs = new();
    private readonly List<uint> _indices = new();

    public void Quad(Float3 a, Float3 b, Float3 c, Float3 d, Float2 ua, Float2 ub, Float2 uc, Float2 ud, Float3 facing)
    {
        uint i = (uint)_vertices.Count;
        _vertices.AddRange([a, b, c, d]);
        _uvs.AddRange([ua, ub, uc, ud]);

        bool flip = Float3.Dot(Float3.Cross(c - a, d - b), facing) < 0f;
        if (flip) _indices.AddRange([i, i + 2, i + 1, i, i + 3, i + 2]);
        else _indices.AddRange([i, i + 1, i + 2, i, i + 2, i + 3]);
    }

    public Mesh Build()
    {
        var mesh = new Mesh { Vertices = _vertices.ToArray(), UV = _uvs.ToArray(), Indices = _indices.ToArray() };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }
}

/// <summary>
/// Left click and drag any dynamic body. Each step the grabbed point is given a velocity toward the
/// mouse, so the body still collides and spins about where it is held. The mouse wheel pulls it closer
/// or pushes it away.
/// </summary>
public sealed class PhysicsGrabber : Component
{
    public Game Game = null!;

    /// <summary>How much of the gap to the mouse is closed per second.</summary>
    public float Stiffness = 12f;

    /// <summary>The hardest the grab may accelerate a body, so heavy bodies feel heavy.</summary>
    public float MaxAcceleration = 60f;

    private Rigidbody3D? _held;
    private Float3 _localAnchor;
    private float _distance;
    private Float3 _target;
    private LineRenderer _line = null!;

    public bool IsHolding => _held.IsValid();

    private void CreateLine()
    {
        _line = AddComponent<LineRenderer>();
        _line.Material = new Material(Shader.LoadDefault(DefaultShader.Line));
        _line.StartWidth = 0.03f;
        _line.EndWidth = 0.03f;
        _line.StartColor = SampleHud.Accent;
        _line.EndColor = new Color(1f, 1f, 1f, 0.8f);
    }

    public override void Update()
    {
        if (_line == null) CreateLine();
        Camera camera = GetComponent<Camera>()!;
        var size = Window.InternalWindow.Size;
        Ray ray = camera.ScreenPointToRay(new Float2(Input.MousePosition.X, Input.MousePosition.Y), new Float2(size.X, size.Y));

        bool overUi = Game.PaperInstance != null && Game.PaperInstance.WantsCapturePointer;
        if (Input.GetMouseButtonDown(0) && !overUi)
            TryGrab(ray);
        else if (!Input.GetMouseButton(0))
            _held = null;

        _line.Points.Clear();
        if (!IsHolding) return;

        _distance = Maths.Clamp(_distance + Input.MouseWheelDelta * 0.5f, 1f, 80f);
        _target = ray.Origin + ray.Direction * _distance;
        _line.Points.Add(WorldAnchor());
        _line.Points.Add(_target);
    }

    private void TryGrab(Ray ray)
    {
        PhysicsWorld physics = GameObject.Scene.Physics;
        if (!physics.Raycast(ray.Origin, ray.Direction, out RaycastHit hit, 200f, QueryFilter.Default)) return;

        Rigidbody3D body = hit.Rigidbody;
        if (body.IsNotValid() || body.MotionType != Jitter2.Dynamics.MotionType.Dynamic) return;

        _held = body;
        _localAnchor = Quaternion.Inverse(body.Rotation) * (hit.Point - body.Position);
        _distance = hit.Distance;
        _target = hit.Point;
    }

    private Float3 WorldAnchor() => _held!.Transform.Position + _held.Transform.Rotation * _localAnchor;

    public override void FixedUpdate()
    {
        if (!IsHolding) return;

        Rigidbody3D body = _held!;
        float dt = Time.FixedDeltaTime;
        Float3 anchor = body.Position + body.Rotation * _localAnchor;

        // The velocity the grabbed point should have to close the gap, minus what it already has. Gravity
        // is added back since the step will take it away again.
        Float3 wanted = (_target - anchor) * Stiffness;
        Float3 change = wanted - body.GetPointVelocity(anchor);
        if (body.AffectedByGravity) change -= GameObject.Scene.Physics.Gravity * dt;

        float limit = MaxAcceleration * dt;
        float length = Float3.Length(change);
        if (length > limit) change *= limit / length;

        // Half the change per step: pushing off centre also spins the body, which moves the point more
        // than its mass alone would say, and asking for all of it at once overshoots.
        body.ApplyImpulse(change * (body.Mass * 0.5f), anchor);
        body.AngularVelocity *= 1f - MathF.Min(1f, 3f * dt);
    }
}

/// <summary>Draws the station title, description, stats, key help, the station bar and the sample's controls.</summary>
public sealed class SampleHud : Component
{
    public StationGame Game = null!;
    public bool Visible = true;

    public static readonly Color Panel = new(0.02f, 0.03f, 0.06f, 0.72f);
    public static readonly Color Bright = new(0.95f, 0.96f, 1f, 1f);
    public static readonly Color Dim = new(0.62f, 0.68f, 0.8f, 1f);
    public static readonly Color Accent = new(1f, 0.72f, 0.35f, 1f);
    public static readonly Color Control = new(1f, 1f, 1f, 0.08f);
    public static readonly Color ControlHover = new(1f, 1f, 1f, 0.16f);

    // Frame times in milliseconds, oldest first from _frameIndex.
    private readonly float[] _frameTimes = new float[240];
    private int _frameIndex;
    private float _graphScale = 20f;

    public override void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
            Visible = !Visible;

        _frameTimes[_frameIndex] = Time.UnscaledDeltaTime * 1000f;
        _frameIndex = (_frameIndex + 1) % _frameTimes.Length;
    }

    public override void OnGui(Paper paper)
    {
        FontFile? font = FontAsset.LoadDefault().FontFile;
        if (font == null || !Visible || Game.Stations.Count == 0) return;

        int index = Game.CurrentStation;
        StationGame.Station station = Game.Stations[index];

        using (paper.Column("left")
            .PositionType(PositionType.SelfDirected)
            .AnchorLeft(20).AnchorTop(20).Width(560).Height(UnitValue.Auto)
            .Gap(10)
            .Enter())
        {
            Info(paper, font, station, index);

            if (Game.HasSceneControls)
                using (paper.Column("scene controls").Width(300).Height(UnitValue.Auto).BackgroundColor(Panel).Rounded(10).Padding(14).Gap(6).Enter())
                    Game.DrawSceneControls(paper, font);
        }

        using (paper.Column("right")
            .PositionType(PositionType.SelfDirected)
            .AnchorRight(20).AnchorTop(20).Width(300).Height(UnitValue.Auto)
            .Gap(10)
            .Enter())
        {
            Performance(paper, font);

            if (Game.HasStationControls)
                using (paper.Column("controls").Height(UnitValue.Auto).BackgroundColor(Panel).Rounded(10).Padding(14).Gap(6).Enter())
                    Game.DrawControls(paper, font);
        }

        // A sample that is one world to walk around has nowhere else to go.
        if (Game.Stations.Count > 1) StationBar(paper, font, index);
    }

    /// <summary>The station's title, description, live stats and the key help.</summary>
    private void Info(Paper paper, FontFile font, StationGame.Station station, int index)
    {
        using (paper.Column("info").Height(UnitValue.Auto)
            .BackgroundColor(Panel).Rounded(10)
            .Padding(16, 16, 12, 14).Gap(6)
            .Enter())
        {
            if (!Game.CompactHud)
            {
                paper.Box("title").Height(30)
                    .Text(Game.Stations.Count > 1 ? $"{(index + 1) % 10}  {station.Name}" : station.Name, font).FontSize(24).TextColor(Accent)
                    .Alignment(TextAlignment.MiddleLeft);

                paper.Box("description").Height(UnitValue.Auto)
                    .Text(station.Description, font).FontSize(16).TextColor(Bright)
                    .Wrap(TextWrapMode.Wrap)
                    .Alignment(TextAlignment.Left);
            }

            string stats = Game.Stats;
            if (!string.IsNullOrEmpty(stats))
                paper.Box("stats").Height(UnitValue.Auto)
                    .Text(stats, font).FontSize(14).TextColor(Dim)
                    .Wrap(TextWrapMode.Wrap)
                    .Alignment(TextAlignment.Left);

            if (!Game.CompactHud)
                paper.Box("keys").Height(UnitValue.Auto)
                    .Text(Game.KeyHelp + "    F1  hide", font).FontSize(14).TextColor(Dim)
                    .Wrap(TextWrapMode.Wrap)
                    .Alignment(TextAlignment.Left);
        }
    }

    /// <summary>One button per station along the bottom edge.</summary>
    private void StationBar(Paper paper, FontFile font, int index)
    {
        using (paper.Row("stations")
            .PositionType(PositionType.SelfDirected)
            .AnchorLeft(20).AnchorRight(20).AnchorBottom(20).Height(34)
            .Gap(6)
            .Enter())
        {
            for (int i = 0; i < Game.Stations.Count; i++)
            {
                int target = i;
                bool current = i == index;
                paper.Box("station", i)
                    .Width(UnitValue.Stretch()).Height(34)
                    .BackgroundColor(current ? Accent : Panel).Rounded(8)
                    .Hovered.BackgroundColor(current ? Accent : ControlHover).End()
                    .Text($"{(i + 1) % 10}  {Game.Stations[i].Name}", font).FontSize(14)
                    .TextColor(current ? new Color(0.05f, 0.05f, 0.08f, 1f) : Bright)
                    .Alignment(TextAlignment.MiddleCenter)
                    .TextTruncate()
                    .Cursor(PaperCursor.Pointer)
                    .OnClick(_ => Game.GoToStation(target));
            }
        }
    }

    /// <summary>The frame rate readout and its graph, drawn straight onto Paper's Quill canvas.</summary>
    private void Performance(Paper paper, FontFile font)
    {
        float sum = 0f, best = float.MaxValue, worst = 0f;
        foreach (float ms in _frameTimes)
        {
            sum += ms;
            if (ms > 0f) best = MathF.Min(best, ms);
            worst = MathF.Max(worst, ms);
        }
        float average = sum / _frameTimes.Length;
        float fps = average > 0f ? 1000f / average : 0f;

        using (paper.Column("performance").Height(UnitValue.Auto).BackgroundColor(Panel).Rounded(10).Padding(14).Gap(6).Enter())
        {
            using (paper.Row("readout").Height(30).Enter())
            {
                paper.Box("fps").Width(UnitValue.Stretch())
                    .Text($"{fps:0} FPS", font).FontSize(24).TextColor(Accent)
                    .Alignment(TextAlignment.MiddleLeft);
                paper.Box("frame time").Width(UnitValue.Stretch())
                    .Text($"{average:0.00} ms", font).FontSize(14).TextColor(Dim)
                    .Alignment(TextAlignment.MiddleRight);
            }

            if (Game.CompactHud) return;

            using (paper.Box("graph").Height(96).Enter())
                paper.Draw((canvas, rect) => DrawGraph(canvas, rect, font, worst));

            paper.Box("range").Height(18)
                .Text($"best {best:0.0} ms    worst {worst:0.0} ms", font).FontSize(13).TextColor(Dim)
                .Alignment(TextAlignment.MiddleLeft);
        }
    }

    private void DrawGraph(Prowl.Quill.Canvas canvas, Rect rect, FontFile font, float worst)
    {
        float x = rect.Min.X, y = rect.Min.Y, w = rect.Size.X, h = rect.Size.Y;
        int count = _frameTimes.Length;

        // Ease the vertical scale toward the worst recent frame so spikes don't make it jump.
        _graphScale += (MathF.Max(20f, worst * 1.2f) - _graphScale) * 0.05f;
        float Y(float ms) => y + h - 4f - MathF.Min(ms / _graphScale, 1f) * (h - 8f);
        float X(int i) => x + i / (float)(count - 1) * w;
        float Sample(int i) => _frameTimes[(_frameIndex + i) % count];

        canvas.RoundedRectFilled(x, y, w, h, 6f, new Color(0f, 0f, 0f, 0.35f));

        // Reference lines for 120, 60 and 30 frames per second, where they fit.
        foreach ((float ms, string label) in new[] { (1000f / 120f, "120"), (1000f / 60f, "60"), (1000f / 30f, "30") })
        {
            if (ms > _graphScale) continue;
            float ly = Y(ms);
            canvas.BeginPath();
            canvas.MoveTo(x + 4f, ly);
            canvas.LineTo(x + w - 4f, ly);
            canvas.SetStrokeColor(new Color(1f, 1f, 1f, 0.12f));
            canvas.SetStrokeWidth(1f);
            canvas.Stroke();
            canvas.DrawText(label, x + w - 6f, ly - 2f, new Color(1f, 1f, 1f, 0.35f), 10f, font, origin: new Float2(1f, 1f));
        }

        // The area under the curve, fading out toward the bottom.
        canvas.BeginPath();
        canvas.MoveTo(x, y + h);
        for (int i = 0; i < count; i++)
            canvas.LineTo(X(i), Y(Sample(i)));
        canvas.LineTo(x + w, y + h);
        canvas.ClosePath();
        canvas.SetFillColor(Color.White);
        canvas.SetLinearBrush(x, y, x, y + h, new Color(Accent.R, Accent.G, Accent.B, 0.45f), new Color(Accent.R, Accent.G, Accent.B, 0f));
        canvas.FillComplex();
        canvas.ClearBrush();

        // The curve itself.
        canvas.BeginPath();
        canvas.MoveTo(X(0), Y(Sample(0)));
        for (int i = 1; i < count; i++)
            canvas.LineTo(X(i), Y(Sample(i)));
        canvas.SetStrokeColor(Accent);
        canvas.SetStrokeWidth(1.5f);
        canvas.SetStrokeJoint(Prowl.Quill.JointStyle.Round);
        canvas.Stroke();

        // Frames slower than 30 per second get a red marker.
        for (int i = 0; i < count; i++)
            if (Sample(i) > 1000f / 30f)
                canvas.CircleFilled(X(i), Y(Sample(i)), 2.5f, new Color(1f, 0.3f, 0.25f, 1f));

        // A glowing dot on the newest frame.
        float headX = X(count - 1), headY = Y(Sample(count - 1));
        canvas.CircleFilled(headX, headY, 7f, new Color(Accent.R, Accent.G, Accent.B, 0.2f));
        canvas.CircleFilled(headX, headY, 3.5f, Accent);
    }
}

/// <summary>Small helpers every sample uses: materials, meshes, curves, procedural textures and Paper widgets.</summary>
public static class Sample
{
    /// <summary>Randomness for sample content, seeded so every run builds the same scene.</summary>
    public static readonly Random Rng = new(1);

    /// <summary>An asset from the Assets folder by its path without the extension, such as "Textures/Asphalt".</summary>
    public static T Load<T>(string path) where T : Asset
    {
        T? asset = AssetDatabase.FindResource<T>(path);
        if (asset.IsNotValid()) throw new FileNotFoundException($"The sample has no {typeof(T).Name} at 'Assets/{path}'.");
        return asset!;
    }

    // ----------------------------------------------------------------
    //  Objects and materials
    // ----------------------------------------------------------------

    /// <summary>
    /// A Standard material. Roughness and metallic come from the surface texture's green and blue
    /// channels times these factors, so a white surface texture makes the factors the final values.
    /// </summary>
    public static Material Lit(Color color, float metallic = 0f, float roughness = 0.6f, DefaultShader shader = DefaultShader.Standard)
    {
        var material = new Material(Shader.LoadDefault(shader));
        material.SetColor("_MainColor", color);
        material.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.White));
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Roughness", roughness);
        return material;
    }

    /// <summary>Makes a Standard material glow. Intensity above 1 feeds bloom.</summary>
    public static Material Emissive(this Material material, Color color, float intensity)
    {
        material.SetTexture("_EmissionTex", Texture2D.LoadDefault(DefaultTexture.White));
        material.SetColor("_EmissiveColor", color);
        material.SetFloat("_EmissionIntensity", intensity);
        return material;
    }

    public static Material Unlit(Color color)
    {
        var material = new Material(Shader.LoadDefault(DefaultShader.Unlit));
        material.SetColor("_MainColor", color);
        return material;
    }

    /// <summary>The runtime grid texture on a floor of this size, tiled so each square is a metre across.</summary>
    public static Material Floor(float width, float depth)
        => Lit(new Color(0.42f, 0.44f, 0.48f, 1f), 0f, 0.9f).With("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid)).Tiled(width * 0.5f, depth * 0.5f);

    /// <summary>Sets a texture and returns the material, to build one in a single expression.</summary>
    public static Material With(this Material material, string property, Texture2D texture)
    {
        material.SetTexture(property, texture);
        return material;
    }

    public static Material Tiled(this Material material, float x, float y)
    {
        material.SetVector("_Tiling", new Float2(x, y));
        return material;
    }

    /// <summary>A GameObject with a MeshRenderer.</summary>
    public static GameObject Model(string name, Mesh mesh, Material material, Float3 position, Float3? euler = null, Float3? scale = null)
    {
        var go = new GameObject(name);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = mesh;
        renderer.Material = material;
        go.Transform.Position = position;
        if (euler.HasValue) go.Transform.LocalEulerAngles = euler.Value;
        if (scale.HasValue) go.Transform.LocalScale = scale.Value;
        return go;
    }

    /// <summary>A flat rectangle in the XZ plane facing up, one sided, with UVs across it.</summary>
    public static Mesh Plane(float width, float depth)
    {
        float x = width * 0.5f, z = depth * 0.5f;
        var mesh = new Mesh();
        mesh.Vertices = [new(-x, 0f, -z), new(x, 0f, -z), new(x, 0f, z), new(-x, 0f, z)];
        mesh.Normals = [Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY];
        mesh.UV = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];
        mesh.Indices = [0, 2, 1, 0, 3, 2];
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    /// <summary>A box with a matching static collider, for floors and walls.</summary>
    public static GameObject Block(string name, Float3 size, Material material, Float3 position, Float3? euler = null)
    {
        GameObject go = Model(name, Mesh.CreateCube(size), material, position, euler);
        go.AddComponent<BoxCollider>().Size = size;
        return go;
    }

    // ----------------------------------------------------------------
    //  Curves and gradients
    // ----------------------------------------------------------------

    public static AnimationCurve Curve(params (float time, float value)[] keys)
        => new(keys.Select(k => new Keyframe(k.time, k.value)).ToArray());

    public static Gradient Grad((float time, Color color)[] colors, (float time, float alpha)[] alphas)
        => new(colors.Select(k => new GradientColorKey(k.time, k.color)), alphas.Select(k => new GradientAlphaKey(k.time, k.alpha)));

    // ----------------------------------------------------------------
    //  Noise
    // ----------------------------------------------------------------

    /// <summary>Tileable value noise in 0 to 1.</summary>
    public static float Noise(float x, float y, int period)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(x0, y0, period), b = Hash(x0 + 1, y0, period);
        float c = Hash(x0, y0 + 1, period), d = Hash(x0 + 1, y0 + 1, period);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>Several octaves of <see cref="Noise"/>, still tileable.</summary>
    public static float Fractal(float u, float v, int baseCells, int octaves)
    {
        float sum = 0f, amplitude = 0.5f, total = 0f;
        int cells = baseCells;
        for (int i = 0; i < octaves; i++)
        {
            sum += Noise(u * cells, v * cells, cells) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            cells *= 2;
        }
        return sum / total;
    }

    private static float Hash(int x, int y, int period)
    {
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    public static Color Lerp(Color a, Color b, float t)
        => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    public static Color Hsv(float hue, float saturation, float value, float alpha = 1f)
    {
        float h = (hue % 1f + 1f) % 1f * 6f;
        float c = value * saturation;
        float x = c * (1f - MathF.Abs(h % 2f - 1f));
        float m = value - c;
        (float r, float g, float b) = (int)h switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return new Color(r + m, g + m, b + m, alpha);
    }

    public static float Saturate(float x) => Math.Clamp(x, 0f, 1f);
    public static float Distance(float x0, float y0, float x1, float y1) => MathF.Sqrt((x0 - x1) * (x0 - x1) + (y0 - y1) * (y0 - y1));

    // ----------------------------------------------------------------
    //  Paper widgets for the controls panel
    // ----------------------------------------------------------------

    public static void Header(Paper paper, FontFile font, string text, int id = 0)
    {
        paper.Box("header " + text, id).Height(24)
            .Text(text, font).FontSize(16).TextColor(SampleHud.Accent)
            .Alignment(TextAlignment.MiddleLeft);
    }

    public static void Label(Paper paper, FontFile font, string text, int id = 0)
    {
        paper.Box("label " + text, id).Height(UnitValue.Auto)
            .Text(text, font).FontSize(14).TextColor(SampleHud.Dim)
            .Wrap(TextWrapMode.Wrap)
            .Alignment(TextAlignment.Left);
    }

    public static void Button(Paper paper, FontFile font, string text, Action onClick, int id = 0)
    {
        paper.Box("button " + text, id).Height(28)
            .BackgroundColor(SampleHud.Control).Rounded(6)
            .Hovered.BackgroundColor(SampleHud.ControlHover).End()
            .Text(text, font).FontSize(14).TextColor(SampleHud.Bright)
            .Alignment(TextAlignment.MiddleCenter)
            .Cursor(PaperCursor.Pointer)
            .OnClick(_ => onClick());
    }

    /// <summary>A checkbox row. <paramref name="onChange"/> receives the flipped value when clicked.</summary>
    public static void Toggle(Paper paper, FontFile font, string text, bool value, Action<bool> onChange, int id = 0)
    {
        using (paper.Row("toggle " + text, id).Height(26).Gap(8)
            .Cursor(PaperCursor.Pointer)
            .OnClick(_ => onChange(!value))
            .Enter())
        {
            paper.Box("box").Width(18).Height(18).Top(4)
                .Rounded(4).BorderWidth(2).BorderColor(value ? SampleHud.Accent : SampleHud.Dim)
                .BackgroundColor(value ? SampleHud.Accent : new Color(0f, 0f, 0f, 0f))
                .IsNotInteractable();
            paper.Box("text").Width(UnitValue.Stretch())
                .Text(text, font).FontSize(14).TextColor(SampleHud.Bright)
                .Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();
        }
    }

    /// <summary>A labelled horizontal slider between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static void Slider(Paper paper, FontFile font, string text, float value, float min, float max, Action<float> onChange, string format = "0.00", int id = 0)
    {
        float t = Saturate((value - min) / (max - min));
        using (paper.Column("slider " + text, id).Height(UnitValue.Auto).Gap(2).Enter())
        {
            paper.Box("label").Height(18)
                .Text($"{text}  {value.ToString(format)}", font).FontSize(14).TextColor(SampleHud.Bright)
                .Alignment(TextAlignment.MiddleLeft);

            void Set(ElementEvent e) => onChange(min + Saturate(e.NormalizedPosition.X) * (max - min));

            using (paper.Box("track").Height(14)
                .BackgroundColor(SampleHud.Control).Rounded(7)
                .Hovered.BackgroundColor(SampleHud.ControlHover).End()
                .Cursor(PaperCursor.Pointer)
                .OnPress(e => Set(e))
                .OnDragging(e => Set(e))
                .Enter())
            {
                paper.Box("fill").Width(UnitValue.Percentage(t * 100f)).Height(14)
                    .BackgroundColor(SampleHud.Accent).Rounded(7)
                    .IsNotInteractable();
            }
        }
    }

    /// <summary>Cycles through the values of an enum on click.</summary>
    public static void Cycle<T>(Paper paper, FontFile font, string text, T value, Action<T> onChange, int id = 0) where T : struct, Enum
    {
        T[] values = Enum.GetValues<T>();
        int next = (Array.IndexOf(values, value) + 1) % values.Length;
        Button(paper, font, $"{text}: {value}", () => onChange(values[next]), id);
    }
}
