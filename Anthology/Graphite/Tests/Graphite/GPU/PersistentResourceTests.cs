using System;

using Xunit;

namespace Prowl.Graphite.Tests;

file sealed class FillOncePass(uint size, uint[]? values) : IInstancePass
{
    public GraphKind Kind => TestViews.Kind;

    public static readonly RenderResourceID Id = RenderResourceID.Intern("persistent_values");
    private BufferHandle _handle;

    public string Name => "FillOnce";

    public void Setup(InstanceContextBuilder builder)
        => _handle = builder.DeclareOutputBuffer(Id, GraphBufferDesc.Of(size, BufferUsage.StructuredBufferReadWrite), usage: BufferAccess.TransferWrite, persistent: true);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        if (values != null)
            cmd.UpdateBuffer(context.GetRenderBuffer(_handle), 0, values);
    }
}

file sealed class CopyOutPass(DeviceBuffer destination) : IInstancePass
{
    public GraphKind Kind => TestViews.Kind;

    private BufferHandle _handle;

    public string Name => "CopyOut";

    public void Setup(InstanceContextBuilder builder) => _handle = builder.DeclareInputBuffer(FillOncePass.Id, BufferAccess.TransferRead);

    public void Render(InstanceContext context, CommandBuffer cmd) => cmd.CopyBuffer(context.GetRenderBuffer(_handle), 0, destination, 0, 16);
}

public abstract class PersistentResourceTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private uint[] RunAndRead(GraphStore store, uint size, uint[]? values)
    {
        DeviceBuffer result = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        using RenderGraph graph = TestViews.Build([new FillOncePass(size, values), new CopyOutPass(result)], store: store);
        GD.DispatchGraph(graph, TestViews.Single(graph));
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(result);
        uint[] read = GD.Map<uint>(readback)[..4].ToArray();
        GD.Unmap(readback);
        return read;
    }

    [Fact]
    public void PersistentBuffer_KeepsItsContents_AcrossExecutionsOfARebuiltGraph()
    {
        using GraphStore store = new();
        RunAndRead(store, 16, [1, 2, 3, 4]);

        Assert.Equal([1u, 2u, 3u, 4u], RunAndRead(store, 16, null));
    }

    [Fact]
    public void PersistentBuffer_ThatGrows_KeepsItsContents()
    {
        using GraphStore store = new();
        RunAndRead(store, 16, [5, 6, 7, 8]);

        Assert.Equal([5u, 6u, 7u, 8u], RunAndRead(store, 64, null));
    }

    [Fact]
    public void PersistentBuffer_ResolvedThroughTheStore_IsTheOneALaterGraphUses()
    {
        using GraphStore store = new();
        GraphInstanceKey key = new(TestViews.Kind, 0);
        GraphBufferDesc desc = GraphBufferDesc.Of(16, BufferUsage.StructuredBufferReadWrite);
        DeviceBuffer early = store.ResolvePersistent(GD, FillOncePass.Id, desc, key);

        Assert.Equal([9u, 9u, 9u, 9u], RunAndRead(store, 16, [9, 9, 9, 9]));

        Assert.Same(early, store.ResolvePersistent(GD, FillOncePass.Id, desc, key));
    }

    [Fact]
    public void ReleasedPersistentTexture_IsDisposedAndMadeAgainOnTheNextResolve()
    {
        using GraphStore store = new();
        RenderResourceID id = RenderResourceID.Intern("persistent_released");
        GraphInstanceKey key = new(TestViews.Kind, 0);
        GraphTextureDesc desc = GraphTextureDesc.Sized(16, 16, false, PixelFormat.R8_G8_B8_A8_UNorm);
        RenderTexture first = store.ResolvePersistent(GD, id, desc, key);

        store.ReleasePersistent(id, key);

        Assert.True(first.ColorTextures[0].IsDisposed);
        Assert.NotSame(first, store.ResolvePersistent(GD, id, desc, key));
    }

    [Fact]
    public void PersistentCube_ResolvedThroughTheStore_HasItsMipsAndAFramebufferOnEach()
    {
        using GraphStore store = new();
        GraphTextureDesc desc = GraphTextureDesc.Sized(64, 64, false, PixelFormat.R8_G8_B8_A8_UNorm) with { Cube = true, MipLevels = 4 };
        RenderTexture cube = store.ResolvePersistent(GD, RenderResourceID.Intern("persistent_cube"), desc, new GraphInstanceKey(TestViews.Kind, 0));

        Assert.Equal(TextureType.TextureCube, cube.ColorTextures[0].Type);
        Assert.Equal(4u, cube.ColorTextures[0].MipLevels);
        Assert.Equal(8u, cube.MipFramebuffer(3).Width);
        Assert.Same(cube.MipFramebuffer(2), cube.MipFramebuffer(2));
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanPersistentResourceTests : PersistentResourceTests<VulkanDeviceCreator> { }
#endif
