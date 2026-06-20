using System.Linq;

using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Compiles the shared shader suite through the HLSL target and checks the HLSL output, entry points,
// vertex input semantics, and resource reflection. D3D11 binds vertex inputs by semantic name (not
// location) and gives each register class its own index space, so resources reflect to their raw
// register slot (b#/t#/s#). Profile-independent shaders use the shared ReflectionTestbed; ParameterBlock
// register placement depends on the shader model, so those keep an explicit per-profile binding table.
public class D3D11CompilationTests
{
    const ShaderStages VF = ShaderStages.Vertex | ShaderStages.Fragment;

    static ShaderDescription Compile(string module) =>
        CompilerTestHarness.CompileShared(module, () => new DXCompiler()).Backends[0].Description;

    static ShaderDescription CompileWith(string module, string profile) =>
        CompilerTestHarness.CompileShared(module, () => new DXCompiler(profile)).Backends[0].Description;


    static ResourceLayoutElementDescription Ubo(string name, int register, params UniformBlockField[] fields)
        => new(name, ResourceKind.UniformBuffer, VF, register, ResourceLayoutElementOptions.None, name, fields);


    [Fact]
    public void Graphics_StagesHaveExpectedEntryPoints()
    {
        ReflectionTestbed.AssertStages(Compile("Graphics"),
            (ShaderStages.Vertex, "vertex"), (ShaderStages.Fragment, "fragment"));
    }


    // Blended user-facing name, raw D3D11 semantic, format. All three inputs are semantic index 0,
    // so the location (which D3D11 uses as the semantic index) is 0 for each.
    [Theory]
    [InlineData("POSITION0", "POSITION", VertexElementFormat.Float3)]
    [InlineData("UV0", "UV", VertexElementFormat.Float2)]
    [InlineData("COLOR0", "COLOR", VertexElementFormat.Float4)]
    public void Graphics_VertexInputsBlendedNameRawSemanticAndIndex(string blended, string raw, VertexElementFormat format)
    {
        ShaderDescription d = Compile("Graphics");

        VertexLayoutDescription layout = CompilerTestHarness.LayoutWithName(d, blended);
        VertexElementDescription element = CompilerTestHarness.Single(layout);

        Assert.Equal(format, element.Format);
        Assert.Equal(raw, element.D3D11SemanticName);
        Assert.Equal(0u, layout.Location); // semantic index, carried in the location for D3D11
    }


    [Theory]
    [InlineData("Graphics")]
    [InlineData("Modules")]
    [InlineData("ConstantBuffers")]
    [InlineData("ParameterBlocks")]
    [InlineData("UVOriginUsage")]
    public void Hlsl_MatchesKnownGood(string module)
    {
        ShaderDescription d = Compile(module);

        Assert.Equal(
            CompilerTestHarness.KnownGoodText($"{module}.vertex.hlsl"),
            CompilerTestHarness.NormalizeSourcePaths(CompilerTestHarness.StageText(d, ShaderStages.Vertex)));

        Assert.Equal(
            CompilerTestHarness.KnownGoodText($"{module}.fragment.hlsl"),
            CompilerTestHarness.NormalizeSourcePaths(CompilerTestHarness.StageText(d, ShaderStages.Fragment)));
    }


    // Two raw constant buffers take b0/b1 (register slots are profile-independent for plain cbuffers).
    [Fact]
    public void ConstantBuffers_ReflectBlocksAndFields()
    {
        ReflectionTestbed.AssertResourceLayouts(Compile("ConstantBuffers"),
            new ResourceLayoutDescription(0,
                Ubo("camera", 0,
                    new UniformBlockField("viewProj", 0, 64, UniformScalarType.Float4x4),
                    new UniformBlockField("cameraPos", 64, 12, UniformScalarType.Float3),
                    new UniformBlockField("time", 76, 4, UniformScalarType.Float1)),
                Ubo("material", 1,
                    new UniformBlockField("baseColor", 0, 16, UniformScalarType.Float4),
                    new UniformBlockField("tiling", 16, 8, UniformScalarType.Float2),
                    new UniformBlockField("flags", 24, 4, UniformScalarType.Int1))));
    }


    // `lighting` is declared in the imported Common module; it must surface at b1 after base `globals`.
    [Fact]
    public void Modules_SurfaceImportedModuleBlock()
    {
        ReflectionTestbed.AssertResourceLayouts(Compile("Modules"),
            new ResourceLayoutDescription(0,
                Ubo("globals", 0,
                    new UniformBlockField("viewProj", 0, 64, UniformScalarType.Float4x4),
                    new UniformBlockField("tint", 64, 16, UniformScalarType.Float4)),
                Ubo("lighting", 1,
                    new UniformBlockField("sunDirection", 0, 16, UniformScalarType.Float4),
                    new UniformBlockField("sunColor", 16, 16, UniformScalarType.Float4),
                    new UniformBlockField("ambientIntensity", 32, 4, UniformScalarType.Float1))));
    }


