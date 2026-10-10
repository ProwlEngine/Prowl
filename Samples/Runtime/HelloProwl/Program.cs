// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Hello Prowl
//
// The smallest complete game: a scene with a light, a camera and a floor, a component that
// spins and bobs a cube, and another that spawns physics cubes when you press a key.
// Everything else in the samples builds on these few ideas.
//
// Controls:
//   Space    Change the cube's color
//   C        Drop a physics cube
//   X        Remove every dropped cube
//   Escape   Quit
//

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace HelloProwl;

internal class Program
{
    static void Main(string[] args)
    {
        new HelloProwlGame().Run("Hello Prowl", 1280, 720);
    }
}

public sealed class HelloProwlGame : Game
{
    public override void Initialize()
    {
        var scene = new Scene();

        // A light, angled down like an afternoon sun.
        var sun = new GameObject("Sun");
        sun.AddComponent<DirectionalLight>();
        sun.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        scene.Add(sun);

        // The camera the game renders through. The "Main Camera" tag marks it as the one to use.
        var cameraObject = new GameObject("Main Camera") { Tag = "Main Camera" };
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.HDR = true;
        camera.Effects = [new TonemapperEffect(), new FXAAEffect()];
        cameraObject.Transform.Position = new Float3(0f, 3f, -8f);
        cameraObject.Transform.LookAt(new Float3(0f, 1f, 0f));
        scene.Add(cameraObject);

        // A floor with a collider, so dropped cubes have something to land on.
        var floor = new GameObject("Floor");
        var floorRenderer = floor.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = Mesh.CreateCube(new Float3(80f, 0.2f, 80f));
        floorRenderer.Material = MakeMaterial(new Color(0.06f, 0.065f, 0.075f, 1f));
        floor.AddComponent<BoxCollider>().Size = new Float3(80f, 0.2f, 80f);
        floor.Transform.Position = new Float3(0f, -0.1f, 0f);
        scene.Add(floor);

        // The star of the show, with our own component on it.
        var cube = new GameObject("Spinning Cube");
        var cubeRenderer = cube.AddComponent<MeshRenderer>();
        cubeRenderer.Mesh = Mesh.CreateCube(Float3.One);
        cubeRenderer.Material = MakeMaterial(new Color(0.9f, 0.22f, 0.03f, 1f));
        cube.AddComponent<Spinner>();
        cube.Transform.Position = new Float3(0f, 1.5f, 0f);
        scene.Add(cube);

        var spawner = new GameObject("Spawner");
        spawner.AddComponent<CubeSpawner>();
        scene.Add(spawner);

        Scene.Load(scene);
    }

    public static Material MakeMaterial(Color color)
    {
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        material.SetColor("_MainColor", color);
        return material;
    }
}

/// <summary>Spins and bobs its GameObject, and changes color when Space is pressed.</summary>
public sealed class Spinner : MonoBehaviour
{
    public float DegreesPerSecond = 90f;
    public float BobHeight = 0.25f;

    // Material colors are linear, so strong colors use small values in the weaker channels.
    private static readonly Color[] Palette =
    [
        new(0.9f, 0.22f, 0.03f, 1f),
        new(0.05f, 0.5f, 0.08f, 1f),
        new(0.03f, 0.15f, 0.8f, 1f),
        new(0.6f, 0.05f, 0.5f, 1f),
    ];

    private Float3 _start;
    private MeshRenderer _renderer = null!;
    private int _colorIndex;

    public override void Start()
    {
        _start = Transform.Position;
        _renderer = GetComponent<MeshRenderer>()!;
    }

    public override void Update()
    {
        Transform.Rotate(new Float3(0f, DegreesPerSecond * Time.DeltaTime, 0f));
        Transform.Position = _start + new Float3(0f, MathF.Sin(Time.TimeSinceStartup * 2f) * BobHeight, 0f);

        if (Input.GetKeyDown(KeyCode.Space))
        {
            _colorIndex = (_colorIndex + 1) % Palette.Length;
            _renderer.Material!.SetColor("_MainColor", Palette[_colorIndex]);
            Debug.Log($"Switched to color {_colorIndex}");
        }
    }
}

/// <summary>Drops physics cubes on C, clears them on X, quits on Escape and draws the key help.</summary>
public sealed class CubeSpawner : MonoBehaviour
{
    private readonly List<GameObject> _spawned = new();

    public override void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            var cube = new GameObject("Dropped Cube");
            var renderer = cube.AddComponent<MeshRenderer>();
            renderer.Mesh = Mesh.CreateCube(new Float3(0.5f, 0.5f, 0.5f));
            renderer.Material = HelloProwlGame.MakeMaterial(new Color(0.05f, 0.25f, 0.8f, 1f));
            cube.AddComponent<BoxCollider>().Size = new Float3(0.5f, 0.5f, 0.5f);
            cube.AddComponent<Rigidbody3D>();
            cube.Transform.Position = new Float3(Sample.Rng.NextSingle() * 4f - 2f, 6f, Sample.Rng.NextSingle() * 4f - 2f);
            cube.Transform.LocalEulerAngles = new Float3(Sample.Rng.NextSingle() * 360f, Sample.Rng.NextSingle() * 360f, 0f);
            GameObject.Scene!.Add(cube);
            _spawned.Add(cube);
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            foreach (GameObject cube in _spawned)
                cube.Destroy();
            _spawned.Clear();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            Game.Quit();
    }

    public override void OnGui(Paper paper)
    {
        var font = FontAsset.LoadDefault().FontFile;
        if (font == null) return;

        paper.Box("help").Margin(20).Height(30)
            .Text($"Space  change color     C  drop a cube     X  clear     Escape  quit     {_spawned.Count} cubes", font)
            .FontSize(18).TextColor(Color.White);
    }
}
