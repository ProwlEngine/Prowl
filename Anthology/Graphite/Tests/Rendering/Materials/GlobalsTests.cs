using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

file sealed class FakeResources : IMaterialResources
{
    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName)
        => texture != null ? 42u : defaultName == "black" ? 5u : 9u;

    public uint SamplerSlot(SamplerDescription? sampler) => sampler == null ? 0u : 3u;
}

file readonly struct NoView : IRenderView
{
    public Framebuffer? Target => null;
    public uint PixelWidth => 1;
    public uint PixelHeight => 1;
    public int ViewId => 0;
}

file sealed class UploadPass(Uploader uploader, DeviceBuffer buffer) : IPass
{
    public string Name => "UploadGlobals";

    public void Setup(RenderContextBuilder builder) => builder.DeclareImportedBuffer(RenderResourceID.Intern("Globals"), buffer, BufferAccess.ShaderWrite);

    public void Render(RenderContext context, CommandBuffer cmd) => uploader.Record(cmd, context.Task);
}

file sealed class ReadPass(ComputeProgram program, PropertySet properties, uint regionBase, DeviceBuffer globals, DeviceBuffer output) : IPass
{
    public string Name => "ReadGlobals";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedBuffer(RenderResourceID.Intern("Globals"), globals, BufferAccess.ShaderRead);
        builder.DeclareImportedBuffer(RenderResourceID.Intern("GlobalsOutput"), output, BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        cmd.SetComputeShader(program);
        cmd.SetProperties(properties);
        cmd.SetPushConstants(regionBase);
        cmd.Dispatch(1, 1, 1);
    }
}

file sealed class ReadbackPass(DeviceBuffer output, DeviceBuffer readback) : IPass
{
    public string Name => "ReadbackGlobals";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedBuffer(RenderResourceID.Intern("GlobalsOutput"), output, BufferAccess.TransferRead);
        builder.DeclareImportedBuffer(RenderResourceID.Intern("GlobalsReadback"), readback, BufferAccess.TransferWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd) => cmd.CopyBuffer(output, 0, readback, 0, output.SizeInBytes);
}

