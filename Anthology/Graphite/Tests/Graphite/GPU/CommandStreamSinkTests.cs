#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

internal sealed class SinkRecorder : ICommandStreamProfiler, IPassCommandSink
{
    public readonly List<string> Log = new();
    public readonly List<PropertyState[]> Tables = new();
    public readonly List<(ResourceVersion After, uint Offset, byte[] Data)> BufferUpdates = new();
    public readonly List<(ResourceVersion After, TextureRegion Region, byte[] Data)> TextureUpdates = new();
    public readonly List<VertexBindingUse[]> VertexBindings = new();
    public readonly List<IndexBindingUse> IndexBindings = new();
    public readonly List<PipelineBindInfo> Pipelines = new();
    public readonly List<(ResourceVersion Source, uint SourceOffset, ResourceVersion After, uint Offset, uint Size)> BufferCopies = new();

    public void BeginExecution(ulong executionId, string graphName) { }
    public void EndExecution() { }

    public IPassCommandSink? BeginPassCommands(in PassInfo pass)
    {
        Log.Add("BeginPass:" + pass.Name);
        return this;
    }

    public void End() => Log.Add("EndPass");

    public void SetFramebuffer(in FramebufferInfo framebuffer, in TargetLoadStoreOps ops)
        => Log.Add($"SetFramebuffer:{framebuffer.Colors.Length}:{framebuffer.Depth.HasValue}:{ops.Color.Load}");

    public void ClearColorTarget(uint index, Color color) => Log.Add("ClearColorTarget");
    public void ClearDepthStencil(float depth, byte stencil) => Log.Add("ClearDepthStencil");

    public void SetPipeline(in PipelineBindInfo pipeline)
    {
        Pipelines.Add(pipeline);
        Log.Add(pipeline.IsCompute ? "SetPipeline:Compute" : "SetPipeline:Graphics");
    }

    public void SetViewport(in Viewport viewport) => Log.Add("SetViewport");
    public void SetScissor(uint x, uint y, uint width, uint height) => Log.Add("SetScissor");
    public void SetStencilReference(uint reference) => Log.Add("SetStencilReference");
    public void SetBlendConstants(Color constants) => Log.Add("SetBlendConstants");

    public void BindVertexBuffers(ReadOnlySpan<VertexBindingUse> bindings)
    {
        VertexBindings.Add(bindings.ToArray());
        Log.Add("BindVertexBuffers");
    }

    public void BindIndexBuffer(in IndexBindingUse binding)
    {
        IndexBindings.Add(binding);
        Log.Add("BindIndexBuffer");
    }

    public void SetProperties(ReadOnlySpan<PropertyState> properties)
    {
        Tables.Add(properties.ToArray());
        Log.Add("Props:" + properties.Length);
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) => Log.Add($"Draw:{vertexCount}:{instanceCount}:{firstVertex}:{firstInstance}");

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        => Log.Add($"DrawIndexed:{indexCount}:{instanceCount}:{firstIndex}:{vertexOffset}:{firstInstance}");

    public void DrawIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride) => Log.Add("DrawIndirect");
    public void DrawIndexedIndirect(in ResourceVersion buffer, uint offset, uint drawCount, uint stride) => Log.Add("DrawIndexedIndirect");
    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ) => Log.Add($"Dispatch:{groupCountX}:{groupCountY}:{groupCountZ}");
    public void DispatchIndirect(in ResourceVersion buffer, uint offset) => Log.Add("DispatchIndirect");

    public void UpdateBuffer(in ResourceVersion after, uint offset, ReadOnlySpan<byte> data)
    {
        BufferUpdates.Add((after, offset, data.ToArray()));
        Log.Add("UpdateBuffer");
    }

    public void UpdateTexture(in ResourceVersion after, in TextureRegion region, ReadOnlySpan<byte> data)
    {
        TextureUpdates.Add((after, region, data.ToArray()));
        Log.Add("UpdateTexture");
    }

    public void CopyBuffer(in ResourceVersion source, uint sourceOffset, in ResourceVersion destinationAfter, uint destinationOffset, uint sizeInBytes)
    {
        BufferCopies.Add((source, sourceOffset, destinationAfter, destinationOffset, sizeInBytes));
        Log.Add("CopyBuffer");
    }

    public void CopyTexture(in ResourceVersion source, in TextureRegion sourceRegion, in ResourceVersion destinationAfter, in TextureRegion destinationRegion, uint layerCount)
        => Log.Add("CopyTexture");

    public void CopyTextureToBuffer(in ResourceVersion source, in TextureRegion region, in ResourceVersion destinationAfter, uint destinationOffset)
        => Log.Add("CopyTextureToBuffer");

    public void ResolveTexture(in ResourceVersion source, in ResourceVersion destinationAfter) => Log.Add("ResolveTexture");
    public void GenerateMips(in ResourceVersion textureAfter) => Log.Add("GenerateMips");
    public void PushMarker(string name) => Log.Add("PushMarker:" + name);
    public void PopMarker() => Log.Add("PopMarker");
}

