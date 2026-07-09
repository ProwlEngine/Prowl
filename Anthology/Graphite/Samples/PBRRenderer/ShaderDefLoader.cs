using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
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
        ShaderDefinition def = ShaderParser.Parse(source);

        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(s_modules[device.BackendType]());
        compiler.BeginSession(FileLoader.SearchDirectories, FileLoader.Load);

        def.Create(device, compiler);

        ShaderPass pass = def.Passes![passIndex];
        pass.ActiveVariant.TryGetDescription(device.BackendType, out ShaderDescription description);

        compiler.EndSession();

        description.BlendState = pass.State.ToBlendState(BlendStateDescription.SingleDisabled);
        description.DepthStencilState = pass.State.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyLessEqual);
        description.RasterizerState = pass.State.ToRasterizerState(new(FaceCullMode.Back, FrontFace.Clockwise, true, false));

        return device.ResourceFactory.CreateGraphicsProgram(description);
    }
}
