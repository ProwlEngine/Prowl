using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>The targets a GBuffer pass draws, in the order of its outputs. See Graphite/GBuffer.glsl.</summary>
public enum GBufferTarget
{
    /// <summary>Base colour in rgb, metalness in a.</summary>
    Base,

    /// <summary>The world normal, octahedral: the scene's own normal target where it has one.</summary>
    Normal,

    /// <summary>Roughness, specular and occlusion, then the shading model, light layers and shadow flag in a.</summary>
    Surface,

    /// <summary>Four values the shading model reads its own way.</summary>
    Custom,

    /// <summary>The diffuse light that is not realtime, pre-exposed.</summary>
    Baked,

    /// <summary>Pre-exposed emission.</summary>
    Emission,

    /// <summary>How far the surface moved across the screen since last frame, in uv: the scene's own motion target where it has one.</summary>
    Motion,
}

/// <summary>
/// Framebuffers over targets of more than one texture, such as a GBuffer's beside the scene's, kept while the same
/// targets come back and let go a few frames after they stop.
/// </summary>
public sealed class TargetSets : IDisposable
{
    private const int Frames = 8;

    private readonly List<(Texture?[] Targets, Framebuffer Framebuffer, int Age)> _sets = [];

    /// <summary>The framebuffer over a depth target, or none, and colour targets in order.</summary>
    public Framebuffer Get(ResourceFactory factory, int layers, Texture? depth, params ReadOnlySpan<Texture> colors)
    {
        for (int i = 0; i < _sets.Count; i++)
        {
            Texture?[] targets = _sets[i].Targets;
            if (targets.Length != colors.Length + 1 || targets[0] != depth)
                continue;
            bool same = true;
            for (int c = 0; c < colors.Length && same; c++)
                same = targets[c + 1] == colors[c];
            if (!same)
                continue;
            _sets[i] = (targets, _sets[i].Framebuffer, 0);
            return _sets[i].Framebuffer;
        }
        Texture[] attached = colors.ToArray();
        Framebuffer framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depth, attached) { ViewCount = layers > 1 ? (uint)layers : 0 });
        _sets.Add(([depth, .. attached], framebuffer, 0));
        return framebuffer;
    }

    /// <summary>Once a frame: lets go of the framebuffers nothing asked for lately.</summary>
    public void Age()
    {
        for (int i = _sets.Count - 1; i >= 0; i--)
        {
            if (_sets[i].Age < Frames)
            {
                _sets[i] = (_sets[i].Targets, _sets[i].Framebuffer, _sets[i].Age + 1);
                continue;
            }
            _sets[i].Framebuffer.Dispose();
            _sets.RemoveAt(i);
        }
    }

    public void Dispose()
    {
        foreach ((_, Framebuffer framebuffer, _) in _sets)
            framebuffer.Dispose();
        _sets.Clear();
    }
}

/// <summary>
/// Draws the GBuffer pass of a deferred view's opaque renderables, the shader pass tagged "LightMode" = "GBuffer", and
/// publishes the view's GBuffer as <see cref="RendererOutputs.GBuffer"/>. The pass draws against the scene's depth, and
/// writes its normal and motion into the scene's own targets where the scene has them, so the features after it read
/// the scene as they do in a forward view. The GBuffer itself holds the rest, with a normal and a motion target of its
/// own only where the scene has none. Renderables whose shader has no such pass are left to the forward passes. The
/// surfaces the prepass drew test depth equal, see <see cref="EarlyDepth"/>. While the view lays decals over its
/// GBuffer the normals go to a target of the GBuffer's own, which the decals then write onto the scene's, and while
/// it displaces its GBuffer both normals and motion do, for <see cref="DisplacementFeature"/> to put on the scene. Group
/// <see cref="FeatureGroup.GBuffer"/>.
/// </summary>
public sealed class GBufferFeature : RenderFeature
{
    /// <summary>The targets a GBuffer pass draws.</summary>
    public const int TargetCount = 7;

