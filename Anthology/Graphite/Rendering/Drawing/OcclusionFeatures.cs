using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Builds the farthest depth pyramid of a view that culls what is hidden, once its early prepass is drawn, in two
/// dispatches, and publishes it as <see cref="Output"/> for <see cref="OcclusionCullFeature"/>. Levels 1 to
/// <see cref="CameraFrame.OcclusionLevels"/> lie side by side in one texture, see Graphite/Internal/OcclusionLevels.glsl.
/// </summary>
public sealed class OcclusionHiZFeature : RenderFeature
{
    /// <summary>The name the pyramid is published under.</summary>
    public const string Output = "Graphite.OcclusionHiZ";

    private static readonly PropertyID s_depth = "HiZ_Depth", s_packed = "HiZ_Packed";
    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    private sealed class State
    {
        public TextureHandle Packed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public int Width, Height, BaseLevel, Levels;
    }

    // Each level the screen halved rounding up, side by side, as Graphite/Internal/OcclusionLevels.glsl lays them out
    private static int PackedWidth(int width)
    {
        int packed = 0;
        for (int level = 1; level <= CameraFrame.OcclusionLevels; level++)
            packed += (width + (1 << level) - 1) >> level;
        return packed;
    }

    internal OcclusionHiZFeature(RendererResources resources) : base(RenderSlot.Prepass, 10, runsOn: ViewKinds.All)
    {
        _resources = resources;
    }

    public override bool HasWork(in FeatureView view) => view.Occludes;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        GraphTextureDesc desc = view.Texture(PixelFormat.R32_Float) with { Width = PackedWidth((int)view.Width), Height = ((int)view.Height + 1) / 2 };
        State state = builder.GetViewState<State>();
        state.Packed = builder.Temporary("Packed", desc, TextureState.Storage | TextureState.Sampled);
        builder.Publish(Output, state.Packed);
    }

    public override void Render(FeatureContext context)
    {
        Texture depth = context.Depth!;
        Texture packed = context.Get(context.GetViewState<State>().Packed).ColorTextures[0];
        CommandBuffer cmd = context.Command;
        cmd.SetComputeShader(_resources.OcclusionHiZProgram.Program);
        _properties.SetTexture(s_depth, depth, _resources.PointClamp);
        _properties.SetTexture(s_packed, packed);
        cmd.SetProperties(_properties);
        uint width = context.View.Width, height = context.View.Height;
        cmd.SetPushConstants(new Push { Width = (int)width, Height = (int)height, BaseLevel = 0, Levels = CameraFrame.OcclusionLevels });
        cmd.Dispatch((width + 63) / 64, (height + 63) / 64, 1);

        // Levels 7 and up from level 6, written by the dispatch before
        cmd.ComputeBarrier();
        cmd.SetPushConstants(new Push { Width = (int)width, Height = (int)height, BaseLevel = 6, Levels = CameraFrame.OcclusionLevels });
        uint level6Width = (width + 63) / 64, level6Height = (height + 63) / 64;
        cmd.Dispatch((level6Width + 63) / 64, (level6Height + 63) / 64, 1);
    }
}

/// <summary>
/// The late GPU cull of a view that culls what is hidden: tests what the early cull found in the frustum against the
/// farthest depth pyramid, and adds what it left out and is in view to the late prepass and the passes after it. See
/// Graphite/Internal/CullLate.glsl.
/// </summary>
public sealed class OcclusionCullFeature : RenderFeature
{
    private readonly RendererResources _resources;

    private sealed class State
    {
        public TextureHandle HiZ;
    }

    internal OcclusionCullFeature(RendererResources resources) : base(RenderSlot.Prepass, 20, runsOn: ViewKinds.All)
    {
        _resources = resources;
    }

    public override bool HasWork(in FeatureView view) => view.Occludes;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.GetViewState<State>().HiZ = builder.Read(OcclusionHiZFeature.Output);
        builder.WriteLateCull();
    }

    public override void Render(FeatureContext context)
        => context.CullLate(context.Get(context.GetViewState<State>().HiZ).ColorTextures[0], _resources.PointClamp);
}

/// <summary>Draws the prepass of the renderables a view's late cull found in view, after <see cref="OcclusionCullFeature"/>.</summary>
public sealed class LatePrepassFeature : RenderFeature
{
    /// <summary>Its order in the Prepass slot. Renderables drawn before it in a view that culls what is hidden miss the late cull's.</summary>
    public const int LateOrder = 30;

    internal LatePrepassFeature() : base(RenderSlot.Prepass, LateOrder, runsOn: ViewKinds.All) { }

    public override bool HasWork(in FeatureView view) => view.Occludes;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.DrawsRenderables();
        builder.DrawToScene();
    }

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        context.DrawPrepassLate();
    }
}
