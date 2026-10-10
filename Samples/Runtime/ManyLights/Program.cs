// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Many Lights
//
// A night plaza lit by thousands of small static point lights without shadows. Everything in it is
// static: the columns, walls and bulbs are merged into a few draws by static batching, and the lights
// sit in the static light tree, so each pixel is only shaded by the handful of lights that reach it.
//
// Controls:
//   WASD, Q/E   Fly, hold Right Mouse to look, Shift to go faster
//   F1          Hide the HUD
//   The panel on the right changes how many lights are on.
//

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace ManyLights;

internal class Program
{
    static void Main(string[] args)
    {
        new ManyLightsGame().Run("Many Lights", 1600, 900);
    }
}

public sealed class ManyLightsGame : StationGame
{
    private const float ColumnSpacing = 8f;
    private const float AreaPerLight = 7f;
    private const int MaxLights = 16384;
    private const int DefaultLights = 8192;

    // Big enough for every light at the same density, so more lights fill more plaza rather than crowding it
    private static readonly float PlazaSize = MathF.Ceiling(MathF.Sqrt(MaxLights * AreaPerLight) / ColumnSpacing) * ColumnSpacing;

    private readonly List<GameObject> _lights = new();
    private readonly List<(Float3 Position, int Color)> _slots = new();
    private readonly Random _random = new(1234);
    private Material[] _bulbMaterials = null!;
    private Mesh _bulb = null!;
    private int _count;

    // A few strong colors, so the bulbs share materials and batch into a handful of draws
    private static readonly Color[] Palette =
    [
        new(1f, 0.45f, 0.15f, 1f), new(1f, 0.8f, 0.35f, 1f), new(0.3f, 0.55f, 1f, 1f), new(0.35f, 1f, 0.5f, 1f),
        new(1f, 0.3f, 0.55f, 1f), new(0.7f, 0.4f, 1f, 1f), new(0.3f, 0.95f, 1f, 1f), new(1f, 1f, 0.9f, 1f),
    ];

    protected override void Build()
    {
        AddStation("Many lights", "Thousands of static point lights, every one casting shadows. Shadow maps are cached, so a light only draws its shadow again when something in its range changes, and lights share one atlas sized by how much of the screen they cover. More lights fill more of the plaza at the same density.",
            Float3.Zero, new Float3(0f, 14f, -40f), 0f);

        SampleScene.Fog.Mode = Scene.FogParams.FogMode.Off;
        SampleScene.Skybox.Mode = Scene.SkyboxMode.SolidColor;
        SampleScene.Skybox.SolidColor = new Color(0.004f, 0.006f, 0.012f, 1f);
        SampleScene.Ambient.Color = new Float4(0.2f, 0.25f, 0.4f, 1f);
        SampleScene.Ambient.Strength = 0.04f;
        MainCamera.FarClipPlane = 300f;
        CameraObject.GetComponent<FlyCamera>().Speed = 12f;
        CameraObject.GetComponent<FlyCamera>().FastSpeed = 40f;

        BuildPlaza();
        PlanSlots();

        _bulb = Mesh.CreateSphere(0.08f, 4, 6);
        _bulbMaterials = Palette.Select(c => Unlit(new Color(c.R * 6f, c.G * 6f, c.B * 6f, 1f))).ToArray();
        SetLightCount(DefaultLights);
    }

    private void BuildPlaza()
    {
        Material stone = Lit(new Color(0.16f, 0.15f, 0.14f, 1f), 0f, 0.8f);
        Material dark = Lit(new Color(0.05f, 0.05f, 0.06f, 1f), 0f, 0.5f);

        Static(Model("Floor", Plane(PlazaSize, PlazaSize), Floor(PlazaSize, PlazaSize), Float3.Zero));

        Mesh column = Mesh.CreateCylinder(0.35f, 4f, 12);
        Mesh cap = Mesh.CreateCube(new Float3(1.1f, 0.3f, 1.1f));
        Mesh wall = Mesh.CreateCube(new Float3(ColumnSpacing - 1f, 1.2f, 0.4f));
        int perSide = (int)(PlazaSize / ColumnSpacing);
        for (int x = 0; x < perSide; x++)
        {
            for (int z = 0; z < perSide; z++)
            {
                Float3 p = new((x - (perSide - 1) * 0.5f) * ColumnSpacing, 0f, (z - (perSide - 1) * 0.5f) * ColumnSpacing);
                Static(Model("Column", column, stone, p + new Float3(0f, 2f, 0f)));
                Static(Model("Cap", cap, dark, p + new Float3(0f, 4.15f, 0f)));

                // Low walls between some columns give the light something to fall across
                if ((x * 7 + z * 13) % 5 == 0)
                    Static(Model("Wall", wall, stone, p + new Float3(ColumnSpacing * 0.5f, 0.6f, 0f)));
            }
        }
    }

    private GameObject Static(GameObject go)
    {
        go.IsStatic = true;
        return Add(go);
    }

    /// <summary>Turns lights on or off to reach <paramref name="count"/>, creating more when needed, then merges the static geometry again.</summary>
    private void SetLightCount(int count)
    {
        count = Math.Clamp(count, 0, MaxLights);
        while (_lights.Count < count)
            _lights.Add(CreateLight());
        for (int i = 0; i < _lights.Count; i++)
            _lights[i].Enabled = i < count;
        _count = count;
        SampleScene.UpdateStaticGeometry();
    }

    // Random spots over the whole plaza, nearest the middle first, so the first lights fill a square around the center
    private void PlanSlots()
    {
        float half = PlazaSize * 0.5f - 2f;
        for (int i = 0; i < MaxLights; i++)
            _slots.Add((new Float3(Lerp(-half, half), 0.4f + _random.NextSingle() * 3.2f, Lerp(-half, half)), _random.Next(Palette.Length)));
        _slots.Sort((a, b) => MathF.Max(MathF.Abs(a.Position.X), MathF.Abs(a.Position.Z)).CompareTo(MathF.Max(MathF.Abs(b.Position.X), MathF.Abs(b.Position.Z))));
    }

    private GameObject CreateLight()
    {
        (Float3 position, int color) = _slots[_lights.Count];

        GameObject go = Model("Light", _bulb, _bulbMaterials[color], position);
        PointLight light = go.AddComponent<PointLight>();
        light.Color = Palette[color];
        light.Range = 6f;
        light.Intensity = 5f;
        return Static(go);
    }

    private float Lerp(float a, float b) => a + (b - a) * _random.NextSingle();

    public override string Stats => $"{_count:N0} static point lights, {SampleScene.StaticGeometry.GroupCount} merged draw groups";

    public override void DrawControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Lights");
        Label(paper, font, $"{_count:N0} on", 1);
        Button(paper, font, "Double", () => SetLightCount(Math.Max(1024, _count * 2)), 2);
        Button(paper, font, "Halve", () => SetLightCount(_count / 2), 3);

        Header(paper, font, "Shadow atlas", 4);
        Label(paper, font, $"{ShadowAtlas.GetSize()} x {ShadowAtlas.GetSize()}, GPU limit {Graphics.MaxTextureSize}", 5);
        Button(paper, font, ShadowAtlas.RequestedSize >= 16384 ? "Use 8k" : "Use 16k", () => ShadowAtlas.RequestedSize = ShadowAtlas.RequestedSize >= 16384 ? 8192 : 16384, 6);
    }
}
