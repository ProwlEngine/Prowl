using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

using Silk.NET.Input;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class LightsScene : DemoScene
{
    private const int MaxLights = 262144;
    private const int Grid = 60;
    private const float Spacing = 10f;
    private const int DynamicStride = 8;

    private readonly Random _random = new(7);
    private readonly Double3[] _base = new Double3[MaxLights];
    private readonly Float3[] _motion = new Float3[MaxLights];
    private readonly LightDesc[] _descs = new LightDesc[MaxLights];
    private readonly List<LightHandle> _handles = new();
    private readonly LightHandle[] _spots = new LightHandle[4];
    private readonly LightDesc[] _spotDescs = new LightDesc[4];
    private int _target = 250000;

    public LightsScene(Demo demo) : base(demo)
    {
        Add(Demo.Cube, Lit(new Float3(0.55f, 0.55f, 0.57f), 0f, 0.55f, grid: 2f), new Double3(0, -0.25, 0), new Double3(800, 0.5, 800));

        Material[] pillars =
        [
            Lit(new Float3(0.75f, 0.75f, 0.72f), 0f, 0.7f),
            Lit(new Float3(0.2f, 0.22f, 0.26f), 0.8f, 0.3f),
            Lit(new Float3(0.9f, 0.9f, 0.92f), 0f, 0.25f),
        ];
        for (int x = 0; x < Grid; x++)
        {
            for (int z = 0; z < Grid; z++)
            {
                double height = 2 + _random.NextDouble() * 8;
                Double3 position = new((x - Grid / 2) * Spacing, height * 0.5, (z - Grid / 2) * Spacing);
                Add(Demo.Cube, pillars[(x * 7 + z * 3) % pillars.Length], position, new Double3(2.4, height, 2.4));
            }
        }

        for (int i = 0; i < MaxLights; i++)
        {
            double angle = _random.NextDouble() * Math.PI * 2;
            double radius = Math.Sqrt(_random.NextDouble()) * 300;
            _base[i] = new Double3(Math.Cos(angle) * radius, 0.8 + _random.NextDouble() * 2.7, Math.Sin(angle) * radius);
            _motion[i] = new Float3(1.5f + (float)_random.NextDouble() * 2.5f, (float)(_random.NextDouble() * Math.PI * 2), 0.3f + (float)_random.NextDouble() * 0.9f);
            _descs[i] = new LightDesc
            {
                Name = $"Point {i}",
                Type = LightType.Point,
                Position = _base[i],
                Color = Hsv((float)_random.NextDouble(), 0.75f, 1f),
                Intensity = 0.15f,
                Range = 5f,
                SourceRadius = 0.08f,
                Static = i % DynamicStride != 0,
            };
        }
        SetCount(_target);

        World.AddLight(new LightDesc
        {
            Name = "Rect",
            Type = LightType.Rect,
            Position = new Double3(0, 7, 0),
            Direction = new Float3(0, -1, 0),
            Up = new Float3(0, 0, 1),
            Size = new Float2(10, 4),
            Color = new Float3(1f, 0.95f, 0.88f),
            Intensity = 4f,
            Range = 32f,
            Static = true,
        });
        Add(Demo.Cube, Lit(new Float3(0, 0, 0), 0f, 1f, new Float3(10f, 9.5f, 8.8f)), new Double3(0, 7.05, 0), new Double3(10, 0.1, 4), flags: RenderableFlags.None);

        World.AddLight(new LightDesc
        {
            Name = "Tube",
            Type = LightType.Tube,
            Position = new Double3(-24, 1.5, -24),
            Direction = new Float3(1, 0, 0),
            Size = new Float2(16, 0),
            SourceRadius = 0.15f,
            Color = new Float3(0.2f, 0.9f, 1f),
            Intensity = 3f,
            Range = 22f,
            Static = true,
        });
        Add(Demo.Cylinder, Lit(new Float3(0, 0, 0), 0f, 1f, new Float3(1.5f, 7f, 10f)), new Double3(-24, 1.5, -24), new Double3(0.3, 16, 0.3), Quaternion.AxisAngle(Float3.UnitZ, MathF.PI * 0.5f), RenderableFlags.None);

        World.AddLight(new LightDesc
        {
            Name = "Disk",
            Type = LightType.Disk,
            Position = new Double3(24, 6, 12),
            Direction = new Float3(0, -1, 0),
            Size = new Float2(3, 0),
            Color = new Float3(1f, 0.25f, 0.8f),
            Intensity = 3f,
            Range = 22f,
            Static = true,
        });
        Add(Demo.Cylinder, Lit(new Float3(0, 0, 0), 0f, 1f, new Float3(9f, 2f, 7f)), new Double3(24, 6.05, 12), new Double3(6, 0.1, 6), flags: RenderableFlags.None);

        Double3[] corners = [new(-120, 40, -120), new(120, 40, -120), new(120, 40, 120), new(-120, 40, 120)];
        for (int i = 0; i < _spots.Length; i++)
        {
            _spotDescs[i] = new LightDesc
            {
                Name = $"Spot {i}",
                Type = LightType.Spot,
                Position = corners[i],
                Direction = Float3.Normalize(new Float3((float)-corners[i].X, (float)-corners[i].Y, (float)-corners[i].Z)),
                Color = i % 2 == 0 ? new Float3(1f, 0.95f, 0.85f) : new Float3(0.7f, 0.85f, 1f),
                Intensity = 3000f,
                Range = 260f,
                SourceRadius = 0.2f,
                InnerAngle = 5f,
                OuterAngle = 11f,
                Shadow = new ShadowDesc { Enabled = true, Resolution = 2048 },
            };
            _spots[i] = World.AddLight(_spotDescs[i]);
        }

        LightHandle moon = World.AddLight(new LightDesc
        {
            Name = "Moon",
            Type = LightType.Directional,
            Direction = Float3.Normalize(new Float3(-0.3f, -1f, -0.4f)),
            Color = new Float3(0.55f, 0.65f, 1f),
            Intensity = 0.04f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 2048, Cascades = 4, Distance = 400 },
        });

        World.Environment = new EnvironmentDesc
        {
            Ambient = AmbientMode.Uniform,
            AmbientColor = new Float4(0.03f, 0.04f, 0.07f, 1),
            SkyColor = new Float4(0.01f, 0.015f, 0.03f, 1),
            Sun = moon,
            Fog = new FogDesc { Mode = FogMode.ExponentialSquared, Color = new Float4(0.02f, 0.03f, 0.05f, 1), Density = 0.004f },
        };
    }

    public override string Name => "Lights";

    public override Double3 StartPosition => new(0, 60, 345);

    public override float StartYaw => MathF.PI;

    public override float StartPitch => -0.3f;

    public override string Controls => "Up and Down double and halve the point lights";

    public override string Status => $"{_handles.Count} point lights ({_handles.Count / DynamicStride} moving), 3 area lights, 4 shadowed spots";

    public override void Update(float time, float deltaTime)
    {
        for (int i = 0; i < _handles.Count; i += DynamicStride)
        {
            Float3 motion = _motion[i];
            float angle = time * motion.Z + motion.Y;
            _descs[i].Position = _base[i] + new Double3(Math.Cos(angle) * motion.X, Math.Sin(angle * 1.7f) * 0.6, Math.Sin(angle) * motion.X);
            World.SetLight(_handles[i], _descs[i]);
        }

        for (int i = 0; i < _spots.Length; i++)
        {
            float angle = time * 0.25f + i * MathF.PI * 0.5f;
            Double3 aim = new(Math.Cos(angle) * 90, 0, Math.Sin(angle) * 90);
            Double3 from = _spotDescs[i].Position;
            Float3 direction = Float3.Normalize(new Float3((float)(aim.X - from.X), (float)(aim.Y - from.Y), (float)(aim.Z - from.Z)));
            _spotDescs[i].Direction = direction;
            World.SetLight(_spots[i], _spotDescs[i]);
        }
    }

    public override void OnKey(Key key)
    {
        if (key == Key.Up)
            SetCount(Math.Min(_target * 2, MaxLights));
        else if (key == Key.Down)
            SetCount(Math.Max(_target / 2, 16));
    }

    private void SetCount(int count)
    {
        _target = count;
        while (_handles.Count < count)
            _handles.Add(World.AddLight(_descs[_handles.Count]));
        while (_handles.Count > count)
        {
            World.RemoveLight(_handles[^1]);
            _handles.RemoveAt(_handles.Count - 1);
        }
    }
}
