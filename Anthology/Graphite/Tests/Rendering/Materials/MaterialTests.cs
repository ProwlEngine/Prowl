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
        => texture != null ? 42u : defaultName switch { "white" => 7u, "normal" => 8u, _ => 9u };

    public uint SamplerSlot(SamplerDescription? sampler) => sampler == null ? 0u : 3u;
}

file readonly struct NoView : IRenderView
{
    public Framebuffer? Target => null;
    public uint PixelWidth => 1;
    public uint PixelHeight => 1;
    public int ViewId => 0;
}

public sealed class MaterialTests : IDisposable
{
    private const string Source = """
        Shader "Test/Lit"
        {
            Properties
            {
                _Roughness("Roughness", Float) = 0.5 [Range(0, 1)]
                _Offset("Offset", Vector3) = (1, 2, 3)
                _Count("Count", Integer) = 4
                _Tiling("Tiling", Vector2) = (2, 3)
                _Color("Tint", Color) = (0.5, 0.25, 1, 1) [Gamma]
                _MainTex("Albedo", Texture2D) = "white" {}
                _Bend("Bend", Matrix) = ((1,2,3,4)(5,6,7,8)(9,10,11,12)(13,14,15,16))
                _Linear("Linear", Color) = (0.5, 0.5, 0.5, 1)
            }

            Pass
            {
                Tags { "LightMode" = "Forward" "RenderQueue" = "2450" }
                GLSLPROGRAM
                Shared
                {
                    #pragma variant _NORMALMAP
                    #pragma variant global FOG OFF LINEAR
                }
                ENDGLSL
            }
        }
        """;

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly Shader _shader;
    private readonly MaterialSystem _system;
    private readonly List<string> _reports = new();

    public MaterialTests()
    {
        _shader = new Shader(ShaderParser.Parse(Source), _compiler);
        _system = new MaterialSystem(_device, new FakeResources()) { Reporter = _reports.Add };
    }

    public void Dispose()
    {
        _system.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private uint[] Record(Material material)
    {
        _system.Extract(new List<Material>());
        return material.Table!.Records.Read(material.Slot).ToArray();
    }

    private static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);

    [Fact]
    public void Schema_LaysOutPropertiesInDeclarationOrder_Std430()
    {
        IReadOnlyList<MaterialProperty> p = _shader.Properties;
        Assert.Equal(new uint[] { 0, 16, 28, 32, 48, 64, 96, 160 }, p.Select(x => x.Offset));
        Assert.Equal(80u, p[5].ScaleOffsetOffset);
        Assert.Equal(176u / 4, _shader.Schema.StrideWords);
        Assert.True(p[4].Gamma);
        Assert.False(p[7].Gamma);
        Assert.True(p[0].HasAttribute("Range"));
        Assert.Equal(MaterialPropertyType.Float3, p[1].Type);
    }

    [Fact]
    public void Shader_SplitsAxesByScope_AndReadsItsQueueAndPasses()
    {
        Assert.Equal(["_NORMALMAP"], _shader.MaterialAxes.Select(a => a.Name));
        Assert.Equal(["FOG"], _shader.GlobalAxes.Select(a => a.Name));
        Assert.Equal(2450, _shader.RenderQueue);
        Assert.True(_shader.HasPass(PassType.Forward));
        Assert.False(_shader.HasPass(PassType.ShadowCaster));
    }

    [Fact]
    public void Records_HoldDefaults_WithGammaColoursLinearised()
    {
        uint[] r = Record(_system.Create(_shader));

        Assert.Equal(0.5f, F(r[0]));
        Assert.Equal(new[] { 1f, 2f, 3f }, r[4..7].Select(F));
        Assert.Equal(4u, r[7]);
        Assert.Equal(new[] { 2f, 3f }, r[8..10].Select(F));
        Assert.Equal(0.214041f, F(r[12]), 5);
        Assert.Equal(0.050876f, F(r[13]), 5);
        Assert.Equal(1f, F(r[14]));
        Assert.Equal([7u, 0u], r[16..18]);
        Assert.Equal(new[] { 1f, 1f, 0f, 0f }, r[20..24].Select(F));
        Assert.Equal(new[] { 1f, 2f, 3f, 4f, 5f }, r[24..29].Select(F));
        Assert.Equal(0.5f, F(r[40]));
    }

