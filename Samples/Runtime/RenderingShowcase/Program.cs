// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Rendering Showcase
//
// A row of stations, each built around a part of the renderer:
//   1  Materials        the Standard shader across metallic and roughness
//   2  Surface maps     albedo, normal, parallax, surface and emission textures
//   3  Decals           projected albedo, normal and mask textures on walls, floors and curved surfaces
//   4  Transparency     cutout, transparent, double sided and refraction shaders
//   5  Lights           point and spot lights with hard, soft and contact hardening shadows
//   6  Area lights      rectangle, disk and tube lights, their shapes showing in reflections
//   7  Many lights      hundreds of small point lights without shadows
//   8  Fog              volumetric fog, fog volumes and lights that scatter in it
//   9  Instancing       thousands of cubes as moving, static or immediate instanced draws
//   0  Render textures  a second camera drawing into a texture shown on a monitor
//   -  Clouds           volumetric clouds and flat cloud layers over the whole scene
//
// Textures and materials load from the sample's Assets folder.
//
// Controls:
//   1 to 9, 0, -  Jump to a station
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
    private const int SurfaceStation = 1, DecalStation = 2, LightStation = 4, AreaLightStation = 5, ManyLightStation = 6;
    private const int FogStation = 7, InstancingStation = 8, CloudStation = 10, StationCount = 11;

    // The time of day each station is shown at, in hours.
    private static readonly float[] StationHours = [14f, 15f, 13f, 13f, 22f, 22f, 22f, 23f, 11f, 14f, 16f];

    private Light _sun = null!;
    private VolumetricFog _fog = null!;
    private VolumetricClouds _clouds = null!;
    private float _cloudFlightSpeed = 300f;
    private Bloom _bloom = null!;
    private Tonemapping _tonemapping = null!;

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
        Add(sun);

        _fog = MainCamera.Profile!.Add<VolumetricFog>(overrideAll: true);
        _fog.Enabled.Value = false;
        _fog.GlobalDensity.Value = 0.02f;
        _fog.MaxDistance.Value = 60f;
        _bloom = MainCamera.Profile.Get<Bloom>()!;
        _tonemapping = MainCamera.Profile.Get<Tonemapping>()!;
        SampleScene.Environment.Fog.Mode = FogMode.Off;
        SampleScene.Environment.Ambient.Mode = AmbientMode.Uniform;

        float length = StationSpacing * StationCount + 30f;
        Add(Block("Floor", new Float3(length, 1f, 60f), Floor(length, 60f), new Float3(StationSpacing * (StationCount - 1) * 0.5f, -0.5f, 0f)));

        BuildMaterials(StationCenter(0));
        BuildSurfaceMaps(StationCenter(1));
        BuildDecals(StationCenter(2));
        BuildTransparency(StationCenter(3));
        BuildLights(StationCenter(4));
        BuildAreaLights(StationCenter(5));
        BuildManyLights(StationCenter(6));
        BuildFog(StationCenter(7));
        BuildInstancing(StationCenter(8));
        BuildRenderTextures(StationCenter(9));
        BuildClouds(StationCenter(10));
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
        if (_cycleTint)
        {
            Color tint = Hsv(Time.TimeSinceStartup * 0.08f % 1f, 0.75f, 0.9f);
            _splat.Color = new Color(tint.R, tint.G, tint.B, _splat.Color.A);
        }
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
        _sun.Intensity = 100000f * day * _sunStrength;

        SampleScene.Environment.Ambient.Intensity = (1600f + 18400f * day) * _ambientStrength;
    }

    public override string Stats
    {
        get
        {
            RenderStats.Frame frame = RenderStats.Last;
            string common = $"{frame.DrawCalls} draw calls ({frame.DrawCommands} meshes), {frame.ShadowDrawCalls} shadow draw calls";
            return CurrentStation switch
            {
                ManyLightStation => $"{_wanderers.Count(w => w.GameObject.Enabled)} point lights    {common}",
                InstancingStation => $"{_cubeCount:N0} cubes    {common}",
                _ => common,
            };
        }
    }

    public override bool HasStationControls => CurrentStation is SurfaceStation or DecalStation or LightStation or AreaLightStation
        or ManyLightStation or FogStation or InstancingStation or CloudStation;

    public override void DrawControls(Paper paper, FontFile font)
    {
        switch (CurrentStation)
        {
            case SurfaceStation: SurfaceControls(paper, font); break;
            case DecalStation: DecalControls(paper, font); break;
            case LightStation: LightControls(paper, font); break;
            case AreaLightStation: AreaLightControls(paper, font); break;
            case ManyLightStation: ManyLightControls(paper, font); break;
            case FogStation: FogControls(paper, font); break;
            case InstancingStation: InstancingControls(paper, font); break;
            case CloudStation: CloudControls(paper, font); break;
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
        Cycle(paper, font, "Tonemapper", _tonemapping.Mode.Value, v => _tonemapping.Mode.Value = v);
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
            .With("base_color_map", Load<Texture2D>("Textures/Bricks"))
            .With("geometry_normal_map", Load<Texture2D>("Textures/Bricks Normal"))
            .With("geometry_height_map", Load<Texture2D>("Textures/Bricks Height"));
        _bricks.SetFloat("height_scale", _parallax);
        Add(Model("Bricks", Mesh.CreateCube(new Float3(2.5f, 2.5f, 0.2f)), _bricks, c + new Float3(-3f, 1.6f, 0f)));

        _panel = Lit(Color.White, 1f, 1f)
            .With("base_color_map", Load<Texture2D>("Textures/Worn Panel"))
            .With("specular_roughness_map", Load<Texture2D>("Textures/Worn Panel Surface"))
            .With("base_metalness_map", Load<Texture2D>("Textures/Worn Panel Surface"))
            .With("geometry_normal_map", Load<Texture2D>("Textures/Worn Panel Normal"));
        _panel.SetTextureChannel("specular_roughness_map", 1);
        _panel.SetTextureChannel("base_metalness_map", 2);
        Add(Model("Worn Panel", Mesh.CreateCube(new Float3(2.5f, 2.5f, 0.2f)), _panel, c + new Float3(0f, 1.6f, 0f)));

        Add(Model("Circuit", Mesh.CreateCube(new Float3(2.5f, 2.5f, 0.2f)), Lit(new Color(0.02f, 0.05f, 0.03f, 1f), 0f, 0.4f).Emissive(Color.White, 80000f).With("emission_color_map", Load<Texture2D>("Textures/Circuit Emission")), c + new Float3(3f, 1.6f, 0f)));
    }

    private void SurfaceControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Surface maps");
        Toggle(paper, font, "Normal maps", _normalMaps, v =>
        {
            _normalMaps = v;
            Texture2D flat = Texture2D.LoadDefault(DefaultTexture.Normal);
            _bricks.SetTexture("geometry_normal_map", v ? Load<Texture2D>("Textures/Bricks Normal") : flat);
            _panel.SetTexture("geometry_normal_map", v ? Load<Texture2D>("Textures/Worn Panel Normal") : flat);
        });
        Slider(paper, font, "Parallax height", _parallax, 0f, 0.1f, v => { _parallax = v; _bricks.SetFloat("height_scale", v); }, "0.000");
    }

    // ----------------------------------------------------------------
    //  3  Decals
    // ----------------------------------------------------------------

    private Decal _splat = null!;
    private Decal _crater = null!;
    private Decal _hazard = null!;
    private bool _cycleTint = true;

    private void BuildDecals(Float3 c)
    {
        AddStation("Decals", "Decals project textures onto whatever is inside their box. The splat takes its colour from the decal's tint, the crater dents the wall with a normal map, the puddle makes the ground glossy through a mask texture, and the hazard sign slides across the curved pillar, wrapping around it and fading where the surface turns away. Deepen its box to reach the wall behind.", c, new Float3(0f, 3f, -8f), 1.2f);

        Material plaster = Lit(new Color(0.75f, 0.73f, 0.7f, 1f), 0f, 0.8f);
        Add(Block("Decal Wall", new Float3(10f, 4f, 0.4f), plaster, c + new Float3(0f, 2f, 2f)));
        Add(Model("Decal Pillar", Mesh.CreateCylinder(0.6f, 4f, 48), plaster, c + new Float3(2.5f, 2f, 0.6f)));
        Add(Model("Decal Ball", Mesh.CreateSphere(0.7f, 32, 48), Lit(new Color(0.3f, 0.32f, 0.36f, 1f), 0.6f, 0.4f), c + new Float3(-3.2f, 0.7f, -1f)));

        // A decal's Forward points out of the surfaces it lands on: back along -Z for the wall's front, up for the floor
        Quaternion fromWall = Quaternion.LookRotation(-Float3.UnitZ, Float3.UnitY);
        Quaternion fromFloor = Quaternion.LookRotation(Float3.UnitY, Float3.UnitZ);

        _splat = AddDecal("Paint Splat", c + new Float3(-2.2f, 2.3f, 1.8f), fromWall, new Float3(2.2f, 2.2f, 0.6f), Load<Texture2D>("Textures/Decal Splat"));
        _splat.Color = new Color(0.9f, 0.2f, 0.3f, 1f);

        _crater = AddDecal("Crater", c + new Float3(0.3f, 2.1f, 1.8f), fromWall, new Float3(2f, 2f, 0.6f), Load<Texture2D>("Textures/Decal Crater"));
        _crater.Normal = Load<Texture2D>("Textures/Decal Crater Normal");
        _crater.NormalStrength = 1.5f;

        Decal puddle = AddDecal("Puddle", c + new Float3(-0.8f, 0f, -1.4f), fromFloor, new Float3(3.5f, 2.6f, 0.4f), Load<Texture2D>("Textures/Decal Puddle"));
        puddle.Mask = Load<Texture2D>("Textures/Decal Puddle Mask");

        _hazard = AddDecal("Hazard Sign", c + new Float3(2.2f, 1.6f, -0.2f), fromWall, new Float3(1.6f, 1.6f, 3f), Load<Texture2D>("Textures/Decal Hazard"));
        _hazard.AngleFade = 0.3f;
        Slide slide = _hazard.AddComponent<Slide>();
        slide.Origin = _hazard.Transform.Position;
        slide.Offset = new Float3(1.2f, 0.3f, 0f);
    }

    private Decal AddDecal(string name, Float3 position, Quaternion rotation, Float3 size, Texture2D albedo)
    {
        var go = new GameObject(name);
        go.Transform.Position = position;
        go.Transform.Rotation = rotation;
        Decal decal = go.AddComponent<Decal>();
        decal.Size = size;
        decal.Albedo = albedo;
        Add(go);
        return decal;
    }

    private void DecalControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Paint splat");
        Toggle(paper, font, "Cycle the tint", _cycleTint, v => _cycleTint = v);
        Slider(paper, font, "Coverage", _splat.Color.A, 0f, 1f, v => _splat.Color = new Color(_splat.Color.R, _splat.Color.G, _splat.Color.B, v));
        Header(paper, font, "Crater", 1);
        Slider(paper, font, "Normal strength", _crater.NormalStrength, 0f, 2f, v => _crater.NormalStrength = v);
        Header(paper, font, "Hazard sign", 2);
        Toggle(paper, font, "Slide", _hazard.GetComponent<Slide>()!.Enabled, v => _hazard.GetComponent<Slide>()!.Enabled = v, 1);
        Slider(paper, font, "Angle fade", _hazard.AngleFade, 0f, 0.95f, v => _hazard.AngleFade = v);
        Slider(paper, font, "Box depth", _hazard.Size.Z, 0.2f, 4f, v => _hazard.Size = new Float3(_hazard.Size.X, _hazard.Size.Y, v), "0.0");
    }

    // ----------------------------------------------------------------
    //  4  Transparency
    // ----------------------------------------------------------------

    private void BuildTransparency(Float3 c)
    {
        AddStation("Transparency", "Leaf cards use the cutout shader, which drops pixels below an alpha threshold and still casts shaped shadows. The colored panes blend with the transparent shader, the flag is a double sided plane, and the sphere bends what is behind it with the refraction shader.", c, new Float3(0f, 2.5f, -9f), 1.5f);

        Add(Model("Checker Wall", Mesh.CreateCube(new Float3(12f, 4f, 0.2f)), Lit(Color.White, 0f, 0.8f).With("base_color_map", Load<Texture2D>("Textures/Checker")), c + new Float3(0f, 2f, 3f)));

        // Cutout leaves on crossed cards.
        Material leafMaterial = Lit(Color.White, 0f, 0.7f, SurfaceType.Cutout, SurfaceCull.Off).With("base_color_map", Load<Texture2D>("Textures/Leaves"));
        leafMaterial.SetFloat("alpha_cutoff", 0.5f);
        leafMaterial.SetTexture("geometry_opacity_map", Load<Texture2D>("Textures/Leaves"));
        leafMaterial.SetTextureChannel("geometry_opacity_map", 3);
        for (int i = 0; i < 2; i++)
            Add(Model("Leaf Card", Plane(3f, 3f), leafMaterial, c + new Float3(-4f, 1.5f, 0f), new Float3(90f, 45f + i * 90f, 0f)));

        // Transparent panes in a row.
        Color[] tints = [new(1f, 0.1f, 0.05f, 0.35f), new(0.1f, 0.9f, 0.2f, 0.35f), new(0.1f, 0.3f, 1f, 0.35f)];
        for (int i = 0; i < tints.Length; i++)
            Add(Model("Glass", Mesh.CreateCube(new Float3(1.2f, 1.8f, 0.05f)), Lit(tints[i], 0f, 0.05f, SurfaceType.Transparent),
                c + new Float3(-1.2f + i * 0.5f, 1f, -0.5f + i * 0.6f), new Float3(0f, 20f, 0f)));

        // A double sided flag, readable from both sides.
        Add(Model("Flag Pole", Mesh.CreateCylinder(0.04f, 3f, 8), _dark, c + new Float3(1.8f, 1.5f, 0f)));
        Add(Model("Flag", Plane(1.6f, 1f), Lit(Color.White, 0f, 0.8f, SurfaceType.Opaque, SurfaceCull.Off).With("base_color_map", Load<Texture2D>("Textures/Flag")), c + new Float3(2.6f, 2.4f, 0f), new Float3(-90f, 0f, 0f)));

        // Refraction bends the scene behind the sphere.
        var refraction = new Material(Shader.LoadDefault(DefaultShader.Refraction));
        refraction.SetFloat("_RefractionStrength", 0.2f);
        refraction.SetColor("_Tint", new Color(0.85f, 0.95f, 1f, 1f));
        Add(Model("Refraction Sphere", Mesh.CreateSphere(0.9f, 24, 32), refraction, c + new Float3(4.5f, 1.2f, 0f)));
    }

    // ----------------------------------------------------------------
    //  5  Lights
    // ----------------------------------------------------------------

    private Light _orbitLight = null!;
    private Light _spotLight = null!;
    private GameObject _orbitBulb = null!;

    private void BuildLights(Float3 c)
    {
        AddStation("Lights", "It is night here, so only local lights matter. The warm point light circles the pillars casting cube map shadows in every direction, and the cool spot light sweeps across them with a cone shadow. Both use contact hardening shadows: sharp where a pillar meets the floor and softer the further the shadow falls from it, more so the bigger the light. Switch to hard or tent filtering to compare.", c, new Float3(0f, 6f, -11f), 0.5f);

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
        _orbitLight.Intensity = 950000f;
        _orbitLight.ShadowResolution = ShadowResolution._1024;
        _orbitLight.ShadowFilter = ShadowFilter.ContactHardening;
        _orbitLight.SourceRadius = 0.12f;
        _orbitBulb = orbit;
        Orbit orbiter = orbit.AddComponent<Orbit>();
        orbiter.Center = c + new Float3(0f, 1.2f, 0f);
        orbiter.Radius = 4.5f;

        var spot = new GameObject("Spot Light");
        _spotLight = spot.AddComponent<Light>();
        _spotLight.Type = LightType.Spot;
        _spotLight.Color = new Color(0.4f, 0.65f, 1f, 1f);
        _spotLight.Range = 20f;
        _spotLight.Intensity = 6300000f;
        _spotLight.OuterAngle = 35f;
        _spotLight.InnerAngle = 25f;
        _spotLight.ShadowResolution = ShadowResolution._1024;
        _spotLight.ShadowFilter = ShadowFilter.ContactHardening;
        _spotLight.SourceRadius = 0.2f;
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
        Slider(paper, font, "Point light size", _orbitLight.SourceRadius, 0.01f, 0.5f, v =>
        {
            _orbitLight.SourceRadius = v;
            _orbitBulb.Transform.LocalScale = new Float3(v / 0.12f);
        });
        Header(paper, font, "Spot light", 1);
        Toggle(paper, font, "Spot shadows", _spotLight.CastShadows, v => _spotLight.CastShadows = v);
        Cycle(paper, font, "Spot shadow filter", _spotLight.ShadowFilter, v => _spotLight.ShadowFilter = v);
        Slider(paper, font, "Spot light size", _spotLight.SourceRadius, 0.01f, 0.6f, v => _spotLight.SourceRadius = v, "0.00", 1);
        Slider(paper, font, "Spot angle", _spotLight.OuterAngle, 10f, 80f, v => { _spotLight.OuterAngle = v; _spotLight.InnerAngle = v * 0.7f; }, "0");
    }

    // ----------------------------------------------------------------
    //  6  Area lights
    // ----------------------------------------------------------------

    private Light _rectLight = null!, _diskLight = null!, _tubeLight = null!;
    private GameObject _rectPanel = null!, _diskPanel = null!, _tubePanel = null!;

    private void BuildAreaLights(Float3 c)
    {
        AddStation("Area lights", "Lights with a shape instead of a point. The rectangle stands behind the spheres like a softbox, the disk hangs above them and the tube lies along the front. Each lights the scene by its whole surface, so the polished floor and spheres reflect the actual shape, stretched and blurred by their roughness. Area lights cast no shadows yet.", c, new Float3(0f, 3.5f, -9f), 1f);

        Add(Block("Polished Floor", new Float3(14f, 0.04f, 10f), Lit(new Color(0.03f, 0.03f, 0.035f, 1f), 0f, 0.12f), c + new Float3(0f, 0.02f, 0f)));
        float[] roughness = [0.05f, 0.25f, 0.5f, 0.8f];
        for (int i = 0; i < roughness.Length; i++)
        {
            Add(Model("Metal Sphere", Mesh.CreateSphere(0.55f, 32, 48), Lit(new Color(0.95f, 0.95f, 0.95f, 1f), 1f, roughness[i]), c + new Float3((i - 1.5f) * 1.6f, 0.6f, 0.6f)));
            Add(Model("Plastic Sphere", Mesh.CreateSphere(0.4f, 32, 48), Lit(new Color(0.8f, 0.15f, 0.12f, 1f), 0f, roughness[i]), c + new Float3((i - 1.5f) * 1.6f, 0.45f, -1f)));
        }

        // The rectangle and the disk shine along Forward, the tube lies along it
        (_rectLight, _rectPanel) = AddAreaLight(LightType.Rect, new Color(1f, 0.85f, 0.7f, 1f), c + new Float3(0f, 1.6f, 3.5f), Quaternion.LookRotation(-Float3.UnitZ, Float3.UnitY), Mesh.CreateCube(Float3.One));
        _rectLight.Width = 4f;
        _rectLight.Height = 1.6f;

        (_diskLight, _diskPanel) = AddAreaLight(LightType.Disk, new Color(0.6f, 0.8f, 1f, 1f), c + new Float3(-3.5f, 3.2f, -0.2f), Quaternion.LookRotation(-Float3.UnitY, Float3.UnitZ), Mesh.CreateCylinder(0.5f, 1f, 48));
        _diskLight.Width = 1.6f;

        (_tubeLight, _tubePanel) = AddAreaLight(LightType.Tube, new Color(1f, 0.35f, 0.75f, 1f), c + new Float3(0.5f, 0.3f, -2.6f), Quaternion.LookRotation(Float3.UnitX, Float3.UnitY), Mesh.CreateCylinder(0.5f, 1f, 24));
        _tubeLight.Length = 5f;
        _tubeLight.SourceRadius = 0.05f;
        UpdateAreaPanels();
    }

    // A light and the unlit shape that shows where it is, kept apart so the shape can be scaled to the light's size
    private (Light, GameObject) AddAreaLight(LightType type, Color color, Float3 position, Quaternion rotation, Mesh shape)
    {
        var go = new GameObject($"{type} Light");
        go.Transform.Position = position;
        go.Transform.Rotation = rotation;
        Light light = go.AddComponent<Light>();
        light.Type = type;
        light.Color = color;
        light.Intensity = 950000f;
        light.Range = 12f;
        Add(go);
        GameObject panel = Add(Model($"{type} Shape", shape, Unlit(new Color(color.R * 6f, color.G * 6f, color.B * 6f, 1f)), position));
        return (light, panel);
    }

    private void UpdateAreaPanels()
    {
        _rectPanel.Transform.Rotation = _rectLight.Transform.Rotation;
        _rectPanel.Transform.LocalScale = new Float3(_rectLight.Width, _rectLight.Height, 0.02f);
        _diskPanel.Transform.LocalScale = new Float3(_diskLight.Width, 0.02f, _diskLight.Width);
        _tubePanel.Transform.LocalEulerAngles = new Float3(0f, 0f, 90f);
        float thickness = Maths.Max(_tubeLight.SourceRadius * 2f, 0.02f);
        _tubePanel.Transform.LocalScale = new Float3(thickness, _tubeLight.Length, thickness);
        foreach ((Light light, GameObject panel) in new[] { (_rectLight, _rectPanel), (_diskLight, _diskPanel), (_tubeLight, _tubePanel) })
            panel.Enabled = light.GameObject.Enabled;
    }

    private void AreaLightControls(Paper paper, FontFile font)
    {
        Header(paper, font, "Rectangle");
        Toggle(paper, font, "On", _rectLight.GameObject.Enabled, v => { _rectLight.GameObject.Enabled = v; UpdateAreaPanels(); });
        Slider(paper, font, "Width", _rectLight.Width, 0.2f, 8f, v => { _rectLight.Width = v; UpdateAreaPanels(); }, "0.0");
        Slider(paper, font, "Height", _rectLight.Height, 0.2f, 3f, v => { _rectLight.Height = v; UpdateAreaPanels(); }, "0.0");
        Slider(paper, font, "Intensity", _rectLight.Intensity, 0f, 3000000f, v => _rectLight.Intensity = v, "0");

        Header(paper, font, "Disk", 1);
        Toggle(paper, font, "On", _diskLight.GameObject.Enabled, v => { _diskLight.GameObject.Enabled = v; UpdateAreaPanels(); }, 1);
        Slider(paper, font, "Diameter", _diskLight.Width, 0.2f, 4f, v => { _diskLight.Width = v; UpdateAreaPanels(); }, "0.0", 1);
        Slider(paper, font, "Intensity", _diskLight.Intensity, 0f, 3000000f, v => _diskLight.Intensity = v, "0", 1);

        Header(paper, font, "Tube", 2);
        Toggle(paper, font, "On", _tubeLight.GameObject.Enabled, v => { _tubeLight.GameObject.Enabled = v; UpdateAreaPanels(); }, 2);
        Slider(paper, font, "Length", _tubeLight.Length, 0.2f, 8f, v => { _tubeLight.Length = v; UpdateAreaPanels(); }, "0.0", 2);
        Slider(paper, font, "Radius", _tubeLight.SourceRadius, 0f, 0.3f, v => { _tubeLight.SourceRadius = v; UpdateAreaPanels(); }, "0.00", 2);
        Slider(paper, font, "Intensity", _tubeLight.Intensity, 0f, 3000000f, v => _tubeLight.Intensity = v, "0", 2);
    }

    // ----------------------------------------------------------------
    //  7  Many lights
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
            light.Intensity = 240000f;
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
    //  8  Fog
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
            spot.Intensity = 4700000f;
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
    //  9  Instancing
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
    //  0  Render textures
    // ----------------------------------------------------------------

    private void BuildRenderTextures(Float3 c)
    {
        AddStation("Render textures", "The camera on the pole renders into a texture every frame, before the main camera, and the monitor shows that texture with an unlit material. The feed is HDR light at a fixed exposure, and the monitor gives back the light the camera saw, so the main camera exposes and tonemaps it once and it matches the view around it. Fly in front of the security camera to see yourself.", c, new Float3(0f, 3f, -9f), 1.8f);

        // Scene light pre-exposed by the security camera, kept linear and unclamped
        var target = new RenderTexture(512, 288, true, [TextureFormat.RGBA16Float]);

        GameObject pole = Add(Model("Pole", Mesh.CreateCylinder(0.06f, 3f, 8), _dark, c + new Float3(-4f, 1.5f, 3f)));
        GameObject head = Model("Security Camera", Mesh.CreateCube(new Float3(0.3f, 0.3f, 0.6f)), Lit(new Color(0.6f, 0.6f, 0.6f, 1f), 0.5f, 0.4f), c + new Float3(-4f, 3.1f, 3f));
        Camera security = head.AddComponent<Camera>();
        // No tonemapping or auto exposure, so the texture holds scene light times a known exposure
        security.Profile = new VolumeProfile { Name = "Security Camera Profile" };
        security.EV100 = LegacyLightUnits.EV100;
        security.Target = target;
        security.Depth = -2;
        security.FieldOfView = 70f;
        Pan pan = head.AddComponent<Pan>();
        pan.Pitch = 15f;
        pan.Center = 150f;
        head.SetParent(pole);

        // One over the security camera's exposure turns its texels back into the nits it saw
        Material screen = Unlit(Color.White);
        screen.SetTexture("base_color_map", target.MainTexture);
        screen.SetFloat("emission_luminance", 1.2f * MathF.Pow(2f, security.EV100));
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
    //  -  Clouds
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

/// <summary>Slides back and forth between its origin and the origin plus an offset.</summary>
public sealed class Slide : Component
{
    public Float3 Origin;
    public Float3 Offset;
    public float Speed = 0.5f;

    public override void Update()
        => Transform.Position = Origin + Offset * MathF.Sin(Time.TimeSinceStartup * Speed);
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