public sealed class GlobalsTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly GlobalRegistry _registry;
    private readonly Uploader _uploader;
    private readonly List<RendererError> _reports = new();

    public GlobalsTests()
    {
        InternalShaders.Register();
        _registry = new GlobalRegistry(_device, new FakeResources()) { Reporter = _reports.Add };
        _uploader = new Uploader(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));
    }

    public void Dispose()
    {
        _uploader.Dispose();
        _registry.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);

    private uint[] Region(int region) => _registry.ReadRegion(region).ToArray();

    [Fact]
    public void Offsets_AppendInStd430_AndNeverMove()
    {
        _registry.Renderer.SetFloat("_A", 1);
        _registry.Renderer.SetFloat3("_B", new Float3(1, 2, 3));
        _registry.Renderer.SetFloat("_C", 2);
        _registry.Renderer.SetMatrix("_D", Float4x4.Identity);
        Assert.True(_registry.Declare("_E", MaterialPropertyType.Texture2D, out _, out _));

        Assert.Equal(new uint[] { 0, 4, 7, 8, 24 }, _registry.Properties.Select(p => p.Offset));

        _registry.Renderer.SetFloat("_A", 5);
        Assert.Equal(0u, _registry.Properties[0].Offset);
    }

    [Fact]
    public void ConflictingTypes_FailForTheSecondDeclaration_AndHostValuesAreRefused()
    {
        _registry.Renderer.SetFloat("_Wind", 0.4f);
        Assert.False(_registry.Declare("_Wind", MaterialPropertyType.Float4, out _, out string? error));
        Assert.Contains("_Wind", error);
        Assert.True(_registry.Declare("_Tint", MaterialPropertyType.Color, out _, out _));
        Assert.True(_registry.Declare("_Tint", MaterialPropertyType.Float4, out _, out _));

        _registry.Renderer.SetTexture("_Wind", null);
        _registry.Renderer.SetTexture("_Wind", null);
        Assert.Single(_reports);
        Assert.True(_registry.Renderer.TryGet("_Wind", out MaterialValue value));
        Assert.Equal(0.4f, value.Float);
    }

    [Fact]
    public void WorldRegions_OverrideTheRendererValues()
    {
        _registry.Renderer.SetFloat("_Wind", 0.4f);
        _registry.Renderer.SetFloat("_Rain", 0.1f);
        (GlobalProperties world, int region) = _registry.AddWorld();
        world.SetFloat("_Rain", 0.9f);
        Assert.True(_registry.Declare("_Noise", MaterialPropertyType.Texture2D, out _, out _));
        _registry.Extract();

        Assert.Equal(1, region);
        Assert.Equal(new[] { 0.4f, 0.1f }, Region(0)[..2].Select(F));
        Assert.Equal(new[] { 0.4f, 0.9f }, Region(1)[..2].Select(F));
        Assert.Equal([5u, 0u], Region(1)[2..4]);

        world.Clear("_Rain");
        _registry.Extract();
        Assert.Equal(0.1f, F(Region(1)[1]));
    }

    [Fact]
    public void OnlyChangedRegionsAreWritten()
    {
        _registry.Renderer.SetFloat("_Wind", 0.4f);
        (GlobalProperties first, _) = _registry.AddWorld();
        _registry.AddWorld();
        _registry.Extract();
        _registry.Flush(_uploader, 0);
        uint words = _registry.RegionWords;

        uint before = _uploader.QueuedWords;
        first.SetFloat("_Wind", 0.5f);
        _registry.Extract();
        _registry.Flush(_uploader, 0);
        Assert.Equal(words, _uploader.QueuedWords - before);

        before = _uploader.QueuedWords;
        _registry.Renderer.SetFloat("_Wind", 0.6f);
        _registry.Extract();
        _registry.Flush(_uploader, 0);
        Assert.Equal(words * 3, _uploader.QueuedWords - before);
    }

    [Fact]
    public void ManyGlobals_GrowTheRegions_KeepingEveryValue_AndReportTheNewBuffer()
    {
        (GlobalProperties world, int region) = _registry.AddWorld();
        world.SetFloat("_First", 7);
        _registry.Extract();
        Assert.False(_registry.Flush(_uploader, 0));
        DeviceBuffer before = _registry.Buffer;

        for (int i = 0; i < 40; i++)
            _registry.Renderer.SetFloat4("_Grow" + i, new Float4(i, 0, 0, 0));
        _registry.Extract();

        Assert.True(_registry.RegionWords >= 161);
        Assert.True(_registry.Flush(_uploader, 1));
        Assert.NotSame(before, _registry.Buffer);
        Assert.Equal(7f, F(Region(region)[0]));
        Assert.Equal(39f, F(Region(region)[(int)_registry.Properties[^1].Offset]));
    }

    [Fact]
    public void KeywordSnapshots_ChangeOnlyAtExtraction()
    {
        (GlobalProperties world, _) = _registry.AddWorld();
        _registry.Renderer.SetKeyword("FOG", "LINEAR");
        Assert.False(world.KeywordSnapshot.ContainsKey("FOG"));

        _registry.Extract();
        Assert.Equal("LINEAR", world.KeywordSnapshot["FOG"]);

        world.SetKeyword("FOG", "EXP");
        Assert.Equal("LINEAR", world.KeywordSnapshot["FOG"]);
        _registry.Extract();
        Assert.Equal("EXP", world.KeywordSnapshot["FOG"]);
        Assert.Equal("LINEAR", _registry.Renderer.KeywordSnapshot["FOG"]);
    }

    [Fact]
    public void NewGlobals_GrowTheRegionsOnlyAtExtraction()
    {
        uint words = _registry.RegionWords;
        DeviceBuffer buffer = _registry.Buffer;
        for (int i = 0; i < 40; i++)
            Assert.True(_registry.Declare("_Late" + i, MaterialPropertyType.Float4, out _, out _));

        Assert.True(_registry.NeedsGrowth);
        Assert.Equal(words, _registry.RegionWords);
        Assert.Same(buffer, _registry.Buffer);

        _registry.Extract();
        Assert.False(_registry.NeedsGrowth);
        Assert.True(_registry.RegionWords > words);
    }

    [Fact]
    public void Keywords_ResolveWorldThenRenderer()
    {
        (GlobalProperties world, _) = _registry.AddWorld();
        Assert.Null(world.GetKeyword("FOG"));

        _registry.Renderer.SetKeyword("FOG", "LINEAR");
        Assert.Equal("LINEAR", world.GetKeyword("FOG"));

        uint version = world.KeywordVersion;
        world.SetKeyword("FOG", "EXP");
        world.SetKeyword("FOG", "EXP");
        Assert.Equal("EXP", world.GetKeyword("FOG"));
        Assert.Equal(version + 1, world.KeywordVersion);

        world.ClearKeyword("FOG");
        Assert.Equal("LINEAR", world.GetKeyword("FOG"));
    }

    [Fact]
    public void RemovedWorldRegions_AreReusedAfterRetire()
    {
        (_, int first) = _registry.AddWorld();
        _registry.RemoveWorld(first);
        _registry.Flush(_uploader, 3);
        (_, int second) = _registry.AddWorld();
        Assert.NotEqual(first, second);

        _registry.Reclaim(3);
        (_, int third) = _registry.AddWorld();
        Assert.Equal(first, third);
    }

    [Fact]
    public void Regions_ReadBackInGlsl()
    {
        _registry.Renderer.SetFloat("_Wind", 0.4f);
        _registry.Renderer.SetFloat3("_Sun", new Float3(1, 2, 3));
        (GlobalProperties world, int region) = _registry.AddWorld();
        world.SetFloat3("_Sun", new Float3(4, 5, 6));
        _registry.Extract();
        _registry.Flush(_uploader, 0);

        const string source = """
            Compute
            {
                layout(local_size_x = 1) in;
                layout(set = 0, binding = 0, std430) readonly buffer Graphite_Globals { uint globals[]; };
                layout(set = 0, binding = 1, std430) buffer Output { float values[]; };
                layout(push_constant) uniform Push { uint regionBase; } push;

                void main()
                {
                    values[0] = uintBitsToFloat(globals[push.regionBase + 0u]);
                    values[1] = uintBitsToFloat(globals[push.regionBase + 4u]);
                    values[2] = uintBitsToFloat(globals[push.regionBase + 6u]);
                }
            }
            """;
        using ComputeProgram program = _device.ResourceFactory.CreateComputeProgram(
            _compiler.CompileCompute(new ShaderPass { State = new PassState(), Source = source }, [], _device.BackendType));
        using DeviceBuffer output = _device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        using DeviceBuffer readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.Staging));
        PropertySet properties = new();
        properties.SetBuffer("Graphite_Globals", _registry.Buffer);
        properties.SetBuffer("Output", output);
        using RenderPipeline pipeline = new([
            new UploadPass(_uploader, _registry.Buffer),
            new ReadPass(program, properties, (uint)region * _registry.RegionWords, _registry.Buffer, output),
            new ReadbackPass(output, readback)]);
        _device.DispatchGraph([new GraphDispatch(pipeline, [new NoView()])]);
        _device.WaitForIdle();

        float[] values = _device.Map<float>(readback).ToArray();
        _device.Unmap(readback);
        Assert.Equal(new[] { 0.4f, 4f, 6f }, values[..3]);
    }
}
