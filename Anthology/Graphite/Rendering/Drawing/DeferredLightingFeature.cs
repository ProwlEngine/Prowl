using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Lights a deferred view's GBuffer onto its scene before the forward passes, which then draw only the renderables the
/// GBuffer does not hold, in one fullscreen draw of the program <see cref="ShadingModels"/> writes, which lights each
/// pixel by its shading model, at the view's PBR tier. Where the view traced <see cref="RendererOutputs.Reflections"/>
/// they take the reflection probes' place in the top specular lobe. A view whose tier's program is still compiling is left out. It writes the scene's colour and, where the scene has one, its material target, which
/// screen space reflections read, and reads the scene's depth beside the GBuffer.
/// Group <see cref="FeatureGroup.DeferredLighting"/>.
/// </summary>
public sealed class DeferredLightingFeature : RenderFeature
{
    private sealed class State : IDisposable
    {
        public GBufferRead GBuffer;
        public TextureHandle Scene;
        public TextureHandle? Reflections;
        public readonly TargetSets Sets = new();

        public void Dispose() => Sets.Dispose();
    }

    private readonly RendererResources _resources;
    private static readonly PropertyID s_contactShadows = "Graphite_ContactShadows", s_reflections = "Graphite_Reflections",
        s_hasReflections = "Graphite_HasReflections";

    private readonly PropertySet _properties = new();
    private readonly Action<FullscreenProgram, FullscreenProgram> _replaced;

    internal DeferredLightingFeature(RendererResources resources)
        : base(RenderSlot.Opaque, -100, FeatureGroup.DeferredLighting)
    {
        _resources = resources;
        _replaced = ReplaceProgram;
        Paths = RenderPaths.Deferred;
    }

    public const string TierAxis = "PBR_TIER";

    public override void PrepareFrame() => _resources.ShadingModels.Update(_replaced);

    public override bool IsActive(in FeatureView view) => Program(view) != null;

    private FullscreenProgram? Program(in FeatureView view)
        => _resources.ShadingModels.For(view.Keyword(TierAxis), view.ContactShadows.Length > 0, view.LayerCount);

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        State state = builder.GetViewState<State>();
        state.GBuffer = GBufferRead.Declare(builder);
        state.Reflections = builder.Has(RendererOutputs.Reflections) ? builder.Read(RendererOutputs.Reflections) : null;
        builder.ReadViews();
        builder.DrawsRenderables();
        state.Scene = builder.WriteSceneTargets();
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        state.Sets.Age();
        RenderTexture scene = context.Get(state.Scene);
        context.CopyBack(scene);
        state.GBuffer.Bind(context, _properties, _resources.PointClamp, scene);
        ContactShadows contact = context.View.ContactShadows;
        _properties.SetFloat4(s_contactShadows, new Float4(MathF.Max(contact.Length, 0), Math.Clamp(contact.Steps, 1, 64), MathF.Max(contact.Thickness, 0), contact.LocalLights ? 1 : 0));
        _properties.SetTexture(s_reflections, state.Reflections is { } reflections ? context.Get(reflections).ColorTextures[0] : state.GBuffer.Own(context)[0], _resources.PointClamp);
        _properties.SetFloat(s_hasReflections, state.Reflections != null ? 1 : 0);
        Texture[] targets = scene.ColorTextures;
        Framebuffer framebuffer = targets.Length > SceneTarget.Material
            ? state.Sets.Get(_resources.Factory, context.View.LayerCount, null, targets[SceneTarget.Color], targets[SceneTarget.Material])
            : state.Sets.Get(_resources.Factory, context.View.LayerCount, null, targets[SceneTarget.Color]);
        if (Program(context.View) is { } program)
            context.Fullscreen(program, framebuffer, _properties);
    }
}
