using System;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>When a world's sky is captured into its sky probe.</summary>
public enum SkyCaptureMode : byte
{
    /// <summary>When the sky material, the environment or the directional lights change, at most every other frame.</summary>
    OnChange,
    EveryFrame,

    /// <summary>Once, when the world first draws.</summary>
    Never,
}

/// <summary>
/// The render side of a world's reflections: one cube array prefiltered by roughness across its mips, layer 0 holding the
/// sky. The sky is captured with a six layer view that draws only the sky, then filtered with GGX into every mip, reading
/// the source mip whose texels match each sample's solid angle.
/// </summary>
internal sealed class WorldReflections : IDisposable
{
    /// <summary>Edge length of every probe's cube.</summary>
    public const uint Resolution = 128;

    /// <summary>The smallest prefiltered mip is 4 texels across, below that the roughest reflections turn blocky.</summary>
    public const uint SmallestMip = 4;

    public const uint MipCount = 6;
    public const int SkyLayer = 0;
    public const int PrefilterSamples = 64;
    private const int SkyFrameInterval = 2;

    /// <summary>A reversed Z perspective with an infinite far plane and a 90 degree field of view, looking down +Z.</summary>
    public static readonly Float4x4 FaceProjection = new(new Float4(1, 0, 0, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1), new Float4(0, 0, 0.05f, 0));

    // Each face's view: its rows are the directions the image's x, y and depth run along, the cube's own face layout
    private static readonly Float4x4[] s_faceViews =
    [
        Rows(new(0, 0, -1), new(0, -1, 0), new(1, 0, 0)),
        Rows(new(0, 0, 1), new(0, -1, 0), new(-1, 0, 0)),
        Rows(new(1, 0, 0), new(0, 0, 1), new(0, 1, 0)),
        Rows(new(1, 0, 0), new(0, 0, -1), new(0, -1, 0)),
        Rows(new(1, 0, 0), new(0, -1, 0), new(0, 0, 1)),
        Rows(new(-1, 0, 0), new(0, -1, 0), new(0, 0, -1)),
    ];

    private static readonly SamplerDescription s_sampler = SamplerDescription.Linear with
    {
        AddressModeU = SamplerAddressMode.Clamp,
        AddressModeV = SamplerAddressMode.Clamp,
        AddressModeW = SamplerAddressMode.Clamp,
    };

    private readonly GraphicsDevice _device;
    private readonly IMaterialResources _resources;
    private bool _captured;
    private int _capturedFrame;
    private Material? _capturedSky;
    private uint _capturedVersion;
    private int _capturedInputs;

    public WorldReflections(GraphicsDevice device, IMaterialResources resources)
    {
        _device = device;
        _resources = resources;
    }

    /// <summary>The prefiltered cube array, layer 0 the sky. Null until the first capture.</summary>
    public Texture? Probes { get; private set; }

    /// <summary>The sky rendered into a cube with a full mip chain, the prefilter's source.</summary>
    public Texture? SkyCube { get; private set; }

    public Framebuffer? CaptureTarget { get; private set; }

    /// <summary>One view per prefiltered mip of the sky layer, each drawing all six faces at once.</summary>
    public PrefilterView[] PrefilterViews { get; private set; } = [];

    public uint ProbeSlot { get; private set; }
    public uint SamplerSlot { get; private set; }

    /// <summary>Whether the sky is captured this frame. Decided before the frame's records are written.</summary>
    public bool CaptureDue { get; private set; }

    public bool Ready => _captured;

    public static Float4x4 FaceView(int face) => s_faceViews[face];

    /// <summary>Decides whether this frame captures the sky. Render thread, before the lighting records flush.</summary>
    public void Plan(int frame, WorldLighting lighting)
    {
        EnvironmentDesc environment = lighting.Environment;
        Material? sky = environment.Sky;
        bool changed = !_captured || sky != _capturedSky || lighting.SkyVersion != _capturedVersion || lighting.SkyInputs != _capturedInputs;
        CaptureDue = environment.SkyCapture switch
        {
            SkyCaptureMode.EveryFrame => true,
            SkyCaptureMode.Never => !_captured,
            _ => changed && (!_captured || frame - _capturedFrame >= SkyFrameInterval),
        };
        if (!CaptureDue)
            return;

        EnsureResources();
        _captured = true;
        _capturedFrame = frame;
        _capturedSky = sky;
        _capturedVersion = lighting.SkyVersion;
        _capturedInputs = lighting.SkyInputs;
        lighting.SetReflections(ProbeSlot, SamplerSlot, MipCount);
    }

