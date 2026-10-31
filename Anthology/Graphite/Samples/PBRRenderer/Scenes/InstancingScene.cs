using System;

using Prowl.Graphite.Rendering;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class InstancingScene : DemoScene
{
    private const int Side = 150;
    private const float Spacing = 1.1f;
    private const int Rings = 20;
    private const int PerRing = 400;

    private readonly GpuMesh _orbiterMesh;
    private readonly Material _instanced, _glossy;
    private readonly RenderableHandle[] _wave = new RenderableHandle[Side * Side];
    private readonly RenderableHandle[] _orbiters = new RenderableHandle[Rings * PerRing];

    public InstancingScene(Demo demo) : base(demo)
    {
        _instanced = Pbr(new Float3(1, 1, 1), 0.1f, 0.35f, instanceColor: true);
        _glossy = Pbr(new Float3(1, 1, 1), 0.9f, 0.2f, instanceColor: true);

        Add(Demo.Cube, Pbr(new Float3(0.45f, 0.45f, 0.48f), 0f, 0.8f, grid: 4f), new Double3(0, -0.25, 0), new Double3(900, 0.5, 900));

        FillWave(0f);
        _orbiterMesh = demo.Renderer.CreateMesh(Geometry.Sphere(10, 20));
        FillOrbiters(0f);

        LightHandle sun = World.AddLight(new LightDesc
        {
            Name = "Sun",
            Type = LightType.Directional,
            Direction = Float3.Normalize(new Float3(0.45f, -0.6f, 0.5f)),
            Color = new Float3(1f, 0.92f, 0.8f),
            Intensity = 100000f,
            AngularRadius = 0.5f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 4096, Cascades = 4, Distance = 300 },
        });

        World.Environment = new EnvironmentDesc
        {
            Ambient = AmbientMode.Sky,
            AmbientIntensity = 0.5f,
            Sun = sun,
            Sky = Track(demo.Renderer.CreateMaterial(demo.Renderer.Skies.Procedural)),
            Fog = new FogDesc { Mode = FogMode.ExponentialSquared, Density = 0.0025f, UseSky = true },
        };
    }

    public override string Name => "Instancing";

    public override Double3 StartPosition => new(0, 42, 125);

    public override float StartYaw => MathF.PI;

    public override float StartPitch => -0.3f;

    public override string Status => $"{_wave.Length + _orbiters.Length} renderables, all shadowed";

    public override void Update(float time, float deltaTime)
    {
        FillWave(time);
        FillOrbiters(time);
    }

    public override void Dispose()
    {
        base.Dispose();
        _orbiterMesh.Dispose();
    }

    private void FillWave(float time)
    {
        float half = Side * 0.5f * Spacing;
        for (int z = 0; z < Side; z++)
        {
            for (int x = 0; x < Side; x++)
            {
                float px = x * Spacing - half;
                float pz = z * Spacing - half;
                float distance = MathF.Sqrt(px * px + pz * pz);
                float height = 3f + MathF.Sin(distance * 0.12f - time * 2f) * 2.5f + MathF.Sin(px * 0.09f + time) * 1.5f;
                float hue = distance * 0.004f + time * 0.03f;
                Set(_wave, z * Side + x, Demo.Cube, _instanced, Compose(new Float3(px, height * 0.5f, pz), new Float3(1f, height, 1f)), new Float4(Hsv(hue, 0.65f, 0.95f), 1));
            }
        }
    }

    private void FillOrbiters(float time)
    {
        for (int ring = 0; ring < Rings; ring++)
        {
            float radius = 14f + ring * 2.6f;
            float height = 12f + MathF.Sin(ring * 0.6f + time) * 3f;
            float speed = (ring % 2 == 0 ? 1f : -1f) * (0.6f - ring * 0.01f);
            for (int i = 0; i < PerRing; i++)
            {
                float angle = i / (float)PerRing * MathF.PI * 2f + time * speed;
                float bob = MathF.Sin(angle * 6f + time * 2f) * 1.2f;
                Set(_orbiters, ring * PerRing + i, _orbiterMesh, _glossy, Compose(new Float3(MathF.Cos(angle) * radius, height + bob, MathF.Sin(angle) * radius), new Float3(0.7f, 0.7f, 0.7f)), new Float4(Hsv(ring / (float)Rings + angle * 0.02f, 0.8f, 1f), 1));
            }
        }
    }

    // Adds the renderable the first time, then moves and recolours it
    private void Set(RenderableHandle[] handles, int index, GpuMesh mesh, Material material, in Float4x4 transform, Float4 color)
    {
        if (!handles[index].IsValid)
        {
            handles[index] = World.Add(new RenderableDesc { Mesh = mesh, Transform = (Double4x4)transform, Custom0 = color }, [material]);
            return;
        }
        World.SetTransform(handles[index], (Double4x4)transform);
        World.SetCustomData(handles[index], color, default);
    }

    private static Float4x4 Compose(Float3 position, Float3 scale)
        => new(new Float4(scale.X, 0, 0, 0), new Float4(0, scale.Y, 0, 0), new Float4(0, 0, scale.Z, 0), new Float4(position.X, position.Y, position.Z, 1));
}
