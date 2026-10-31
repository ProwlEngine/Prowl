using System;
using System.Collections.Generic;
using System.Linq;

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

[Collection("GPU Tests")]
public sealed class GlobalsTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly RenderStore _store = new();
    private readonly GlslShaderCompiler _compiler = TestCompiler.Shared;
    private readonly GlobalRegistry _registry;
    private readonly Uploader _uploader;
    private readonly List<RendererError> _reports = new();

    private static readonly GlobalAxis s_fog = GlobalAxis.Define("TEST_FOG", "OFF", "LINEAR", "EXP");

    public GlobalsTests()
    {
        InternalShaders.Register();
        _registry = new GlobalRegistry(_device, _store, new FakeResources()) { Reporter = _reports.Add };
        _uploader = new Uploader(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));
    }

    public void Dispose()
    {
        _uploader.Dispose();
        _store.Dispose();
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
        _registry.Renderer.Set(s_fog, "LINEAR");
        Assert.False(world.KeywordSnapshot.ContainsKey(s_fog.Name));

        _registry.Extract();
        Assert.Equal("LINEAR", world.KeywordSnapshot[s_fog.Name]);

        world.Set(s_fog, "EXP");
        Assert.Equal("LINEAR", world.KeywordSnapshot[s_fog.Name]);
        _registry.Extract();
        Assert.Equal("EXP", world.KeywordSnapshot[s_fog.Name]);
        Assert.Equal("LINEAR", _registry.Renderer.KeywordSnapshot[s_fog.Name]);
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
        Assert.Null(world.Get(s_fog));

        _registry.Renderer.Set(s_fog, "LINEAR");
        Assert.Equal("LINEAR", world.Get(s_fog));

        uint version = world.KeywordVersion;
        world.Set(s_fog, "EXP");
        world.Set(s_fog, "EXP");
        Assert.Equal("EXP", world.Get(s_fog));
        Assert.Equal(version + 1, world.KeywordVersion);

        world.Set(s_fog, null);
        Assert.Equal("LINEAR", world.Get(s_fog));
    }

    [Fact]
    public void Keywords_RejectValuesTheirAxisLacks()
    {
        GlobalProperties globals = _registry.Renderer;
        uint version = globals.KeywordVersion;

        Assert.Throws<ArgumentException>(() => globals.Set(s_fog, "FAST"));
        Assert.Throws<ArgumentException>(() => globals.Set(s_fog, true));
        Assert.Throws<ArgumentException>(() => globals.Set(GlobalAxis.Decals, "maybe"));
        Assert.Throws<ArgumentException>(() => s_fog["FAST"]);
        Assert.Throws<ArgumentException>(() => GlobalAxis.PbrTier[false]);
        Assert.Null(globals.Get(s_fog));
        Assert.Equal(version, globals.KeywordVersion);

        globals.Set(GlobalAxis.Decals, true);
        Assert.Equal(VariantSpace.ToggleValues[1], globals.Get(GlobalAxis.Decals));
        Assert.Equal(new GlobalKeyword[] { GlobalAxis.Decals[true] }, globals.LocalKeywords);
    }

    [Fact]
    public void DefiningAnAxisAgain_GivesTheSameAxis_OnlyWithTheSameValues()
    {
        Assert.Same(s_fog, GlobalAxis.Define("TEST_FOG", "OFF", "LINEAR", "EXP"));
        Assert.Same(s_fog, GlobalAxis.Find("TEST_FOG"));
        Assert.Same(GlobalAxis.LightMasks, GlobalAxis.Toggle("GRAPHITE_LIGHT_MASKS"));
        Assert.Throws<ArgumentException>(() => GlobalAxis.Define("TEST_FOG", "OFF", "LINEAR"));
        Assert.Throws<ArgumentException>(() => GlobalAxis.Toggle("TEST_FOG"));
        Assert.Throws<ArgumentException>(() => GlobalAxis.Define("PBR_TIER", "LITE", "FULL"));
        Assert.Throws<ArgumentException>(() => GlobalAxis.Define("TEST_SINGLE", "ONLY"));
    }

    [Fact]
    public void AShaderDeclaringAnAxisWithOtherValues_FailsToLoad()
    {
        GlobalAxis quality = GlobalAxis.Define("TEST_QUALITY", "HIGH", "MEDIUM", "LOW");

        Assert.Equal(["HIGH", "LOW"], KeywordShader("Test/QualitySubset", "TEST_QUALITY HIGH LOW").GlobalAxes.Single().Values);
        Assert.Throws<InvalidOperationException>(() => KeywordShader("Test/QualityExtra", "TEST_QUALITY HIGH ULTRA"));
        Assert.Throws<InvalidOperationException>(() => KeywordShader("Test/QualityDefault", "TEST_QUALITY LOW HIGH"));
        Assert.Throws<InvalidOperationException>(() => KeywordShader("Test/QualityToggle", "TEST_QUALITY"));
        Assert.Same(quality, GlobalAxis.Find("TEST_QUALITY"));
    }

    [Fact]
    public void DefiningAnAxisALoadedShaderDeclaresWithOtherValues_Throws()
    {
        KeywordShader("Test/Late", "TEST_LATE A B");

        Assert.Throws<ArgumentException>(() => GlobalAxis.Define("TEST_LATE", "A", "C"));
        Assert.Throws<ArgumentException>(() => GlobalAxis.Define("TEST_LATE", "B", "A"));
        Assert.Null(GlobalAxis.Find("TEST_LATE"));
        Assert.Equal(["A", "B", "C"], GlobalAxis.Define("TEST_LATE", "A", "B", "C").Values);
    }

    private Shader KeywordShader(string name, string axis) => new(ShaderParser.Parse($$"""
        Shader "{{name}}"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                GLSLPROGRAM
                Shared
                {
                    #pragma variant global {{axis}}
                }
                ENDGLSL
            }
        }
        """), _compiler);

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
        TestGraph.Run(_device, graph =>
        {
            graph.Uploads("UploadGlobals", _uploader, _registry.Buffer);
            graph.Dispatches("ReadGlobals", program, properties, (uint)region * _registry.RegionWords, [_registry.Buffer], output);
            graph.Copies("ReadbackGlobals", output, readback);
        });
        _device.WaitForIdle();

        float[] values = _device.Map<float>(readback).ToArray();
        _device.Unmap(readback);
        Assert.Equal(new[] { 0.4f, 4f, 6f }, values[..3]);
    }
}
