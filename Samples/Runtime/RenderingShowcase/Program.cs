// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Rendering Showcase
//
// A row of stations, each built around a part of the renderer:
//   1  Materials        the Standard shader across metallic and roughness
//   2  Surface maps     albedo, normal, parallax, surface and emission textures
//   3  Transparency     cutout, transparent, double sided and refraction shaders
//   4  Lights           point and spot lights with shadows, hard and soft
//   5  Many lights      hundreds of small point lights without shadows
//   6  Fog              volumetric fog, fog volumes and lights that scatter in it
//   7  Instancing       thousands of cubes as moving, static or immediate instanced draws
//   8  Render textures  a second camera drawing into a texture shown on a monitor
//   9  Clouds           volumetric clouds and flat cloud layers over the whole scene
//
// Textures and materials load from the sample's Assets folder.
//
// Controls:
//   1 to 9      Jump to a station
//   WASD, Q/E   Fly, hold Right Mouse to look, Shift to go faster
//   F1          Hide the HUD
//   The panels on the right hold each station's own controls and the scene wide settings:
//   time of day, sun, ambient light, distance fog and post effects.
//

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace RenderingShowcase;

internal class Program
{
    static void Main(string[] args)
    {
        new RenderingShowcaseGame().Run("Rendering Showcase", 1600, 900);
    }
}

public sealed class RenderingShowcaseGame : StationGame
{
    private const float StationSpacing = 30f;
    private const int StationCount = 9;
    private const int FogStation = 5;
    private const int CloudStation = 8;

    // The time of day each station is shown at, in hours.
    private static readonly float[] StationHours = [14f, 15f, 13f, 22f, 22f, 23f, 11f, 14f, 16f];

    private Light _sun = null!;
    private VolumetricFog _fog = null!;
    private VolumetricClouds _clouds = null!;
    private float _cloudFlightSpeed = 300f;
    private Bloom _bloom = null!;
    private Tonemapping _tonemapper = null!;
    private float _hours = 14f;
    private bool _animateSky;
    private float _sunStrength = 1f;
    private float _ambientStrength = 1f;

    private Material _dark = null!;
    private Material _concrete = null!;

    private static Float3 StationCenter(int index) => new(index * StationSpacing, 0f, 0f);

    protected override void Build()
    {
        _dark = Lit(new Color(0.02f, 0.022f, 0.026f, 1f), 0f, 0.6f);
        _concrete = Lit(new Color(0.12f, 0.12f, 0.12f, 1f), 0f, 0.85f);

        var sun = new GameObject("Sun");
        _sun = sun.AddComponent<Light>();
        _sun.Type = LightType.Directional;
        _sun.ShadowDistance = 80f;
        _sun.Cascades = ShadowCascades.Four;
        Add(sun);

        _fog = MainCamera.Profile!.Add<VolumetricFog>(overrideAll: true);
        _fog.Enabled.Value = false;
        _fog.GlobalDensity.Value = 0.02f;
        _fog.MaxDistance.Value = 60f;
        _bloom = MainCamera.Profile.Get<Bloom>()!;
        _tonemapper = MainCamera.Profile.Get<Tonemapping>()!;
        SampleScene.Environment.Fog.Mode = FogMode.Off;

        float length = StationSpacing * StationCount + 30f;
        Add(Block("Floor", new Float3(length, 1f, 60f), Floor(length, 60f), new Float3(StationSpacing * (StationCount - 1) * 0.5f, -0.5f, 0f)));

        BuildMaterials(StationCenter(0));
        BuildSurfaceMaps(StationCenter(1));
        BuildTransparency(StationCenter(2));
        BuildLights(StationCenter(3));
        BuildManyLights(StationCenter(4));
        BuildFog(StationCenter(5));
        BuildInstancing(StationCenter(6));
        BuildRenderTextures(StationCenter(7));
        BuildClouds(StationCenter(8));
    }

    protected override void OnStationChanged(int index)
    {
        _hours = StationHours[index];
        _animateSky = false;
        _fog.Enabled.Value = index == FogStation;
        SetTimeOfDay(_hours);
        SetFlightSpeed(index == CloudStation ? _cloudFlightSpeed : 7f);
    }

    // Shift flies about three times faster, as the fly camera does by default
    private void SetFlightSpeed(float speed)
    {
        FlyCamera fly = CameraObject.GetComponent<FlyCamera>();
        fly.Speed = speed;
        fly.FastSpeed = speed * 3f;
    }

    protected override void Tick()
    {
        if (_animateSky)
        {
            _hours = (_hours + Time.DeltaTime * 1.5f) % 24f;
            SetTimeOfDay(_hours);
        }
    }