file readonly struct SinkView : IRenderView
{
    public uint PixelWidth => 32;
    public uint PixelHeight => 32;
    public int ViewId => 0;
}

file sealed class SinkPass : IPass
{
    private readonly Action<CommandBuffer> _record;

    public SinkPass(Action<CommandBuffer> record) => _record = record;

    public string Name => "SinkPass";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext context, CommandBuffer cmd) => _record(cmd);
}

public abstract class CommandStreamSinkTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Size = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointVertex
    {
        public Float2 Position;
        public Float4 Color;
    }

    private (Framebuffer Fb, Texture Target) CreateTarget()
    {
        Texture target = RF.CreateTexture(TextureDescription.Texture2D(
            Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        return (RF.CreateFramebuffer(new FramebufferDescription(null, target)), target);
    }

    private GraphicsProgram CreatePointProgram()
    {
        VertexLayoutDescription layout = new(0, (uint)Unsafe.SizeOf<PointVertex>(),
            new VertexElementDescription("POSITION", VertexElementFormat.Float2),
            new VertexElementDescription("COLOR", VertexElementFormat.Float4));

        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "FloatColorVertexAttribs.slang");
        return RF.CreateGraphicsProgram(new ShaderDescription(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.Default,
            VertexLayouts = [layout],
            ResourceLayouts =
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements =
                    [
                        new ResourceLayoutElementDescription("Model", ResourceKind.UniformBuffer, ShaderStages.Vertex, 0)
                        {
                            UniformFields =
                            [
                                new UniformBlockField("Ortho", 0, sizeof(float) * 16, UniformScalarType.Float4x4),
                                new UniformBlockField("ColorNormalizationFactor", sizeof(float) * 16, sizeof(uint), UniformScalarType.Int1),
                            ]
                        }
                    ]
                }
            ],
        });
    }

    private ComputeProgram CreateFillProgram()
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "ComputeColoredQuadGenerator.slang");
        ResourceLayoutDescription[] layouts =
        [
            new ResourceLayoutDescription
            {
                Set = 0,
                Elements = [new ResourceLayoutElementDescription("OutputVertices", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 0)]
            }
        ];
        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));
    }

    private DeviceBuffer CreateVertexBuffer(int count = 4)
        => RF.CreateBuffer(new BufferDescription((uint)(Unsafe.SizeOf<PointVertex>() * count), BufferUsage.VertexBuffer));

    private SinkRecorder Record(Action<CommandBuffer> record)
    {
        SinkRecorder sink = new();
        using RenderPipeline pipeline = new([new SinkPass(record)]);
        GD.DispatchGraph(pipeline, new SinkView[] { new() }, sink);
        GD.WaitForIdle();

        return sink;
    }

    private static PropertySet PointProps(int factor = 1)
    {
        PropertySet props = new();
        props.SetMatrix("Ortho", Float4x4.CreateOrthoOffCenter(0, Size, Size, 0, -1, 1));
        props.SetInt("ColorNormalizationFactor", factor);
        return props;
    }

    [SkippableFact]
    public void MixedPass_ReportsExactSequence()
    {
        (Framebuffer fb, _) = CreateTarget();
        GraphicsProgram program = CreatePointProgram();
        ComputeProgram compute = CreateFillProgram();
        DeviceBuffer vb = CreateVertexBuffer();
        DeviceBuffer storage = RF.CreateBuffer(new BufferDescription(64, BufferUsage.StructuredBufferReadWrite));
        VertexSource source = new VertexSource(PrimitiveTopology.PointList).SetBuffer("POSITION", vb);
        PropertySet props = PointProps();
        PropertySet computeProps = new();
        computeProps.SetBuffer("OutputVertices", storage);

        SinkRecorder sink = Record(cl =>
        {
            cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cl.SetShader(program);
            cl.SetVertexSource(source);
            cl.SetProperties(props);
            cl.Draw(4);
            cl.ClearProperties();
            cl.SetComputeShader(compute);
            cl.SetProperties(computeProps);
            cl.Dispatch(1, 1, 1);
        });

        string[] expected =
        [
            "BeginPass:SinkPass",
            "SetFramebuffer:1:False:Clear", "SetViewport", "SetScissor",
            "SetStencilReference", "SetBlendConstants",
            "Props:2", "SetPipeline:Graphics", "BindVertexBuffers", "Draw:4:1:0:0",
            "SetPipeline:Compute", "Props:1", "Dispatch:1:1:1",
            "EndPass",
        ];
        Assert.Equal(expected, sink.Log);
        Assert.Same(program, sink.Pipelines[0].Program);
        Assert.Same(compute, sink.Pipelines[1].Program);
    }

    [SkippableFact]
    public void SetProperties_ReportsWholeTableBeforeEveryDraw()
    {
        (Framebuffer fb, _) = CreateTarget();
        GraphicsProgram program = CreatePointProgram();
        DeviceBuffer vb = CreateVertexBuffer();
        VertexSource source = new VertexSource(PrimitiveTopology.PointList).SetBuffer("POSITION", vb);
        PropertySet props = PointProps(1);
        PropertySet second = new();
        second.SetInt("ColorNormalizationFactor", 7);

        SinkRecorder sink = Record(cl =>
        {
            cl.SetFramebuffer(fb, TargetLoadStoreOps.Clear(Color.Black));
            cl.SetShader(program);
            cl.SetVertexSource(source);
            cl.SetProperties(props);
            cl.Draw(1);
            cl.Draw(1);
            cl.SetProperties(second);
            cl.Draw(1);
        });

        Assert.Equal(3, sink.Tables.Count);
        Assert.Equal(3, sink.VertexBindings.Count);
        Assert.Equal(sink.Tables[0].Length, sink.Tables[2].Length);
        PropertyState last = Assert.Single(sink.Tables[2], p => p.Name == (PropertyID)"ColorNormalizationFactor");
        Assert.Equal(PropertyKind.Uniform, last.Kind);
        Assert.Equal(UniformScalarType.Int1, last.UniformType);
        Assert.Equal(7, last.Uniform.Read<int>());
    }

    [SkippableFact]
    public void SetMutatedAfterSetProperties_ReportsNewValueAtNextDraw()
    {
        (Framebuffer fb, _) = CreateTarget();
        GraphicsProgram program = CreatePointProgram();
        DeviceBuffer vb = CreateVertexBuffer();
        VertexSource source = new VertexSource(PrimitiveTopology.PointList).SetBuffer("POSITION", vb);
        PropertySet props = PointProps(1);

        SinkRecorder sink = Record(cl =>
        {
            cl.SetFramebuffer(fb, TargetLoadStoreOps.Clear(Color.Black));
            cl.SetShader(program);
            cl.SetVertexSource(source);
            cl.SetProperties(props);
            cl.Draw(1);
            props.SetInt("ColorNormalizationFactor", 9);
            cl.Draw(1);
        });

        Assert.Equal(2, sink.Tables.Count);
        PropertyState last = Assert.Single(sink.Tables[1], p => p.Name == (PropertyID)"ColorNormalizationFactor");
        Assert.Equal(9, last.Uniform.Read<int>());
    }

    [SkippableFact]
    public void BufferWrittenBetweenDispatches_IsReEmittedWithNewVersion()
    {
        ComputeProgram compute = CreateFillProgram();
        DeviceBuffer storage = RF.CreateBuffer(new BufferDescription(64, BufferUsage.StructuredBufferReadWrite));
        PropertySet props = new();
        props.SetBuffer("OutputVertices", storage);
        byte[] payload = [1, 2, 3, 4];

        SinkRecorder sink = Record(cl =>
        {
            cl.SetComputeShader(compute);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);
            cl.Dispatch(1, 1, 1);
            cl.UpdateBuffer(storage, 8, (ReadOnlySpan<byte>)payload);
            cl.Dispatch(1, 1, 1);
        });

        Assert.Equal(3, sink.Tables.Count);
        PropertyState[] states = sink.Tables.Select(table => Assert.Single(table)).ToArray();
        Assert.All(states, state => Assert.Equal(PropertyKind.Buffer, state.Kind));
        Assert.All(states, state => Assert.Equal(storage.ResourceId, state.Resource.Resource));
        Assert.All(states, state => Assert.Equal(ResourceRange.Bytes(0, 64), state.Range));
        Assert.True(states[1].Resource.Version > states[0].Resource.Version);
        Assert.True(states[2].Resource.Version > states[1].Resource.Version);

        (ResourceVersion after, uint offset, byte[] data) = Assert.Single(sink.BufferUpdates);
        Assert.Equal(8u, offset);
        Assert.Equal(payload, data);
        Assert.True(after.Version > states[1].Resource.Version);
        Assert.True(states[2].Resource.Version >= after.Version);
    }

    [SkippableFact]
    public void InPassUpdates_CarryBytesAndVersions()
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(
            2, 2, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        byte[] bufferBytes = [9, 8, 7, 6, 5];
        byte[] texels = Enumerable.Range(0, 16).Select(i => (byte)(i * 3)).ToArray();
        ResourceVersion bufferBefore = buffer.CurrentVersion;
        ResourceVersion textureBefore = texture.CurrentVersion;

        SinkRecorder sink = Record(cl =>
        {
            cl.UpdateBuffer(buffer, 4, (ReadOnlySpan<byte>)bufferBytes);
            cl.UpdateTexture(texture, (ReadOnlySpan<byte>)texels);
        });

        (ResourceVersion bufferAfter, uint offset, byte[] data) = Assert.Single(sink.BufferUpdates);
        Assert.Equal(new ResourceVersion(buffer.ResourceId, bufferBefore.Version + 1), bufferAfter);
        Assert.Equal(4u, offset);
        Assert.Equal(bufferBytes, data);

        (ResourceVersion textureAfter, TextureRegion region, byte[] texelData) = Assert.Single(sink.TextureUpdates);
        Assert.Equal(new ResourceVersion(texture.ResourceId, textureBefore.Version + 1), textureAfter);
        Assert.Equal(2u, region.Width);
        Assert.Equal(texels, texelData);
    }

    [SkippableFact]
    public void VertexAndIndexBindings_ReportedAtEveryDraw()
    {
        (Framebuffer fb, _) = CreateTarget();
        GraphicsProgram program = CreatePointProgram();
        DeviceBuffer vb = CreateVertexBuffer();
        DeviceBuffer ib = RF.CreateBuffer(new BufferDescription(16, BufferUsage.IndexBuffer));
        VertexSource source = new VertexSource(PrimitiveTopology.PointList).SetBuffer("POSITION", vb).SetIndexBuffer(ib, IndexFormat.UInt16, 4);
        PropertySet props = PointProps();

        SinkRecorder sink = Record(cl =>
        {
            cl.SetFramebuffer(fb, TargetLoadStoreOps.Clear(Color.Black));
            cl.SetShader(program);
            cl.SetVertexSource(source);
            cl.SetProperties(props);
            cl.DrawIndexed();
            cl.DrawIndexed(2, 1, 0, 0);
        });

        Assert.Equal(2, sink.VertexBindings.Count);
        Assert.Equal(sink.VertexBindings[0], sink.VertexBindings[1]);
        VertexBindingUse binding = Assert.Single(sink.VertexBindings[0]);
        Assert.Equal(vb.ResourceId, binding.Buffer.Resource);
        Assert.Equal((uint)Unsafe.SizeOf<PointVertex>(), binding.Stride);
        Assert.Equal(2, sink.IndexBindings.Count);
        IndexBindingUse index = sink.IndexBindings[0];
        Assert.Equal(ib.ResourceId, index.Buffer.Resource);
        Assert.Equal(IndexFormat.UInt16, index.Format);
        Assert.Equal(4u, index.IndexCount);
        Assert.Equal(2, sink.Log.Count(entry => entry.StartsWith("DrawIndexed")));
        Assert.Contains("DrawIndexed:4:1:0:0:0", sink.Log);
        Assert.Contains("DrawIndexed:4:2:1:0:0", sink.Log);
    }

    [SkippableFact]
    public void ReferencedResources_AreTracked()
    {
        (Framebuffer fb, Texture target) = CreateTarget();
        (Framebuffer clearedFb, _) = CreateTarget();
        GraphicsProgram program = CreatePointProgram();
        DeviceBuffer vb = CreateVertexBuffer();
        VertexSource source = new VertexSource(PrimitiveTopology.PointList).SetBuffer("POSITION", vb);
        PropertySet props = PointProps();
        ResourceVersion vbBefore = vb.CurrentVersion;
        IReadOnlyList<ReferencedResource>? referenced = null;

        Record(cl =>
        {
            cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded));
            cl.SetFramebuffer(clearedFb, TargetLoadStoreOps.Clear(Color.Black));
            cl.SetShader(program);
            cl.SetVertexSource(source);
            cl.SetProperties(props);
            cl.Draw(1);
            referenced = cl.ReferencedResources.ToArray();
        });

        Assert.Contains(referenced!, r => r.Buffer == vb && r.FirstVersion == vbBefore);
        Assert.Contains(referenced!, r => r.Texture == target);
    }

    [SkippableFact]
    public void PassBeginAndEnd_BracketCommands()
    {
        SinkRecorder sink = new();
        using RenderPipeline pipeline = new([new SinkPass(cmd => { })]);
        GD.DispatchGraph(pipeline, new SinkView[] { new() }, sink);
        GD.WaitForIdle();

        Assert.Equal(["BeginPass:SinkPass", "EndPass"], sink.Log);
    }

    [SkippableFact]
    public void NoSinkRegistered_ReportsNothing()
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        int references = -1;

        GD.RunTestGraph((context, cl) =>
        {
            cl.UpdateBuffer(buffer, 0, (ReadOnlySpan<byte>)new byte[] { 1, 2 });
            references = cl.ReferencedResources.Count;
        });
        GD.WaitForIdle();

        Assert.Equal(0, references);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanCommandStreamSinkTests : CommandStreamSinkTests<VulkanDeviceCreator> { }
#endif
