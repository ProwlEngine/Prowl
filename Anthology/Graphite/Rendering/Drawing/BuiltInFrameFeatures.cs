using System;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The names the renderer's built in stages provide and its passes read. A replacement keeping the built in consumers
/// provides the same names: a frame feature by exporting a resource of that id, a view feature by publishing that name.
/// </summary>
public static class RendererOutputs
{
    /// <summary>The geometry streams deforming copies are written into.</summary>
    public static readonly RenderResourceID DeformedVertices = RenderResourceID.Intern("Graphite.Deform.Vertices");

    /// <summary>The per object light probe harmonics.</summary>
    public static readonly RenderResourceID ObjectProbes = RenderResourceID.Intern("Graphite.LightProbes.Objects");

    /// <summary>A world's shadow pages and its shadow face records.</summary>
    public static readonly RenderResourceID ShadowPages = RenderResourceID.Intern("Graphite.Shadows.Pages");
    public static readonly RenderResourceID ShadowFaces = RenderResourceID.Intern("Graphite.Shadows.Faces");

    /// <summary>A world's voxel volume, see Graphite/VoxelData.glsl.</summary>
    public static readonly RenderResourceID Voxels = RenderResourceID.Intern("Graphite.Voxels");

    /// <summary>A world's prefiltered probe cubes and its sky ambient harmonics.</summary>
    public static readonly RenderResourceID CaptureCubes = RenderResourceID.Intern("Graphite.Captures.Cubes");
    public static readonly RenderResourceID SkyAmbient = RenderResourceID.Intern("Graphite.Captures.SkyAmbient");

    /// <summary>The staging copies the host's readback callbacks read.</summary>
    public static readonly RenderResourceID ReadbackStaging = RenderResourceID.Intern("Graphite.Readback.Staging");

    /// <summary>The view's cleared scene: colour, depth and G-buffer in one texture.</summary>
    public const string Scene = "Graphite.Scene";

    /// <summary>A deferred view's GBuffer, its targets in the order of <see cref="GBufferTarget"/>, with its own depth.</summary>
    public const string GBuffer = "Graphite.GBuffer";

    /// <summary>
    /// The GBuffer's Base, Normal and Surface targets with the world's decals applied, which readers take in place of
    /// the GBuffer's own while the world has decals.
    /// </summary>
    public const string GBufferDecals = "Graphite.GBuffer.Decals";

    /// <summary>
    /// A displaced deferred view's source offsets: in rg how far away, in pixels, the pixel each pixel now shows came
    /// from, which the GBuffer's own targets are read at. See <see cref="DisplacementFeature"/>.
    /// </summary>
    public const string GBufferOffset = "Graphite.GBuffer.Offset";

    /// <summary>
    /// A deferred view's screen space reflections: in rgb the light its top specular lobe reflects, in a how far the
    /// trace is trusted over the reflection probes. See <see cref="ScreenSpaceReflections"/>.
    /// </summary>
    public const string Reflections = "Graphite.Reflections";

    /// <summary>
    /// A deferred view's screen space mask over the sun's shadow: in r how much of the sun still reaches each pixel
    /// past what its shadow map says, which the lighting dims the sun by.
    /// </summary>
    public const string SunShadowMask = "Graphite.SunShadowMask";

    /// <summary>A texture of the view's cascade depth, once its cascades are drawn.</summary>
    public const string Cascades = "Graphite.Cascades";

    /// <summary>The view's nearest depth pyramid, built after its prepass while a feature reads it. See Graphite/HiZ.glsl.</summary>
    public const string HiZ = "Graphite.HiZ";
}

/// <summary>Fills the new deformed copies and deforms the ones whose bones or blend shape weights changed. Group <see cref="FeatureGroup.Deform"/>.</summary>
public sealed class DeformFeature : FrameFeature
{
    internal DeformFeature() : base(FrameSlot.Deform, group: FeatureGroup.Deform) { }

    internal override bool IsBuiltIn => true;

    public override void Setup(FrameBuilder builder) => throw new NotSupportedException();

    public override void Render(FrameContext context) => throw new NotSupportedException();