    /// <summary>Moves the sun across the sky and fades the sunlight and ambient light with it.</summary>
    private void SetTimeOfDay(float hours)
    {
        // 6 is sunrise on the right, 12 is noon behind the camera, 18 is sunset on the left.
        float angle = (hours - 6f) / 12f * MathF.PI;
        Float3 toSun = Float3.Normalize(new Float3(MathF.Cos(angle), MathF.Sin(angle) * 0.8f, -MathF.Sin(angle) * 0.5f));
        // A directional light shines along its Forward, so it faces away from where the sun is.
        _sun.Transform.Forward = -toSun;

        float height = toSun.Y;
        float day = Saturate((height + 0.05f) / 0.25f);
        float warm = Saturate(1f - height * 2.5f);
        _sun.Color = Lerp(new Color(1f, 0.97f, 0.92f, 1f), new Color(1f, 0.55f, 0.25f, 1f), warm);
        _sun.Intensity = 0.6f * day * _sunStrength;

        SampleScene.Environment.Ambient.Intensity = (0.08f + 0.92f * day) * _ambientStrength;
    }

    public override string Stats
    {
        get
        {
            RenderStats.Frame frame = RenderStats.Last;
            string common = $"{frame.DrawCalls} draw calls ({frame.DrawCommands} meshes), {frame.ShadowDrawCalls} shadow draw calls";
            return CurrentStation switch
            {
                4 => $"{_wanderers.Count(w => w.GameObject.Enabled)} point lights    {common}",
                6 => $"{_cubeCount:N0} cubes    {common}",
                _ => common,
            };
        }
    }

    public override bool HasStationControls => CurrentStation is 1 or 3 or 4 or 5 or 6 or 8;

    public override void DrawControls(Paper paper, FontFile font)
    {
        switch (CurrentStation)
        {
            case 1: SurfaceControls(paper, font); break;
            case 3: LightControls(paper, font); break;
            case 4: ManyLightControls(paper, font); break;
            case 5: FogControls(paper, font); break;
            case 6: InstancingControls(paper, font); break;
            case 8: CloudControls(paper, font); break;
        }
    }

    public override void DrawSceneControls(Paper paper, FontFile font)
    {
        RenderStats.Frame frame = RenderStats.Last;
        Header(paper, font, "Last frame");
        Label(paper, font, $"Frame time  {frame.FrameTimeMs:0.00} ms", 0);
        Label(paper, font, $"Draw calls  {frame.DrawCalls:N0}  ({frame.DrawCommands:N0} meshes)", 1);
        Label(paper, font, $"Shadow draw calls  {frame.ShadowDrawCalls:N0}  ({frame.ShadowDrawCommands:N0} meshes)", 2);
        Label(paper, font, $"Scene sync  {frame.SyncMs:0.00} ms", 3);
        Label(paper, font, $"Synced  {frame.Synced:N0}    static, left alone  {frame.Parked:N0}", 4);

        Header(paper, font, "Scene", 1);
        Slider(paper, font, "Time of day", _hours, 0f, 24f, v => { _hours = v; SetTimeOfDay(v); }, "0.0");
        Toggle(paper, font, "Run the clock", _animateSky, v => _animateSky = v);
        Slider(paper, font, "Sun strength", _sunStrength, 0f, 3f, v => { _sunStrength = v; SetTimeOfDay(_hours); });
        Slider(paper, font, "Ambient strength", _ambientStrength, 0f, 3f, v => { _ambientStrength = v; SetTimeOfDay(_hours); });

        Header(paper, font, "Distance fog", 1);
        Cycle(paper, font, "Mode", SampleScene.Environment.Fog.Mode, v => SampleScene.Environment.Fog.Mode = v);
        if (SampleScene.Environment.Fog.Mode == FogMode.Linear)
        {
            Slider(paper, font, "Start", SampleScene.Environment.Fog.Start, 0f, 100f, v => SampleScene.Environment.Fog.Start = v, "0");
            Slider(paper, font, "End", SampleScene.Environment.Fog.End, 10f, 300f, v => SampleScene.Environment.Fog.End = v, "0");
        }
        else if (SampleScene.Environment.Fog.Mode != FogMode.Off)
        {
            Slider(paper, font, "Density", SampleScene.Environment.Fog.Density, 0f, 0.1f, v => SampleScene.Environment.Fog.Density = v, "0.000");
        }
        if (SampleScene.Environment.Fog.Mode != FogMode.Off)
            Toggle(paper, font, "Use sky", SampleScene.Environment.Fog.UseSky, v => SampleScene.Environment.Fog.UseSky = v);

        Header(paper, font, "Post effects", 2);
        Toggle(paper, font, "Bloom", _bloom.Active, v => _bloom.Active = v);
        Slider(paper, font, "Bloom intensity", _bloom.Intensity, 0f, 2f, v => _bloom.Intensity.Value = v);
        Toggle(paper, font, "Volumetric fog", _fog.Enabled, v => _fog.Enabled.Value = v);
        Cycle(paper, font, "Tonemapper", _tonemapper.Mode.Value, v => _tonemapper.Mode.Value = v);
    }

