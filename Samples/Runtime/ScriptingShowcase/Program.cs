// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Scripting Showcase
//
// A row of stations, each built around a part of the object model and game loop:
//   1  Lifecycle        OnEnable, OnDisable, Start, OnDispose and what triggers each
//   2  Hierarchy        parenting, local vs world transforms, reparenting with and without keeping position
//   3  Instantiate      copying a template object tree, with its components and children
//   4  Async tasks      GameTask sequences, waiting on frames and time, work on a worker thread
//   5  Input actions    action maps, composites, Tap, Hold and MultiTap interactions, rebinding a key
//   6  Time             time scale, scaled vs unscaled time, Update vs FixedUpdate
//   7  Layers and tags  custom layers, camera culling masks, finding objects by tag
//
// Controls:
//   1 to 7      Jump to a station
//   WASD, Q/E   Fly, hold Right Mouse to look, Shift to go faster
//   F1          Hide the HUD
//   The panel on the right holds each station's own controls.
//

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace ScriptingShowcase;

internal class Program
{
    static void Main(string[] args)
    {
        new ScriptingShowcaseGame().Run("Scripting Showcase", 1600, 900);
    }
}

public sealed class ScriptingShowcaseGame : StationGame
{
    private const float StationSpacing = 22f;
    private const int StationCount = 7;
    private const int InputStation = 4;
    private const int TimeStation = 5;

    private Material _dark = null!;
    private Material _stone = null!;

    private static Float3 StationCenter(int index) => new(index * StationSpacing, 0f, 0f);

    protected override void Build()
    {
        _dark = Lit(new Color(0.03f, 0.032f, 0.04f, 1f), 0f, 0.7f);
        _stone = Lit(new Color(0.08f, 0.08f, 0.09f, 1f), 0f, 0.8f);

        var sun = new GameObject("Sun");
        DirectionalLight light = sun.AddComponent<DirectionalLight>();
        light.ShadowDistance = 60f;
        light.Intensity = 0.6f;
        sun.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        Add(sun);

        float length = StationSpacing * StationCount + 20f;
        Add(Block("Floor", new Float3(length, 1f, 40f), Floor(length, 40f), new Float3(StationSpacing * (StationCount - 1) * 0.5f, -0.5f, 0f)));

        BuildLifecycle(StationCenter(0));
        BuildHierarchy(StationCenter(1));
        BuildInstantiate(StationCenter(2));
        BuildAsync(StationCenter(3));
        BuildInput(StationCenter(4));
        BuildTime(StationCenter(5));
        BuildLayers(StationCenter(6));
    }

    private GameObject Pedestal(Float3 center, Float3 size)
        => Add(Block("Pedestal", size, _dark, center + new Float3(0f, size.Y * 0.5f, 0f)));

    public override string Stats => CurrentStation switch
    {
        0 => _lamp.IsValid() ? $"Lamp object {(_lamp.Enabled ? "on" : "off")}, probe component {(_lamp.GetComponent<LifecycleProbe>()!.Enabled ? "on" : "off")}, parent {(_lifecycleParent.Enabled ? "on" : "off")}" : "Lamp destroyed",
        1 => $"Moon local position {Format(_moon.Transform.LocalPosition)}    world position {Format(_moon.Transform.Position)}",
        2 => $"{_clones.Count} copies in the scene",
        3 => $"Courier: {_courier.Step}    Worker: {_paintStatus}",
        4 => _inputDemo.Status,
        5 => _timeCounter.Status,
        6 => _layerStatus,
        _ => string.Empty,
    };

