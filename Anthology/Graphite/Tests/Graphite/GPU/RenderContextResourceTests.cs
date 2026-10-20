#nullable enable

using System;
using System.Collections.Generic;


using Xunit;

namespace Prowl.Graphite.Tests;

file sealed class ResolvingPass : IInstancePass
{
    public GraphKind Kind => TestViews.Kind;

    private readonly RenderResourceID _id;
    private readonly GraphTextureDesc _desc;
    private readonly bool _isOutput;
    private readonly int _resolvesPerRender;
    private TextureHandle _handle;

    public ResolvingPass(string name, RenderResourceID id, GraphTextureDesc desc, bool isOutput = true, int resolvesPerRender = 1)
    {
        Name = name;
        _id = id;
        _desc = desc;
        _isOutput = isOutput;
        _resolvesPerRender = resolvesPerRender;
    }

    public string Name { get; }

    public List<RenderTexture> Resolved { get; } = new();

    public void Setup(InstanceContextBuilder builder)
        => _handle = _isOutput ? builder.DeclareOutputTexture(_id, _desc) : builder.DeclareInputTexture(_id);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        for (int i = 0; i < _resolvesPerRender; i++)
            Resolved.Add(context.GetRenderTexture(_handle));
    }
}

file sealed class ImportingPass : IInstancePass
{
    public GraphKind Kind => TestViews.Kind;

    private readonly RenderResourceID _id;
    private readonly RenderTexture _external;
    private TextureHandle _handle;

    public ImportingPass(RenderResourceID id, RenderTexture external)
    {
        _id = id;
        _external = external;
    }

    public string Name => "Import";
    public RenderTexture? Resolved { get; private set; }

    public void Setup(InstanceContextBuilder builder) => _handle = builder.DeclareImportedTexture(_id, _external);

    public void Render(InstanceContext context, CommandBuffer cmd) => Resolved = context.GetRenderTexture(_handle);
}

public abstract class RenderContextResourceTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private static GraphTextureDesc ColorDesc(float scale = 1f)
        => GraphTextureDesc.InstanceSized(PixelFormat.R8_G8_B8_A8_UNorm, scale);

    [Fact]
    public void GetRenderTexture_SameHandleWithinContext_ReturnsCachedInstance()
    {
        ResolvingPass pass = new("Pass", RenderResourceID.Intern("resourcetest_cache"), ColorDesc(), resolvesPerRender: 3);
        using RenderGraph graph = TestViews.Build([pass]);

        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(64, 64)));
        GD.WaitForIdle();

        Assert.Equal(3, pass.Resolved.Count);
        Assert.Same(pass.Resolved[0], pass.Resolved[1]);
        Assert.Same(pass.Resolved[0], pass.Resolved[2]);
    }

    [Fact]
    public void GetRenderTexture_WriterAndReaderOfSameResource_ResolveToSameInstance()
    {
        RenderResourceID id = RenderResourceID.Intern("resourcetest_shared");
        ResolvingPass writer = new("Writer", id, ColorDesc(), isOutput: true);
        ResolvingPass reader = new("Reader", id, ColorDesc(), isOutput: false);
        using RenderGraph graph = TestViews.Build([writer, reader]);

        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(64, 64)));
        GD.WaitForIdle();

        Assert.Same(writer.Resolved[0], reader.Resolved[0]);
    }

    [Fact]
    public void GetRenderTexture_DifferentDeclaredResources_ResolveToDistinctInstances()
    {
        ResolvingPass a = new("A", RenderResourceID.Intern("resourcetest_distinct_a"), ColorDesc());
        ResolvingPass b = new("B", RenderResourceID.Intern("resourcetest_distinct_b"), ColorDesc());
        using RenderGraph graph = TestViews.Build([a, b]);

        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(64, 64)));
        GD.WaitForIdle();

        Assert.NotSame(a.Resolved[0], b.Resolved[0]);
    }

    [Fact]
    public void GetRenderTexture_TwoInstancesInOneDispatch_ResolveToIndependentCorrectlySizedTextures()
    {
        ResolvingPass pass = new("Pass", RenderResourceID.Intern("resourcetest_perview"), ColorDesc());
        using RenderGraph graph = TestViews.Build([pass]);

        GD.DispatchGraph(graph, TestViews.Of(graph, (64u, 48u), (128u, 96u)));
        GD.WaitForIdle();

        Assert.Equal(2, pass.Resolved.Count);
        Assert.NotSame(pass.Resolved[0], pass.Resolved[1]);
        Assert.Equal(64u, pass.Resolved[0].Desc.Width);
        Assert.Equal(48u, pass.Resolved[0].Desc.Height);
        Assert.Equal(128u, pass.Resolved[1].Desc.Width);
        Assert.Equal(96u, pass.Resolved[1].Desc.Height);
    }

    [Fact]
    public void GetRenderTexture_InstanceSizedResource_ScalesToInstanceExtent()
    {
        ResolvingPass pass = new("Pass", RenderResourceID.Intern("resourcetest_scale"), ColorDesc(0.5f));
        using RenderGraph graph = TestViews.Build([pass]);

        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(200, 100)));
        GD.WaitForIdle();

        Assert.Equal(100u, pass.Resolved[0].Desc.Width);
        Assert.Equal(50u, pass.Resolved[0].Desc.Height);
    }

    [Fact]
    public void DeclareImportedTexture_ResolvesToTheExternalTexture()
    {
        RenderTexture external = RF.CreateRenderTexture(new RenderTextureDescription(
            64, 64, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, false, TextureSampleCount.Count1));
        ImportingPass pass = new(RenderResourceID.Intern("resourcetest_imported"), external);
        using RenderGraph graph = TestViews.Build([pass]);

        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(64, 64)));
        GD.WaitForIdle();

        Assert.Same(external, pass.Resolved);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanRenderContextResourceTests : RenderContextResourceTests<VulkanDeviceCreator> { }
#endif
