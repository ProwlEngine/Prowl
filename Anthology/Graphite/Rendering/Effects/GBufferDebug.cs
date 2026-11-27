namespace Prowl.Graphite.Rendering;

/// <summary>What <see cref="GBufferDebug"/> shows of a deferred view's GBuffer.</summary>
public enum GBufferDebugMode
{
    BaseColor,
    Metalness,
    Normals,
    Roughness,
    Specular,
    Occlusion,

    /// <summary>A colour per shading model.</summary>
    ShadingModel,
    Custom,
    Baked,
    Emission,
    Motion,
    Depth,
}

/// <summary>
/// Shows one part of a deferred view's GBuffer in place of the view's image, black where no surface was drawn into it.
/// Its image is display encoded: colours are encoded and other values shown as they are stored, so 0.5 is mid grey.
/// Runs on deferred views only.
/// </summary>
public sealed class GBufferDebug : PostEffect
{
    private static readonly PropertyID s_mode = "_Mode";

    private sealed class State
    {
        public GBufferRead GBuffer;
    }

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private GBufferDebugMode _mode;

    public GBufferDebug(Renderer renderer) : base(renderer, RenderSlot.DisplayPost)
    {
        _program = Program("GBufferDebug");
        Order = 1000;
        Paths = RenderPaths.Deferred;
    }

    public GBufferDebugMode Mode { get; set; }

    public override void Extract() => _mode = Mode;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.GetViewState<State>().GBuffer = GBufferRead.Declare(builder);
        builder.ReadScene();
        builder.EncodesForDisplay();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        context.GetViewState<State>().GBuffer.Bind(context, _properties, PointClamp, context.SceneRead!);
        _properties.SetInt(s_mode, (int)_mode);
        context.Fullscreen(_program, _properties);
    }
}