    private static string Format(Float3 v) => $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})";

    protected override void OnStationChanged(int index)
    {
        CameraObject.GetComponent<FlyCamera>()!.Enabled = index != InputStation;
        if (index != TimeStation)
            Time.TimeScale = 1f;
        MainCamera.CullingMask = LayerMask.Everything;
        _layerVisible = [true, true, true];
    }

    public override void DrawControls(Paper paper, FontFile font)
    {
        switch (CurrentStation)
        {
            case 0: LifecycleControls(paper, font); break;
            case 1: HierarchyControls(paper, font); break;
            case 2: InstantiateControls(paper, font); break;
            case 3: AsyncControls(paper, font); break;
            case 4: _inputDemo.DrawControls(paper, font); break;
            case 5: TimeControls(paper, font); break;
            case 6: LayerControls(paper, font); break;
        }
    }

    // ----------------------------------------------------------------
    //  1  Lifecycle
    // ----------------------------------------------------------------

    private Float3 _lifecycleCenter;
    private GameObject _lifecycleParent = null!;
    private GameObject _lamp = null!;

    private void BuildLifecycle(Float3 c)
    {
        AddStation("Lifecycle", "The glowing cube has a component that records every lifecycle call it gets. Turn the object, the component or its parent off and on, or destroy it, and watch which callbacks fire and in what order.", c, new Float3(0f, 3f, -8f), 1.5f);

        _lifecycleCenter = c;
        _lifecycleParent = Pedestal(c, new Float3(2f, 1f, 2f));
        _lifecycleParent.Name = "Parent";
        SpawnLamp();
    }

    private void SpawnLamp()
    {
        Color orange = new(1f, 0.35f, 0.05f, 1f);
        _lamp = Model("Lamp", Mesh.CreateCube(new Float3(0.8f, 0.8f, 0.8f)), Lit(orange).Emissive(orange, 3f), _lifecycleCenter + new Float3(0f, 1.8f, 0f));
        _lamp.AddComponent<LifecycleProbe>();
        _lamp.SetParent(_lifecycleParent);
    }

    private void LifecycleControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Lamp");
        if (_lamp.IsValid())
        {
            var probe = _lamp.GetComponent<LifecycleProbe>()!;
            Toggle(paper, font, "GameObject enabled", _lamp.Enabled, v => _lamp.Enabled = v);
            Toggle(paper, font, "Probe component enabled", probe.Enabled, v => probe.Enabled = v);
            Toggle(paper, font, "Parent enabled", _lifecycleParent.Enabled, v => _lifecycleParent.Enabled = v);
            Button(paper, font, "Destroy", () => _lamp.Destroy());
        }
        else
        {
            Button(paper, font, "Create a new lamp", SpawnLamp);
        }

        Header(paper, font, "Calls, newest last", 1);
        for (int i = 0; i < LifecycleProbe.Log.Count; i++)
            Label(paper, font, LifecycleProbe.Log[i], i);
    }

    // ----------------------------------------------------------------
    //  2  Hierarchy
    // ----------------------------------------------------------------

    private GameObject _planet = null!;
    private GameObject _moon = null!;
    private bool _moonAttached = true;
    private bool _keepWorldPosition = true;

    private void BuildHierarchy(Float3 c)
    {
        AddStation("Hierarchy", "The planet is a child of the spinning sun and the moon is a child of the planet, so each one only spins in place and the parents carry it around. Detach the moon and it stops where it is. Reattach it with or without keeping its world position.", c, new Float3(0f, 7f, -11f), 1.5f);

        Color yellow = new(1f, 0.7f, 0.2f, 1f);
        GameObject sun = Add(Model("Sun", Mesh.CreateSphere(0.9f, 16, 24), Lit(yellow).Emissive(yellow, 4f), c + new Float3(0f, 2f, 0f)));
        sun.AddComponent<Spin>().DegreesPerSecond = new Float3(0f, 30f, 0f);

        _planet = Model("Planet", Mesh.CreateSphere(0.45f, 12, 18), Lit(new Color(0.01f, 0.08f, 0.5f, 1f), 0f, 0.4f), Float3.Zero);
        _planet.SetParent(sun);
        _planet.Transform.LocalPosition = new Float3(4f, 0f, 0f);
        _planet.AddComponent<Spin>().DegreesPerSecond = new Float3(0f, 120f, 0f);

        _moon = Model("Moon", Mesh.CreateSphere(0.18f, 10, 14), Lit(new Color(0.3f, 0.3f, 0.3f, 1f)), Float3.Zero);
        _moon.SetParent(_planet);
        _moon.Transform.LocalPosition = new Float3(1.1f, 0f, 0f);

        var trail = Add(new GameObject("Moon Trail")).AddComponent<Trail>();
        trail.Target = _moon.Transform;
        trail.Color = new Color(0.6f, 0.75f, 1f, 1f);
    }

    private void HierarchyControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Moon");
        Toggle(paper, font, "Keep world position when reparenting", _keepWorldPosition, v => _keepWorldPosition = v);
        Button(paper, font, _moonAttached ? "Detach from planet" : "Attach to planet", () =>
        {
            _moonAttached = !_moonAttached;
            _moon.SetParent(_moonAttached ? _planet : null!, _keepWorldPosition);
        });
        Label(paper, font, "Without keeping world position, the moon's local position is reused under its new parent, so it jumps.");
    }

    // ----------------------------------------------------------------
    //  3  Instantiate
    // ----------------------------------------------------------------

    private Float3 _instantiateCenter;
    private GameObject _template = null!;
    private readonly List<GameObject> _clones = new();

    private void BuildInstantiate(Float3 c)
    {
        AddStation("Instantiate", "A template lantern is built once and never added to the scene. Each copy gets the whole tree, a post with a glowing head on top, including the Bob component that makes the head float.", c, new Float3(0f, 6f, -12f), 0.5f);

        _instantiateCenter = c;

        // The template: a post with a glowing head as its child, and a component on the head.
        _template = Model("Lantern", Mesh.CreateCylinder(0.08f, 1.2f, 12), _stone, Float3.Zero);
        GameObject head = Model("Head", Mesh.CreateSphere(0.22f, 10, 14), Lit(Color.White), new Float3(0f, 0.85f, 0f));
        head.SetParent(_template);
        head.AddComponent<Bob>();

        for (int i = 0; i < 24; i++)
            SpawnLantern();
    }

    private void SpawnLantern()
    {
        GameObject copy = GameObject.Instantiate(_template, SampleScene)!;
        copy.Transform.Position = _instantiateCenter + new Float3(Random.Shared.NextSingle() * 14f - 7f, 0.6f, Random.Shared.NextSingle() * 8f - 4f);

        // Every copy shares the template's materials, so give each head its own to tint it.
        Color color = Hsv(Random.Shared.NextSingle(), 0.8f, 1f);
        copy.GetComponentInChildren<Bob>()!.GetComponent<MeshRenderer>()!.Material = Lit(color).Emissive(color, 2.5f);
        _clones.Add(copy);
    }

    private void InstantiateControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Lanterns");
        Button(paper, font, "Spawn 10", () => { for (int i = 0; i < 10; i++) SpawnLantern(); });
        Button(paper, font, "Clear", () =>
        {
            foreach (GameObject clone in _clones)
                clone.Destroy();
            _clones.Clear();
        }, 1);
    }

    // ----------------------------------------------------------------
    //  4  Async tasks
    // ----------------------------------------------------------------

    private Courier _courier = null!;
    private Material _canvas = null!;
    private string _paintStatus = "idle";
    private int _paintSeed;

    private void BuildAsync(Float3 c)
    {
        AddStation("Async tasks", "The courier runs one async method that loops forever: move to a pad, wait, change color, move on. Painting runs the slow part on a worker thread and comes back to the main thread to make the texture.", c, new Float3(0f, 6f, -11f), 1f);

        Float3[] pads = [c + new Float3(-5f, 0f, -1f), c + new Float3(0f, 0f, 3f), c + new Float3(5f, 0f, -1f)];
        Color[] colors = [new(0.5f, 0.02f, 0.01f, 1f), new(0.02f, 0.4f, 0.03f, 1f), new(0.01f, 0.08f, 0.6f, 1f)];
        for (int i = 0; i < pads.Length; i++)
            Add(Model($"Pad {i + 1}", Mesh.CreateCylinder(0.9f, 0.1f, 24), Lit(colors[i], 0f, 0.5f), pads[i] + new Float3(0f, 0.05f, 0f)));

        GameObject courier = Add(Model("Courier", Mesh.CreateCube(new Float3(0.8f, 0.8f, 0.8f)), Lit(new Color(0.5f, 0.5f, 0.5f, 1f), 0f, 0.4f), pads[0] + new Float3(0f, 0.5f, 0f)));
        _courier = courier.AddComponent<Courier>();
        _courier.Stops = pads;
        _courier.Colors = colors;

        _canvas = Lit(new Color(0.4f, 0.4f, 0.4f, 1f), 0f, 0.9f).With("_MainTex", Load<Texture2D>("Textures/Checker"));
        Add(Model("Canvas", Mesh.CreateCube(new Float3(3f, 3f, 0.1f)), _canvas, c + new Float3(0f, 2.5f, 6f)));
    }

    private async void Paint()
    {
        if (_paintStatus.StartsWith("painting")) return;
        _paintStatus = "painting on a worker thread";
        int seed = ++_paintSeed;
        var timer = System.Diagnostics.Stopwatch.StartNew();

        // Everything after this line runs on a worker, so the game keeps running while it works.
        await GameTask.WorkerThread();
        const int size = 512;
        var pixels = new Color32[size * size];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                float n = Fractal(x / (float)size + seed * 0.37f, y / (float)size, 4, 6);
                Color color = Hsv(n * 1.5f + seed * 0.13f, 0.7f, 0.3f + n * 0.7f);
                pixels[y * size + x] = new Color32((byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255), 255);
            }
        });

        // Textures belong to the main thread, so hop back before touching one.
        await GameTask.MainThread();
        var texture = new Texture2D((uint)size, (uint)size);
        texture.SetData(new Memory<Color32>(pixels));
        texture.GenerateMipmaps();
        texture.SetTextureFilters(TextureMin.LinearMipmapLinear, TextureMag.Linear);
        _canvas.SetTexture("_MainTex", texture);
        _paintStatus = $"painted in {timer.ElapsedMilliseconds} ms without stalling a frame";
    }

    private void AsyncControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Worker thread");
        Button(paper, font, "Paint the canvas", Paint);
        Label(paper, font, "GameTask.WorkerThread() moves the rest of the method to a worker, GameTask.MainThread() brings it back.");
    }

    // ----------------------------------------------------------------
    //  5  Input actions
    // ----------------------------------------------------------------

    private InputDemo _inputDemo = null!;

    private void BuildInput(Float3 c)
    {
        AddStation("Input actions", "The fly camera is off here, so WASD, the arrows or a left stick drive the puck through one Move action. Tap Space to hop, hold it to charge a big jump, and double tap Shift to dash. Rebind the jump key from the panel.", c, new Float3(0f, 7f, -10f), 0f);

        Add(Block("Arena", new Float3(12f, 0.2f, 8f), _stone, c + new Float3(0f, 0.1f, 0f)));
        GameObject puck = Add(Model("Puck", Mesh.CreateCylinder(0.5f, 0.3f, 24), Lit(new Color(0.6f, 0.12f, 0.01f, 1f), 0f, 0.4f), c + new Float3(0f, 0.35f, 0f)));
        _inputDemo = puck.AddComponent<InputDemo>();
        _inputDemo.Center = c;
    }

    // ----------------------------------------------------------------
    //  6  Time
    // ----------------------------------------------------------------

    private TimeCounter _timeCounter = null!;

    private void BuildTime(Float3 c)
    {
        AddStation("Time", "The left cube turns with Time.DeltaTime and the right with Time.UnscaledDeltaTime. The ball bounces under physics, which steps in FixedUpdate. Slow time down and only the scaled things slow with it.", c, new Float3(0f, 4f, -10f), 1.5f);

        GameObject scaled = Add(Model("Scaled Spinner", Mesh.CreateCube(Float3.One), Lit(new Color(0.6f, 0.12f, 0.01f, 1f)), c + new Float3(-4f, 1.5f, 0f)));
        scaled.AddComponent<Spin>().DegreesPerSecond = new Float3(0f, 90f, 0f);

        GameObject unscaled = Add(Model("Unscaled Spinner", Mesh.CreateCube(Float3.One), Lit(new Color(0.01f, 0.1f, 0.6f, 1f)), c + new Float3(4f, 1.5f, 0f)));
        var unscaledSpin = unscaled.AddComponent<Spin>();
        unscaledSpin.DegreesPerSecond = new Float3(0f, 90f, 0f);
        unscaledSpin.Unscaled = true;

        GameObject ball = Add(Model("Ball", Mesh.CreateSphere(0.4f, 12, 18), Lit(new Color(0.9f, 0.9f, 0.9f, 1f), 1f, 0.2f), c + new Float3(0f, 4f, 0f)));
        ball.AddComponent<SphereCollider>().Radius = 0.4f;
        var body = ball.AddComponent<Rigidbody3D>();
        body.Restitution = 1f;
        body.LinearDamping = 0f;

        _timeCounter = Add(new GameObject("Time Counter")).AddComponent<TimeCounter>();
    }

    private void TimeControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Time");
        Slider(paper, font, "Time scale", Time.TimeScale, 0f, 2f, v => Time.TimeScale = v);
        Button(paper, font, "Pause", () => Time.TimeScale = 0f);
        Button(paper, font, "Normal speed", () => Time.TimeScale = 1f, 1);
    }

    // ----------------------------------------------------------------
    //  7  Layers and tags
    // ----------------------------------------------------------------

    private static readonly string[] LayerNames = ["Default", "Pickups", "Enemies"];
    private bool[] _layerVisible = [true, true, true];
    private string _layerStatus = string.Empty;

    private void BuildLayers(Float3 c)
    {
        AddStation("Layers and tags", "Pickups and enemies sit on their own layers, so the camera's culling mask can hide a whole layer at once. Enemies are also tagged, and finding them by tag makes them jump.", c, new Float3(0f, 4f, -10f), 1f);

        // Layers 8 and up are free for games to name.
        TagLayerManager.layers[8] = "Pickups";
        TagLayerManager.layers[9] = "Enemies";
        if (!TagLayerManager.tags.Contains("Enemy"))
            TagLayerManager.tags.Add("Enemy");

        Material gold = Lit(new Color(1f, 0.7f, 0.2f, 1f), 1f, 0.25f);
        Material red = Lit(new Color(0.5f, 0.01f, 0.01f, 1f), 0f, 0.5f);

        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 4f;
            Add(Model("Crate", Mesh.CreateCube(new Float3(1f, 1f, 1f)), _stone, c + new Float3(x, 0.5f, 2f)));

            GameObject pickup = Add(Model("Coin", Mesh.CreateCylinder(0.4f, 0.1f, 24), gold, c + new Float3(x - 1.2f, 1f, -1f), new Float3(90f, 0f, 0f)));
            pickup.Layer = "Pickups";
            pickup.AddComponent<Spin>().DegreesPerSecond = new Float3(0f, 0f, 120f);

            GameObject enemy = Add(Model("Enemy", Mesh.CreateCone(0.5f, 1.2f, 20), red, c + new Float3(x + 1.2f, 0.6f, -1f)));
            enemy.Layer = "Enemies";
            enemy.Tag = "Enemy";
            enemy.AddComponent<Hop>();
        }
    }

    private void LayerControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Camera culling mask");
        for (int i = 0; i < LayerNames.Length; i++)
        {
            int index = i;
            Toggle(paper, font, $"Show {LayerNames[i]}", _layerVisible[i], v =>
            {
                _layerVisible[index] = v;
                LayerMask mask = MainCamera.CullingMask;
                int layer = LayerMask.NameToLayer(LayerNames[index]);
                if (v) mask.SetLayer(layer);
                else mask.RemoveLayer(layer);
                MainCamera.CullingMask = mask;
            }, i);
        }

        Header(paper, font, "Tags", 1);
        Button(paper, font, "Find everything tagged Enemy", () =>
        {
            GameObject[] found = SampleScene.AllObjects.Where(go => go.CompareTag("Enemy")).ToArray();
            foreach (GameObject enemy in found)
                enemy.GetComponent<Hop>()!.Jump();
            _layerStatus = $"Found {found.Length} objects tagged Enemy";
        });
    }
}

