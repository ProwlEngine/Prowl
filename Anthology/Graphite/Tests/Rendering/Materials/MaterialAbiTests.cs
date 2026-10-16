using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

internal sealed class SlotResources : IMaterialResources
{
    public readonly Dictionary<Texture, uint> Slots = new();

    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => texture != null ? Slots[texture] : 0u;

    public uint SamplerSlot(SamplerDescription? sampler) => 1u;
}

file readonly struct NoView : IRenderView
{
    public Framebuffer? Target => null;
    public uint PixelWidth => 4;
    public uint PixelHeight => 4;
    public int ViewId => 0;
}

file sealed class DrawPass(Action<CommandBuffer> draw) : IPass
{
    public string Name => "Draw";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext context, CommandBuffer cmd) => draw(cmd);
}

public sealed class MaterialAbiTests : IDisposable
{
    private const string Source = """
        Shader "Test/Abi"
        {
            Properties
            {
                _Scale("Scale", Float) = 1
                _Color("Tint", Color) = (1, 1, 1, 1)
                _MainTex("Albedo", Texture2D) = "white" {}
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                GLSLPROGRAM
                Vertex
                {
                    layout(push_constant) uniform Push { uint slot; } push;
                    layout(location = 31) flat out uint graphite_MaterialSlot;

                    void main()
                    {
                        graphite_MaterialSlot = push.slot;
                        float x = -1.0 + float((gl_VertexIndex & 1) << 2);
                        float y = -1.0 + float((gl_VertexIndex & 2) << 1);
                        gl_Position = vec4(x, y, 0.0, 1.0);
                    }
                }
                Fragment
                {
                    #pragma global float _Wind
                    uniform float _Scale;
                    uniform vec4 _Color;
                    uniform sampler2D _MainTex;
                    uniform vec4 _MainTex_ST;
                    layout(location = 0) out vec4 outColor;

                    vec4 Fetch(texture2D tex, sampler smp, vec2 uv)
                    {
                        return texture(sampler2D(tex, smp), uv) * float(textureSize(sampler2D(tex, smp), 0).x);
                    }

                    void main()
                    {
                        outColor = Fetch(_MainTex_Texture, _MainTex_Sampler, vec2(0.5)) * texture(_MainTex, vec2(0.5)).a * _MainTex_ST.x * _Color * _Scale + vec4(_Wind);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private static readonly ResourceLayoutDescription s_frameTable = new(GraphiteBindings.FrameSet,
        new ResourceLayoutElementDescription("Graphite_Textures2D", ResourceKind.TextureReadOnly, ShaderStages.Vertex | ShaderStages.Fragment, GraphiteBindings.Texture2DTable) { ArrayCount = 16 },
        new ResourceLayoutElementDescription("Graphite_Textures2DArray", ResourceKind.TextureReadOnly, ShaderStages.Vertex | ShaderStages.Fragment, GraphiteBindings.Texture2DArrayTable) { ArrayCount = 4 },
        new ResourceLayoutElementDescription("Graphite_Textures3D", ResourceKind.TextureReadOnly, ShaderStages.Vertex | ShaderStages.Fragment, GraphiteBindings.Texture3DTable) { ArrayCount = 4 },
        new ResourceLayoutElementDescription("Graphite_TexturesCube", ResourceKind.TextureReadOnly, ShaderStages.Vertex | ShaderStages.Fragment, GraphiteBindings.TextureCubeTable) { ArrayCount = 4 },
        new ResourceLayoutElementDescription("Graphite_TexturesCubeArray", ResourceKind.TextureReadOnly, ShaderStages.Vertex | ShaderStages.Fragment, GraphiteBindings.TextureCubeArrayTable) { ArrayCount = 4 })
    {
        Table = true,
    };

    private static readonly ResourceLayoutDescription s_viewTable = new(GraphiteBindings.ViewSet,
        new ResourceLayoutElementDescription("Graphite_Samplers", ResourceKind.Sampler, ShaderStages.Vertex | ShaderStages.Fragment, GraphiteBindings.SamplerTable) { ArrayCount = 4 },
        new ResourceLayoutElementDescription("Graphite_Globals", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex | ShaderStages.Fragment, GraphiteBindings.Globals))
    {
        Table = true,
    };

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly SlotResources _resources = new();
    private readonly GlobalRegistry _registry;
    private readonly MaterialSystem _materials;
    private readonly List<IDisposable> _owned = new();

    public MaterialAbiTests()
    {
        _registry = new GlobalRegistry(_device, _resources);
        _materials = new MaterialSystem(_device, _resources);
    }

    public void Dispose()
    {
        _device.WaitForIdle();
        for (int i = _owned.Count - 1; i >= 0; i--)
            _owned[i].Dispose();
        _materials.Dispose();
        _registry.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    private ShaderDescription Build(string source, out Shader shader)
    {
        ShaderDefinition definition = ShaderParser.Parse(source);
        shader = new Shader(definition, _compiler);
        ShaderPass pass = definition.Passes![0];
        ShaderDescription compiled = _compiler.Compile(pass, [], _device.BackendType);
        compiled.Specializations = MaterialLayout.GlobalOffsets(_compiler.GetGlobals(pass), _registry);
        compiled.ResourceLayouts = MaterialLayout.Layouts(shader.Name, compiled.ResourceLayouts, s_frameTable, s_viewTable);
        return compiled;
    }

    private Texture Solid(float r, float g, float b)
    {
        Texture texture = Own(_device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled)));
        Float4 texel = new(r, g, b, 1);
        _device.UpdateTexture<Float4>(texture, [texel, texel, texel, texel]);
        return texture;
    }

    [Fact]
    public void MaterialPasses_ReadTheirRecordsGlobalsAndBindlessTextures_PerDraw()
    {
        ShaderDescription description = Build(Source, out Shader shader);
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        GraphicsProgram program = Own(_device.ResourceFactory.CreateGraphicsProgram(description));

        Texture red = Solid(0.25f, 0, 0);
        Texture blue = Solid(0, 0, 0.125f);
        _resources.Slots[red] = 3;
        _resources.Slots[blue] = 7;

        Material first = _materials.Create(shader);
        first.SetTexture("_MainTex", red);
        first.SetFloat("_Scale", 2);
        Material second = _materials.Create(shader);
        second.SetTexture("_MainTex", blue);
        second.SetColor("_Color", new Float4(1, 1, 0.5f, 1));
        _materials.Extract(new List<Material>());
        _registry.Renderer.SetFloat("_Wind", 0.0625f);
        _registry.Extract();

        GpuTable records = first.Table!.Records;
        _device.UpdateBuffer(records.Buffer, 0, MemoryMarshal.Cast<uint, byte>(Words(records)).ToArray());
        DeviceBuffer globals = Own(_device.ResourceFactory.CreateBuffer(new BufferDescription(_registry.RegionWords * 4, BufferUsage.StructuredBufferReadOnly)));
        _device.UpdateBuffer(globals, 0, _registry.ReadRegion(0).ToArray());

        DescriptorTable frame = Own(_device.ResourceFactory.CreateDescriptorTable(s_frameTable));
        frame.SetTexture("Graphite_Textures2D", 3, red);
        frame.SetTexture("Graphite_Textures2D", 7, blue);
        DescriptorTable view = Own(_device.ResourceFactory.CreateDescriptorTable(s_viewTable));
        view.SetSampler("Graphite_Samplers", 1, Own(_device.ResourceFactory.CreateSampler(SamplerDescription.Point)));
        view.SetBuffer("Graphite_Globals", 0, globals);
        PropertySet pass = new();
        pass.SetBuffer(GraphiteBindings.MaterialsName, records.Buffer);

        Assert.Equal(new Float4(0.25f * 2 * 2 + 0.0625f, 0.0625f, 0.0625f, 2 * 2 + 0.0625f), Draw(program, frame, view, pass, first.Slot));
        Assert.Equal(new Float4(0.0625f, 0.0625f, 0.125f * 2 * 0.5f + 0.0625f, 2 + 0.0625f), Draw(program, frame, view, pass, second.Slot));
    }

    private static uint[] Words(GpuTable table)
    {
        uint[] words = new uint[table.Count * table.StrideWords];
        for (uint i = 0; i < table.Count; i++)
            table.Read(i).CopyTo(words.AsSpan((int)(i * table.StrideWords)));
        return words;
    }

    private Float4 Draw(GraphicsProgram program, DescriptorTable frame, DescriptorTable view, PropertySet pass, uint slot)
    {
        Texture target = Own(_device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled)));
        Framebuffer framebuffer = Own(_device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, target)));
        DeviceBuffer readback = Own(_device.ResourceFactory.CreateBuffer(new BufferDescription(4 * 4 * 16, BufferUsage.Staging)));

        using RenderPipeline pipeline = new([new DrawPass(cmd =>
        {
            cmd.SetFramebuffer(framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cmd.SetFullViewport();
            cmd.SetShader(program);
            cmd.SetVertexSource(VertexSource.None);
            cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame);
            cmd.SetDescriptorTable(GraphiteBindings.ViewSet, view);
            cmd.SetProperties(pass);
            cmd.SetPushConstants(slot);
            cmd.Draw(3);
        })]);
        _device.DispatchGraph([new GraphDispatch(pipeline, [new NoView()])]);
        _device.WaitForIdle();
        _device.Record(cmd => cmd.CopyTextureToBuffer(target, readback, 0, TextureRegion.Whole(target, 0, 0))).Wait();

        Float4 center = _device.Map<Float4>(readback)[2 * 4 + 2];
        _device.Unmap(readback);
        return center;
    }

    [Fact]
    public void LooseUniforms_ThatAreNeitherPropertiesNorGlobals_FailWhereTheyAreRead()
    {
        string source = Source.Replace("#pragma global float _Wind", "uniform float _Wind;");
        ShaderCompileException error = Assert.Throws<ShaderCompileException>(() => Build(source, out _));
        Assert.Contains("_Wind_is_neither_a_property_nor_a_global", error.Message);

        Build(source.Replace(" + vec4(_Wind)", ""), out _);
    }

    [Fact]
    public void PropertiesDeclaredWithAnotherType_FailToCompile()
    {
        string source = Source.Replace("uniform float _Scale;", "uniform vec2 _Scale;").Replace("* _Scale", "* _Scale.x");
        ShaderCompileException error = Assert.Throws<ShaderCompileException>(() => Build(source, out _));
        Assert.Contains("_Scale", error.Message);
    }

    [Fact]
    public void ArraysOfResources_FailToLoad()
    {
        string source = Source
            .Replace("uniform sampler2D _MainTex;", "uniform sampler2D _MainTex;\n                    layout(set = 3, binding = 0) uniform texture2D _Layers[2];")
            .Replace("* _Scale + vec4(_Wind)", "* _Scale + vec4(_Wind) + texture(sampler2D(_Layers[1], _MainTex_Sampler), vec2(0.5))");
        ShaderLoadException error = Assert.Throws<ShaderLoadException>(() => Build(source, out _));
        Assert.Contains("_Layers", error.Message);
    }

    [Fact]
    public void GlobalsDeclaredWithAnotherTypeThanTheRegistryHas_FailToLoad()
    {
        _registry.Renderer.SetFloat4("_Wind", Float4.Zero);
        Assert.Throws<ShaderLoadException>(() => Build(Source, out _));
    }

    [Fact]
    public void VertexStages_ReadingMaterialsWithoutWritingTheSlot_FailToCompile()
    {
        string source = Source
            .Replace("layout(location = 31) flat out uint graphite_MaterialSlot;", "uniform float _Scale;")
            .Replace("graphite_MaterialSlot = push.slot;", "float s = _Scale + float(push.slot);")
            .Replace("gl_Position = vec4(x, y, 0.0, 1.0);", "gl_Position = vec4(x, y, 0.0, s);");
        ShaderCompileException error = Assert.Throws<ShaderCompileException>(() => Build(source, out _));
        Assert.Contains("graphite_MaterialSlot", error.Message);
    }

    [Fact]
    public void FragmentStages_ReadingMaterials_NeedAVertexStageWritingTheSlot()
    {
        string source = Source
            .Replace("layout(location = 31) flat out uint graphite_MaterialSlot;", "")
            .Replace("graphite_MaterialSlot = push.slot;", "float unused = float(push.slot);");
        ShaderCompileException error = Assert.Throws<ShaderCompileException>(() => Build(source, out _));
        Assert.Contains("graphite_MaterialSlot", error.Message);
    }

    [Fact]
    public void MaterialPasses_UseTheRendererSets()
    {
        ShaderDescription description = Build(Source, out _);

        Assert.Equal(new uint[] { 0, 1, 2 }, Array.ConvertAll(description.ResourceLayouts, l => l.Set));
        ResourceLayoutElementDescription materials = Assert.Single(description.ResourceLayouts[2].Elements);
        Assert.Equal(GraphiteBindings.MaterialsName, materials.Name.Text());
        Assert.Equal(ShaderStages.Fragment, materials.Stages);
    }
}