    [Fact]
    public void IndexedSemantics_BlendDistinctlyButShareRawSemantic()
    {
        // UV0 and UV3 share the raw semantic "UV" but differ by index. The blended names keep them
        // distinct for user lookup, the raw semantic collapses to "UV", and the index rides the location.
        const string source = """
            module MultiUV;
            struct VIn { float2 a : UV0; float2 b : UV3; }
            [shader("vertex")] float4 vertex(VIn i) : SV_Position { return float4(i.a, i.b); }
            [shader("fragment")] float4 fragment() : SV_Target { return 0; }
            """;

        ShaderDescription d = CompilerTestHarness
            .Compile(source, "MultiUV", () => new DXCompiler())
            .CompiledVariants[0].Backends[0].Description;

        VertexLayoutDescription uv0 = CompilerTestHarness.LayoutWithName(d, "UV0");
        VertexLayoutDescription uv3 = CompilerTestHarness.LayoutWithName(d, "UV3");

        Assert.Equal("UV", CompilerTestHarness.Single(uv0).D3D11SemanticName);
        Assert.Equal("UV", CompilerTestHarness.Single(uv3).D3D11SemanticName);
        Assert.Equal(0u, uv0.Location);
        Assert.Equal(3u, uv3.Location);
    }


    // The two source-of-truth binding tables for the ParameterBlocks shader. Same resources, same
    // shader, but the register slots the backend reflects differ by shader model.
    //
    // sm_5_0: no register spaces, everything in space 0, slots run across the whole module per class.
    // Top level takes b0/t0/s0; perObject's implicit cbuffer + resources take b1/t1/s1; onlyTex t2/s2.
    static readonly Binding[] Sm50 =
    [
        new("globals",    0, 0, ResourceKind.UniformBuffer),
        new("albedo",     0, 0, ResourceKind.TextureReadOnly),
        new("samp",       0, 0, ResourceKind.Sampler),
        new("perObject",  0, 1, ResourceKind.UniformBuffer),
        new("detail",     0, 1, ResourceKind.TextureReadOnly),
        new("detailSamp", 0, 1, ResourceKind.Sampler),
        new("tex",        0, 2, ResourceKind.TextureReadOnly),
        new("s",          0, 2, ResourceKind.Sampler),
    ];

    // sm_5_1: each ParameterBlock opens its own register space, so blocks restart their slots at 0.
    // Top level in space 0; perObject in space 1 (b0/t0/s0); onlyTex in space 2 (t0/s0).
    static readonly Binding[] Sm51 =
    [
        new("globals",    0, 0, ResourceKind.UniformBuffer),
        new("albedo",     0, 0, ResourceKind.TextureReadOnly),
        new("samp",       0, 0, ResourceKind.Sampler),
        new("perObject",  1, 0, ResourceKind.UniformBuffer),
        new("detail",     1, 0, ResourceKind.TextureReadOnly),
        new("detailSamp", 1, 0, ResourceKind.Sampler),
        new("tex",        2, 0, ResourceKind.TextureReadOnly),
        new("s",          2, 0, ResourceKind.Sampler),
    ];

    public record Binding(string Name, uint Space, int Register, ResourceKind Kind);

    public static TheoryData<string> Profiles => new() { "sm_5_0", "sm_5_1" };

    static Binding[] ExpectedFor(string profile) => profile == "sm_5_0" ? Sm50 : Sm51;


    [Theory]
    [MemberData(nameof(Profiles))]
    public void ParameterBlocks_BindToTheirRegisterSlots(string profile)
    {
        ShaderDescription d = CompileWith("ParameterBlocks", profile);
        Binding[] expected = ExpectedFor(profile);

        Assert.Equal(expected.Length, d.ResourceLayouts.Sum(l => l.Elements.Length));

        foreach (Binding b in expected)
            AssertBinding(d, b);
    }


    [Theory]
    [MemberData(nameof(Profiles))]
    public void ParameterBlock_WithoutUniformData_HasNoConstantBuffer(string profile)
    {
        ShaderDescription d = CompileWith("ParameterBlocks", profile);

        // onlyTex holds no ordinary data, so no implicit constant buffer is emitted for it.
        Assert.DoesNotContain(d.ResourceLayouts.SelectMany(l => l.Elements),
            e => e.Kind == ResourceKind.UniformBuffer && e.Name == (PropertyID)"onlyTex");
    }


    // Uniform field layout is independent of the register model, so it is checked once at sm_5_0.
    [Fact]
    public void ParameterBlocks_UniformBuffersCarryFieldLayout()
    {
        ShaderDescription d = CompileWith("ParameterBlocks", "sm_5_0");

        Assert.Equal(
            new[]
            {
                new UniformBlockField("viewProj", 0, 64, UniformScalarType.Float4x4),
                new UniformBlockField("tint", 64, 16, UniformScalarType.Float4),
            },
            ElementNamed(d, "globals").UniformFields);

        Assert.Equal(
            new[]
            {
                new UniformBlockField("color", 0, 16, UniformScalarType.Float4),
                new UniformBlockField("uvOffset", 16, 8, UniformScalarType.Float2),
            },
            ElementNamed(d, "perObject").UniformFields);
    }


    static ResourceLayoutDescription SpaceAt(ShaderDescription d, uint space)
        => d.ResourceLayouts.Single(l => l.Set == space);


    static ResourceLayoutElementDescription ElementNamed(ShaderDescription d, string name)
        => d.ResourceLayouts.SelectMany(l => l.Elements).Single(e => e.Name == (PropertyID)name);


    static void AssertBinding(ShaderDescription d, Binding b)
    {
        ResourceLayoutElementDescription e = ElementNamed(d, b.Name);

        Assert.Equal(b.Kind, e.Kind);
        Assert.Equal(b.Register, e.BindingIndex);
        Assert.Contains(e, SpaceAt(d, b.Space).Elements);
    }
}
