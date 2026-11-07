using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// A height field the ocean's floor is read from: heights in its red channel over a rectangle of the world, the first
/// row at <see cref="Min"/>'s z, columns along +x and rows along +z. A height is the texel times
/// <see cref="Scale"/> plus <see cref="Offset"/>, in metres.
/// </summary>
public sealed class OceanDepthInput
{
    public Texture? Heights { get; set; }

    /// <summary>The corner of the rectangle with the lowest x and z.</summary>
    public Double2 Min { get; set; }

    /// <summary>The rectangle's size along x and z in metres.</summary>
    public Float2 Size { get; set; } = new(1f, 1f);

    public float Scale { get; set; } = 1f;
    public float Offset { get; set; }
}

/// <summary>
/// The water's own level over a rectangle of the world, for lakes and rivers above or below the sea:
/// a height field read as <see cref="OceanDepthInput"/>'s is, or without one <see cref="Offset"/> alone, a flat
/// lake at that height. Later inputs replace earlier ones where they overlap.
/// </summary>
public sealed class OceanLevelInput
{
    public Texture? Heights { get; set; }
    public Double2 Min { get; set; }
    public Float2 Size { get; set; } = new(1f, 1f);
    public float Scale { get; set; } = 1f;
    public float Offset { get; set; }
}

/// <summary>
/// A current over a rectangle of the world, which carries the waves, foam and ripples along:
/// a flow map whose red and green, less a half, are the x and z of the current times <see cref="Strength"/>, or
/// without one <see cref="Velocity"/> alone. Inputs add where they overlap.
/// </summary>
public sealed class OceanFlowInput
{
    public Texture? Flow { get; set; }
    public Double2 Min { get; set; }
    public Float2 Size { get; set; } = new(1f, 1f);

    /// <summary>The current at a flow map's full red or green, in m/s.</summary>
    public float Strength { get; set; } = 1f;

    /// <summary>The current in m/s along x and z, without a flow map.</summary>
    public Float2 Velocity { get; set; }

    /// <summary>How far in from the rectangle's edges the current fades in, in metres.</summary>
    public float Feather { get; set; } = 10f;
}

public sealed partial class OceanFeature
{
    public const int DepthResolution = 256;
    public const int DepthSlices = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct DepthPush
    {
        public Float4 Slices, Viewer, Area, Heights;
    }

    // An input as the slice shader draws it: its texture if any, its area relative to the viewer, and what it adds
    private readonly record struct SliceDraw(Texture? Field, Float4 Area, Float4 Values, float Feather = 0f);

    private const float DepthMode = 0f, LevelMode = 1f, FlowMode = 2f;

    private InternalCompute _depthProgram = null!;
    private Sampler _heightSampler = null!;
    private Texture _noHeights = null!;
    private TextureHandle _depthHandle;
    private TextureView _depthTop = null!;
    private TextureHandle _stateHandle;
    private readonly List<SliceDraw> _depthDraws = new();
    private readonly List<SliceDraw> _levelDraws = new();
    private readonly List<SliceDraw> _flowDraws = new();
    private TextureHandle _levelHandle, _flowHandle;
    private Float4 _depthSlices;
    private Float2 _viewerWrapped;

    /// <summary>The water depth around <see cref="Viewer"/>, a layer per slice, in metres below sea level up to 1000.</summary>
    public Texture Depth { get; private set; } = null!;

    /// <summary>The water's level above sea level on the depth's slices, 0 where no level input reaches.</summary>
    public Texture Levels { get; private set; } = null!;

    /// <summary>The current on the depth's slices in m/s, its x and z in red and green.</summary>
    public Texture Flow { get; private set; } = null!;

    /// <summary>The currents carrying the water along. Host thread.</summary>
    public List<OceanFlowInput> FlowInputs { get; } = new();

    /// <summary>The water levels of lakes and rivers. Host thread.</summary>
    public List<OceanLevelInput> LevelInputs { get; } = new();

    /// <summary>
    /// What draws need of this frame's slices, written on the GPU beside them so they always match: the viewer's xz
    /// modulo the largest depth texel.
    /// </summary>
    public Texture State { get; private set; } = null!;

    /// <summary>The height of the sea in metres.</summary>
    public double SeaLevel { get; set; }

    /// <summary>Where the depth's slices centre, the camera the ocean is seen from.</summary>
    public Double3 Viewer { get; set; }

    /// <summary>How much waves shrink in water shallower than half their length, 0 to 1. At 1 they flatten fully at the shore.</summary>
    public float ShallowAttenuation { get; set; } = 0.95f;

    /// <summary>The planet's radius in metres, which drops the water away from the viewer toward the horizon. 0 is flat.</summary>
    public float PlanetRadius { get; set; } = 6371000f;

    /// <summary>The width of the depth's first slice in metres. Each next slice is twice as wide.</summary>
    public float DepthSliceSize { get; set; } = 64f;

    /// <summary>The height fields the floor is read from. Host thread.</summary>
    public List<OceanDepthInput> DepthInputs { get; } = new();

    /// <summary>The depth's parameters for shaders as the settings are now: the viewer's xz modulo the largest texel, the first slice's width and the resolution. Host thread.</summary>
    internal Float4 DepthParams
    {
        get
        {
            float width = MathF.Max(DepthSliceSize, 1f);
            double largestTexel = width * Math.Pow(2, DepthSlices - 1) / DepthResolution;
            return new Float4((float)Wrap(Viewer.X, largestTexel), (float)Wrap(Viewer.Z, largestTexel), width, DepthResolution);
        }
    }

