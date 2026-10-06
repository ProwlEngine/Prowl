using PropertyDeltaKind = Prowl.Graphite.Debugger.Trace.PropertyDeltaKind;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

public class TraceModelTests
{
    private static BlobRef Blob(byte seed, ulong length = 4) => new(EquatableArray.Create<byte>(seed, 1, 2, 3), length);

    private static TraceVersion Version(uint resource, uint version) => new(new TraceResourceId(resource), version);

    private static TraceResource Resource(PixelFormat format, uint id = 1) => new(
        new TraceResourceId(id),
        "Color",
        GraphResourceKind.Texture,
        true,
        GraphTextureDesc.ViewSized(format, 0.5f, true),
        null,
        Version(id, 3),
        new SnapshotRef(new TraceResourceId(id), 3, SnapshotState.Captured, Blob(9)));

    private static TracePass Pass(string name, params TraceCommand[] commands) => new(
        name,
        0,
        EquatableArray.Create(new TraceAccess(new TraceResourceId(1), GraphResourceKind.Texture, true, TextureState.Attachment, null, BufferAccess.None)),
        EquatableArray.Create(new TraceUse(new TraceResourceId(1), 3, TraceRange.Subresources(0, 1, 0, 1), TraceResourceUsage.Sampled)),
        EquatableArray<TraceUse>.Empty,
        EquatableArray<TraceLoadedAttachment>.Empty,
        EquatableArray.Create(new TraceResourceId(5)),
        true,
        EquatableArray<SnapshotRef>.Empty,
        PassMarks.None,
        EquatableArray.Create<TraceCommand>(commands));

    [Fact]
    public void EquatableArray_ComparesByContent()
    {
        Assert.Equal(EquatableArray.Create(1, 2, 3), EquatableArray.Create(1, 2, 3));
        Assert.NotEqual(EquatableArray.Create(1, 2, 3), EquatableArray.Create(1, 2, 4));
        Assert.NotEqual(EquatableArray.Create(1, 2), EquatableArray.Create(1, 2, 3));
        Assert.Equal(EquatableArray<int>.Empty, default);
        Assert.Equal(EquatableArray.Create(1, 2, 3).GetHashCode(), EquatableArray.Create(1, 2, 3).GetHashCode());
    }

    [Fact]
    public void BlobRef_ComparesByHash()
    {
        Assert.Equal(Blob(1), Blob(1));
        Assert.NotEqual(Blob(1), Blob(2));
        Assert.NotEqual(Blob(1), Blob(1, 8));
    }

