using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

public sealed partial class OceanFeature
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WhitecapPush
    {
        public Float4 Sizes, Depth, Shore, Tile, Moved, Rule;
    }

    private InternalCompute _whitecapProgram = null!;
    private readonly Texture[] _whitecaps = new Texture[2];
    private WhitecapPush _whitecapBase;
    private Double3 _whitecapViewer;
    private Float2 _whitecapWrapped;
    private bool _whitecapsStarted;

    /// <summary>The whitecaps on the depth's slices, a foam simulation in world space, 0 for none and 1 for full.</summary>
    public Texture Whitecaps => _whitecaps[0];

    private void CreateWhitecaps(Renderer renderer)
    {
        _whitecapProgram = renderer.Effects.Compute("Internal/OceanWhitecaps.glsl");
        ushort[] empty = new ushort[DepthResolution * DepthResolution];
        for (int i = 0; i < 2; i++)
        {
            _whitecaps[i] = renderer.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(DepthResolution, DepthResolution, 1, DepthSlices, PixelFormat.R16_Float, TextureUsage.Storage | TextureUsage.Sampled));
            _whitecaps[i].Name = i == 0 ? "Ocean Whitecaps" : "Ocean Whitecaps Back";
            for (uint slice = 0; slice < DepthSlices; slice++)
                renderer.Device.UpdateTexture<ushort>(_whitecaps[i], empty, TextureRegion.Whole(_whitecaps[i], 0, slice));
        }
    }

    // The step's settings, and how far the viewer moved since the last step
    private void ExtractWhitecaps()
    {
        Double3 viewer = Viewer;
        Float4 parameters = DepthParams;
        Float2 wrapped = new(parameters.X, parameters.Y);
        double largest = CascadeSize(0);
        Float2 moved = _whitecapsStarted ? new Float2((float)(viewer.X - _whitecapViewer.X), (float)(viewer.Z - _whitecapViewer.Z)) : Float2.Zero;
        Float2 previous = _whitecapsStarted ? _whitecapWrapped : wrapped;
        _whitecapBase = new WhitecapPush
        {
            Sizes = new Float4(CascadeSize(0), CascadeSize(1), CascadeSize(2), CascadeSize(3)),
            Depth = parameters,
            Shore = new Float4(Math.Clamp(ShallowAttenuation, 0f, 1f), DepthSlices, RippleSlices, Resolution),
            Tile = new Float4((float)(viewer.X - Math.Floor(viewer.X / largest) * largest), (float)(viewer.Z - Math.Floor(viewer.Z / largest) * largest), previous.X, previous.Y),
            Moved = new Float4(moved.X, moved.Y, MathF.Max(RippleFoam, 0f), 0f),
            Rule = new Float4(0f, FoamCoverage, MathF.Max(FoamStrength, 0f), MathF.Max(FoamFade, 0f)),
        };
        _whitecapViewer = viewer;
        _whitecapWrapped = wrapped;
        _whitecapsStarted = true;
    }

    // Steps from the first texture into the second, then moves the result back without time
    private void RenderWhitecaps(CommandBuffer cmd, PropertySet properties, float deltaTime)
    {
        uint groups = (DepthResolution + 7) / 8;
        for (int pass = 0; pass < 2; pass++)
        {
            properties.SetTexture("Ocean_WhitecapSource", _whitecaps[pass]);
            properties.SetTexture("Ocean_WhitecapTarget", _whitecaps[1 - pass]);
            cmd.SetProperties(properties);
            WhitecapPush push = pass == 0
                ? _whitecapBase with { Rule = _whitecapBase.Rule with { X = Math.Clamp(deltaTime, 0f, 0.1f) } }
                : _whitecapBase with { Tile = _whitecapBase.Tile with { Z = _whitecapBase.Depth.X, W = _whitecapBase.Depth.Y }, Moved = _whitecapBase.Moved with { X = 0f, Y = 0f } };
            cmd.SetPushConstants(push);
            cmd.Dispatch(groups, groups, DepthSlices);
            cmd.ComputeBarrier();
        }
    }
}