    // ----------------------------------------------------------------
    //  1  Materials
    // ----------------------------------------------------------------

    private void BuildMaterials(Float3 c)
    {
        AddStation("Materials", "One Standard material per sphere. Left to right roughness goes from 0 to 1, bottom to top metallic goes from 0 to 1. Roughness and metallic come from the surface texture's green and blue channels times the factors.", c, new Float3(0f, 3f, -9f), 2.6f);

        Add(Block("Backdrop", new Float3(9f, 6f, 0.5f), _dark, c + new Float3(0f, 3f, 2f)));

        Mesh sphere = Mesh.CreateSphere(0.4f, 24, 32);
        Color albedo = new(0.8f, 0.45f, 0.1f, 1f);
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 7; col++)
            {
                float metallic = row / 4f;
                float roughness = col / 6f;
                Add(Model($"Sphere m{metallic:0.00} r{roughness:0.00}", sphere, Lit(albedo, metallic, roughness),
                    c + new Float3((col - 3f) * 1.1f, 0.6f + row * 1.0f, 0f)));
            }

        // A probe boxed to the floor and the backdrop, so the spheres reflect both where they really are. It follows
        // the time of day, one face a frame.
        var probeObject = new GameObject("Reflection Probe");
        probeObject.Transform.Position = c + new Float3(0f, 3f, -2f);
        ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
        probe.Mode = ReflectionProbeMode.Realtime;
        probe.RefreshMode = ReflectionProbeRefreshMode.EveryFrame;
        probe.TimeSlicing = ReflectionProbeTimeSlicing.IndividualFaces;
        probe.BoxSize = new Float3(16f, 6f, 7.5f);
        probe.BlendDistance = 0.5f;
        Add(probeObject);
    }

    // ----------------------------------------------------------------
    //  2  Surface maps
    // ----------------------------------------------------------------

    private Material _bricks = null!;
    private Material _panel = null!;
    private bool _normalMaps = true;
    private float _parallax = 0.04f;

    private void BuildSurfaceMaps(Float3 c)
    {
        AddStation("Surface maps", "Three panels built from generated textures. Bricks use albedo, a normal map and a parallax height map. The panel is worn paint over metal, with metallic and roughness painted into its surface texture. The last one glows from an emission map.", c, new Float3(0f, 2.5f, -8f), 1.8f);

        _bricks = Lit(Color.White, 0f, 0.9f)
            .With("_MainTex", Load<Texture2D>("Textures/Bricks"))
            .With("_NormalTex", Load<Texture2D>("Textures/Bricks Normal"))
            .With("_ParallaxMap", Load<Texture2D>("Textures/Bricks Height"));
        _bricks.SetFloat("_Parallax", _parallax);
        Add(Model("Bricks", Mesh.CreateCube(new Float3(2.5f, 2.5f, 0.2f)), _bricks, c + new Float3(-3f, 1.6f, 0f)));

        _panel = Lit(Color.White, 1f, 1f)
            .With("_MainTex", Load<Texture2D>("Textures/Worn Panel"))
            .With("_SurfaceTex", Load<Texture2D>("Textures/Worn Panel Surface"))
            .With("_NormalTex", Load<Texture2D>("Textures/Worn Panel Normal"));
        Add(Model("Worn Panel", Mesh.CreateCube(new Float3(2.5f, 2.5f, 0.2f)), _panel, c + new Float3(0f, 1.6f, 0f)));

        Add(Model("Circuit", Mesh.CreateCube(new Float3(2.5f, 2.5f, 0.2f)), Lit(new Color(0.02f, 0.05f, 0.03f, 1f), 0f, 0.4f).Emissive(Color.White, 4f).With("_EmissionTex", Load<Texture2D>("Textures/Circuit Emission")), c + new Float3(3f, 1.6f, 0f)));
    }

    private void SurfaceControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Surface maps");
        Toggle(paper, font, "Normal maps", _normalMaps, v =>
        {
            _normalMaps = v;
            Texture2D flat = Texture2D.LoadDefault(DefaultTexture.Normal);
            _bricks.SetTexture("_NormalTex", v ? Load<Texture2D>("Textures/Bricks Normal") : flat);
            _panel.SetTexture("_NormalTex", v ? Load<Texture2D>("Textures/Worn Panel Normal") : flat);
        });
        Slider(paper, font, "Parallax height", _parallax, 0f, 0.1f, v => { _parallax = v; _bricks.SetFloat("_Parallax", v); }, "0.000");
    }

    // ----------------------------------------------------------------
    //  3  Transparency
    // ----------------------------------------------------------------

    private void BuildTransparency(Float3 c)
    {
        AddStation("Transparency", "Leaf cards use the cutout shader, which drops pixels below an alpha threshold and still casts shaped shadows. The colored panes blend with the transparent shader, the flag is a double sided plane, and the sphere bends what is behind it with the refraction shader.", c, new Float3(0f, 2.5f, -9f), 1.5f);

        Add(Model("Checker Wall", Mesh.CreateCube(new Float3(12f, 4f, 0.2f)), Lit(Color.White, 0f, 0.8f).With("_MainTex", Load<Texture2D>("Textures/Checker")), c + new Float3(0f, 2f, 3f)));

        // Cutout leaves on crossed cards.
        Material leafMaterial = Lit(Color.White, 0f, 0.7f, SurfaceType.Cutout, SurfaceCull.Off).With("_MainTex", Load<Texture2D>("Textures/Leaves"));
        leafMaterial.SetFloat("_AlphaCutoff", 0.5f);
        for (int i = 0; i < 2; i++)
            Add(Model("Leaf Card", Plane(3f, 3f), leafMaterial, c + new Float3(-4f, 1.5f, 0f), new Float3(90f, 45f + i * 90f, 0f)));

        // Transparent panes in a row.
        Color[] tints = [new(1f, 0.1f, 0.05f, 0.35f), new(0.1f, 0.9f, 0.2f, 0.35f), new(0.1f, 0.3f, 1f, 0.35f)];
        for (int i = 0; i < tints.Length; i++)
            Add(Model("Glass", Mesh.CreateCube(new Float3(1.2f, 1.8f, 0.05f)), Lit(tints[i], 0f, 0.05f, SurfaceType.Transparent),
                c + new Float3(-1.2f + i * 0.5f, 1f, -0.5f + i * 0.6f), new Float3(0f, 20f, 0f)));

        // A double sided flag, readable from both sides.
        Add(Model("Flag Pole", Mesh.CreateCylinder(0.04f, 3f, 8), _dark, c + new Float3(1.8f, 1.5f, 0f)));
        Add(Model("Flag", Plane(1.6f, 1f), Lit(Color.White, 0f, 0.8f, SurfaceType.Opaque, SurfaceCull.Off).With("_MainTex", Load<Texture2D>("Textures/Flag")), c + new Float3(2.6f, 2.4f, 0f), new Float3(-90f, 0f, 0f)));

        // Refraction bends the scene behind the sphere.
        var refraction = new Material(Shader.LoadDefault(DefaultShader.Refraction));
        refraction.SetFloat("_RefractionStrength", 0.2f);
        refraction.SetColor("_Tint", new Color(0.85f, 0.95f, 1f, 1f));
        Add(Model("Refraction Sphere", Mesh.CreateSphere(0.9f, 24, 32), refraction, c + new Float3(4.5f, 1.2f, 0f)));
    }

    // ----------------------------------------------------------------
    //  4  Lights
    // ----------------------------------------------------------------

    private Light _orbitLight = null!;
    private Light _spotLight = null!;

    private void BuildLights(Float3 c)
    {
        AddStation("Lights", "It is night here, so only local lights matter. The warm point light circles the pillars casting cube map shadows in every direction, and the cool spot light sweeps across them with a cone shadow. Switch shadows off or between hard and soft.", c, new Float3(0f, 6f, -11f), 0.5f);

        for (int i = 0; i < 5; i++)
        {
            float angle = i / 5f * MathF.Tau;
            Add(Model("Pillar", Mesh.CreateCube(new Float3(0.6f, 3f, 0.6f)), _concrete, c + new Float3(MathF.Cos(angle) * 3f, 1.5f, MathF.Sin(angle) * 3f)));
        }
        Add(Model("Statue", Mesh.CreateSphere(0.8f, 24, 32), Lit(new Color(0.7f, 0.7f, 0.7f, 1f), 1f, 0.3f), c + new Float3(0f, 0.8f, 0f)));

        Color warm = new(1f, 0.6f, 0.25f, 1f);
        GameObject orbit = Add(Model("Orbit Light", Mesh.CreateSphere(0.12f, 8, 12), Unlit(new Color(4f, 2.4f, 1f, 1f)), c + new Float3(4.5f, 1.2f, 0f)));
        _orbitLight = orbit.AddComponent<Light>();
        _orbitLight.Type = LightType.Point;
        _orbitLight.Color = warm;
        _orbitLight.Range = 14f;
        _orbitLight.Intensity = 6f;
        _orbitLight.ShadowResolution = ShadowResolution._1024;
        Orbit orbiter = orbit.AddComponent<Orbit>();
        orbiter.Center = c + new Float3(0f, 1.2f, 0f);
        orbiter.Radius = 4.5f;

        var spot = new GameObject("Spot Light");
        _spotLight = spot.AddComponent<Light>();
        _spotLight.Type = LightType.Spot;
        _spotLight.Color = new Color(0.4f, 0.65f, 1f, 1f);
        _spotLight.Range = 20f;
        _spotLight.Intensity = 40f;
        _spotLight.OuterAngle = 35f;
        _spotLight.InnerAngle = 25f;
        _spotLight.ShadowResolution = ShadowResolution._1024;
        spot.Transform.Position = c + new Float3(-6f, 7f, -5f);
        Sweep sweep = spot.AddComponent<Sweep>();
        sweep.Target = c;
        sweep.Width = 4f;
        Add(spot);
    }

    private void LightControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Point light");
        Toggle(paper, font, "Point shadows", _orbitLight.CastShadows, v => _orbitLight.CastShadows = v);
        Cycle(paper, font, "Point shadow filter", _orbitLight.ShadowFilter, v => _orbitLight.ShadowFilter = v);
        Header(paper, font, "Spot light", 1);
        Toggle(paper, font, "Spot shadows", _spotLight.CastShadows, v => _spotLight.CastShadows = v);
        Cycle(paper, font, "Spot shadow filter", _spotLight.ShadowFilter, v => _spotLight.ShadowFilter = v);
        Slider(paper, font, "Spot angle", _spotLight.OuterAngle, 10f, 80f, v => { _spotLight.OuterAngle = v; _spotLight.InnerAngle = v * 0.7f; }, "0");
    }

    // ----------------------------------------------------------------
    //  5  Many lights
    // ----------------------------------------------------------------

    private readonly List<Wanderer> _wanderers = [];

    private void BuildManyLights(Float3 c)
    {
        AddStation("Many lights", "Hundreds of small point lights drift between the columns. Lights without shadows are cheap, the renderer only shades each pixel with the lights that can reach it.", c, new Float3(0f, 9f, -14f), 0f);

        for (int x = 0; x < 6; x++)
            for (int z = 0; z < 6; z++)
                Add(Model("Column", Mesh.CreateCylinder(0.3f, 2.5f, 16), _concrete, c + new Float3((x - 2.5f) * 3f, 1.25f, (z - 2.5f) * 3f)));

        Mesh bulb = Mesh.CreateSphere(0.06f, 6, 8);
        for (int i = 0; i < 256; i++)
        {
            Color color = Hsv(Sample.Rng.NextSingle(), 0.75f, 1f);
            GameObject go = Model("Wandering Light", bulb, Unlit(new Color(color.R * 4f, color.G * 4f, color.B * 4f, 1f)), c);
            Light light = go.AddComponent<Light>();
            light.Type = LightType.Point;
            light.Color = color;
            light.Range = 1.25f;
            light.Intensity = 1.5f;
            light.CastShadows = false;
            Wanderer wanderer = go.AddComponent<Wanderer>();
            wanderer.Center = c + new Float3(0f, 0.5f, 0f);
            wanderer.Extent = new Float3(9f, 1.5f, 9f);
            _wanderers.Add(wanderer);
            Add(go);
        }
    }

    private void ManyLightControls(Paper paper, FontFile font)
    {
        int active = _wanderers.Count(w => w.GameObject.Enabled);
        Header(paper, font, "Lights");
        Slider(paper, font, "Count", active, 0f, _wanderers.Count, v =>
        {
            int count = (int)v;
            for (int i = 0; i < _wanderers.Count; i++)
                _wanderers[i].GameObject.Enabled = i < count;
        }, "0");
    }

    // ----------------------------------------------------------------
    //  6  Fog
    // ----------------------------------------------------------------

    private void BuildFog(Float3 c)
    {
        AddStation("Fog", "Volumetric fog is a camera effect, switched on only at this station. Lights with a Fog Light component scatter in it, so the spot lights draw visible beams, and fog volumes thicken and tint it locally.", c, new Float3(0f, 4f, -13f), 2.5f);

        Color[] beams = [new(1f, 0.5f, 0.2f, 1f), new(0.3f, 0.6f, 1f, 1f), new(0.6f, 1f, 0.4f, 1f)];
        for (int i = 0; i < beams.Length; i++)
        {
            var go = new GameObject("Beam");
            Light spot = go.AddComponent<Light>();
            spot.Type = LightType.Spot;
            spot.Color = beams[i];
            spot.Range = 14f;
            spot.Intensity = 30f;
            spot.OuterAngle = 25f;
            spot.InnerAngle = 15f;
            go.AddComponent<FogLight>().IntensityMultiplier = 4f;
            go.Transform.Position = c + new Float3((i - 1) * 5f, 8f, 1f);
            Sweep sweep = go.AddComponent<Sweep>();
            sweep.Target = c + new Float3((i - 1) * 5f, 0f, 1f);
            sweep.Width = 2f;
            sweep.Speed = 0.4f + i * 0.15f;
            Add(go);
        }

        for (int i = 0; i < 6; i++)
            Add(Model("Block", Mesh.CreateCube(new Float3(1f, 2f + i % 3, 1f)), _concrete, c + new Float3((i - 2.5f) * 2.2f, 1f + (i % 3) * 0.5f, 3f)));

        var cloud = new GameObject("Green Fog Volume");
        FogVolume volume = cloud.AddComponent<FogVolume>();
        volume.Shape = FogVolumeShape.Sphere;
        volume.DensityMultiplier = 6f;
        volume.ColorTint = new Color(0.4f, 1f, 0.5f, 1f);
        cloud.Transform.Position = c + new Float3(-4f, 1f, -1f);
        cloud.Transform.LocalScale = new Float3(2.5f, 2.5f, 2.5f);
        Add(cloud);

        var bank = new GameObject("Ground Fog Volume");
        FogVolume ground = bank.AddComponent<FogVolume>();
        ground.Shape = FogVolumeShape.Box;
        ground.DensityMultiplier = 1.5f;
        bank.Transform.Position = c + new Float3(4f, 0.5f, 0f);
        bank.Transform.LocalScale = new Float3(4f, 0.6f, 4f);
        Add(bank);
    }

    private void FogControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Volumetric fog");
        Toggle(paper, font, "Enabled", _fog.Enabled, v => _fog.Enabled.Value = v);
        Slider(paper, font, "Density", _fog.GlobalDensity, 0f, 0.1f, v => _fog.GlobalDensity.Value = v, "0.000");
        Slider(paper, font, "Scattering", _fog.Scattering, 0f, 1f, v => _fog.Scattering.Value = v);
    }

    // ----------------------------------------------------------------
    //  7  Instancing
    // ----------------------------------------------------------------

    private enum CubeMode { Moving, Static, Immediate }

    private GameObject _cubeRoot = null!;
    private Wave _wave = null!;
    private InstancedWave _instancedWave = null!;
    private CubeMode _cubeMode = CubeMode.Immediate;
    private int _cubeCount;

    private void BuildInstancing(Float3 c)
    {
        AddStation("Instancing", "The same field of cubes drawn three ways. Graphite batches all of them into a few draws on the GPU, so the difference is the CPU. As moving GameObjects every cube syncs its transform each frame. Made static they sync once and are then left alone, so they stop. Drawn with Graphics.DrawMeshInstanced one script fills one array a frame, with a colour per cube and no GameObjects.", c, new Float3(0f, 14f, -20f), 0f);

        Mesh cube = Mesh.CreateCube(new Float3(0.4f, 0.4f, 0.4f));
        Material material = Lit(new Color(0.6f, 0.6f, 0.62f, 1f), 0.5f, 0.35f);
        const int side = 64;
        _cubeCount = side * side;

        // One GameObject per cube, grouped under a root so the whole set can be switched off.
        _cubeRoot = Add(new GameObject("Cube GameObjects"));
        var cubes = new List<Transform>();
        for (int x = 0; x < side; x++)
            for (int z = 0; z < side; z++)
            {
                GameObject go = Model("Cube", cube, material, c + new Float3((x - side * 0.5f) * 0.45f, 0.2f, (z - side * 0.5f) * 0.45f));
                go.SetParent(_cubeRoot);
                cubes.Add(go.Transform);
            }
        _wave = _cubeRoot.AddComponent<Wave>();
        _wave.Cubes = cubes;
        _wave.Center = c;

        // The same field drawn by one script each frame, with no GameObject per cube.
        GameObject instanced = Add(new GameObject("Instanced Cubes"));
        instanced.Transform.Position = c;
        _instancedWave = instanced.AddComponent<InstancedWave>();
        _instancedWave.Mesh = cube;
        _instancedWave.Material = Lit(Color.White, 0.5f, 0.35f);
        _instancedWave.Material.SetInt("_InstanceTint", 1);
        _instancedWave.Side = side;
        _instancedWave.Spacing = 0.45f;
        _cubeRoot.Enabled = false;
    }

    private void InstancingControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Draw the cubes as");
        Toggle(paper, font, $"{_cubeCount:N0} moving GameObjects", _cubeMode == CubeMode.Moving, _ => SetCubeMode(CubeMode.Moving));
        Toggle(paper, font, $"{_cubeCount:N0} static GameObjects", _cubeMode == CubeMode.Static, _ => SetCubeMode(CubeMode.Static), 1);
        Toggle(paper, font, "Graphics.DrawMeshInstanced", _cubeMode == CubeMode.Immediate, _ => SetCubeMode(CubeMode.Immediate), 2);
    }

    private void SetCubeMode(CubeMode mode)
    {
        _cubeMode = mode;
        _cubeRoot.Enabled = mode != CubeMode.Immediate;
        _wave.Enabled = mode == CubeMode.Moving;
        foreach (Transform cube in _wave.Cubes)
            cube.GameObject.IsStatic = mode == CubeMode.Static;
        _instancedWave.GameObject.Enabled = mode == CubeMode.Immediate;
    }

    // ----------------------------------------------------------------
    //  8  Render textures
    // ----------------------------------------------------------------

    private void BuildRenderTextures(Float3 c)
    {
        AddStation("Render textures", "The camera on the pole renders into a texture every frame, before the main camera, and the monitor shows that texture with an unlit material. Fly in front of the security camera to see yourself.", c, new Float3(0f, 3f, -9f), 1.8f);

        var target = new RenderTexture(512, 288, true, [TextureFormat.RGBA8]);

        GameObject pole = Add(Model("Pole", Mesh.CreateCylinder(0.06f, 3f, 8), _dark, c + new Float3(-4f, 1.5f, 3f)));
        GameObject head = Model("Security Camera", Mesh.CreateCube(new Float3(0.3f, 0.3f, 0.6f)), Lit(new Color(0.6f, 0.6f, 0.6f, 1f), 0.5f, 0.4f), c + new Float3(-4f, 3.1f, 3f));
        Camera security = head.AddComponent<Camera>();
        security.Target = target;
        security.Depth = -2;
        security.FieldOfView = 70f;
        security.Profile = new VolumeProfile();
        security.Profile.Add<Tonemapping>().Mode.Set(TonemappingMode.AgX);
        Pan pan = head.AddComponent<Pan>();
        pan.Pitch = 15f;
        pan.Center = 150f;
        head.SetParent(pole);

        Material screen = Unlit(Color.White);
        screen.SetTexture("_MainTex", target.MainTexture);
        Add(Model("Monitor Frame", Mesh.CreateCube(new Float3(3.4f, 2f, 0.15f)), _dark, c + new Float3(2f, 1.8f, 2f)));
        Add(Model("Monitor Screen", Plane(3.2f, 1.8f), screen, c + new Float3(2f, 1.8f, 1.92f), new Float3(-90f, 0f, 0f)));

        for (int i = 0; i < 5; i++)
        {
            Color color = Hsv(i / 5f, 0.85f, 0.7f);
            GameObject shape = Add(Model("Shape", i % 2 == 0 ? Mesh.CreateCube(Float3.One) : Mesh.CreateSphere(0.5f, 16, 24), Lit(color, 0f, 0.4f), c + new Float3(-6f + i * 1.6f, 0.5f, -3f)));
            shape.AddComponent<Spin>().Speed = new Float3(0f, 30f + i * 10f, 0f);
        }
    }

    // ----------------------------------------------------------------
    //  9  Clouds
    // ----------------------------------------------------------------

    private void BuildClouds(Float3 c)
    {
        AddStation("Clouds", "Clouds cover the whole scene, so they are over every station. They are ray marched at a reduced resolution, shaped by a coverage map and noise, and lit from a sun map so each step reads its light once.", c, new Float3(0f, 2f, -6f), 8f);

        var clouds = new GameObject("Clouds");
        _clouds = clouds.AddComponent<VolumetricClouds>();
        _clouds.Layers =
        [
            new CloudLayer { Style = CloudLayerStyle.Cirrus, Altitude = 9000f, Coverage = 0.5f, Opacity = 0.6f },
            new CloudLayer { Enabled = false, Style = CloudLayerStyle.Stratus, Altitude = 5000f, Coverage = 0.45f, Opacity = 0.6f, WindMultiplier = 1.5f },
        ];
        Add(clouds);
    }

    private void CloudControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Clouds");
        Slider(paper, font, "Flight speed", _cloudFlightSpeed, 7f, 3000f, v => { _cloudFlightSpeed = v; SetFlightSpeed(v); }, "0");
        Slider(paper, font, "Coverage", _clouds.Coverage, 0f, 1f, v => _clouds.Coverage = v);
        Slider(paper, font, "Density", _clouds.Density, 0f, 4f, v => _clouds.Density = v);
        Slider(paper, font, "Erosion", _clouds.Erosion, 0f, 1f, v => _clouds.Erosion = v);
        Slider(paper, font, "Puff strength", _clouds.PuffStrength, 0f, 1f, v => _clouds.PuffStrength = v);
        Slider(paper, font, "Puff scale", _clouds.PuffScale, 0.5f, 10f, v => _clouds.PuffScale = v, "0.0");
        Toggle(paper, font, "Temporal upscale", _clouds.TemporalUpscale, v => _clouds.TemporalUpscale = v);
        Slider(paper, font, "Reach", _clouds.Reach, 8000f, 128000f, v => _clouds.Reach = v, "0");
        Toggle(paper, font, "March toward the sun", _clouds.MarchToSun, v => _clouds.MarchToSun = v);
        if (_clouds.MarchToSun)
            Slider(paper, font, "Light samples", _clouds.LightSamples, 1f, 8f, v => _clouds.LightSamples = (int)MathF.Round(v), "0");
        Slider(paper, font, "Altitude", _clouds.Altitude, 200f, 4000f, v => _clouds.Altitude = v, "0");
        Slider(paper, font, "Thickness", _clouds.Thickness, 100f, 3000f, v => _clouds.Thickness = v, "0");
        Slider(paper, font, "Wind speed", _clouds.WindSpeed, 0f, 200f, v => _clouds.WindSpeed = v, "0");
        Cycle(paper, font, "Resolution", _clouds.Resolution, v => _clouds.Resolution = v);
        Toggle(paper, font, "Cirrus layer", _clouds.Layers[0].Enabled, v => _clouds.Layers[0].Enabled = v);
        Toggle(paper, font, "Stratus layer", _clouds.Layers[1].Enabled, v => _clouds.Layers[1].Enabled = v);
    }
}

