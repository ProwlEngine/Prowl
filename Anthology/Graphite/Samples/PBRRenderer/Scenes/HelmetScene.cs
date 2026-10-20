using System;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

using Silk.NET.Input;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class HelmetScene : DemoScene
{
    private readonly ModelAsset _model;
    private readonly GpuMesh _mesh;
    private readonly RenderableHandle _helmet;
    private readonly RenderableHandle _bulb;
    private readonly Float3 _center;
    private readonly float _scale;
    private readonly LightHandle _spot;
    private readonly LightHandle _orbit;
    private LightDesc _spotDesc;
    private LightDesc _orbitDesc;
    private bool _spotOn = true;

    public HelmetScene(Demo demo) : base(demo)
    {
        _model = ModelAsset.Load(demo.Device, "Assets/Models/DamagedHelmet.glb", unwrapLightmapUVs: false);
        _mesh = demo.Renderer.CreateMesh(ModelGeometry.FromModel(_model));

        MaterialInfo info = _model.Materials.Length > 0 ? _model.Materials[0] : default;
        Material helmetMaterial = Track(demo.Renderer.CreateMaterial(demo.Lit));
        helmetMaterial.SetFloat("_Metallic", 1f);
        helmetMaterial.SetFloat("_Roughness", 1f);
        if (info.AlbedoTexture != null)
            helmetMaterial.SetTexture("_BaseMap", info.AlbedoTexture, SamplerDescription.Linear);
        if (info.NormalTexture != null)
            helmetMaterial.SetTexture("_NormalMap", info.NormalTexture, SamplerDescription.Linear);
        if (info.MetallicRoughnessTexture != null)
            helmetMaterial.SetTexture("_MetalRoughMap", info.MetallicRoughnessTexture, SamplerDescription.Linear);
        if (info.EmissiveTexture != null)
        {
            helmetMaterial.SetTexture("_EmissionMap", info.EmissiveTexture, SamplerDescription.Linear);
            helmetMaterial.SetColor("_Emission", new Float4(4, 4, 4, 1));
        }

        Float3 extents = _model.Bounds.Extents;
        _center = _model.Bounds.Center;
        _scale = 2.4f / (MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z)) * 2f);
        _helmet = World.Add(new RenderableDesc { Mesh = _mesh, Transform = HelmetTransform(0f) }, [helmetMaterial]);

        Add(Demo.Cube, Lit(new Float3(0.5f, 0.5f, 0.52f), 0f, 0.85f, grid: 1f), new Double3(0, -0.2, 0), new Double3(80, 0.4, 80));
        Add(Demo.Cylinder, Lit(new Float3(0.12f, 0.12f, 0.14f), 0.9f, 0.35f), new Double3(0, 0.15, 0), new Double3(3.4, 0.3, 3.4));

        Float3[] gold = [new(1.0f, 0.78f, 0.34f), new(1.0f, 0.78f, 0.34f), new(1.0f, 0.78f, 0.34f), new(0.95f, 0.64f, 0.54f), new(0.85f, 0.1f, 0.1f), new(0.1f, 0.35f, 0.85f)];
        float[] metal = [1f, 1f, 1f, 1f, 0f, 0f];
        float[] rough = [0.05f, 0.25f, 0.5f, 0.8f, 0.1f, 0.6f];
        for (int i = 0; i < gold.Length; i++)
            Add(Demo.Sphere, Lit(gold[i], metal[i], rough[i]), new Double3(-6.25 + i * 2.5, 0.8, -4), new Double3(1.6, 1.6, 1.6));

        LightHandle sun = World.AddLight(new LightDesc
        {
            Type = LightType.Directional,
            Direction = Float3.Normalize(new Float3(0.55f, -0.65f, 0.35f)),
            Color = new Float3(1f, 0.93f, 0.82f),
            Intensity = 0.45f,
            AngularRadius = 0.5f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 2048, Cascades = 4, Distance = 40 },
        });

        _spotDesc = new LightDesc
        {
            Type = LightType.Spot,
            Position = new Double3(-4, 4.5, -4.5),
            Direction = Float3.Normalize(new Float3(4, -2.9f, 4.5f)),
            Color = new Float3(1f, 0.5f, 0.2f),
            Intensity = 9f,
            Range = 18f,
            SourceRadius = 0.05f,
            InnerAngle = 14f,
            OuterAngle = 28f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 1024 },
        };
        _spot = World.AddLight(_spotDesc);

        _orbitDesc = new LightDesc
        {
            Type = LightType.Point,
            Color = new Float3(0.25f, 0.65f, 1f),
            Intensity = 0.6f,
            Range = 7f,
            SourceRadius = 0.12f,
        };
        _orbit = World.AddLight(_orbitDesc);
        _bulb = Add(Demo.Sphere, Lit(new Float3(0, 0, 0), 0f, 1f, new Float3(1.5f, 4f, 8f)), new Double3(0, 2, 3), new Double3(0.24, 0.24, 0.24), flags: RenderableFlags.None);

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
        World.SetTransform(_helmet, HelmetTransform(time * 0.35f));

        Double3 orbit = new(MathF.Cos(time * 0.9f) * 2.8, 1.5 + MathF.Sin(time * 1.7f) * 0.5, MathF.Sin(time * 0.9f) * 2.8);
        _orbitDesc.Position = orbit;
        World.SetLight(_orbit, _orbitDesc);
        World.SetTransform(_bulb, Double4x4.CreateTRS(orbit, Quaternion.Identity, new Double3(0.24, 0.24, 0.24)));

        _spotDesc.Intensity = _spotOn ? 9f : 0f;
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
        _mesh.Dispose();
        _model.Dispose();
    }

    private Double4x4 HelmetTransform(float angle)
    {
        Quaternion spin = Quaternion.AxisAngle(Float3.UnitY, angle);
        return Double4x4.CreateTranslation(new Double3(0, 1.75, 0))
            * Double4x4.CreateFromQuaternion(spin)
            * Double4x4.CreateScale(_scale)
            * Double4x4.CreateTranslation(new Double3(-_center.X, -_center.Y, -_center.Z));
    }
}
