#nullable enable

using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct VersionView : IRenderView
{
    public uint PixelWidth => 32;
    public uint PixelHeight => 32;
    public int ViewId => 0;
}

file sealed class VersionClearPass : RasterPass
{
    private readonly RenderResourceID _id;
    private readonly List<uint> _seen;

    public VersionClearPass(RenderResourceID id, List<uint> seen)
    {
        _id = id;
        _seen = seen;
    }

    public override string Name => "VersionClear";

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, _id, GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 1)));

    public override void Render(RenderContext context, CommandBuffer cmd)
        => _seen.Add(context.GetRenderTexture(new TextureHandle(_id)).ColorTextures[0].ContentVersion);
}

file sealed class VersionReadPass : IPass
{
    private readonly RenderResourceID _id;
    private readonly List<uint> _seen;
    private TextureHandle _handle;

    public VersionReadPass(RenderResourceID id, List<uint> seen)
    {
        _id = id;
        _seen = seen;
    }

    public string Name => "VersionRead";

    public void Setup(RenderContextBuilder builder) => _handle = builder.DeclareInputTexture(_id, TextureState.Sampled);

    public void Render(RenderContext context, CommandBuffer cmd)
        => _seen.Add(context.GetRenderTexture(_handle).ColorTextures[0].ContentVersion);
}

