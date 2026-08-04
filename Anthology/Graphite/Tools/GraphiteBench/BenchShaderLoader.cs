using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace Prowl.Graphite.Bench;


internal static class BenchShaderLoader
{
    private static string ShaderDirectory => Path.Combine(AppContext.BaseDirectory, "Shaders");

    private static readonly Dictionary<GraphicsBackend, Func<CompilerModule>> s_modules = new()
    {
        [GraphicsBackend.Vulkan] = () => new VulkanCompiler("spirv_1_4"),
    };

    private static Memory<byte>? LoadFile(string path)
    {
        string full = Path.IsPathRooted(path) ? path : Path.Combine(ShaderDirectory, path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    public static GraphicsProgram Create(GraphicsDevice gd, string moduleFile)
    {
        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(s_modules[gd.BackendType]());
        compiler.BeginSession([new DirectoryInfo(ShaderDirectory)], LoadFile);

        string source = File.ReadAllText(Path.Combine(ShaderDirectory, moduleFile));
        ShaderPass pass = new() { State = new PassState(), InlineSlang = source };
        ShaderDescription description = compiler.Compile(pass, [], gd.BackendType);

        compiler.EndSession();

        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.Default;

        return gd.ResourceFactory.CreateGraphicsProgram(description);
    }
}
