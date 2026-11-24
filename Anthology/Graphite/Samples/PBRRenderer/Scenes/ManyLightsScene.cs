using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

using Silk.NET.Input;


namespace Prowl.Graphite.Samples.PBRRenderer;


/// <summary>
/// Static point lights without shadows over a floor of pillars, and nothing else lighting it, to time the local light
/// path alone. Spread apart they sit on a grid two ranges apart, so no two overlap, over a floor grown to hold them.
/// </summary>
internal sealed class ManyLightsScene : DemoScene
{
    private const int MaxScattered = 8192;
    private const int Grid = 12;
    private const float Spacing = 10f;

    private readonly LightDesc[] _descs;
    private readonly List<LightHandle> _handles = new();
    private readonly bool _spread;
    private int _target;
    private float _range;

    public ManyLightsScene(Demo demo, int count = 1000, float range = 8f, bool spread = false) : base(demo)
    {
        _spread = spread;
        _range = range;
        _target = count;
        _descs = new LightDesc[spread ? Math.Max(count, 1) : MaxScattered];
        int side = (int)Math.Ceiling(Math.Sqrt(_descs.Length));
        double floor = spread ? Math.Max(160, side * 2.0 * range + 20) : 160;

        Random random = new(11);
        Add(Demo.Cube, Pbr(new Float3(0.55f, 0.55f, 0.57f), 0f, 0.55f, grid: 2f), new Double3(0, -0.25, 0), new Double3(floor, 0.5, floor));

        Material[] pillars =
        [
            Pbr(new Float3(0.75f, 0.75f, 0.72f), 0f, 0.7f),
            Pbr(new Float3(0.2f, 0.22f, 0.26f), 0.8f, 0.3f),
            Pbr(new Float3(0.9f, 0.9f, 0.92f), 0f, 0.25f),
        ];
        for (int x = 0; x < Grid; x++)
        {
            for (int z = 0; z < Grid; z++)
            {
                double height = 2 + random.NextDouble() * 8;
                Double3 position = new((x - Grid / 2) * Spacing, height * 0.5, (z - Grid / 2) * Spacing);
                Add(Demo.Cube, pillars[(x * 7 + z * 3) % pillars.Length], position, new Double3(2.4, height, 2.4));
            }
        }

        for (int i = 0; i < _descs.Length; i++)
        {
            double angle = random.NextDouble() * Math.PI * 2;
            double radius = Math.Sqrt(random.NextDouble()) * 55;
            Double3 position = new(Math.Cos(angle) * radius, 0.8 + random.NextDouble() * 2.7, Math.Sin(angle) * radius);
            if (spread)
                position = new Double3((i % side - (side - 1) * 0.5) * 2.0 * range, range * 0.5, (i / side - (side - 1) * 0.5) * 2.0 * range);
            _descs[i] = new LightDesc
            {
                Type = LightType.Point,
                Position = position,
                Color = Hsv((float)random.NextDouble(), 0.75f, 1f),
                Intensity = 1.13f,
                Range = _range,
                Static = true,
            };
        }
        SetCount(_target);

        World.Environment = new EnvironmentDesc
        {
            Ambient = AmbientMode.Uniform,
            AmbientColor = new Float4(0.03f, 0.04f, 0.07f, 1),
            SkyColor = new Float4(0.01f, 0.015f, 0.03f, 1),
        };
    }

    public override float StartEV100 => 1f;

    public override string Name => "Many lights";

    public override Double3 StartPosition => new(0, 18, 95);

    public override float StartYaw => MathF.PI;

    public override float StartPitch => -0.3f;

    public override string Controls => _spread ? "" : "Up and Down double and halve the lights, Left and Right their range";

    public override string Status => $"{_handles.Count} static point lights of range {_range:0.#} m, {(_spread ? "spread apart" : "scattered")}, no shadows";

    public override void Update(float time, float deltaTime)
    {
    }

    public override void OnKey(Key key)
    {
        if (_spread)
            return;
        if (key == Key.Up)
            SetCount(Math.Min(_target * 2, MaxScattered));
        else if (key == Key.Down)
            SetCount(Math.Max(_target / 2, 1));
        else if (key == Key.Right)
            SetRange(MathF.Min(_range * 2f, 64f));
        else if (key == Key.Left)
            SetRange(MathF.Max(_range * 0.5f, 1f));
    }

    private void SetCount(int count)
    {
        _target = Math.Min(count, _descs.Length);
        count = _target;
        while (_handles.Count < count)
            _handles.Add(World.AddLight(_descs[_handles.Count]));
        while (_handles.Count > count)
        {
            World.RemoveLight(_handles[^1]);
            _handles.RemoveAt(_handles.Count - 1);
        }
    }

    private void SetRange(float range)
    {
        _range = range;
        for (int i = 0; i < _descs.Length; i++)
        {
            _descs[i].Range = range;
            if (i < _handles.Count)
                World.SetLight(_handles[i], _descs[i]);
        }
    }
}