    private sealed class State : IDisposable
    {
        public TextureHandle Target, Scene;
        public readonly TargetSets Sets = new();

        public void Dispose() => Sets.Dispose();
    }

    private readonly RendererResources _resources;

    internal GBufferFeature(RendererResources resources)
        : base(RenderSlot.GBuffer, group: FeatureGroup.GBuffer, passes: [PassType.GBuffer])
    {
        _resources = resources;
        Paths = RenderPaths.Deferred;
    }

    public override bool IsActive(in FeatureView view) => _resources.SupportsGBuffer;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        State state = builder.GetViewState<State>();
        builder.DrawsRenderables();
        int scene = builder.SceneTargetCount;
        PixelFormat[] formats = _resources.GBufferFormats;
        List<PixelFormat> own =
        [
            formats[(int)GBufferTarget.Base], formats[(int)GBufferTarget.Surface], formats[(int)GBufferTarget.Custom],
            formats[(int)GBufferTarget.Baked], formats[(int)GBufferTarget.Emission],
        ];
        if (scene <= SceneTarget.Normals || view.HasDisplacement || (view.HasDecals && builder.Runs(FeatureGroup.GBufferDecals)))
            own.Add(formats[(int)GBufferTarget.Normal]);
        if (scene <= SceneTarget.Motion || view.HasDisplacement)
            own.Add(formats[(int)GBufferTarget.Motion]);
        GraphTextureDesc desc = new()
        {
            Width = (int)view.Width,
            Height = (int)view.Height,
            ColorFormats = [.. own],
            ArrayLayers = view.LayerCount > 1 ? (uint)view.LayerCount : 0,
        };
        state.Target = builder.Temporary("GBuffer", desc);
        state.Scene = builder.WriteSceneTargets();
        builder.Publish(RendererOutputs.GBuffer, state.Target);
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        state.Sets.Age();
        Texture[] own = context.Get(state.Target).ColorTextures;
        RenderTexture scene = context.Get(state.Scene);
        context.CopyBack(scene);
        bool ownNormal = own.Length > GBufferRead.OwnNormal, ownMotion = own.Length > GBufferRead.OwnMotion;
        Texture normal = ownNormal ? own[GBufferRead.OwnNormal] : scene.ColorTextures[SceneTarget.Normals];
        Framebuffer framebuffer = state.Sets.Get(_resources.Factory, context.View.LayerCount, scene.DepthTexture,
            own[0], normal, own[1], own[2], own[3], own[4], ownMotion ? own[GBufferRead.OwnMotion] : scene.ColorTextures[SceneTarget.Motion]);

        // The GBuffer's own targets start undefined but for Surface, whose clear says no surface. The scene's are kept.
        TargetLoadStoreOps ops = new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded)
            .WithColorLoad((int)GBufferTarget.Surface, LoadAction.Clear);
        if (!ownNormal)
            ops = ops.WithColorLoad((int)GBufferTarget.Normal, LoadAction.Load);
        if (!ownMotion)
            ops = ops.WithColorLoad((int)GBufferTarget.Motion, LoadAction.Load);
        context.Command.SetFramebuffer(framebuffer, ops);
        context.Command.SetFullViewport();
        context.DrawPass(PassType.GBuffer);
    }
}

/// <summary>
/// A feature's read of the view's GBuffer as Graphite/GBuffer.glsl names its targets: the GBuffer's own, the Base and
/// Surface ones from <see cref="RendererOutputs.GBufferDecals"/> while the world has decals, and the scene's depth,
/// with its normal and motion where the scene has them.
/// </summary>
public struct GBufferRead
{
    /// <summary>The indices of a GBuffer texture's own normal and motion targets, where it has them.</summary>
    public const int OwnNormal = 5, OwnMotion = 6;

    /// <summary>The index of the normal among the decals' targets, where the scene has none for them to write.</summary>
    public const int DecaledNormal = 2;

