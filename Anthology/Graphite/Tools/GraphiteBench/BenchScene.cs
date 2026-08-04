using System;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;


public sealed class BenchScene : IDisposable
{
    public const uint TargetWidth = 256;
    public const uint TargetHeight = 256;

    private readonly Texture _colorTarget;

    public GraphicsDevice Device { get; }
    public GraphicsProgram Program { get; }
    public Framebuffer Framebuffer { get; }
    public PropertySet ViewProperties { get; }

    internal BenchMesh Mesh { get; }

    public BenchScene(GraphicsDevice gd)
    {
        Device = gd;
        Program = BenchShaderLoader.Create(gd, "BenchShader.slang");
        Mesh = BenchMesh.CreateCube(gd);

        _colorTarget = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            TargetWidth, TargetHeight, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer = gd.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _colorTarget));

        Float4x4 projection = Float4x4.CreatePerspectiveFov(1.0472f, 1f, 0.1f, 100f);
        Float4x4 view = Float4x4.CreateLookAt(new Float3(0, 0, 6), Float3.Zero, Float3.UnitY);

        ViewProperties = new PropertySet();
        ViewProperties.SetMatrix("MatrixViewProjection", projection * view);
        ViewProperties.SetFloat4("ViewTint", new Float4(1, 1, 1, 1));
    }

    // A set-1 PropertySet: the per-draw model block plus the texture and sampler.
    public PropertySet CreateModelProperties(Float4 color)
    {
        PropertySet props = new();
        props.SetMatrix("MatrixModel", Float4x4.Identity);
        props.SetFloat4("Color", color);
        props.SetTexture("MainTexture", Device.NullTexture2D, Device.PointSampler);
        return props;
    }

    public IVertexSource VertexSource => Mesh;

    public uint IndexCount => Mesh.IndexCount;

    public void Dispose()
    {
        Framebuffer.Dispose();
        _colorTarget.Dispose();
        Mesh.Dispose();
        Program.Dispose();
    }
}