/// <summary>Turns its GameObject at a fixed rate in local space.</summary>
public sealed class Spin : Component
{
    public Float3 Speed = new(0f, 90f, 0f);

    public override void Update() => Transform.Rotate(Speed * Time.DeltaTime);
}

/// <summary>Flies around a horizontal circle.</summary>
public sealed class Orbit : Component
{
    public Float3 Center;
    public float Radius = 3f;
    public float Speed = 0.6f;

    private float _angle;

    public override void Update()
    {
        _angle += Speed * Time.DeltaTime;
        Transform.Position = Center + new Float3(MathF.Cos(_angle) * Radius, MathF.Sin(_angle * 2f) * 0.5f, MathF.Sin(_angle) * Radius);
    }
}

/// <summary>Points its light at a target that slides from side to side.</summary>
public sealed class Sweep : Component
{
    public Float3 Target;
    public float Width = 3f;
    public float Speed = 0.5f;

    public override void Update()
        => Transform.LookAt(Target + new Float3(MathF.Sin(Time.TimeSinceStartup * Speed) * Width, 0f, 0f));
}

/// <summary>Turns a camera from side to side around a center heading.</summary>
public sealed class Pan : Component
{
    public float Center;
    public float Pitch;
    public float Range = 40f;

    public override void Update()
        => Transform.LocalEulerAngles = new Float3(Pitch, Center + MathF.Sin(Time.TimeSinceStartup * 0.4f) * Range, 0f);
}