    [Fact]
    public void Values_ResolveLocalThenParentsThenDefault()
    {
        Material root = _system.Create(_shader);
        Material middle = root.CreateChild();
        Material leaf = middle.CreateChild();
        root.SetFloat("_Roughness", 0.1f);
        middle.SetInt("_Count", 9);
        leaf.SetFloat("_Roughness", 0.9f);

        uint[] r = Record(leaf);
        Assert.Equal(0.9f, F(r[0]));
        Assert.Equal(9u, r[7]);
        Assert.Equal(new[] { 2f, 3f }, r[8..10].Select(F));

        leaf.ClearOverride("_Roughness");
        Assert.Equal(0.1f, F(Record(leaf)[0]));
        Assert.True(leaf.GetLocal("_Roughness").IsEmpty);
        Assert.True(leaf.TryGet("_Roughness", out MaterialValue value));
        Assert.Equal(0.1f, value.Float);
    }

    [Fact]
    public void ParentChanges_RepackTheirChildren()
    {
        Material parent = _system.Create(_shader);
        Material child = parent.CreateChild();
        Record(child);

        parent.SetFloat("_Roughness", 0.25f);
        Assert.Equal(0.25f, F(Record(child)[0]));

        Material other = _system.Create(_shader);
        other.SetFloat("_Roughness", 0.75f);
        child.Parent = other;
        Assert.Equal(0.75f, F(Record(child)[0]));
    }

    [Fact]
    public void Cycles_Throw()
    {
        Material a = _system.Create(_shader);
        Material b = a.CreateChild();
        Assert.Throws<InvalidOperationException>(() => a.Parent = b);
        Assert.Throws<InvalidOperationException>(() => a.Parent = a);
    }

    [Fact]
    public void UnknownNames_AreKept_ReportedOnce_AndGoLiveWhenTheShaderGainsThem()
    {
        Shader small = new(ShaderParser.Parse("""
            Shader "Test/Small"
            {
                Properties { _Count("Count", Integer) = 1 }
                Pass { GLSLPROGRAM ENDGLSL }
            }
            """), _compiler);
        Material material = _system.Create(small);
        material.SetFloat("_Roughness", 0.3f);
        material.SetFloat("_Roughness", 0.35f);
        material.SetInt("_Count", 5);
        Assert.Single(_reports);
        Assert.Equal(16u / 4, small.Schema.StrideWords);
        Assert.Equal(5u, Record(material)[0]);

        material.Shader = _shader;
        uint[] r = Record(material);
        Assert.Equal(0.35f, F(r[0]));
        Assert.Equal(5u, r[7]);
        Assert.Equal(2, material.LocalValues.Count());
    }

    [Fact]
    public void MismatchedTypes_FallBackToTheDefault()
    {
        Material material = _system.Create(_shader);
        material.SetMatrix("_Roughness", Float4x4.Identity);
        Assert.Equal(0.5f, F(Record(material)[0]));
        Assert.Single(_reports);

        material.SetColor("_Offset", new Float4(4, 5, 6, 7));
        Assert.Equal(new[] { 4f, 5f, 6f }, Record(material)[4..7].Select(F));
    }

    [Fact]
    public void Textures_StoreTheirSlots_AndKeepTheirScaleOffset()
    {
        using Texture texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        Material material = _system.Create(_shader);
        material.SetTextureScaleOffset("_MainTex", new Float4(2, 2, 0.5f, 0));
        material.SetTexture("_MainTex", texture, SamplerDescription.Linear);

        uint[] r = Record(material);
        Assert.Equal([42u, 3u], r[16..18]);
        Assert.Equal(new[] { 2f, 2f, 0.5f, 0f }, r[20..24].Select(F));

        material.SetTexture("_MainTex", null);
        Assert.Equal([7u, 0u], Record(material)[16..18]);
    }

    [Fact]
    public void Keywords_InheritAndDefault_AndGlobalAxesAreRefused()
    {
        Material parent = _system.Create(_shader);
        Material child = parent.CreateChild();
        Assert.Equal("false", child.GetKeyword("_NORMALMAP"));

        parent.EnableKeyword("_NORMALMAP");
        Assert.Equal("true", child.GetKeyword("_NORMALMAP"));

        child.SetKeyword("FOG", "LINEAR");
        child.SetKeyword("FOG", "LINEAR");
        Assert.Empty(child.LocalKeywords);
        Assert.Single(_reports);
    }

    [Fact]
    public void DrawChanges_AreReported_ButValueChangesAreNot()
    {
        Material material = _system.Create(_shader);
        List<Material> drawChanged = new();
        _system.Extract(drawChanged);
        Assert.Equal([material], drawChanged);

        drawChanged.Clear();
        material.SetFloat("_Roughness", 0.2f);
        _system.Extract(drawChanged);
        Assert.Empty(drawChanged);

        material.EnableKeyword("_NORMALMAP");
        material.RenderQueue = 3000;
        _system.Extract(drawChanged);
        Assert.Equal([material], drawChanged);
        Assert.Equal(3000, material.RenderQueue);
    }

    [Fact]
    public void FreedSlotsAndIds_ReturnOnlyAfterRetire()
    {
        Material first = _system.Create(_shader);
        Record(first);
        uint slot = first.Slot;
        int id = first.Id;
        Uploader uploader = new(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));

