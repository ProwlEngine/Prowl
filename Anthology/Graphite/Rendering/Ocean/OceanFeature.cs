using System;
using System.Numerics;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The ocean's waves: cascades of FFT waves on square tiles that repeat across the world, each a band of wavelengths,
/// the largest tile first and each next a quarter its size. Every frame the cascades move to the frame's time and are
/// transformed into two texture arrays, a layer per cascade, which every view's draws can sample: the displacement
/// (x, height, z, dDx/dz) and the slopes (dh/dx, dh/dz, dDx/dx, dDz/dz), each with mips so far water samples the
/// waves filtered. A third array keeps each cascade's foam from frame to frame, built where waves fold and fading.
/// The waves loop over <see cref="LoopPeriod"/>. The floor's depth is kept in slices around <see cref="Viewer"/>,
/// from <see cref="DepthInputs"/>, which waves feel in shallow water.
/// </summary>
public sealed partial class OceanFeature : FrameFeature, IDisposable
{
    public const int Cascades = 4;
    private const int TileRatio = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpectrumPush
    {
        public Float4 Power0, Power1, Power2, Power3;
        public Float4 Wind, Sizes, Grid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UpdatePush
    {
        public Float4 Sizes, Grid, Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FftPush
    {
        public uint Size, Columns, Reset;
        public float DeltaTime, Coverage, Strength, Fade, Pad;
    }

    private readonly InternalCompute _spectrum, _update, _fft;
    private readonly Texture _initial, _spectrumA, _spectrumB;
    private readonly TextureView _displacementTop, _slopesTop, _foamTop;
    private float _foamCoverage, _foamStrength, _foamFade;
    private bool _foamStarted;
    private readonly PropertySet _properties = new();
    private SpectrumPush _settings;
    private SpectrumPush? _built;
    private float _chop, _timeScale = 1f;
    private readonly float[] _sizes = new float[Cascades];
    private TextureHandle _initialHandle, _aHandle, _bHandle, _displacementHandle, _slopesHandle, _foamHandle;

    /// <param name="resolution">Texels along each side of a cascade, a power of two from 16 to 256.</param>
    public OceanFeature(Renderer renderer, int resolution = 128) : base(FrameSlot.BeforeViews)
    {
        if (resolution is < 16 or > 256 || !int.IsPow2(resolution))
            throw new ArgumentOutOfRangeException(nameof(resolution), "The resolution is a power of two from 16 to 256.");
        Resolution = resolution;
        _spectrum = renderer.Effects.Compute("Internal/OceanSpectrum.glsl");
        _update = renderer.Effects.Compute("Internal/OceanUpdate.glsl");
        _fft = renderer.Effects.Compute("Internal/OceanFft.glsl");

        ResourceFactory factory = renderer.Device.ResourceFactory;
        Texture Array(string name, PixelFormat format, TextureUsage usage, uint mips = 1)
        {
            Texture texture = factory.CreateTexture(TextureDescription.Texture2D((uint)resolution, (uint)resolution, mips, Cascades, format, usage));
            texture.Name = name;
            return texture;
        }
        _initial = Array("Ocean Initial", PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Storage);
        _spectrumA = Array("Ocean Spectrum A", PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Storage);
        _spectrumB = Array("Ocean Spectrum B", PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Storage);
        uint mips = (uint)BitOperations.Log2((uint)resolution) + 1;
        Displacement = Array("Ocean Displacement", PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Storage | TextureUsage.Sampled, mips);
        Slopes = Array("Ocean Slopes", PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Storage | TextureUsage.Sampled, mips);
        _displacementTop = factory.CreateTextureView(new TextureViewDescription(Displacement, 0, 1));
        _slopesTop = factory.CreateTextureView(new TextureViewDescription(Slopes, 0, 1));
        Foam = Array("Ocean Foam", PixelFormat.R16_Float, TextureUsage.Storage | TextureUsage.Sampled, mips);
        _foamTop = factory.CreateTextureView(new TextureViewDescription(Foam, 0, 1));
        CreateDepth(renderer);
        CreateRipples(renderer);
        CreateClip(renderer);
        CreateWhitecaps(renderer);
    }

    public int Resolution { get; }

    /// <summary>The starting amplitudes, h0 at each wave and the conjugate of h0 at its opposite, for tests.</summary>
    internal Texture Initial => _initial;

    /// <summary>The cascades' displacement, a layer per cascade: x, height, z and dDx/dz, in metres.</summary>
    public Texture Displacement { get; }

    /// <summary>The cascades' slopes, a layer per cascade: dh/dx, dh/dz, dDx/dx and dDz/dz.</summary>
    public Texture Slopes { get; }

    /// <summary>The cascades' foam, 0 to 1, a layer per cascade.</summary>
    public Texture Foam { get; }

    /// <summary>How folded the waves must be before they foam: foam builds where the Jacobian falls under this.</summary>
    public float FoamCoverage { get; set; } = 0.55f;

    /// <summary>How white the foam on folding wave crests is.</summary>
    public float FoamStrength { get; set; } = 1f;

    /// <summary>The share of foam that fades each second.</summary>
    public float FoamFade { get; set; } = 0.8f;

    /// <summary>How hard ripples and wakes pull the water toward their crests, folding them into foam.</summary>
    public float RippleFoam { get; set; } = 1f;

    /// <summary>The power in each octave of wavelength, and the chop and time scale.</summary>
    public OceanSpectrum Spectrum { get; } = new();

    /// <summary>Wind speed in m/s, which cuts off waves too long for it. 41.7 is 150 km/h.</summary>
    public float WindSpeed { get; set; } = 150f / 3.6f;

    /// <summary>The direction the wind blows toward, in radians from +X toward +Z.</summary>
    public float WindDirection { get; set; }

    /// <summary>How far waves spread from the wind's direction, 0 to 1. At 1 they travel every way.</summary>
    public float Turbulence { get; set; } = 0.145f;

    /// <summary>The largest cascade's tile size in metres. Each next cascade's is a quarter of the one before.</summary>
    public float TileSize { get; set; } = 1024f;

    /// <summary>Seconds after which the waves repeat, each wave's frequency rounded to fit. 0 never repeats.</summary>
    public float LoopPeriod { get; set; } = 600f;

    public float Gravity { get; set; } = 9.81f;

    /// <summary>The tile size of a cascade in metres.</summary>
    public float CascadeSize(int cascade) => _sizes[cascade];

    public override void Extract()
    {
        float tile = MathF.Max(TileSize, 1f);
        for (int i = 0; i < Cascades; i++)
            _sizes[i] = tile / MathF.Pow(TileRatio, i);

        float Octave(int octave) => octave < OceanSpectrum.Octaves ? Spectrum.Power(octave) : 0f;
        Float4 Power(int first) => new(Octave(first), Octave(first + 1), Octave(first + 2), Octave(first + 3));
        float period = MathF.Max(LoopPeriod, 0f);
        _settings = new SpectrumPush
        {
            Power0 = Power(0),
            Power1 = Power(4),
            Power2 = Power(8),
            Power3 = Power(12),
            Wind = new Float4(MathF.Cos(WindDirection), MathF.Sin(WindDirection), MathF.Max(WindSpeed, 0.01f), Math.Clamp(Turbulence, 0f, 1f)),
            Sizes = new Float4(_sizes[0], _sizes[1], _sizes[2], _sizes[3]),
            Grid = new Float4(Resolution, MathF.Max(Gravity, 0.01f), period, Cascades),
        };
        _chop = Spectrum.Chop;
        _timeScale = Spectrum.TimeScale;
        _foamCoverage = FoamCoverage;
        _foamStrength = MathF.Max(FoamStrength, 0f);
        _foamFade = MathF.Max(FoamFade, 0f);
        ExtractDepth();
        ExtractRipples();
    }

    public override void Setup(FrameBuilder builder)
    {
        _initialHandle = builder.Write(_initial);
        _aHandle = builder.Write(_spectrumA);
        _bHandle = builder.Write(_spectrumB);
        _displacementHandle = builder.Write(Displacement);
        _slopesHandle = builder.Write(Slopes);
        _foamHandle = builder.Write(Foam);
        builder.ExportToDraws(_displacementHandle);
        builder.ExportToDraws(_slopesHandle);
        builder.ExportToDraws(_foamHandle);
        SetupDepth(builder);
        SetupRipples(builder);
    }

    public override void Render(FrameContext context)
    {
        CommandBuffer cmd = context.Command;
        double seconds = context.Time.Time * _timeScale;
        float period = _settings.Grid.Z;
        float time = (float)(period > 0f ? seconds - Math.Floor(seconds / period) * period : seconds);
        uint groups = (uint)(Resolution + 7) / 8;

        if (_built is not { } built || !built.Equals(_settings))
        {
            _properties.Clear();
            _properties.SetTexture("Ocean_Initial", context.Get(_initialHandle).ColorTextures[0]);
            cmd.SetComputeShader(_spectrum.Program);
            cmd.SetProperties(_properties);
            cmd.SetPushConstants(_settings);
            cmd.Dispatch(groups, groups, Cascades);
            cmd.ComputeBarrier();
            _built = _settings;
        }

        _properties.Clear();
        _properties.SetTexture("Ocean_Initial", context.Get(_initialHandle).ColorTextures[0]);
        _properties.SetTexture("Ocean_SpectrumA", context.Get(_aHandle).ColorTextures[0]);
        _properties.SetTexture("Ocean_SpectrumB", context.Get(_bHandle).ColorTextures[0]);
        cmd.SetComputeShader(_update.Program);
        cmd.SetProperties(_properties);
        cmd.SetPushConstants(new UpdatePush { Sizes = _settings.Sizes, Grid = _settings.Grid, Time = new Float4(time, _chop, 0f, 0f) });
        cmd.Dispatch(groups, groups, Cascades);
        cmd.ComputeBarrier();

        _properties.Clear();
        _properties.SetTexture("Ocean_SpectrumA", context.Get(_aHandle).ColorTextures[0]);
        _properties.SetTexture("Ocean_SpectrumB", context.Get(_bHandle).ColorTextures[0]);
        _properties.SetTexture("Ocean_Displacement", _displacementTop);
        _properties.SetTexture("Ocean_Slopes", _slopesTop);
        _properties.SetTexture("Ocean_Foam", _foamTop);
        cmd.SetComputeShader(_fft.Program);
        cmd.SetProperties(_properties);
        FftPush fft = new()
        {
            Size = (uint)Resolution,
            Reset = _foamStarted ? 0u : 1u,
            DeltaTime = Math.Clamp(context.Time.DeltaTime * _timeScale, 0f, 0.1f),
            Coverage = _foamCoverage,
            Strength = _foamStrength,
            Fade = _foamFade,
        };
        _foamStarted = true;
        cmd.SetPushConstants(fft);
        cmd.Dispatch(1, (uint)Resolution, Cascades);
        cmd.ComputeBarrier();
        cmd.SetPushConstants(fft with { Columns = 1 });
        cmd.Dispatch(1, (uint)Resolution, Cascades);
        cmd.GenerateMipmaps(Displacement);
        cmd.GenerateMipmaps(Slopes);
        cmd.GenerateMipmaps(Foam);
        RenderDepth(cmd);
        RenderRipples(cmd, context.Time.DeltaTime * _timeScale);
    }

    public void Dispose()
    {
        _initial.Dispose();
        _spectrumA.Dispose();
        _spectrumB.Dispose();
        _displacementTop.Dispose();
        _slopesTop.Dispose();
        _foamTop.Dispose();
        _depthTop.Dispose();
        State.Dispose();
        Levels.Dispose();
        Flow.Dispose();
        Clip.Dispose();
        _whitecaps[0].Dispose();
        _whitecaps[1].Dispose();
        Depth.Dispose();
        _ripples[0].Dispose();
        _ripples[1].Dispose();
        Foam.Dispose();
        Displacement.Dispose();
        Slopes.Dispose();
    }
}
