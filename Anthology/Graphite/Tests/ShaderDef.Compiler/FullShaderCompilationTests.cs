using System;
using System.Linq;

using Prowl.Graphite.ShaderDef;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


// End-to-end coverage of the full pipeline: ShaderDef markup text -> ShaderParser.Parse ->
// SlangShaderCompiler -> compiled ShaderDescription. Every other suite in this project compiles a
// raw .slang module directly; these tests instead go through the same calls a real asset pipeline or
// sample uses (ShaderParser.Parse -> SlangShaderCompiler, see ShaderDef/README.md), so a full
// .shaderdef document with Properties, Tags, PassState commands, and an embedded SLANGPROGRAM block is
// proven to parse and compile end to end, including variant specialization discovered from a parsed
// pass's own source.
public class FullShaderCompilationTests
{
    const string SinglePassShader = """
        Shader "Tests/SinglePass"
        {
            Properties
            {
                _Color("Tint", Color) = (1, 1, 1, 1)
            }

            Pass 0
            {
                Name "Forward"
                Tags { "LightMode" = "ForwardBase" }
                Cull Back

                SLANGPROGRAM
                struct VertexInput
                {
                    float3 position : POSITION;
                    float4 color    : COLOR;
                }

                struct VertexOutput
                {
                    float4 clipPosition : SV_Position;
                    float4 color : COLOR;
                }

                [shader("vertex")]
                VertexOutput vertex(VertexInput input)
                {
                    VertexOutput output;
                    output.clipPosition = float4(input.position, 1);
                    output.color = input.color;
                    return output;
                }

                [shader("fragment")]
                float4 fragment(VertexOutput input) : SV_Target
                {
                    return input.color;
                }
                ENDSLANG
            }

            Fallback ""
        }
        """;


    [Fact]
    public void ParsesFullDocument()
    {
        ShaderDefinition shader = ShaderParser.Parse(SinglePassShader);

        Assert.Equal("Tests/SinglePass", shader.Name);
        Assert.Single(shader.Passes!);
        Assert.Equal("Forward", shader.Passes![0].Name);
        Assert.Single(shader.Properties!);
    }


    [Fact]
    public void CompilesParsedPass_ForVulkan()
    {
        ShaderDefinition shader = ShaderParser.Parse(SinglePassShader);
        ShaderPass pass = shader.Passes![0];

        ShaderDescription description = CompilerTestHarness.CompilePass(pass, () => new VulkanCompiler()).Backends.Single().Description;

        ReflectionTestbed.AssertStages(description, (ShaderStages.Vertex, "main"), (ShaderStages.Fragment, "main"));
        Assert.NotEmpty(CompilerTestHarness.StageOf(description, ShaderStages.Vertex).ShaderBytes);
        Assert.NotEmpty(CompilerTestHarness.StageOf(description, ShaderStages.Fragment).ShaderBytes);
    }


    [Fact]
    public void CompilesParsedPass_VertexLayoutReflectsSource()
    {
        ShaderDefinition shader = ShaderParser.Parse(SinglePassShader);
        ShaderPass pass = shader.Passes![0];

        ShaderDescription description = CompilerTestHarness.CompilePass(pass, () => new VulkanCompiler()).Backends.Single().Description;

        ReflectionTestbed.AssertVertexLocations(description,
            (0, VertexElementFormat.Float3), (1, VertexElementFormat.Float4));
    }


    [Fact]
    public void CompilesParsedPass_SpirvIsValid()
    {
        ShaderDefinition shader = ShaderParser.Parse(SinglePassShader);
        ShaderPass pass = shader.Passes![0];

        ShaderDescription description = CompilerTestHarness.CompilePass(pass, () => new VulkanCompiler()).Backends.Single().Description;

        foreach (ShaderStages stage in new[] { ShaderStages.Vertex, ShaderStages.Fragment })
        {
            byte[] spirv = CompilerTestHarness.StageOf(description, stage).ShaderBytes;
            string validation = CompilerTestHarness.TryValidateSpirv(spirv);

            // null => spirv-val unavailable in this environment; the reflection/byte checks above
            // still cover correctness.
            if (validation != null)
                Assert.True(validation.Length == 0, $"spirv-val rejected parsed pass {stage}:\n{validation}");
        }
    }