/// <summary>Drifts smoothly around inside a box, picking a new destination whenever it arrives.</summary>
public sealed class Wanderer : Component
{
    public Float3 Center;
    public Float3 Extent = new(5f, 1f, 5f);

    private Float3 _target;

    public override void Start()
    {
        Transform.Position = Pick();
        _target = Pick();
    }

    private Float3 Pick() => Center + new Float3(
        (Sample.Rng.NextSingle() * 2f - 1f) * Extent.X,
        Sample.Rng.NextSingle() * Extent.Y,
        (Sample.Rng.NextSingle() * 2f - 1f) * Extent.Z);

    public override void Update()
    {
        Float3 toTarget = _target - Transform.Position;
        float distance = Float3.Length(toTarget);
        if (distance < 0.1f)
        {
            _target = Pick();
            return;
        }
        Transform.Position += toTarget / distance * MathF.Min(distance, 1.5f * Time.DeltaTime);
    }
}

/// <summary>
/// Moves a whole field of cubes drawn with Graphics.DrawMeshInstanced: each frame it fills one array of transforms and
/// colours, so no GameObject exists per cube.
/// </summary>
public sealed class InstancedWave : Component
{
    public Mesh Mesh = null!;
    public Material Material = null!;
    public int Side = 64;
    public float Spacing = 0.45f;