/// <summary>Records lifecycle calls into a shared log so the HUD can show them.</summary>
public sealed class LifecycleProbe : MonoBehaviour
{
    public static readonly List<string> Log = new();

    private void Record(string call)
    {
        Log.Add($"{Time.FrameCount}  {call}");
        if (Log.Count > 12) Log.RemoveAt(0);
    }

    public override void OnAddedToScene() => Record("OnAddedToScene");
    public override void OnEnable() => Record("OnEnable");
    public override void Start() => Record("Start");
    public override void OnDisable() => Record("OnDisable");
    public override void OnRemovedFromScene() => Record("OnRemovedFromScene");
    protected override void OnDispose() => Record("OnDispose");

    public override void Update() => Transform.Rotate(new Float3(0f, 45f * Time.DeltaTime, 0f));
}

/// <summary>Turns its GameObject at a fixed rate in local space.</summary>
public sealed class Spin : MonoBehaviour
{
    public Float3 DegreesPerSecond = new(0f, 90f, 0f);
    public bool Unscaled;

    public override void Update()
        => Transform.Rotate(DegreesPerSecond * (Unscaled ? Time.UnscaledDeltaTime : Time.DeltaTime));
}

/// <summary>Floats its GameObject up and down around where it started, out of step with its neighbours.</summary>
public sealed class Bob : MonoBehaviour
{
    private Float3 _start;
    private float _phase;