    public void Dispose()
    {
        foreach (PrefilterView view in PrefilterViews)
            view.Target.Dispose();
        CaptureTarget?.Dispose();
        SkyCube?.Dispose();
        Probes?.Dispose();
    }

    private void EnsureResources()
    {
        if (Probes != null)
            return;

        ResourceFactory factory = _device.ResourceFactory;
        // Two layers at least, so the array is always viewed as a cube array
        Probes = factory.CreateTexture(TextureDescription.CreateCube(Resolution, MipCount, 2, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled | TextureUsage.RenderTarget));
        Probes.Name = "Reflection probes";
        uint fullChain = (uint)Math.Log2(Resolution) + 1;
        SkyCube = factory.CreateTexture(TextureDescription.CreateCube(Resolution, fullChain, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled | TextureUsage.RenderTarget));
        SkyCube.Name = "Sky capture";
        CaptureTarget = factory.CreateFramebuffer(new FramebufferDescription(null, SkyCube) { ViewCount = 6 });

        PrefilterViews = new PrefilterView[MipCount];
        for (uint mip = 0; mip < MipCount; mip++)
        {
            FramebufferDescription description = new(null, [new FramebufferAttachment(Probes, SkyLayer * 6, mip)]) { ViewCount = 6 };
            PrefilterViews[mip] = new PrefilterView(factory.CreateFramebuffer(description), (int)mip, Resolution >> (int)mip);
        }

        ProbeSlot = _resources.TextureSlot(Probes, MaterialPropertyType.TextureCubeArray, "black");
        SamplerSlot = _resources.SamplerSlot(s_sampler);
    }

    private static Float4x4 Rows(Float3 x, Float3 y, Float3 z)
        => new(new Float4(x.X, y.X, z.X, 0), new Float4(x.Y, y.Y, z.Y, 0), new Float4(x.Z, y.Z, z.Z, 0), new Float4(0, 0, 0, 1));
}

/// <summary>One prefiltered mip of a probe layer as a render view: its six faces drawn at once.</summary>
internal sealed class PrefilterView(Framebuffer target, int mip, uint size) : IRenderView
{
    public Framebuffer Target { get; } = target;
    Framebuffer? IRenderView.Target => Target;
    public int Mip { get; } = mip;
    public uint PixelWidth { get; } = size;
    public uint PixelHeight { get; } = size;
    public int ViewId => -1 - Mip;
}

/// <summary>
/// Filters a captured cube into every roughness mip of a probe layer, run once per mip view. The first view builds the
/// source's mips, which the filter reads by each sample's solid angle.
/// </summary>
internal sealed class PrefilterPass(GraphicsProgram program, Sampler sampler) : IPass
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public float Roughness;
        public float SourceSize;
        public uint SampleCount;
        private uint _pad;
    }

    private readonly PropertySet _properties = new();
    private TextureHandle _target;

    public Texture Source = null!;

    public string Name => "Graphite.Prefilter";

    public void Setup(RenderContextBuilder builder) => _target = builder.DeclareViewTarget();

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        PrefilterView view = (PrefilterView)context.View;
        if (view.Mip == 0)
            cmd.GenerateMipmaps(Source);

        int mips = (int)WorldReflections.MipCount;
        cmd.SetFramebuffer(context.GetRenderTexture(_target).Framebuffer, new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded));
        cmd.SetFullViewport();
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        _properties.SetTexture("Graphite_Source", Source, sampler);
        cmd.SetProperties(_properties);
        cmd.SetPushConstants(new Push
        {
            Roughness = mips > 1 ? view.Mip / (float)(mips - 1) : 0,
            SourceSize = Source.Width,
            SampleCount = view.Mip == 0 ? 1u : WorldReflections.PrefilterSamples,
        });
        cmd.Draw(3);
    }
}
