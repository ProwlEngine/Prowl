using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>A sphere moving through the water that makes ripples and a wake. Host thread.</summary>
public sealed class OceanInteractor
{
    public Double3 Position { get; set; }
    public float Radius { get; set; } = 1f;

    /// <summary>How fast it moves through the water, in m/s.</summary>
    public Float3 Velocity { get; set; }

    /// <summary>Scales how hard it pushes the water.</summary>
    public float Weight { get; set; } = 1f;
}

public sealed partial class OceanFeature
{
    public const int RippleSlices = 5;
    private const float RippleRate = 60f;
    private const int MostRippleSteps = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct RipplePush
    {
        public Float4 Slices, Viewer, Step, Sphere, Motion, Moved;
    }

    private readonly record struct Interaction(Float4 Sphere, Float4 Motion);

    private InternalCompute _rippleProgram = null!;
    private readonly Texture[] _ripples = new Texture[2];
    private readonly List<Interaction> _interactions = new();
    private Float2 _rippleSource;
    private Double3 _rippleViewer, _rippleSourceViewer;
    private double _rippleTime;
    private bool _ripplesStarted;
    private float _rippleDamping, _rippleGravity, _rippleShallow;
    private TextureHandle _rippleHandleA, _rippleHandleB;

    /// <summary>
    /// The ripples in the first depth slices, a layer per slice on the depth's texels: height, vertical velocity and
    /// the slopes dh/dx and dh/dz.
    /// </summary>
    public Texture Ripples => _ripples[0];

    /// <summary>Spheres pushing the water this frame. Host thread.</summary>
    public List<OceanInteractor> Interactors { get; } = new();

    /// <summary>The share of ripple motion lost each second.</summary>
    public float RippleDamping { get; set; } = 0.05f;