    public override void Start()
    {
        _start = Transform.LocalPosition;
        _phase = Random.Shared.NextSingle() * MathF.Tau;
    }

    public override void Update()
        => Transform.LocalPosition = _start + new Float3(0f, MathF.Sin(Time.TimeSinceStartup * 2f + _phase) * 0.15f, 0f);
}

/// <summary>Jumps once when asked, then settles back down.</summary>
public sealed class Hop : MonoBehaviour
{
    private float _baseY;
    private float _velocity;
    private bool _airborne;

    public override void Start() => _baseY = Transform.Position.Y;

    public void Jump()
    {
        _velocity = 6f;
        _airborne = true;
    }

    public override void Update()
    {
        if (!_airborne) return;
        _velocity -= 20f * Time.DeltaTime;
        Float3 p = Transform.Position;
        p.Y += _velocity * Time.DeltaTime;
        if (p.Y <= _baseY)
        {
            p.Y = _baseY;
            _airborne = false;
        }
        Transform.Position = p;
    }
}

/// <summary>Draws a fading line behind a target with a LineRenderer.</summary>
public sealed class Trail : MonoBehaviour
{
    public Transform Target = null!;
    public Color Color = Color.White;
    public int Length = 150;

    private LineRenderer _line = null!;

    public override void Start()
    {
        _line = AddComponent<LineRenderer>();
        _line.Material = new Material(Shader.LoadDefault(DefaultShader.Line));
        _line.StartWidth = 0.06f;
        _line.EndWidth = 0f;
        _line.StartColor = Color;
        _line.EndColor = new Color(Color.R, Color.G, Color.B, 0f);
    }

