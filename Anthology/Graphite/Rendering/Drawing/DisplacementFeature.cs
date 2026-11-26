using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Screen space displacement of a deferred view: the surfaces whose GBuffer pass kept a height, the Lit model's with a
/// height_scale, are pushed out along their normals by it, as a grid of the view's own pixels drawn again with a depth
/// test. It runs once the GBuffer and its decals are drawn and leaves the scene's depth, normal and motion as the
/// lifted surfaces have them, so everything after it sees them, and publishes how far each pixel came from as
/// <see cref="RendererOutputs.GBufferOffset"/> for the lighting to read the rest of the GBuffer at. It adds its passes
/// on the frames the view draws such a surface. <see cref="RenderView.DisplacementCell"/> sets the grid, 0 for none.
/// Group <see cref="FeatureGroup.Displacement"/>.
/// </summary>
public sealed class DisplacementFeature : RenderFeature
{
    private static readonly PropertyID s_surface = "_Surface", s_custom = "_Custom", s_normal = "_Normal", s_motion = "_Motion",
        s_depth = "_Depth", s_height = "_Height", s_gridDepth = "_GridDepth", s_grid = "_Grid", s_fade = "_Fade";

    private sealed class State : IDisposable
    {
        public TextureHandle GBuffer, Displaced, Height, Scene;
        public TextureHandle? Decals;
        public readonly TargetSets Sets = new();

        public void Dispose() => Sets.Dispose();
    }

    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    internal DisplacementFeature(RendererResources resources)
        : base(RenderSlot.GBuffer, 75, FeatureGroup.Displacement)
    {
        _resources = resources;
        Paths = RenderPaths.Deferred;
        Uses(resources.DisplacementFillProgram);
        Uses(resources.DisplacementGridProgram);
        Uses(resources.DisplacementDepthProgram);
    }

    public override bool IsActive(in FeatureView view) => view.DisplacementCell > 0;

    public override bool HasWork(in FeatureView view) => view.HasDisplacement;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        State state = builder.GetViewState<State>();
        state.GBuffer = builder.Read(RendererOutputs.GBuffer);
        state.Decals = builder.Has(RendererOutputs.GBufferDecals) ? builder.Read(RendererOutputs.GBufferDecals) : null;
        builder.ReadViews();
        uint layers = view.LayerCount > 1 ? (uint)view.LayerCount : 0;
        state.Displaced = builder.Temporary("Displaced", new GraphTextureDesc
        {
            Width = (int)view.Width,
            Height = (int)view.Height,
            ColorFormats = [PixelFormat.R16_G16_Float],
            EnableDepth = true,
            DepthFormat = _resources.SceneDepthFormat,
            ArrayLayers = layers,
        });
        state.Height = builder.Temporary("Height", new GraphTextureDesc
        {
            Width = (int)view.Width,
            Height = (int)view.Height,
            ColorFormats = [PixelFormat.R16_G16_B16_A16_Float],
            ArrayLayers = layers,
        });
        state.Scene = builder.WriteSceneTargets();
        builder.Publish(RendererOutputs.GBufferOffset, state.Displaced);
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        state.Sets.Age();
        Texture[] own = context.Get(state.GBuffer).ColorTextures;
        Texture[]? decaled = state.Decals is { } decals ? context.Get(decals).ColorTextures : null;
        if (own.Length <= GBufferRead.OwnMotion)
            return;
        RenderTexture displaced = context.Get(state.Displaced);
        RenderTexture scene = context.Get(state.Scene);
        context.CopyBack(scene);
        Texture offset = displaced.ColorTextures[0], height = context.Get(state.Height).ColorTextures[0];
        Texture sceneNormal = scene.ColorTextures[SceneTarget.Normals], sceneMotion = scene.ColorTextures[SceneTarget.Motion];
        Texture normal = decaled is { Length: > GBufferRead.DecaledNormal } ? decaled[GBufferRead.DecaledNormal] : own[GBufferRead.OwnNormal];
        Sampler point = _resources.PointClamp;
        int layers = context.View.LayerCount;
        AttachmentOps cleared = AttachmentOps.Clear(new Color(0f, 0f, 0f, 0f));

        _properties.SetTexture(s_surface, own[1], point);
        _properties.SetTexture(s_custom, own[2], point);
        _properties.SetTexture(s_normal, normal, point);
        _properties.SetTexture(s_motion, own[GBufferRead.OwnMotion], point);
        _properties.SetTexture(s_depth, scene.DepthTexture!, point);
        _properties.SetFloat4(s_fade, new Float4(Math.Clamp(context.View.DisplacementCreaseBlend, 0, 8), 0, 0, 0));
        Framebuffer fill = state.Sets.Get(_resources.Factory, layers, null, offset, sceneNormal, sceneMotion, height);
        context.Fullscreen(_resources.DisplacementFillProgram, fill, _properties,
            new TargetLoadStoreOps(cleared, AttachmentOps.Loaded).WithColorLoad(1, LoadAction.Load).WithColorLoad(2, LoadAction.Load));

        int cell = Math.Clamp(context.View.DisplacementCell, 1, 16);
        int across = ((int)context.View.Width - 1 + cell - 1) / cell, down = ((int)context.View.Height - 1 + cell - 1) / cell;
        if (across > 0 && down > 0)
        {
            _properties.SetTexture(s_depth, scene.DepthTexture!, point);
            _properties.SetTexture(s_height, height, _resources.LinearClamp);
            _properties.SetFloat4(s_grid, new Float4(cell, across, 0, 0));
            Framebuffer grid = state.Sets.Get(_resources.Factory, layers, displaced.DepthTexture, offset, sceneNormal, sceneMotion);
            context.DrawProcedural(_resources.DisplacementGridProgram, grid, (uint)(across * down * 6), _properties,
                new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Clear(0f)));
        }

        _properties.SetTexture(s_gridDepth, displaced.DepthTexture!, point);
        Framebuffer depth = state.Sets.Get(_resources.Factory, layers, scene.DepthTexture, height);
        context.Fullscreen(_resources.DisplacementDepthProgram, depth, _properties);
    }
}
