using System;
using System.IO;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

using Silk.NET.Input;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class HelmetScene : DemoScene
{
    private const double HelmetSize = 2.4;

    private readonly PbrModel _model;
    private readonly RenderableHandle[] _helmet;
    private readonly RenderableHandle _bulb;
    private readonly Double3 _helmetBase;
    private readonly LightHandle _spot;
    private readonly LightHandle _orbit;
    private LightDesc _spotDesc;
    private LightDesc _orbitDesc;
    private bool _spotOn = true;

    public bool SpotOn
    {
        get => _spotOn;
        set => _spotOn = value;
    }

    public HelmetScene(Demo demo) : base(demo)
    {
        _model = PbrModel.Load(demo, Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "DamagedHelmet.glb"), emissionNits: 111112f);
        // The helmet's centre sits 1.75 above the plinth's origin
        _helmetBase = new Double3(0, 1.75 - _model.Bounds.Extents.Y * _model.Scale(HelmetSize), 0);
        _helmet = _model.AddTo(World, _helmetBase, HelmetSize);

        Add(Demo.Cube, Pbr(new Float3(0.5f, 0.5f, 0.52f), 0f, 0.85f, grid: 1f), new Double3(0, -0.2, 0), new Double3(80, 0.4, 80));
        Add(Demo.Cylinder, Pbr(new Float3(0.12f, 0.12f, 0.14f), 0.9f, 0.35f), new Double3(0, 0.15, 0), new Double3(3.4, 0.3, 3.4));

        Float3[] gold = [new(1.0f, 0.78f, 0.34f), new(1.0f, 0.78f, 0.34f), new(1.0f, 0.78f, 0.34f), new(0.95f, 0.64f, 0.54f), new(0.85f, 0.1f, 0.1f), new(0.1f, 0.35f, 0.85f)];
        float[] metal = [1f, 1f, 1f, 1f, 0f, 0f];
        float[] rough = [0.05f, 0.25f, 0.5f, 0.8f, 0.1f, 0.6f];
        for (int i = 0; i < gold.Length; i++)
            Add(Demo.Sphere, Pbr(gold[i], metal[i], rough[i]), new Double3(-6.25 + i * 2.5, 0.8, -4), new Double3(1.6, 1.6, 1.6));

        LightHandle sun = World.AddLight(new LightDesc
        {
            Name = "Sun",
            Type = LightType.Directional,
            Direction = Float3.Normalize(new Float3(0.55f, -0.65f, 0.35f)),
            Color = new Float3(1f, 0.93f, 0.82f),
            Intensity = 100000f,
            AngularRadius = 0.5f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 2048, Cascades = 4, Distance = 40 },
        });

        _spotDesc = new LightDesc
        {
            Name = "Warm spot",
            Type = LightType.Spot,
            Position = new Double3(-4, 4.5, -4.5),
            Direction = Float3.Normalize(new Float3(4, -2.9f, 4.5f)),
            Color = new Float3(1f, 0.5f, 0.2f),
            Intensity = 2000000f,
            Range = 18f,
            SourceRadius = 0.05f,
            InnerAngle = 14f,
            OuterAngle = 28f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 1024 },
        };
        _spot = World.AddLight(_spotDesc);

        _orbitDesc = new LightDesc
        {
            Name = "Orbiting point",
            Type = LightType.Point,
            Color = new Float3(0.25f, 0.65f, 1f),
            Intensity = 133000f,
            Range = 7f,
            SourceRadius = 0.12f,
        };
        _orbit = World.AddLight(_orbitDesc);
        _bulb = Add(Demo.Sphere, Pbr(new Float3(0, 0, 0), 0f, 1f, new Float3(41667f, 111111f, 222222f)), new Double3(0, 2, 3), new Double3(0.24, 0.24, 0.24), flags: RenderableFlags.None);

        World.Environment = new EnvironmentDesc
        {
            Ambient = AmbientMode.Sky,
            AmbientIntensity = 0.35f,
            Sun = sun,
            Sky = Track(demo.Renderer.CreateMaterial(demo.Renderer.Skies.Procedural)),
            Fog = new FogDesc { Mode = FogMode.ExponentialSquared, Density = 0.004f, UseSky = true },
        };
    }

    public override string Name => "Helmet";

    public override Double3 StartPosition => new(0, 2.2, 6.5);

    public override float StartYaw => MathF.PI;

    public override float StartPitch => -0.12f;

    public override string Controls => "L toggles the warm spot light";

    public override string Status => _spotOn ? "sun, spot and point light" : "sun and point light";

    public override void Update(float time, float deltaTime)
    {
        Double4x4 helmet = _model.Placement(_helmetBase, HelmetSize, time * 0.35f);
        foreach (RenderableHandle part in _helmet)
            World.SetTransform(part, helmet);

        Double3 orbit = new(MathF.Cos(time * 0.9f) * 2.8, 1.5 + MathF.Sin(time * 1.7f) * 0.5, MathF.Sin(time * 0.9f) * 2.8);
        _orbitDesc.Position = orbit;
        World.SetLight(_orbit, _orbitDesc);
        World.SetTransform(_bulb, Double4x4.CreateTRS(orbit, Quaternion.Identity, new Double3(0.24, 0.24, 0.24)));

        _spotDesc.Intensity = _spotOn ? 2000000f : 0f;
        World.SetLight(_spot, _spotDesc);
    }

    public override void OnKey(Key key)
    {
        if (key == Key.L)
            _spotOn = !_spotOn;
    }

    public override void Dispose()
    {
        base.Dispose();
        _model.Dispose();
    }
}