    public override void LateUpdate()
    {
        Float3 position = Target.Position;
        if (_line.Points.Count > 0 && Float3.Distance(_line.Points[0], position) < 0.05f)
            return;

        _line.Points.Insert(0, position);
        if (_line.Points.Count > Length)
            _line.Points.RemoveAt(_line.Points.Count - 1);
    }
}

/// <summary>Visits each stop in turn with one async method, changing color at every stop.</summary>
public sealed class Courier : MonoBehaviour
{
    public Float3[] Stops = [];
    public Color[] Colors = [];
    public string Step = "starting";

    public override void Start() => Run();

    private async void Run()
    {
        // The session token cancels the waits when the game shuts down.
        CancellationToken token = GameTask.SessionToken;
        try
        {
            while (true)
            {
                for (int i = 0; i < Stops.Length; i++)
                {
                    Step = $"moving to pad {i + 1}";
                    await MoveTo(Stops[i] + new Float3(0f, 0.5f, 0f), token);

                    Step = "waiting one second";
                    await GameTask.Delay(1f, token);

                    Step = "changing color";
                    GetComponent<MeshRenderer>()!.Material!.SetColor("_MainColor", Colors[i]);
                    await GameTask.Frames(30, token);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task MoveTo(Float3 target, CancellationToken token)
    {
        while (Float3.Distance(Transform.Position, target) > 0.01f)
        {
            Float3 toTarget = target - Transform.Position;
            float step = MathF.Min(4f * Time.DeltaTime, Float3.Length(toTarget));
            Transform.Position += Float3.Normalize(toTarget) * step;
            await GameTask.NextFrame(token);
        }
    }
}

/// <summary>Moves a puck with input actions and shows what each action is doing.</summary>
public sealed class InputDemo : MonoBehaviour
{
    public Float3 Center;
    public string Status = string.Empty;

    private InputActionMap _map = null!;
    private InputAction _move = null!;
    private InputAction _hop = null!;
    private InputAction _jump = null!;
    private InputAction _dash = null!;
    private InputBinding _hopBinding = null!;
    private InputBinding _jumpBinding = null!;

    private Float3 _velocity;
    private float _height;
    private float _charge;
    private float _dashTime;
    private Float2 _facing = new(1f, 0f);
    private bool _rebinding;
    private string _lastEvent = "none yet";

    public override void OnEnable()
    {
        _map = new InputActionMap("Puck");

        // One Value action fed by a WASD composite, an arrow key composite and the left stick.
        _move = _map.AddAction("Move", InputActionType.Value);
        _move.ExpectedValueType = typeof(Float2);
        _move.AddBinding(new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.W), InputBinding.CreateKeyBinding(KeyCode.S),
            InputBinding.CreateKeyBinding(KeyCode.A), InputBinding.CreateKeyBinding(KeyCode.D)));
        _move.AddBinding(new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.Up), InputBinding.CreateKeyBinding(KeyCode.Down),
            InputBinding.CreateKeyBinding(KeyCode.Left), InputBinding.CreateKeyBinding(KeyCode.Right)));
        var stick = InputBinding.CreateGamepadAxisBinding(0);
        stick.Processors.Add(new DeadzoneProcessor(0.15f));
        _move.AddBinding(stick);

        // The same key on two actions with different interactions: a quick tap and a long hold.
        _hop = _map.AddAction("Hop", InputActionType.Button);
        _hopBinding = InputBinding.CreateKeyBinding(KeyCode.Space, InputInteractionType.Tap);
        _hop.AddBinding(_hopBinding);
        _hop.AddBinding(GamepadButton.A, interaction: InputInteractionType.Tap);
        _hop.Performed += _ => { _lastEvent = "Hop performed (Tap)"; Launch(4f); };

        _jump = _map.AddAction("Jump", InputActionType.Button);
        _jumpBinding = InputBinding.CreateKeyBinding(KeyCode.Space, InputInteractionType.Hold);
        _jumpBinding.HoldDuration = 0.5f;
        _jump.AddBinding(_jumpBinding);
        _jump.Performed += _ => { _lastEvent = "Jump performed (Hold 0.5 s)"; Launch(9f); };

        _dash = _map.AddAction("Dash", InputActionType.Button);
        var dashBinding = InputBinding.CreateKeyBinding(KeyCode.ShiftLeft, InputInteractionType.MultiTap);
        dashBinding.TapCount = 2;
        _dash.AddBinding(dashBinding);
        _dash.Performed += _ => { _lastEvent = "Dash performed (MultiTap x2)"; _dashTime = 0.2f; };

        Input.RegisterActionMap(_map);
        _map.Enable();
    }