        first.Dispose();
        _system.Extract(new List<Material>());
        _system.Flush(uploader, 4);
        _system.Reclaim(3);
        Material other = _system.Create(_shader);
        Assert.NotEqual(id, other.Id);
        Record(other);
        Assert.NotEqual(slot, other.Slot);

        _system.Reclaim(4);
        Material reused = _system.Create(_shader);
        Assert.Equal(id, reused.Id);
        Record(reused);
        Assert.Equal(slot, reused.Slot);
        uploader.Dispose();
    }

    [Fact]
    public void MaterialTables_ReadBackInGlsl()
    {
        InternalShaders.Register();
        using Uploader uploader = new(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));
        _system.Create(_shader);
        Material material = _system.Create(_shader);
        material.SetFloat3("_Offset", new Float3(7, 8, 9));
        material.SetFloat2("_Tiling", new Float2(5, 6));
        _system.Extract(new List<Material>());
        Assert.True(_system.Flush(uploader, 0));
        DeviceBuffer table = material.Table!.Records.Buffer;

        const string source = """
            Compute
            {
                layout(local_size_x = 1) in;

                struct Record
                {
                    float roughness;
                    vec3 offset;
                    int count;
                    vec2 tiling;
                    vec4 color;
                    uint mainTex;
                    uint mainTexSampler;
                    vec4 mainTexScaleOffset;
                    mat4 bend;
                    vec4 linearColor;
                };

                layout(set = 0, binding = 0, std430) readonly buffer Materials { Record records[]; };
                layout(set = 0, binding = 1, std430) buffer Output { float values[]; };
                layout(push_constant) uniform Push { uint slot; } push;

                void main()
                {
                    Record r = records[push.slot];
                    values[0] = r.offset.z;
                    values[1] = float(r.count);
                    values[2] = r.tiling.y;
                    values[3] = float(r.mainTex);
                    values[4] = r.mainTexScaleOffset.x;
                    values[5] = r.bend[1][2];
                    values[6] = r.linearColor.x;
                    values[7] = r.roughness;
                }
            }
            """;
        using ComputeProgram program = _device.ResourceFactory.CreateComputeProgram(
            _compiler.CompileCompute(new ShaderPass { State = new PassState(), Source = source }, [], _device.BackendType));
        using DeviceBuffer output = _device.ResourceFactory.CreateBuffer(new BufferDescription(32, BufferUsage.StructuredBufferReadWrite));
        using DeviceBuffer readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(32, BufferUsage.Staging));
        PropertySet properties = new();
        properties.SetBuffer("Materials", table);
        properties.SetBuffer("Output", output);
        using RenderPipeline pipeline = new([
            new UploadTablePass(uploader, table),
            new ReadTablePass(program, properties, material.Slot, table, output),
            new ReadbackPass(output, readback)]);
        _device.DispatchGraph([new GraphDispatch(pipeline, [new NoView()])]);
        _device.WaitForIdle();

        float[] values = _device.Map<float>(readback).ToArray();
        _device.Unmap(readback);
        Assert.Equal(new[] { 9f, 4f, 6f, 7f, 1f, 7f, 0.5f, 0.5f }, values);
    }
}

file sealed class UploadTablePass(Uploader uploader, DeviceBuffer table) : IPass
{
    public string Name => "UploadTable";

    public void Setup(RenderContextBuilder builder) => builder.DeclareImportedBuffer(RenderResourceID.Intern("MaterialTable"), table, BufferAccess.ShaderWrite);

    public void Render(RenderContext context, CommandBuffer cmd) => uploader.Record(cmd, context.Task);
}

file sealed class ReadTablePass(ComputeProgram program, PropertySet properties, uint slot, DeviceBuffer table, DeviceBuffer output) : IPass
{
    public string Name => "ReadTable";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedBuffer(RenderResourceID.Intern("MaterialTable"), table, BufferAccess.ShaderRead);
        builder.DeclareImportedBuffer(RenderResourceID.Intern("MaterialOutput"), output, BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        cmd.SetComputeShader(program);
        cmd.SetProperties(properties);
        cmd.SetPushConstants(slot);
        cmd.Dispatch(1, 1, 1);
    }
}

file sealed class ReadbackPass(DeviceBuffer output, DeviceBuffer readback) : IPass
{
    public string Name => "Readback";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedBuffer(RenderResourceID.Intern("MaterialOutput"), output, BufferAccess.TransferRead);
        builder.DeclareImportedBuffer(RenderResourceID.Intern("MaterialReadback"), readback, BufferAccess.TransferWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd) => cmd.CopyBuffer(output, 0, readback, 0, output.SizeInBytes);
}