    [Fact]
    public void TraceResource_ComparesGraphTextureFormatsByContent()
    {
        TraceResource a = Resource(PixelFormat.R8_G8_B8_A8_UNorm);
        TraceResource b = Resource(PixelFormat.R8_G8_B8_A8_UNorm);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, Resource(PixelFormat.R16_G16_B16_A16_Float));
        Assert.NotEqual(a, Resource(PixelFormat.R8_G8_B8_A8_UNorm, 2));
    }

    [Fact]
    public void TraceResource_ComparesBufferAndMissingTexture()
    {
        TraceResource a = Resource(PixelFormat.R8_G8_B8_A8_UNorm) with { Texture = null, Buffer = GraphBufferDesc.Uniform(64) };
        TraceResource b = Resource(PixelFormat.R8_G8_B8_A8_UNorm) with { Texture = null, Buffer = GraphBufferDesc.Uniform(64) };
        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { Buffer = GraphBufferDesc.Uniform(128) });
    }

    [Fact]
    public void TracePass_ComparesCommandListsInOrder()
    {
        TraceCommand[] first = { new SetViewportCommand(new Viewport(0, 0, 8, 8, 0, 1)), new DrawCommand(3, 1, 0, 0) };
        TraceCommand[] same = { new SetViewportCommand(new Viewport(0, 0, 8, 8, 0, 1)), new DrawCommand(3, 1, 0, 0) };
        TraceCommand[] swapped = { new DrawCommand(3, 1, 0, 0), new SetViewportCommand(new Viewport(0, 0, 8, 8, 0, 1)) };

        Assert.Equal(Pass("Main", first), Pass("Main", same));
        Assert.NotEqual(Pass("Main", first), Pass("Main", swapped));
        Assert.NotEqual(Pass("Main", first), Pass("Other", same));
    }

    [Fact]
    public void PropertyDelta_ComparesUniformBytes()
    {
        TracePropertyDelta Delta(byte value) => new(
            "_Tint",
            PropertyDeltaKind.Uniform,
            UniformScalarType.Float1,
            EquatableArray.Create<byte>(value, 0, 0, 0),
            null,
            default,
            null,
            null);

        Assert.Equal(new ApplyPropertyDeltasCommand(EquatableArray.Create(Delta(1))), new ApplyPropertyDeltasCommand(EquatableArray.Create(Delta(1))));
        Assert.NotEqual(new ApplyPropertyDeltasCommand(EquatableArray.Create(Delta(1))), new ApplyPropertyDeltasCommand(EquatableArray.Create(Delta(2))));
    }

    [Fact]
    public void Commands_ComparePipelineAndFramebuffer()
    {
        TracePipelineState Pipeline(PixelFormat format) => new(
            new TraceProgramId(1),
            PrimitiveTopology.TriangleList,
            EquatableArray.Create(format),
            null,
            TextureSampleCount.Count1,
            default,
            default,
            Color.White,
            EquatableArray<BlendAttachmentDescription>.Empty,
            false);

        Assert.Equal(new SetPipelineCommand(Pipeline(PixelFormat.R8_G8_B8_A8_UNorm)), new SetPipelineCommand(Pipeline(PixelFormat.R8_G8_B8_A8_UNorm)));
        Assert.NotEqual(new SetPipelineCommand(Pipeline(PixelFormat.R8_G8_B8_A8_UNorm)), new SetPipelineCommand(Pipeline(PixelFormat.B8_G8_R8_A8_UNorm)));

        SetFramebufferCommand Framebuffer(uint version) => new(
            EquatableArray.Create(new TraceAttachment(new TraceResourceId(1), version, 0, 0)),
            null,
            new TargetLoadStoreOps(new AttachmentOps(LoadAction.Clear, StoreAction.Store), new AttachmentOps(LoadAction.DontCare, StoreAction.DontCare)));

        Assert.Equal(Framebuffer(1), Framebuffer(1));
        Assert.NotEqual(Framebuffer(1), Framebuffer(2));
    }

    [Fact]
    public void Commands_DifferentKindsNeverEqual()
    {
        TraceCommand draw = new DrawCommand(3, 1, 0, 0);
        TraceCommand indexed = new DrawIndexedCommand(3, 1, 0, 0, 0);
        Assert.NotEqual(draw, indexed);
        Assert.Equal(new ClearPropertiesCommand(), new ClearPropertiesCommand());
    }

    [Fact]
    public void Document_ComparesWholeTrace()
    {
        TraceDocument Build(byte seed) => new(
            EquatableArray.Create(new TraceExecution(
                7,
                "Main",
                0,
                640,
                480,
                EquatableArray.Create(Resource(PixelFormat.R8_G8_B8_A8_UNorm)),
                EquatableArray.Create(new TraceGhost(
                    new TraceResourceId(5),
                    "Mesh",
                    Version(5, 1),
                    null,
                    new BufferDescription(256, BufferUsage.VertexBuffer),
                    new SnapshotRef(new TraceResourceId(5), 1, SnapshotState.Captured, Blob(seed)))),
                EquatableArray.Create(Pass("Main", new DrawCommand(3, 1, 0, 0))))),
            EquatableArray.Create(new TraceProgram(new TraceProgramId(1), "Lit", Blob(1), Blob(3))),
            EquatableArray.Create(new ResourceWriteEvent(0, Version(5, 1), TraceRange.Bytes(0, 256), null, Blob(seed))));

        Assert.Equal(Build(1), Build(1));
        Assert.Equal(Build(1).GetHashCode(), Build(1).GetHashCode());
        Assert.NotEqual(Build(1), Build(2));
    }

    [Fact]
    public void SnapshotRef_SkippedHasNoData()
    {
        SnapshotRef skipped = new(new TraceResourceId(1), 2, SnapshotState.Skipped, null);
        Assert.Equal(skipped, new SnapshotRef(new TraceResourceId(1), 2, SnapshotState.Skipped, null));
        Assert.NotEqual(skipped, skipped with { State = SnapshotState.Captured });
    }
}
