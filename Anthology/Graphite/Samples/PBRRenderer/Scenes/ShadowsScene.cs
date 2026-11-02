using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

using Silk.NET.Input;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class ShadowsScene : DemoScene
{
    private const int Lanterns = 8;

    private readonly LightHandle _sun;
    private LightDesc _sunDesc;
    private readonly LightHandle[] _lanternLights = new LightHandle[Lanterns];
    private readonly LightDesc[] _lanternDescs = new LightDesc[Lanterns];
    private readonly Material _bulbMaterial;
    private readonly List<RenderableHandle> _bulbs = new();
    private float _hour = 16.5f;
    private float _lastNight = -1f;
    private bool _running = true;

    public ShadowsScene(Demo demo) : base(demo)
    {
        Random random = new(11);

        Add(Demo.Cube, Pbr(new Float3(0.42f, 0.4f, 0.36f), 0f, 0.9f, grid: 4f), new Double3(0, -0.25, 0), new Double3(700, 0.5, 700));

        Material stone = Pbr(new Float3(0.72f, 0.69f, 0.62f), 0f, 0.8f);
        Material capital = Pbr(new Float3(0.6f, 0.57f, 0.5f), 0f, 0.75f);
        for (int x = -4; x <= 4; x++)
        {
            for (int z = -4; z <= 4; z++)
            {
                if (x == 0 && z == 0)
                    continue;
                double height = 4 + random.NextDouble() * 6;
                Double3 position = new(x * 16 + (random.NextDouble() - 0.5) * 6, 0, z * 16 + (random.NextDouble() - 0.5) * 6);
                Add(Demo.Cylinder, stone, position + new Double3(0, height * 0.5, 0), new Double3(2.2, height, 2.2));
                Add(Demo.Cube, capital, position + new Double3(0, height + 0.3, 0), new Double3(3.2, 0.6, 3.2));
            }
        }

        Add(Demo.Cube, Pbr(new Float3(0.12f, 0.12f, 0.13f), 0.7f, 0.2f), new Double3(0, 9, 0), new Double3(2.4, 18, 2.4), Quaternion.AxisAngle(Float3.UnitY, MathF.PI * 0.25f));

        Float3[] colors = [new(0.95f, 0.95f, 0.97f), new(1f, 0.78f, 0.34f), new(0.95f, 0.64f, 0.54f), new(0.95f, 0.95f, 0.97f), new(0.8f, 0.1f, 0.1f), new(0.1f, 0.5f, 0.9f), new(0.1f, 0.7f, 0.3f)];
        for (int i = 0; i < colors.Length; i++)
        {
            float t = i / (float)(colors.Length - 1);
            bool metal = i < 4;
            Add(Demo.Sphere, Pbr(colors[i], metal ? 1f : 0f, metal ? 0.02f + t * 0.5f : 0.12f), new Double3(-9 + i * 3, 1.3, 80), new Double3(2.6, 2.6, 2.6));
        }

        _bulbMaterial = Pbr(new Float3(0, 0, 0), 0f, 1f, new Float3(187500f, 135400f, 72900f));
        Material pole = Pbr(new Float3(0.08f, 0.08f, 0.09f), 0.5f, 0.4f);
        for (int i = 0; i < Lanterns; i++)
        {
            double side = i % 2 == 0 ? -1 : 1;
            Double3 foot = new(side * 5, 0, 88 - (i / 2) * 11);
            Add(Demo.Cylinder, pole, foot + new Double3(0, 2.5, 0), new Double3(0.2, 5, 0.2));
            Double3 head = foot + new Double3(0, 5.2, 0);
            _bulbs.Add(Add(Demo.Sphere, _bulbMaterial, head, new Double3(0.5, 0.5, 0.5), flags: RenderableFlags.None));
            _lanternDescs[i] = new LightDesc
            {
                Name = $"Lantern {i}",
                Type = LightType.Spot,
                Position = head - new Double3(0, 0.3, 0),
                Direction = new Float3(0, -1, 0),
                Color = new Float3(1f, 0.62f, 0.3f),
                Intensity = 0f,
                Range = 16f,
                SourceRadius = 0.2f,
                InnerAngle = 25f,
                OuterAngle = 60f,
                Static = true,
                Shadow = new ShadowDesc { Enabled = true, Resolution = 512 },
            };
            _lanternLights[i] = World.AddLight(_lanternDescs[i]);
        }

        _sunDesc = new LightDesc
        {
            Name = "Sun",
            Type = LightType.Directional,
            Color = new Float3(1f, 0.95f, 0.85f),
            Intensity = 100000f,
            AngularRadius = 0.5f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 4096, Cascades = 4, Distance = 260, CascadeBlend = 0.15f },
        };
        ApplySun();
        _sun = World.AddLight(_sunDesc);

        World.Environment = new EnvironmentDesc
        {
            Ambient = AmbientMode.Sky,
            Sun = _sun,
            Sky = Track(demo.Renderer.CreateMaterial(demo.Renderer.Skies.Procedural)),
            Fog = new FogDesc { Mode = FogMode.ExponentialSquared, Density = 0.004f, UseSky = true },
        };
    }

    public float Hour
    {
        get => _hour;
        set => _hour = value;
    }

    public override string Name => "Shadows";

    public override Double3 StartPosition => new(0, 3.5, 96);

    public override float StartYaw => MathF.PI;

    public override float StartPitch => 0.05f;

    public override string Controls => "Left and Right move the sun, P stops the day";

    public override string Status => $"{_hour:00.0}h, 4 cascades, {Lanterns} shadowed lanterns";

    public override void Update(float time, float deltaTime)
    {
        if (_running)
            _hour = (_hour + deltaTime * 0.35f) % 24f;

        ApplySun();
        World.SetLight(_sun, _sunDesc);

        float elevation = MathF.Sin((_hour - 6f) / 12f * MathF.PI);
        float night = 1f - Math.Clamp((elevation + 0.1f) * 5f, 0f, 1f);
        if (MathF.Abs(night - _lastNight) > 0.001f)
        {
            _lastNight = night;
            for (int i = 0; i < Lanterns; i++)
            {
                _lanternDescs[i].Intensity = 2500f * night;
                World.SetLight(_lanternLights[i], _lanternDescs[i]);
            }
            _bulbMaterial.SetColor("emission_color", new Float4(12700f * night, 9170f * night, 4940f * night, 1));
        }
    }

    public override void OnKey(Key key)
    {
        if (key == Key.Left)
            _hour = (_hour + 23.5f) % 24f;
        else if (key == Key.Right)
            _hour = (_hour + 0.5f) % 24f;
        else if (key == Key.P)
            _running = !_running;
    }

    private void ApplySun()
    {
        float angle = (_hour - 6f) / 12f * MathF.PI;
        Float3 toSun = Float3.Normalize(new Float3(MathF.Cos(angle) * 0.8f, MathF.Sin(angle), MathF.Cos(angle) * 0.6f + 0.15f));
        float elevation = toSun.Y;
        _sunDesc.Direction = -toSun;
        _sunDesc.Intensity = 100000f * Math.Clamp((elevation + 0.02f) * 6f, 0f, 1f);
        float warm = Math.Clamp(elevation * 3f, 0f, 1f);
        _sunDesc.Color = new Float3(1f, 0.45f + 0.5f * warm, 0.2f + 0.65f * warm);
    }
}