    internal override void AddBuiltIn(FramePipeline frame, WorldFrame? world)
    {
        frame.Deform.Add(frame.Graph);
        frame.Exports.Provide(RendererOutputs.DeformedVertices, null);
    }
}

/// <summary>Samples a world's light probes for its objects' requests. Group <see cref="FeatureGroup.LightProbes"/>.</summary>
public sealed class LightProbeFeature : FrameFeature
{
    internal LightProbeFeature() : base(FrameSlot.LightProbes, group: FeatureGroup.LightProbes) { }

    internal override bool IsBuiltIn => true;

    public override void Setup(FrameBuilder builder) => throw new NotSupportedException();

    public override void Render(FrameContext context) => throw new NotSupportedException();

    internal override void AddBuiltIn(FramePipeline frame, WorldFrame? world)
    {
        frame.Probes.Add(frame.Graph, world!.World.Probes);
        frame.Exports.Provide(RendererOutputs.ObjectProbes, world.World);
    }
}

/// <summary>Draws a world's local light shadow pages. Group <see cref="FeatureGroup.LocalShadows"/>.</summary>
public sealed class LocalShadowFeature : FrameFeature
{
    internal LocalShadowFeature() : base(FrameSlot.Shadows, group: FeatureGroup.LocalShadows) { }

    internal override bool IsBuiltIn => true;

    public override void Setup(FrameBuilder builder) => throw new NotSupportedException();

    public override void Render(FrameContext context) => throw new NotSupportedException();

    internal override void AddBuiltIn(FramePipeline frame, WorldFrame? world)
    {
        ShadowPasses.Add(frame, frame.Graph, world!);
        frame.Exports.Provide(RendererOutputs.ShadowPages, world!.World);
        frame.Exports.Provide(RendererOutputs.ShadowFaces, world.World);
    }
}

/// <summary>Fills a world's voxel volume while it has one, see <see cref="RenderWorld.SetVoxelVolume"/>. Group <see cref="FeatureGroup.Voxelization"/>.</summary>
public sealed class VoxelizationFeature : FrameFeature
{
    internal VoxelizationFeature() : base(FrameSlot.Shadows, order: 100, group: FeatureGroup.Voxelization) { }

    internal override bool IsBuiltIn => true;

    public override void Setup(FrameBuilder builder) => throw new NotSupportedException();

    public override void Render(FrameContext context) => throw new NotSupportedException();

    internal override void AddBuiltIn(FramePipeline frame, WorldFrame? world)
    {
        frame.AddVoxels(world!);
        frame.Exports.Provide(RendererOutputs.Voxels, world!.World);
    }
}

/// <summary>Prefilters a world's captured cubes and projects its sky capture onto harmonics. Group <see cref="FeatureGroup.CaptureFill"/>.</summary>
public sealed class CaptureFillFeature : FrameFeature
{
    internal CaptureFillFeature() : base(FrameSlot.Captures, group: FeatureGroup.CaptureFill) { }

    internal override bool IsBuiltIn => true;

    public override void Setup(FrameBuilder builder) => throw new NotSupportedException();

    public override void Render(FrameContext context) => throw new NotSupportedException();

    internal override void AddBuiltIn(FramePipeline frame, WorldFrame? world)
    {
        frame.AddFills(world!);
        frame.Exports.Provide(RendererOutputs.CaptureCubes, world!.World);
        frame.Exports.Provide(RendererOutputs.SkyAmbient, world.World);
    }
}

/// <summary>Copies the host's requested textures and buffers into staging buffers. Group <see cref="FeatureGroup.Readback"/>.</summary>
public sealed class ReadbackFeature : FrameFeature
{
    internal ReadbackFeature() : base(FrameSlot.Readback, group: FeatureGroup.Readback) { }

    internal override bool IsBuiltIn => true;

    public override void Setup(FrameBuilder builder) => throw new NotSupportedException();

    public override void Render(FrameContext context) => throw new NotSupportedException();

    internal override void AddBuiltIn(FramePipeline frame, WorldFrame? world)
    {
        frame.AddReadbacks();
        frame.Exports.Provide(RendererOutputs.ReadbackStaging, null);
    }
}