    private void CreateDepth(Renderer renderer)
    {
        _depthProgram = renderer.Effects.Compute("Internal/OceanDepth.glsl");
        _heightSampler = renderer.Resources.LinearClamp;
        _noHeights = renderer.Resources.Noise;
        Depth = renderer.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(DepthResolution, DepthResolution, 1, DepthSlices, PixelFormat.R32_Float, TextureUsage.Storage | TextureUsage.Sampled));
        Depth.Name = "Ocean Depth";
        _depthTop = renderer.Device.ResourceFactory.CreateTextureView(new TextureViewDescription(Depth, 0, 1));
        Levels = renderer.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(DepthResolution, DepthResolution, 1, DepthSlices, PixelFormat.R32_Float, TextureUsage.Storage | TextureUsage.Sampled));
        Levels.Name = "Ocean Levels";
        Flow = renderer.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(DepthResolution, DepthResolution, 1, DepthSlices, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Storage | TextureUsage.Sampled));
        Flow.Name = "Ocean Flow";
        State = renderer.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Storage | TextureUsage.Sampled));
        State.Name = "Ocean State";
    }

    private void ExtractDepth()
    {
        Double3 viewer = Viewer;
        Float4 parameters = DepthParams;
        _viewerWrapped = new Float2(parameters.X, parameters.Y);
        _depthSlices = new Float4(parameters.Z, DepthResolution, DepthSlices, 1f);

        Float4 Area(Double2 min, Float2 size) => new((float)(min.X - viewer.X), (float)(min.Y - viewer.Z), MathF.Max(size.X, 1e-3f), MathF.Max(size.Y, 1e-3f));
        static Texture? Bound(Texture? texture) => texture is { IsDisposed: false } ? texture : null;

        _depthDraws.Clear();
        foreach (OceanDepthInput input in DepthInputs)
        {
            if (Bound(input.Heights) is { } heights)
                _depthDraws.Add(new SliceDraw(heights, Area(input.Min, input.Size), new Float4(input.Scale, (float)(input.Offset - SeaLevel), DepthMode, 0f)));
        }

        _levelDraws.Clear();
        foreach (OceanLevelInput input in LevelInputs)
        {
            Texture? heights = Bound(input.Heights);
            _levelDraws.Add(new SliceDraw(heights, Area(input.Min, input.Size), new Float4(heights != null ? input.Scale : 0f, (float)(input.Offset - SeaLevel), LevelMode, 0f)));
        }

        _flowDraws.Clear();
        foreach (OceanFlowInput input in FlowInputs)
        {
            Texture? flow = Bound(input.Flow);
            _flowDraws.Add(new SliceDraw(flow, Area(input.Min, input.Size), new Float4(flow != null ? input.Strength : 0f, input.Velocity.X, FlowMode, input.Velocity.Y), input.Feather));
        }
    }

    private static double Wrap(double value, double period) => value - Math.Floor(value / period) * period;

    private void SetupDepth(FrameBuilder builder)
    {
        _depthHandle = builder.Write(Depth);
        _stateHandle = builder.Write(State);
        _levelHandle = builder.Write(Levels);
        _flowHandle = builder.Write(Flow);
        builder.ExportToDraws(_levelHandle);
        builder.ExportToDraws(_flowHandle);
        builder.ExportToDraws(_depthHandle);
        builder.ExportToDraws(_stateHandle);
    }

    private void RenderDepth(CommandBuffer cmd)
    {
        cmd.SetComputeShader(_depthProgram.Program);
        DrawSlices(cmd, _depthDraws, DepthMode);
        DrawSlices(cmd, _levelDraws, LevelMode);
        DrawSlices(cmd, _flowDraws, FlowMode);
    }

    // Each input in turn over every slice, the first dispatch clearing first, a dispatch that only clears without any
    private void DrawSlices(CommandBuffer cmd, List<SliceDraw> draws, float mode)
    {
        uint groups = (DepthResolution + 7) / 8;
        int count = Math.Max(draws.Count, 1);
        for (int i = 0; i < count; i++)
        {
            bool drawn = i < draws.Count;
            SliceDraw draw = drawn ? draws[i] : new SliceDraw(null, new Float4(0f, 0f, -1f, -1f), new Float4(0f, 0f, mode, 0f));
            bool field = draw.Field != null;
            _properties.Clear();
            _properties.SetTexture("Ocean_Depth", _depthTop);
            _properties.SetTexture("Ocean_Level", Levels);
            _properties.SetTexture("Ocean_Flow", Flow);
            _properties.SetTexture("Ocean_State", State);
            _properties.SetTexture("Ocean_Heights", draw.Field ?? _noHeights, _heightSampler);
            cmd.SetProperties(_properties);
            cmd.SetPushConstants(new DepthPush
            {
                Slices = _depthSlices with { W = i == 0 ? 1f : 0f },
                Viewer = new Float4(_viewerWrapped.X, _viewerWrapped.Y, field ? 1f : 0f, draw.Feather),
                Area = draw.Area,
                Heights = draw.Values,
            });
            cmd.Dispatch(groups, groups, DepthSlices);
            cmd.ComputeBarrier();
        }
    }
}
