using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.Rendering.Tools;
using Prowl.Vector;

using Silk.NET.Input;


namespace Prowl.Graphite.Samples.PBRRenderer;


/// <summary>A 4 km generated and eroded heightfield at 1 m, streamed and drawn by TerrainSurface.</summary>
internal sealed class TerrainScene : DemoScene
{
    private const int Samples = 4097;
    private const double Spacing = 1.0;
    private const double Height = 600.0;
    private const ulong Seed = 7;
    private const int CacheVersion = 4;

    private readonly TerrainData _data;
    private readonly TerrainSurface _surface;
    private readonly TerrainLayers _layers;
    private readonly Texture _grassCard;
    private readonly TerrainForest _forest;
    private double _editMilliseconds;

    public TerrainScene(Demo demo) : base(demo)
    {
        TerrainHeightmap map = LoadOrGenerate();
        map.Holes = new bool[Samples * Samples];
        for (int z = 0; z < Samples; z++)
            for (int x = 0; x < Samples; x++)
                map.Holes[z * Samples + x] = (x - Samples / 2) * (x - Samples / 2) + (z - Samples / 2) * (z - Samples / 2) < 12 * 12;
        MemoryStream package = new();
        TerrainImporter.Import(map, new TerrainImportSettings
        {
            Spacing = Spacing,
            Bottom = 0,
            Range = Height,
            ControlMaxSpacing = double.MaxValue,
            Layers =
            [
                new TerrainLayerRule { Layer = TerrainTextures.Grass, MaxHeight = 440, HeightFade = 50, MaxSlope = 30, SlopeFade = 6, NoiseStrength = 0.5, NoiseSize = 60 },
                new TerrainLayerRule { Layer = TerrainTextures.Dirt, Weight = 0.45, MaxSlope = 38, NoiseStrength = 0.9, NoiseSize = 35 },
                new TerrainLayerRule { Layer = TerrainTextures.Rock, MinSlope = 36, SlopeFade = 6, Weight = 1.2, NoiseStrength = 0.3, NoiseSize = 20 },
                new TerrainLayerRule { Layer = TerrainTextures.Snow, MinHeight = 430, HeightFade = 40, MaxSlope = 44, SlopeFade = 8, Weight = 1.5, NoiseStrength = 0.4, NoiseSize = 40 },
            ],
        }, package);
        package.Position = 0;
        _data = TerrainData.Load(package);
        _data.Position = new Double3(-Samples * Spacing * 0.5, 0, -Samples * Spacing * 0.5);
        _layers = new TerrainLayers(demo.Renderer, TerrainTextures.Generate(512));
        _grassCard = TerrainTextures.GrassCard(demo.Renderer, 256);
        _surface = new TerrainSurface(demo.Renderer, _data, poolPages: 160) { Layers = _layers, CastFarShadow = true, GrassTypes = TerrainTextures.GrassTypes, GrassCards = _grassCard };
        World.Providers.Add(_surface);
        _forest = new TerrainForest(demo.Renderer, World, _data);
        World.Lod.ShadowOffset = 1;

        LightHandle sun = World.AddLight(new LightDesc
        {
            Name = "Sun",
            Type = LightType.Directional,
            Direction = Float3.Normalize(new Float3(0.6f, -0.3f, 0.5f)),
            Color = new Float3(1f, 0.92f, 0.8f),
            Intensity = 100000f,
            AngularRadius = 0.5f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 2048, Cascades = 4, Distance = 600 },
        });
        World.Environment = new EnvironmentDesc
        {
            Ambient = AmbientMode.Sky,
            AmbientIntensity = 0.5f,
            Sun = sun,
            Sky = Track(demo.Renderer.CreateMaterial(demo.Renderer.Skies.Procedural)),
            Fog = new FogDesc { Mode = FogMode.ExponentialSquared, Density = 0.00012f, UseSky = true },
        };
    }

    public override string Name => "Terrain";

    public override Double3 StartPosition => new(-276, Ground(-276, 56) + 1.7, 56);

    public override float StartYaw => 0f;

    public override float StartPitch => -0.1f;

    public override string Status => $"{_surface.Drawn} nodes, {(_surface.UseCache ? "cache" : "direct")}, {_surface.DebugView}, quality {_surface.LodQuality:0.0}, pages {_surface.Streaming.Resident}/{_surface.Streaming.Capacity} by level {string.Join(" ", _surface.Streaming.ResidentByLevel)}, loading {_surface.Streaming.Loading}, {_forest.Props.Count} props in {_forest.Props.Cells} cells, last edit {_editMilliseconds:0.0} ms, {View.Camera.Layers[0].Position.Y - Ground(View.Camera.Layers[0].Position.X, View.Camera.Layers[0].Position.Z):0} m up";

    public override string Controls => "L debug view, C cache, H far shadow, [ ] LOD quality, Z crater, N raise, M flatten, J smooth, P paint dirt, O hole, K cut grass, Y regrow grass, U clear props where you look, Ctrl fast";

    public override void Update(float time, float deltaTime)
    {
        _surface.Viewer = View.Camera.Layers[0].Position;
        _forest.Update(View.Camera.Layers[0].Position);
    }

    public override void OnKey(Key key)
    {
        switch (key)
        {
            case Key.C: _surface.UseCache = !_surface.UseCache; break;
            case Key.H: _surface.CastFarShadow = !_surface.CastFarShadow; break;
            case Key.L: _surface.DebugView = (TerrainDebugView)(((int)_surface.DebugView + 1) % 4); break;
            case Key.LeftBracket: _surface.LodQuality = Math.Max(1.5, _surface.LodQuality - 0.25); break;
            case Key.RightBracket: _surface.LodQuality += 0.25; break;
            case Key.Z: Edit(TerrainBrush.Crater, 8, 4); break;
            case Key.N: Edit(TerrainBrush.Raise, 25, 6); break;
            case Key.M: Edit(TerrainBrush.Flatten, 15, 1); break;
            case Key.J: Edit(TerrainBrush.Smooth, 15, 0.8); break;
            case Key.P: Edit(TerrainBrush.Paint, 8, 1, TerrainTextures.Dirt); break;
            case Key.O: Edit(TerrainBrush.Hole, 3, 1); break;
            case Key.K: Edit(TerrainBrush.Grass, 6, 1); break;
            case Key.Y: Edit(TerrainBrush.Grass, 6, -1); break;
            case Key.U: Edit(TerrainBrush.Detail, 10, 1); break;
        }
    }

    // Applies a brush where the camera looks, flattening to the height it hit
    private void Edit(TerrainBrush brush, double radius, double strength, int layer = 0)
    {
        if (Camera == null || !_data.Raycast(Camera.Position, Camera.Forward, 2000, out TerrainHit hit))
            return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        _data.Apply(new TerrainEdit(brush, new Double2(hit.Position.X, hit.Position.Z), radius, strength, hit.Position.Y, layer));
        _editMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private double Ground(double x, double z) => _data.TryGetHeight(new Double2(x, z), out double h) ? h : 0;

    public override void Dispose()
    {
        World.Providers.Remove(_surface);
        _forest.Dispose();
        _surface.Dispose();
        _layers.Dispose();
        _grassCard.Dispose();
        _data.Dispose();
        base.Dispose();
    }

    // The eroded heightmap takes a few seconds, so it is kept in the temp folder keyed by what shapes it
    private static TerrainHeightmap LoadOrGenerate()
    {
        string path = Path.Combine(Path.GetTempPath(), "Graphite", $"TerrainScene_{Samples}_{Spacing}_{Height}_{Seed}_v{CacheVersion}.r16");
        try
        {
            if (File.Exists(path))
            {
                TerrainHeightmap cached = TerrainHeightmap.Load(path);
                if (cached.Width == Samples && cached.Height == Samples)
                    return cached;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }

        float[] heights = Generate();
        TerrainErosion.Erode(heights, Samples, Samples, Spacing, new TerrainErosionSettings { Seed = Seed, FlowIterations = 20, FlowErosion = 0.012, FlowLakeFill = 0.02, Droplets = 0.05 });
        float top = 0;
        foreach (float h in heights)
            top = Math.Max(top, h);
        float scale = (float)(Height * 0.98) / top;
        for (int i = 0; i < heights.Length; i++)
            heights[i] *= scale;
        TerrainHeightmap map = TerrainErosion.ToHeightmap(heights, Samples, Samples, 0, Height);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, MemoryMarshal.AsBytes(map.Samples.AsSpan()).ToArray());
            File.Move(temporary, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return map;
    }

    // A meandering main valley through ridged mountains and rolling hills, all bent by domain warping
    private static float[] Generate()
    {
        float[] heights = new float[Samples * Samples];
        double size = (Samples - 1) * Spacing;
        Parallel.For(0, Samples, z =>
        {
            for (int x = 0; x < Samples; x++)
            {
                double px = x * Spacing, pz = z * Spacing;
                double qx = px + Fbm(px / 1400, pz / 1400, 4) * 350;
                double qz = pz + Fbm(px / 1400 + 31.7, pz / 1400 - 12.3, 4) * 350;
                double valleyZ = size * (0.5 + Fbm(qx / 2200 + 3.1, 0.37, 3) * 0.5);
                double valley = SmoothStep(120, 750, Math.Abs(qz - valleyZ));
                double massif = SmoothStep(-0.3, 0.2, Fbm(qx / 1800 + 7.1, qz / 1800 - 3.3, 3));
                double hills = Fbm(qx / 650, qz / 650, 5) * 0.5 + 0.5;
                double ridges = Ridged(qx / 1500, qz / 1500, 8);
                heights[z * Samples + x] = (float)(15 + hills * (25 + 70 * valley) + valley * massif * ridges * 700);
            }
        });
        return heights;
    }

    private static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // Ridged multifractal, finer octaves weighted by the ridges below them
    private static double Ridged(double x, double z, int octaves)
    {
        double sum = 0, amplitude = 0.5, weight = 1;
        for (int o = 0; o < octaves; o++)
        {
            double n = 1 - Math.Abs(Noise(x, z) * 1.4);
            n *= n * weight;
            weight = Math.Clamp(n * 1.5, 0, 1);
            sum += n * amplitude;
            (x, z) = (x * 1.6 - z * 1.2 + 17.3, x * 1.2 + z * 1.6 - 9.1);
            amplitude *= 0.5;
        }
        return sum;
    }

    private static double Fbm(double x, double z, int octaves)
    {
        double sum = 0, amplitude = 0.5;
        for (int o = 0; o < octaves; o++)
        {
            sum += Noise(x, z) * amplitude;
            (x, z) = (x * 1.6 - z * 1.2 + 5.2, x * 1.2 + z * 1.6 - 1.7);
            amplitude *= 0.5;
        }
        return sum * 1.4;
    }

    // Gradient noise, roughly from -0.7 to 0.7
    private static double Noise(double x, double z)
    {
        double fx = Math.Floor(x), fz = Math.Floor(z);
        double tx = x - fx, tz = z - fz;
        double ux = tx * tx * tx * (tx * (tx * 6 - 15) + 10);
        double uz = tz * tz * tz * (tz * (tz * 6 - 15) + 10);
        int ix = (int)fx, iz = (int)fz;
        double a = Gradient(ix, iz, tx, tz), b = Gradient(ix + 1, iz, tx - 1, tz);
        double c = Gradient(ix, iz + 1, tx, tz - 1), d = Gradient(ix + 1, iz + 1, tx - 1, tz - 1);
        return (a + (b - a) * ux) * (1 - uz) + (c + (d - c) * ux) * uz;
    }

    private static double Gradient(int x, int z, double dx, double dz)
    {
        uint h = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u;
        h ^= h >> 13;
        h *= 0x85EBCA6Bu;
        h ^= h >> 16;
        double angle = h * (Math.PI * 2 / 4294967296.0);
        return Math.Cos(angle) * dx + Math.Sin(angle) * dz;
    }
}
