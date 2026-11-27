using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Captures the height of a world's geometry from straight above into a height field the ocean reads its floor from:
/// an orthographic view of the chosen layers, its depth turned into heights. Aim it, then
/// submit <see cref="View"/> in a frame; <see cref="Input"/> holds the heights once that frame ran. Where nothing was
/// drawn the height is far below the sea. Host thread.
/// </summary>
public sealed class OceanDepthCapture : IDisposable
{
    private readonly RenderTexture _heights;
    private readonly Texture _output;
    private readonly Framebuffer _framebuffer;
    private readonly HeightFeature _feature;

    public OceanDepthCapture(Renderer renderer, RenderWorld world, int resolution = 512)
    {
        ResourceFactory factory = renderer.Device.ResourceFactory;
        _heights = factory.CreateRenderTexture(new RenderTextureDescription((uint)resolution, (uint)resolution, PixelFormat.R32_Float, false));
        _heights.Name = "Ocean Depth Capture";
        _output = factory.CreateTexture(TextureDescription.Texture2D((uint)resolution, (uint)resolution, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, _output));
        _feature = new HeightFeature(renderer, _heights);
        View = renderer.CreateView(world, "Ocean Depth Capture");
        View.Target = _framebuffer;
        View.Kind = ViewKind.RenderTexture;
        View.Sky = false;
        View.Features = new ExtensionSet<RenderFeature> { _feature };
        Input = new OceanDepthInput { Heights = _heights.ColorTextures[0] };
    }

    /// <summary>The view to submit for the capture to run.</summary>
    public RenderView View { get; }

    /// <summary>The captured heights, for <see cref="OceanFeature.DepthInputs"/>.</summary>
    public OceanDepthInput Input { get; }

    /// <summary>
    /// Aims the capture at a square of the world centred on a point, looking down from height above it, seeing the
    /// layers of the mask down to depth below it. Heights come out relative to the point's height.
    /// </summary>
    public void Aim(Double3 centre, float size, float height = 500f, float depth = 2000f, uint layers = ~0u)
    {
        size = MathF.Max(size, 1e-3f);
        height = MathF.Max(height, 1e-3f);
        float range = height + MathF.Max(depth, 0f);
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        Float4x4 projection = new(new Float4(2f / size, 0, 0, 0), new Float4(0, 2f / size, 0, 0), new Float4(0, 0, -1f / range, 0), new Float4(0, 0, 1, 1));
        View.Camera = new ViewCamera(new ViewLayer { View = down, Projection = projection, Position = centre + new Double3(0, height, 0) });
        View.CullingMask = layers;
        _feature.Above = height;
        Input.Min = new Double2(centre.X - size * 0.5, centre.Z - size * 0.5);
        Input.Size = new Float2(size, size);
        Input.Scale = 1f;
        Input.Offset = (float)centre.Y;
    }

    public void Dispose()
    {
        View.Dispose();
        _feature.Dispose();
        _framebuffer.Dispose();
        _output.Dispose();
        _heights.Dispose();
    }

    // Turns the view's depth into heights relative to the capture's centre, rows from its lowest z up
    private sealed class HeightFeature : RenderFeature, IDisposable
    {
        private const string Fragment = """
            #include "Graphite/Internal/PostView.glsl"
            uniform GRAPHITE_SCREEN_TEXTURE _CaptureDepth;
            uniform float _CaptureAbove;
            void main()
            {
                Graphite_ViewData view = Graphite_PostView();
                vec2 uv = vec2(vUV.x, 1.0 - vUV.y);
                ivec2 size = Graphite_Size(_CaptureDepth, 0);
                float depth = Graphite_Fetch(_CaptureDepth, clamp(ivec2(uv * vec2(size)), ivec2(0), size - 1), 0).r;
                float height = depth > 0.0 ? Graphite_RelativeFromDepth(view, uv, depth).y + _CaptureAbove : -1e6;
                outColor = vec4(height, 0.0, 0.0, 1.0);
            }
            """;

        private readonly FullscreenProgram _program;
        private readonly RenderTexture _target;
        private readonly Sampler _point;
        private readonly PropertySet _properties = new();
        private TextureHandle _handle;
        private float _above;

        public HeightFeature(Renderer renderer, RenderTexture target) : base(RenderSlot.AfterOpaques, order: -100, runsOn: ViewKinds.RenderTexture)
        {
            _program = renderer.CreateFullscreenProgram(Fragment, "Graphite/OceanDepthCapture", source: false);
            _target = target;
            _point = renderer.Resources.PointClamp;
        }

        public float Above { get; set; }

        public override void Extract() => _above = Above;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.ReadScene();
            _handle = builder.Import(_target);
        }

        public override void Render(FeatureContext context)
        {
            _properties.SetTexture("_CaptureDepth", context.Depth!, _point);
            _properties.SetFloat("_CaptureAbove", _above);
            context.Fullscreen(_program, context.Get(_handle), _properties);
        }

        public void Dispose() => _program.Dispose();
    }
}
