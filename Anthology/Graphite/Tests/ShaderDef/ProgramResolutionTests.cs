using System;
using System.IO;

using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class ProgramResolutionTests : IDisposable
{
    private const string Source = """
        Shader "Test/Resolution"
        {
            Pass
            {
                Cull Back
                SLANGPROGRAM
                struct VertexOutput { float4 Position : SV_Position; }

                [shader("vertex")]
                VertexOutput vertex(uint id : SV_VertexID)
                {
                    VertexOutput output;
                    output.Position = float4(id, 0, 0, 1);
                    return output;
                }

                [shader("fragment")]
                float4 fragment(VertexOutput input) : SV_Target { return 1; }
                ENDSLANG
            }
        }
        """;

    private static readonly BlendStateDescription s_blend = BlendStateDescription.SingleDisabled;
    private static readonly DepthStencilStateDescription s_depth = DepthStencilStateDescription.DepthOnlyLessEqual;
    private static readonly RasterizerStateDescription s_raster = new(FaceCullMode.Back, FrontFace.Clockwise, true, false);

    private readonly GraphicsDevice _device;
    private readonly ShaderPass _pass;


    public ProgramResolutionTests()
    {
        _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(false));

        ShaderDefinition definition = Parse.Shader(Source);
        _pass = definition.Passes![0];

        ShaderDescription description = Prowl.Graphite.ShaderDef.Compiler.Tests.SlangThread.Run(() =>
        {
            SlangShaderCompiler compiler = new();
            compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
            compiler.BeginSession([new DirectoryInfo(AppContext.BaseDirectory)]);
            ShaderDescription result = compiler.Compile(_pass, [], GraphicsBackend.Vulkan);
            compiler.EndSession();
            return result;
        });

        Variant variant = new([], [(GraphicsBackend.Vulkan, description)]);
        definition.Create(_device, new ShaderSnapshot { Passes = [new PassSnapshot { Axes = [], Variants = [variant] }] });
    }


    public void Dispose()
    {
        _device.Dispose();
    }


    [Fact]
    public void DifferentBaseState_ProducesDistinctProgram()
    {
        GraphicsProgram lessEqual = _pass.ResolveProgram(0, s_blend, s_depth, s_raster);
        GraphicsProgram disabled = _pass.ResolveProgram(0, s_blend, DepthStencilStateDescription.Disabled, s_raster);

        Assert.NotSame(lessEqual, disabled);
        Assert.Same(lessEqual, _pass.ResolveProgram(0, s_blend, s_depth, s_raster));
    }


    [Fact]
    public void CullOverride_ProducesDistinctProgram()
    {
        GraphicsProgram cullBack = _pass.ResolveProgram(0, s_blend, s_depth, s_raster);
        PassState cullOff = Parse.State("Cull Off").Apply(_pass.State);
        GraphicsProgram overridden = _pass.ResolveProgram(0, cullOff, s_blend, s_depth, s_raster);

        Assert.NotSame(cullBack, overridden);
    }


    [Fact]
    public void IdenticalOverride_HitsCache()
    {
        PassState first = Parse.State("Cull Off").Apply(_pass.State);
        PassState second = Parse.State("Cull Off").Apply(_pass.State);

        GraphicsProgram a = _pass.ResolveProgram(0, first, s_blend, s_depth, s_raster);
        GraphicsProgram b = _pass.ResolveProgram(0, second, s_blend, s_depth, s_raster);

        Assert.NotSame(first, second);
        Assert.Same(a, b);
    }


    [Fact]
    public void OverrideMatchingPassState_SharesProgramWithPlainResolve()
    {
        GraphicsProgram plain = _pass.ResolveProgram(0, s_blend, s_depth, s_raster);
        GraphicsProgram overridden = _pass.ResolveProgram(0, Parse.State("Cull Back").Apply(_pass.State), s_blend, s_depth, s_raster);

        Assert.Same(plain, overridden);
    }


    [Fact]
    public void DefaultResolve_EqualOverrides_ReuseProgram()
    {
        GraphicsProgram first = _pass.ResolveDefaultProgram(0, Parse.State("Cull Off"));
        GraphicsProgram second = _pass.ResolveDefaultProgram(0, Parse.State("Cull Off"));
        GraphicsProgram plain = _pass.ResolveDefaultProgram(0);

        Assert.Same(first, second);
        Assert.NotSame(first, plain);
        Assert.Same(plain, _pass.ResolveDefaultProgram(0, Parse.State("Cull Back")));
    }
}
