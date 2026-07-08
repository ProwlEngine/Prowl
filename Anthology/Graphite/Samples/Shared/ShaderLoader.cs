using System;
using System.Collections.Generic;

using Prowl.Graphite.Compiler;


namespace Prowl.Graphite.Samples;


public static class ShaderLoader
{
    // One compiler module per backend. The profile strings mirror what the samples targeted before
    // the move to the Compiler project. Modules are built lazily so their constructors (which call
    // GlobalSession.FindProfile) only run for the backend actually in use.
    private static Dictionary<GraphicsBackend, Func<CompilerModule>> s_modules = new()
    {
        [GraphicsBackend.Vulkan] = () => new VulkanCompiler("spirv_1_4"),
        [GraphicsBackend.Direct3D11] = () => new DXCompiler("sm_5_0", GraphicsBackend.Direct3D11),
    };


    public static GraphicsProgram CreateShader(GraphicsDevice device)
    {
        CompilationSession session = new();
        session.RegisterModule(s_modules[device.BackendType]());

        session.BeginSession(FileLoader.SearchDirectories, FileLoader.Load);

        CompilationResult result = session.CompileShader("Shader.slang", ShaderType.Rasterization);

        session.EndSession();

        // Reflection fills in the stages, vertex inputs, and resource bindings; the loader still owns
        // the fixed-function pipeline state the shader source does not describe.
        ShaderDescription description = result.CompiledVariants[0].Backends[0].Description;
        description.BlendState = BlendStateDescription.SingleDisabled;
        description.DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual;
        description.RasterizerState = new(FaceCullMode.Back, FrontFace.Clockwise, true, false);

        return device.ResourceFactory.CreateGraphicsProgram(description);
    }
}