    private static readonly PropertyID s_base = "Graphite_GBufferBase", s_normal = "Graphite_GBufferNormal", s_surface = "Graphite_GBufferSurface",
        s_custom = "Graphite_GBufferCustom", s_baked = "Graphite_GBufferBaked", s_emission = "Graphite_GBufferEmission",
        s_motion = "Graphite_GBufferMotion", s_depth = "Graphite_GBufferDepth";

    private static readonly PropertyID s_offset = "Graphite_GBufferOffset", s_displaced = "Graphite_GBufferDisplaced";

    private TextureHandle _gbuffer;
    private TextureHandle? _decals;
    private TextureHandle? _offset;

    /// <summary>Declares the read in Setup. With decals false it reads the GBuffer as its pass left it.</summary>
    public static GBufferRead Declare(FeatureBuilder builder, bool decals = true) => new()
    {
        _gbuffer = builder.Read(RendererOutputs.GBuffer),
        _decals = decals && builder.Has(RendererOutputs.GBufferDecals) ? builder.Read(RendererOutputs.GBufferDecals) : null,
        _offset = decals && builder.Has(RendererOutputs.GBufferOffset) ? builder.Read(RendererOutputs.GBufferOffset) : null,
    };

    /// <summary>The GBuffer's own targets: Base, Surface, Custom, Baked, Emission, then a normal and a motion where it has them.</summary>
    public readonly Texture[] Own(FeatureContext context) => context.Get(_gbuffer).ColorTextures;

    /// <summary>Where a view's GBuffer pass wrote motion: the scene's target, or the GBuffer's own.</summary>
    public static Texture Motion(Texture[] own, RenderTexture scene)
        => scene.ColorTextures.Length > SceneTarget.Motion ? scene.ColorTextures[SceneTarget.Motion] : own[OwnMotion];

    /// <summary>Binds the GBuffer as the view's lighting reads it, decals applied.</summary>
    public readonly void Bind(FeatureContext context, PropertySet properties, Sampler point, RenderTexture scene)
    {
        Texture[] own = context.Get(_gbuffer).ColorTextures;
        Texture[]? decaled = _decals is { } decals ? context.Get(decals).ColorTextures : null;
        Texture normal = scene.ColorTextures.Length > SceneTarget.Normals ? scene.ColorTextures[SceneTarget.Normals]
            : decaled is { Length: > DecaledNormal } ? decaled[DecaledNormal] : own[OwnNormal];
        Bind(properties, point, own, decaled, scene, normal);

        // A displaced view's own targets are read at the pixel each pixel came from
        properties.SetTexture(s_offset, _offset is { } offset ? context.Get(offset).ColorTextures[0] : own[1], point);
        properties.SetFloat(s_displaced, _offset != null ? 1 : 0);
    }

    /// <summary>Binds only the GBuffer's Surface target, decals applied, and where a displaced view reads it.</summary>
    public readonly void BindSurface(FeatureContext context, PropertySet properties, Sampler point)
    {
        Texture[] own = context.Get(_gbuffer).ColorTextures;
        properties.SetTexture(s_surface, _decals is { } decals ? context.Get(decals).ColorTextures[1] : own[1], point);
        properties.SetTexture(s_offset, _offset is { } offset ? context.Get(offset).ColorTextures[0] : own[1], point);
        properties.SetFloat(s_displaced, _offset != null ? 1 : 0);
    }

    /// <summary>Binds the GBuffer as its pass left it, for the decals to read.</summary>
    public readonly void BindUndecaled(FeatureContext context, PropertySet properties, Sampler point, RenderTexture scene)
    {
        Texture[] own = context.Get(_gbuffer).ColorTextures;
        Bind(properties, point, own, null, scene, own[OwnNormal]);
    }

