using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Prowl.Graphite.Compiler;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.Samples;


namespace Prowl.Graphite.Samples.PBRRenderer;


public static class ShaderDefLoader
{
    private static Dictionary<GraphicsBackend, Func<CompilerModule>> s_modules = new()
    {
        [GraphicsBackend.Vulkan] = () => new VulkanCompiler("spirv_1_4"),
        [GraphicsBackend.Direct3D11] = () => new DXCompiler("sm_5_0", GraphicsBackend.Direct3D11),
    };


    public static GraphicsProgram Load(GraphicsDevice device, string shaderDefPath, int passIndex = 0)
    {
        string source = File.ReadAllText(shaderDefPath);
        ParsedShader parsed = ParsedShader.Parse(source);
        ParsedPass pass = parsed.Passes![passIndex];

        CompilationSession session = new();
        session.RegisterModule(s_modules[device.BackendType]());
        session.BeginSession(FileLoader.SearchDirectories, FileLoader.Load);

        byte[] utf8 = Encoding.UTF8.GetBytes(pass.InlineSlang);
        CompilationResult result = session.CompileShader(parsed.Name!, parsed.Name! + ".slang", utf8, ShaderType.Rasterization);

        session.EndSession();

        ShaderDescription description = result.CompiledVariants[0].Backends[0].Description;
        description.BlendState = pass.State.ToBlendState(BlendStateDescription.SingleDisabled);
        description.DepthStencilState = pass.State.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyLessEqual);
        description.RasterizerState = pass.State.ToRasterizerState(new(FaceCullMode.Back, FrontFace.Clockwise, true, false));

        return device.ResourceFactory.CreateGraphicsProgram(description);
    }
}
