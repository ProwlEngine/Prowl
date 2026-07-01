using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Compiles the shared shader suite through the GL target and checks the emitted GLSL, entry points,
// vertex locations, and resource reflection. The expected resource layouts are authored statically
// here (the GL "reflect" target) and compared by the shared ReflectionTestbed; backend-specific GL
// behavior (emitted-name disambiguation, structured-buffer naming) is covered by its own cases.
public class OpenGLCompilationTests
{
    const ShaderStages VF = ShaderStages.Vertex | ShaderStages.Fragment;

    static ShaderDescription Compile(string module) =>
        CompilerTestHarness.CompileShared(module, () => new GLCompiler()).Backends[0].Description;


    static ResourceLayoutElementDescription Ubo(string name, int binding, string glName, params UniformBlockField[] fields)
        => new(name, ResourceKind.UniformBuffer, VF, binding, ResourceLayoutElementOptions.None, glName, fields);

    static ResourceLayoutElementDescription Tex(string name, int binding, string glName)
        => new(name, ResourceKind.TextureReadOnly, VF, binding, ResourceLayoutElementOptions.None, glName, []);

    static ResourceLayoutElementDescription Samp(string name, int binding, string glName)
        => new(name, ResourceKind.Sampler, VF, binding, ResourceLayoutElementOptions.None, glName, []);


    [Fact]
    public void Graphics_StagesHaveExpectedEntryPoints()
    {
        ReflectionTestbed.AssertStages(Compile("Graphics"),
            (ShaderStages.Vertex, "vertex"), (ShaderStages.Fragment, "fragment"));
    }


    [Fact]
    public void Graphics_VertexInputsAtExpectedLocations()
    {
        ReflectionTestbed.AssertVertexLocations(Compile("Graphics"),
            (0, VertexElementFormat.Float3), (1, VertexElementFormat.Float2), (2, VertexElementFormat.Float4));
    }


    [Theory]
    [InlineData("Graphics")]
    [InlineData("Modules")]
    [InlineData("ConstantBuffers")]
    [InlineData("ParameterBlocks")]
    [InlineData("UVOriginUsage")]
    public void Glsl_MatchesKnownGood(string module)
    {
        ShaderDescription d = Compile(module);

        Assert.Equal(
            CompilerTestHarness.KnownGoodText($"{module}.vertex.glsl"),
            CompilerTestHarness.StageText(d, ShaderStages.Vertex));

        Assert.Equal(
            CompilerTestHarness.KnownGoodText($"{module}.fragment.glsl"),
            CompilerTestHarness.StageText(d, ShaderStages.Fragment));
    }


    // Two raw constant buffer blocks: each binds in declaration order in set 0, emitted as block_<Type>,
    // and carries the std140 field layout of its element struct.
    [Fact]
    public void ConstantBuffers_ReflectBlocksAndFields()
    {
        ReflectionTestbed.AssertResourceLayouts(Compile("ConstantBuffers"),
            new ResourceLayoutDescription(0,
                Ubo("camera", 0, "block_Camera_0",
                    new UniformBlockField("viewProj", 0, 64, UniformScalarType.Float4x4),
                    new UniformBlockField("cameraPos", 64, 12, UniformScalarType.Float3),
                    new UniformBlockField("time", 76, 4, UniformScalarType.Float1)),
                Ubo("material", 1, "block_Material_0",
                    new UniformBlockField("baseColor", 0, 16, UniformScalarType.Float4),
                    new UniformBlockField("tiling", 16, 8, UniformScalarType.Float2),
                    new UniformBlockField("flags", 24, 4, UniformScalarType.Int1))));
    }


    // `lighting` is declared in the imported Common module, not the base Modules module; it must still
    // surface in the program reflection alongside the base module's `globals`.
    [Fact]
    public void Modules_SurfaceImportedModuleBlock()
    {
        ReflectionTestbed.AssertResourceLayouts(Compile("Modules"),
            new ResourceLayoutDescription(0,
                Ubo("globals", 0, "block_Globals_0",
                    new UniformBlockField("viewProj", 0, 64, UniformScalarType.Float4x4),
                    new UniformBlockField("tint", 64, 16, UniformScalarType.Float4)),
                Ubo("lighting", 1, "block_Lighting_0",
                    new UniformBlockField("sunDirection", 0, 16, UniformScalarType.Float4),
                    new UniformBlockField("sunColor", 16, 16, UniformScalarType.Float4),
                    new UniformBlockField("ambientIntensity", 32, 4, UniformScalarType.Float1))));
    }


