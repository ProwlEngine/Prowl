using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The terrain as the world's far shadow: a square of shadow heights around the viewer, marched toward the sun over the
/// resident pages, see Graphite/Internal/TerrainFarShadow.glsl. It is rebuilt into a second texture a slice of rows a
/// frame when the sun turns or the viewer moves far enough, and swapped in once whole, so a half built map never shows.
/// </summary>
internal sealed class TerrainFarShadow : IDisposable
{
    private const int Slices = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public Float4 Area, Sun, Terrain, Place;
    }

    private readonly Texture[] _heights = new Texture[2];
    private readonly InternalCompute _program;
    private readonly PropertySet _properties = new();
    private readonly Sampler _point, _linear;
    private int _front;
    private bool _built, _building, _stale;
    private int _nextRow;
    private Double2 _min, _buildMin;
    private Float3 _sun, _buildSun;

    public TerrainFarShadow(Renderer renderer, int resolution)
    {
        Resolution = resolution;
        ResourceFactory factory = renderer.Device.ResourceFactory;
        for (int i = 0; i < 2; i++)
        {
            _heights[i] = factory.CreateTexture(TextureDescription.Texture2D((uint)resolution, (uint)resolution, 1, 1, PixelFormat.R32_Float, TextureUsage.Storage | TextureUsage.Sampled));
            _heights[i].Name = "Graphite Terrain Far Shadow";
        }
        _program = renderer.Effects.Compute("Internal/TerrainFarShadow.glsl");
        _point = factory.CreateSampler(SamplerDescription.Point);
        _linear = factory.CreateSampler(SamplerDescription.Linear);
        Map = new FarShadowMap(renderer) { Enabled = false };
    }

    public int Resolution { get; }
    public FarShadowMap Map { get; }

    /// <summary>The rows the frame fills, 0 for none.</summary>
    public int Rows { get; private set; }

    private Push _push;

    /// <summary>
    /// Starts a rebuild when the sun turned more than a quarter degree or the viewer left the middle eighth, picks the
    /// frame's rows, and swaps in a finished map. Main thread. sunDirection is the way the sunlight travels.
    /// </summary>
    /// <summary>Builds the map again from the next update, after the heights under it changed.</summary>
    public void Invalidate() => _stale = true;

    public void Update(Double2 viewer, Float3 sunDirection, double coverage, double reach, TerrainData data, float fadeStart)
    {
        Float3 sun = Float3.Normalize(sunDirection);
        double cell = coverage / Resolution;
        bool turned = Float3.Dot(sun, _sun) < MathF.Cos(0.25f * MathF.PI / 180f);
        bool moved = Math.Max(Math.Abs(viewer.X - (_min.X + coverage / 2)), Math.Abs(viewer.Y - (_min.Y + coverage / 2))) > coverage / 8;
        if (!_building && (!_built || turned || moved || _stale))
        {
            _building = true;
            _stale = false;
            _nextRow = 0;
            _buildSun = sun;
            _buildMin = new Double2(Math.Floor((viewer.X - coverage / 2) / cell) * cell, Math.Floor((viewer.Y - coverage / 2) / cell) * cell);
        }

        Rows = 0;
        if (_building)
        {
            Rows = Math.Min((Resolution + Slices - 1) / Slices, Resolution - _nextRow);
            Float2 across = new(-_buildSun.X, -_buildSun.Z);
            float flat = MathF.Sqrt(across.X * across.X + across.Y * across.Y);
            float rise = flat > 1e-4f ? -_buildSun.Y / flat : (_buildSun.Y < 0 ? 1e4f : -1f);
            Float2 toward = flat > 1e-4f ? across / flat : Float2.Zero;
            TerrainHeader header = data.Header;
            _push = new Push
            {
                Area = new Float4((float)_buildMin.X, (float)_buildMin.Y, (float)cell, _nextRow),
                Sun = new Float4(toward.X, toward.Y, rise, (float)reach),
                Terrain = new Float4((float)header.Bottom, (float)header.Range, (float)header.Spacing, header.Levels),
                Place = new Float4((float)data.Position.Y, Resolution, Rows, 0),
            };
            _nextRow += Rows;
        }

        // The rows written last frame finished the map, so this frame shows it
        if (_building && _nextRow >= Resolution && Rows == 0)
        {
            _building = false;
            _built = true;
            _front = 1 - _front;
            _min = _buildMin;
            _sun = _buildSun;
        }

        Map.Heights = _heights[_front];
        Map.Enabled = _built;
        Map.Min = new Double2(_min.X + data.Position.X, _min.Y + data.Position.Z);
        Map.Size = cell * Resolution;
        Map.Bias = (float)(cell * 0.5);
        Map.Softness = (float)Math.Max(cell, 1.0);
        Map.FadeStart = fadeStart;
    }

    public Texture Back => _heights[1 - _front];

    /// <summary>Fills the frame's rows of the map being built. Render thread.</summary>
    public void Render(CommandBuffer cmd, TerrainPool pool)
    {
        if (Rows == 0)
            return;
        _properties.SetTexture("Terrain_FarShadow", Back);
        _properties.SetTexture("Terrain_PageTable", pool.PageTable, _point);
        _properties.SetTexture("_TerrainHeights", pool.Heights, _linear);
        cmd.SetComputeShader(_program.Program);
        cmd.SetProperties(_properties);
        cmd.SetPushConstants(_push);
        cmd.Dispatch((uint)(Resolution + 7) / 8, (uint)(Rows + 7) / 8, 1);
    }

    public void Dispose()
    {
        Map.Dispose();
        _program.Dispose();
        _point.Dispose();
        _linear.Dispose();
        foreach (Texture texture in _heights)
            texture.Dispose();
    }
}