    public override void OnDisable()
    {
        _map.Disable();
        Input.UnregisterActionMap(_map);
        StopRebinding();
    }

    private void Launch(float speed)
    {
        if (_height <= 0f)
            _velocity.Y = speed;
    }

    public override void Update()
    {
        Float2 move = _move.ReadValue<Float2>();
        if (Float2.Length(move) > 0.01f)
            _facing = Float2.Normalize(move);

        float speed = _dashTime > 0f ? 18f : 5f;
        _dashTime -= Time.DeltaTime;
        Float3 p = Transform.Position + new Float3(move.X, 0f, move.Y) * speed * Time.DeltaTime;
        if (_dashTime > 0f && Float2.Length(move) < 0.01f)
            p += new Float3(_facing.X, 0f, _facing.Y) * speed * Time.DeltaTime;

        _velocity.Y -= 25f * Time.DeltaTime;
        _height = MathF.Max(0f, _height + _velocity.Y * Time.DeltaTime);
        if (_height <= 0f) _velocity.Y = 0f;

        p.X = Math.Clamp(p.X, Center.X - 5.5f, Center.X + 5.5f);
        p.Z = Math.Clamp(p.Z, Center.Z - 3.5f, Center.Z + 3.5f);
        p.Y = Center.Y + 0.35f + _height;
        Transform.Position = p;

        _charge = Input.GetKey(_jumpBinding.Key!.Value) ? _charge + Time.DeltaTime : 0f;

        Status = $"Move {move.X:0.00}, {move.Y:0.00}    holding {_charge:0.0} s    last: {_lastEvent}";
    }

