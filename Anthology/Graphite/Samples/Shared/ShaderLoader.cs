using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;


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
        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(s_modules[device.BackendType]());

        compiler.BeginSession(FileLoader.SearchDirectories, FileLoader.Load);

        Memory<byte>? loaded = FileLoader.Load("Shader.slang");
        string source = Encoding.UTF8.GetString(loaded!.Value.Span);
        ShaderPass pass = new() { State = new PassState(), InlineSlang = source };
        ShaderDescription description = compiler.Compile(pass, [], device.BackendType);

        compiler.EndSession();

        // Reflection fills in the stages, vertex inputs, and resource bindings; the loader still owns
        // the fixed-function pipeline state the shader source does not describe.
        description.BlendState = BlendStateDescription.SingleDisabled;
        description.DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual;
        description.RasterizerState = new(FaceCullMode.Back, FrontFace.Clockwise, true, false);

        return device.ResourceFactory.CreateGraphicsProgram(description);
    }
}