    const string TwoPassShader = """
        Shader "Tests/TwoPass"
        {
            Pass 0
            {
                Name "A"
                SLANGPROGRAM
                [shader("vertex")]
                float4 vertexA() : SV_Position { return float4(0, 0, 0, 1); }

                [shader("fragment")]
                float4 fragmentA() : SV_Target { return float4(1, 0, 0, 1); }
                ENDSLANG
            }

            Pass 1
            {
                Name "B"
                SLANGPROGRAM
                [shader("vertex")]
                float4 vertexB() : SV_Position { return float4(0, 0, 0, 1); }

                [shader("fragment")]
                float4 fragmentB() : SV_Target { return float4(0, 1, 0, 1); }
                ENDSLANG
            }

            Fallback ""
        }
        """;


    [Fact]
    public void MultiplePasses_EachCompilesIndependently()
    {
        ShaderDefinition shader = ShaderParser.Parse(TwoPassShader);

        Assert.Equal(2, shader.Passes!.Length);

        ShaderDescription a = CompilerTestHarness.CompilePass(shader.Passes[0], () => new VulkanCompiler()).Backends.Single().Description;
        ShaderDescription b = CompilerTestHarness.CompilePass(shader.Passes[1], () => new VulkanCompiler()).Backends.Single().Description;

        byte[] aFragment = CompilerTestHarness.StageOf(a, ShaderStages.Fragment).ShaderBytes;
        byte[] bFragment = CompilerTestHarness.StageOf(b, ShaderStages.Fragment).ShaderBytes;

        Assert.NotEqual(Convert.ToBase64String(aFragment), Convert.ToBase64String(bFragment));
    }


    const string VariantPassShader = """
        Shader "Tests/VariantPass"
        {
            Pass 0
            {
                SLANGPROGRAM
                import VariantAttributes;

                [VariantAxis]
                extern static const bool Bright;

                struct VertexInput { float3 position : POSITION; }

                [shader("vertex")]
                float4 vertex(VertexInput input) : SV_Position
                {
                    return float4(input.position, 1);
                }

                [shader("fragment")]
                float4 fragment() : SV_Target
                {
                    return Bright ? float4(1, 1, 1, 1) : float4(0, 0, 0, 1);
                }
                ENDSLANG
            }

            Fallback ""
        }
        """;


    // Closes the gap between ShaderTests (parses ShaderDef markup but never compiles it) and
    // VariantCompilationTests (compiles a variant axis but from a raw .slang module, never through
    // ShaderDef markup): a [VariantAxis] declared inside a real SLANGPROGRAM block, reached only by
    // parsing a full .shaderdef document, is discovered and produces distinct specialized code.
    [Fact]
    public void ParsedPass_VariantAxisIsDiscoveredAndSpecializes()
    {
        ShaderDefinition shader = ShaderParser.Parse(VariantPassShader);
        ShaderPass pass = shader.Passes![0];

        CompilationResult result = CompilerTestHarness.CompilePassAll(pass, () => new VulkanCompiler());

        VariantSpace space = Assert.Single(result.VariantSpaces);
        Assert.Equal("Bright", space.Name);
        Assert.Equal(2, result.CompiledVariants.Length);

        byte[][] fragmentSpirv = result.CompiledVariants
            .Select(v => v.Backends.Single().Description)
            .Select(d => CompilerTestHarness.StageOf(d, ShaderStages.Fragment).ShaderBytes)
            .ToArray();

        Assert.NotEqual(Convert.ToBase64String(fragmentSpirv[0]), Convert.ToBase64String(fragmentSpirv[1]));
    }
}