    private Float4x4[] _transforms = [];
    private Color[] _colors = [];

    public override void Update()
    {
        if (_transforms.Length != Side * Side)
        {
            _transforms = new Float4x4[Side * Side];
            _colors = new Color[Side * Side];
        }

        float t = Time.TimeSinceStartup;
        Float3 center = Transform.Position;
        int i = 0;
        for (int x = 0; x < Side; x++)
            for (int z = 0; z < Side; z++)
            {
                float px = (x - Side * 0.5f) * Spacing, pz = (z - Side * 0.5f) * Spacing;
                float wave = MathF.Sin(MathF.Sqrt(px * px + pz * pz) * 0.6f - t * 2.5f) * 0.5f + 0.5f;
                _transforms[i] = Float4x4.CreateTranslation(center + new Float3(px, 0.2f + wave * 1.5f, pz));
                _colors[i++] = Hsv(0.55f + wave * 0.4f, 0.7f, 0.9f);
            }
        Graphics.DrawMeshInstanced(Mesh, Material, _transforms, _colors, scene: GameObject.Scene);
    }
}

/// <summary>Moves a field of cubes up and down in rings spreading from the center.</summary>
public sealed class Wave : Component
{
    public List<Transform> Cubes = [];
    public Float3 Center;

    public override void Update()
    {
        float t = Time.TimeSinceStartup;
        foreach (Transform cube in Cubes)
        {
            Float3 p = cube.Position;
            float d = MathF.Sqrt((p.X - Center.X) * (p.X - Center.X) + (p.Z - Center.Z) * (p.Z - Center.Z));
            p.Y = Center.Y + 0.2f + (MathF.Sin(d * 0.6f - t * 2.5f) * 0.5f + 0.5f) * 1.5f;
            cube.Position = p;
        }
    }
}