    private void CreateRipples(Renderer renderer)
    {
        _rippleProgram = renderer.Effects.Compute("Internal/OceanRipples.glsl");
        for (int i = 0; i < 2; i++)
        {
            _ripples[i] = renderer.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(DepthResolution, DepthResolution, 1, RippleSlices, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Storage | TextureUsage.Sampled));
            _ripples[i].Name = i == 0 ? "Ocean Ripples" : "Ocean Ripples Back";
        }
    }

    private void ExtractRipples()
    {
        _rippleDamping = MathF.Max(RippleDamping, 0f);
        _rippleGravity = MathF.Max(Gravity, 0.01f);
        _rippleShallow = Math.Clamp(ShallowAttenuation, 0f, 1f);
        _interactions.Clear();
        Double3 viewer = _rippleViewer = Viewer;
        foreach (OceanInteractor interactor in Interactors)
        {
            Double3 at = interactor.Position;
            Float4 sphere = new((float)(at.X - viewer.X), (float)(at.Y - SeaLevel), (float)(at.Z - viewer.Z), MathF.Max(interactor.Radius, 0.01f));
            Float3 velocity = interactor.Velocity;
            float submersion = Submersion(sphere.Y, sphere.W);
            if (submersion > 0f)
                _interactions.Add(new Interaction(sphere, new Float4(velocity.X, velocity.Y, velocity.Z, 3f * interactor.Weight * submersion)));
        }
    }

    // How deep a sphere sits: 1 when its centre is at the surface, fading as it sinks or rises out
    private static float Submersion(float height, float radius)
    {
        if (height < 0f)
        {
            float depth = -height / (2f * radius);
            return MathF.Exp(-depth * depth);
        }
        return MathF.Sqrt(MathF.Max(1f - height / radius, 0f));
    }

    private void SetupRipples(FrameBuilder builder)
    {
        _rippleHandleA = builder.Write(_ripples[0]);
        _rippleHandleB = builder.Write(_ripples[1]);
        builder.ExportToDraws(_rippleHandleA);
    }

    // Steps at a fixed rate, at least once a frame so the ripples follow the slices, an even number of times so the
    // result lands back in the first texture
    private void RenderRipples(CommandBuffer cmd, float deltaTime)
    {
        cmd.SetComputeShader(_rippleProgram.Program);
        uint groups = (DepthResolution + 7) / 8;
        if (!_ripplesStarted)
        {
            for (int i = 0; i < 2; i++)
                Dispatch(cmd, i, i, new RipplePush { Slices = new Float4(_depthSlices.X, DepthResolution, 2f, 0f) }, groups, groups, RippleSlices);
            _ripplesStarted = true;
            _rippleSource = _viewerWrapped;
            _rippleSourceViewer = _rippleViewer;
        }

        _rippleTime += Math.Clamp(deltaTime, 0f, 0.25f);
        int steps = Math.Min((int)Math.Floor(_rippleTime * RippleRate), MostRippleSteps);
        _rippleTime = Math.Max(_rippleTime - steps / RippleRate, 0);
        int dispatches = Math.Max(steps, 1);
        dispatches += dispatches % 2;

        for (int s = 0; s < dispatches; s++)
        {
            float dt = s < steps ? 1f / RippleRate : 0f;
            int source = s % 2, target = 1 - source;
            Float4 viewer = new(_viewerWrapped.X, _viewerWrapped.Y, s == 0 ? _rippleSource.X : _viewerWrapped.X, s == 0 ? _rippleSource.Y : _viewerWrapped.Y);
            Float4 step = new(dt, _rippleGravity, _rippleDamping, _rippleShallow);
            Float4 moved = s == 0 ? new Float4((float)(_rippleViewer.X - _rippleSourceViewer.X), (float)(_rippleViewer.Z - _rippleSourceViewer.Z), 0f, 0f) : default;
            Dispatch(cmd, source, target, new RipplePush { Slices = new Float4(_depthSlices.X, DepthResolution, 0f, 0f), Viewer = viewer, Step = step, Moved = moved }, groups, groups, RippleSlices);
            if (dt <= 0f)
                continue;
            foreach (Interaction interaction in _interactions)
            {
                int slice = PushSlice(interaction.Sphere);
                float texel = _depthSlices.X * MathF.Pow(2f, slice) / DepthResolution;
                float reach = 3f * (interaction.Sphere.W + texel) + 12f * texel;
                uint footprint = (uint)Math.Clamp(MathF.Ceiling(2f * reach / texel / 8f), 1f, groups);
                RipplePush push = new()
                {
                    Slices = new Float4(_depthSlices.X, DepthResolution, 1f, slice),
                    Viewer = viewer,
                    Step = step,
                    Sphere = interaction.Sphere,
                    Motion = interaction.Motion,
                };
                Dispatch(cmd, source, target, push, footprint, footprint, 1);
            }
        }
        _rippleSource = _viewerWrapped;
        _rippleSourceViewer = _rippleViewer;
    }

    // The finest slice holding all a sphere pushes, which alone takes the push so it is as strong at any distance
    private int PushSlice(Float4 sphere)
    {
        float extent = MathF.Max(MathF.Abs(sphere.X), MathF.Abs(sphere.Z));
        for (int slice = 0; slice < RippleSlices - 1; slice++)
        {
            float width = _depthSlices.X * MathF.Pow(2f, slice);
            if (extent + 3f * (sphere.W + width / DepthResolution) < 0.4f * width)
                return slice;
        }
        return RippleSlices - 1;
    }

    private void Dispatch(CommandBuffer cmd, int source, int target, in RipplePush push, uint x, uint y, uint z)
    {
        _properties.Clear();
        _properties.SetTexture("Ocean_RippleSource", _ripples[source]);
        _properties.SetTexture("Ocean_RippleTarget", _ripples[target]);
        _properties.SetTexture("Ocean_Depth", _depthTop);
        _properties.SetTexture("Ocean_Level", Levels);
        _properties.SetTexture("Ocean_Flow", Flow);
        cmd.SetProperties(_properties);
        cmd.SetPushConstants(push);
        cmd.Dispatch(x, y, z);
        cmd.ComputeBarrier();
    }
}