    // Each ParameterBlock opens its own GL "set" (addressing key); inner resources bind from the block's
    // slots and are emitted with the block path as a name prefix. perObject carries uniform data (an
    // implicit block at slot 0), onlyTex is resource-only (no block, binds from slot 0).
    [Fact]
    public void ParameterBlocks_OpenSetsWithPrefixedNames()
    {
        ReflectionTestbed.AssertResourceLayouts(Compile("ParameterBlocks"),
            new ResourceLayoutDescription(0,
                Ubo("globals", 0, "block_Globals_0",
                    new UniformBlockField("viewProj", 0, 64, UniformScalarType.Float4x4),
                    new UniformBlockField("tint", 64, 16, UniformScalarType.Float4)),
                Tex("albedo", 1, "albedo_0"),
                Samp("samp", 2, "samp_0")),
            new ResourceLayoutDescription(1,
                Ubo("perObject", 0, "block_PerObject_0",
                    new UniformBlockField("color", 0, 16, UniformScalarType.Float4),
                    new UniformBlockField("uvOffset", 16, 8, UniformScalarType.Float2)),
                Tex("detail", 1, "perObject_detail_0"),
                Samp("detailSamp", 2, "perObject_detailSamp_0")),
            new ResourceLayoutDescription(2,
                Tex("tex", 0, "onlyTex_tex_0"),
                Samp("s", 1, "onlyTex_s_0")));
    }


    // Backend-specific: two ConstantBuffers of the same element type share the base name block_Material,
    // so Slang appends an incrementing suffix in declaration order: _0 then _1.
    [Fact]
    public void SameTypedUniformBuffers_GetDistinctDisambiguationSuffixes()
    {
        const string source = """
            module Dup;
            struct Material { float4 tint; }
            ConstantBuffer<Material> a;
            ConstantBuffer<Material> b;
            [shader("vertex")] float4 vertex() : SV_Position { return a.tint + b.tint; }
            [shader("fragment")] float4 fragment() : SV_Target { return 0; }
            """;

        ShaderDescription d = CompilerTestHarness
            .Compile(source, "Dup", () => new GLCompiler())
            .CompiledVariants[0].Backends[0].Description;

        Assert.Equal("block_Material_0", ElementNamed(d, "a").GLUniformName);
        Assert.Equal("block_Material_1", ElementNamed(d, "b").GLUniformName);
    }


    // Backend-specific: a read-only structured buffer is reflected under the GLSL-emitted block name.
    [Fact]
    public void StructuredBuffer_CarriesEmittedNameAndKind()
    {
        const string source = """
            module Sb;
            struct Particle { float3 pos; float life; }
            StructuredBuffer<Particle> particles;
            [shader("vertex")] float4 vertex() : SV_Position { return float4(particles[0].pos, 1); }
            [shader("fragment")] float4 fragment() : SV_Target { return 0; }
            """;

        ShaderDescription d = CompilerTestHarness
            .Compile(source, "Sb", () => new GLCompiler())
            .CompiledVariants[0].Backends[0].Description;

        ResourceLayoutElementDescription e = ElementNamed(d, "particles");
        Assert.Equal(ResourceKind.StructuredBufferReadOnly, e.Kind);
        Assert.Equal("StructuredBuffer_Particle_t_0", e.GLUniformName);
    }


    static ResourceLayoutElementDescription ElementNamed(ShaderDescription d, string name)
    {
        foreach (ResourceLayoutDescription layout in d.ResourceLayouts)
            foreach (ResourceLayoutElementDescription element in layout.Elements)
                if (element.Name == (PropertyID)name)
                    return element;

        throw new System.InvalidOperationException($"No resource named '{name}'.");
    }
}