public abstract class ResourceVersionTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private DeviceBuffer Buffer(BufferUsage usage = BufferUsage.StructuredBufferReadWrite, uint size = 1024)
        => RF.CreateBuffer(new BufferDescription(size, usage));

    private Texture Texture2D(uint mips = 3, TextureUsage usage = TextureUsage.Sampled)
        => RF.CreateTexture(TextureDescription.Texture2D(16, 16, mips, 1, PixelFormat.R8_G8_B8_A8_UNorm, usage));

    private static ComputeProgram CreateComputeProgram(GraphicsDevice device)
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(device.BackendType, "BasicComputeTest.slang");
        ResourceLayoutDescription[] layouts =
        [
            new ResourceLayoutDescription
            {
                Set = 0,
                Elements =
                [
                    new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
                        UniformFields =
                        [
                            new UniformBlockField("Width", 0, sizeof(uint), UniformScalarType.Int1),
                            new UniformBlockField("Height", sizeof(uint), sizeof(uint), UniformScalarType.Int1),
                        ]
                    },
                    new ResourceLayoutElementDescription("Source", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1),
                    new ResourceLayoutElementDescription("Destination", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 2),
                ]
            }
        ];
        return device.ResourceFactory.CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));
    }

    [Fact]
    public void DeviceUpdateBuffer_BumpsOnce()
    {
        DeviceBuffer buffer = Buffer();
        uint before = buffer.ContentVersion;
        GD.UpdateBuffer(buffer, 0, new byte[16]);
        Assert.Equal(before + 1, buffer.ContentVersion);
    }

    [Fact]
    public void CommandBufferUpdateBuffer_BumpsOnce()
    {
        DeviceBuffer buffer = Buffer();
        uint before = buffer.ContentVersion;
        GD.RunTestGraph((context, cl) => cl.UpdateBuffer(buffer, 0, new byte[16]));
        Assert.Equal(before + 1, buffer.ContentVersion);
    }

    [Fact]
    public void MapAndUnmap_BumpOncePerPair()
    {
        DeviceBuffer buffer = Buffer(BufferUsage.Dynamic | BufferUsage.StructuredBufferReadOnly);
        uint before = buffer.ContentVersion;
        Span<byte> span = GD.Map(buffer);
        span[0] = 1;
        Assert.Equal(before, buffer.ContentVersion);
        GD.Unmap(buffer);
        Assert.Equal(before + 1, buffer.ContentVersion);
    }

    [Fact]
    public void CopyBuffer_BumpsDestinationOnly()
    {
        DeviceBuffer source = Buffer();
        DeviceBuffer destination = Buffer();
        uint sourceBefore = source.ContentVersion;
        uint destinationBefore = destination.ContentVersion;
        GD.RunTestGraph((context, cl) => cl.CopyBuffer(source, 0, destination, 0, 64));
        Assert.Equal(sourceBefore, source.ContentVersion);
        Assert.Equal(destinationBefore + 1, destination.ContentVersion);
    }

    [Fact]
    public void DeviceUpdateTexture_BumpsOnce()
    {
        Texture texture = Texture2D(1);
        uint before = texture.ContentVersion;
        GD.UpdateTexture(texture, new byte[16 * 16 * 4].AsSpan());
        Assert.Equal(before + 1, texture.ContentVersion);
    }

    [Fact]
    public void CommandBufferUpdateTexture_BumpsOnce()
    {
        Texture texture = Texture2D(1);
        uint before = texture.ContentVersion;
        GD.RunTestGraph((context, cl) => cl.UpdateTexture(texture, new byte[16 * 16 * 4].AsSpan()));
        Assert.Equal(before + 1, texture.ContentVersion);
    }

    [Fact]
    public void CopyTexture_WholeCopyBumpsDestinationOnce()
    {
        Texture source = Texture2D();
        Texture destination = Texture2D();
        uint sourceBefore = source.ContentVersion;
        uint destinationBefore = destination.ContentVersion;
        GD.RunTestGraph((context, cl) => cl.CopyTexture(source, destination));
        Assert.Equal(sourceBefore, source.ContentVersion);
        Assert.Equal(destinationBefore + 1, destination.ContentVersion);
    }

    [Fact]
    public void CopyTexture_SubresourceBumpsDestinationOnce()
    {
        Texture source = Texture2D();
        Texture destination = Texture2D();
        uint destinationBefore = destination.ContentVersion;
        GD.RunTestGraph((context, cl) => cl.CopyTexture(source, destination, 1, 0));
        Assert.Equal(destinationBefore + 1, destination.ContentVersion);
    }

    [Fact]
    public void CopyTextureToBuffer_BumpsBufferOnly()
    {
        Texture source = Texture2D(1);
        DeviceBuffer destination = Buffer(BufferUsage.Staging, 16 * 16 * 4);
        uint sourceBefore = source.ContentVersion;
        uint destinationBefore = destination.ContentVersion;
        GD.RunTestGraph((context, cl) => cl.CopyTextureToBuffer(source, destination, 0, TextureRegion.Whole(source)));
        Assert.Equal(sourceBefore, source.ContentVersion);
        Assert.Equal(destinationBefore + 1, destination.ContentVersion);
    }

    [Fact]
    public void GenerateMipmaps_BumpsOnce()
    {
        Texture texture = Texture2D();
        uint before = texture.ContentVersion;
        GD.RunTestGraph((context, cl) => cl.GenerateMipmaps(texture));
        Assert.Equal(before + 1, texture.ContentVersion);
    }

    [Fact]
    public void GenerateMipmaps_SingleMipDoesNotBump()
    {
        Texture texture = Texture2D(1);
        uint before = texture.ContentVersion;
        GD.RunTestGraph((context, cl) => cl.GenerateMipmaps(texture));
        Assert.Equal(before, texture.ContentVersion);
    }

    [Fact]
    public void Dispatch_BumpsBoundStorageBuffersPerDispatchAndBindsDoNot()
    {
        DeviceBuffer source = Buffer();
        DeviceBuffer destination = Buffer();
        ComputeProgram program = CreateComputeProgram(GD);

        PropertySet props = new();
        props.SetInt("Width", 16);
        props.SetInt("Height", 16);
        props.SetBuffer("Source", source);
        props.SetBuffer("Destination", destination);

        uint sourceBefore = source.ContentVersion;
        uint destinationBefore = destination.ContentVersion;

        GD.RunTestGraph((context, cl) =>
        {
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            Assert.Equal(sourceBefore, source.ContentVersion);
            Assert.Equal(destinationBefore, destination.ContentVersion);
            cl.Dispatch(1, 1, 1);
            Assert.Equal(sourceBefore + 1, source.ContentVersion);
            Assert.Equal(destinationBefore + 1, destination.ContentVersion);
            cl.Dispatch(1, 1, 1);
        });

        Assert.Equal(sourceBefore + 2, source.ContentVersion);
        Assert.Equal(destinationBefore + 2, destination.ContentVersion);
    }

    [Fact]
    public void AttachmentOutput_BumpsAtPassEndOncePerExecution()
    {
        List<uint> clearSeen = new();
        List<uint> readSeen = new();
        RenderResourceID id = RenderResourceID.Intern("resource_version_target");
        using RenderPipeline pipeline = new([new VersionClearPass(id, clearSeen), new VersionReadPass(id, readSeen)]);
        VersionView[] views = [new()];

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        uint clearBefore = Assert.Single(clearSeen);
        uint readAfter = Assert.Single(readSeen);
        Assert.Equal(clearBefore + 1, readAfter);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanResourceVersionTests : ResourceVersionTests<VulkanDeviceCreator> { }
#endif