    public void DrawControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Actions");
        Label(paper, font, $"Move  {_move.ReadValue<Float2>().X:0.00}, {_move.ReadValue<Float2>().Y:0.00}", 0);
        Label(paper, font, $"Hop  tap {_hopBinding.Key}", 1);
        Label(paper, font, $"Jump  hold {_jumpBinding.Key}", 2);
        Label(paper, font, "Dash  double tap Left Shift", 3);

        Header(paper, font, "Rebinding", 1);
        if (_rebinding)
            Label(paper, font, "Press any key for hop and jump, Escape to cancel", 4);
        else
            Button(paper, font, "Rebind hop and jump", StartRebinding);
    }

    private void StartRebinding()
    {
        _rebinding = true;
        Input.OnKeyEvent += OnKey;
    }

    private void StopRebinding()
    {
        if (!_rebinding) return;
        _rebinding = false;
        Input.OnKeyEvent -= OnKey;
    }

    private void OnKey(KeyCode key, bool down)
    {
        if (!down) return;
        if (key != KeyCode.Escape)
        {
            _hopBinding.Key = key;
            _jumpBinding.Key = key;
            _lastEvent = $"rebound to {key}";
        }
        StopRebinding();
    }
}

/// <summary>Counts Update and FixedUpdate calls over each real second.</summary>
public sealed class TimeCounter : MonoBehaviour
{
    public string Status = string.Empty;

    private int _updates;
    private int _fixedUpdates;
    private float _window;

    public override void FixedUpdate() => _fixedUpdates++;

    public override void Update()
    {
        _updates++;
        _window += Time.UnscaledDeltaTime;
        if (_window < 1f) return;

        Status = $"Time scale {Time.TimeScale:0.00}    {_updates} Update calls and {_fixedUpdates} FixedUpdate calls in the last second    game time {Time.TimeSinceStartup:0.0} s";
        _updates = 0;
        _fixedUpdates = 0;
        _window = 0f;
    }
}