    private static void Bind(PropertySet properties, Sampler point, Texture[] own, Texture[]? decaled, RenderTexture scene, Texture normal)
    {
        properties.SetTexture(s_base, decaled?[0] ?? own[0], point);
        properties.SetTexture(s_normal, normal, point);
        properties.SetTexture(s_surface, decaled?[1] ?? own[1], point);
        properties.SetTexture(s_custom, own[2], point);
        properties.SetTexture(s_baked, own[3], point);
        properties.SetTexture(s_emission, own[4], point);
        properties.SetTexture(s_motion, Motion(own, scene), point);
        properties.SetTexture(s_depth, scene.DepthTexture!, point);
    }
}

/// <summary>
/// Lays the world's decals over a deferred view's GBuffer once its pass has drawn: base colour, metalness, roughness and
/// occlusion into the targets it publishes as <see cref="RendererOutputs.GBufferDecals"/>, and the normal onto the
/// scene's, or beside them where the scene has no normal target or the view goes on to displace its GBuffer. Adds its pass on the frames a decal of the world
/// reaches into the view. Group
/// <see cref="FeatureGroup.GBufferDecals"/>.
/// </summary>
public sealed class GBufferDecalsFeature : RenderFeature
{
    private sealed class State : IDisposable
    {
        public GBufferRead GBuffer;
        public TextureHandle Target, Scene;
        public readonly TargetSets Sets = new();

        public void Dispose() => Sets.Dispose();
    }

    private readonly RendererResources _resources;
    private readonly PropertySet _properties = new();

    internal GBufferDecalsFeature(RendererResources resources) : base(RenderSlot.GBuffer, 50, FeatureGroup.GBufferDecals)
    {
        _resources = resources;
        Paths = RenderPaths.Deferred;
        Uses(resources.GBufferDecalsProgram);
    }

    public override bool IsActive(in FeatureView view) => view.World.Lighting.LiveDecals > 0;

    public override bool HasWork(in FeatureView view) => view.HasDecals;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        State state = builder.GetViewState<State>();
        state.GBuffer = GBufferRead.Declare(builder, decals: false);
        builder.ReadViews();
        builder.DrawsRenderables();
        PixelFormat[] formats = _resources.GBufferFormats;
        PixelFormat[] own = [formats[(int)GBufferTarget.Base], formats[(int)GBufferTarget.Surface]];
        GraphTextureDesc desc = new()
        {
            Width = (int)view.Width,
            Height = (int)view.Height,
            ColorFormats = builder.SceneTargetCount > SceneTarget.Normals && !view.HasDisplacement ? own : [.. own, formats[(int)GBufferTarget.Normal]],
            ArrayLayers = view.LayerCount > 1 ? (uint)view.LayerCount : 0,
        };
        state.Target = builder.Temporary("Decaled", desc);
        state.Scene = builder.WriteSceneTargets();
        builder.Publish(RendererOutputs.GBufferDecals, state.Target);
    }

    public override void Render(FeatureContext context)
    {
        State state = context.GetViewState<State>();
        state.Sets.Age();
        Texture[] decaled = context.Get(state.Target).ColorTextures;
        RenderTexture scene = context.Get(state.Scene);
        context.CopyBack(scene);
        state.GBuffer.BindUndecaled(context, _properties, _resources.PointClamp, scene);
        bool ownNormal = decaled.Length > GBufferRead.DecaledNormal;
        Texture normal = ownNormal ? decaled[GBufferRead.DecaledNormal] : scene.ColorTextures[SceneTarget.Normals];
        Framebuffer framebuffer = state.Sets.Get(_resources.Factory, context.View.LayerCount, null, decaled[0], normal, decaled[1]);
        TargetLoadStoreOps ops = new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded)
            .WithColorLoad((int)GBufferTarget.Surface, LoadAction.Clear);
        if (!ownNormal)
            ops = ops.WithColorLoad((int)GBufferTarget.Normal, LoadAction.Load);
        context.Fullscreen(_resources.GBufferDecalsProgram, framebuffer, _properties, ops);
    }
}
