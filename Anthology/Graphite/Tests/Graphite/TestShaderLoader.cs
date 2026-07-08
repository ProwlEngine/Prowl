using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Graphite.Compiler;

namespace Prowl.Graphite.Tests;

// Compiles the test .slang shaders to per-backend stage descriptions at runtime through the
// Compiler project, mirroring Samples/Shared/ShaderLoader. Each test owns its
// ShaderDescription/ComputeDescription (vertex + resource layouts); this loader only produces
// the compiled stage bytes via the Compiler's reflection.
internal static class TestShaderLoader
{
    private static string ShaderDirectory => Path.Combine(AppContext.BaseDirectory, "Shaders");

    // One compiler module per backend. The profile strings mirror what the tests targeted before
    // the move to the Compiler project. Modules are built lazily so their constructors (which call
    // GlobalSession.FindProfile) only run for the backend actually in use.
    private static readonly Dictionary<GraphicsBackend, Func<CompilerModule>> s_modules = new()
    {
        [GraphicsBackend.Vulkan] = () => new VulkanCompiler("spirv_1_4"),
        [GraphicsBackend.Direct3D11] = () => new DXCompiler("sm_5_0", GraphicsBackend.Direct3D11),
    };

    private static Memory<byte>? LoadFile(string path)
    {
        string full = Path.IsPathRooted(path) ? path : Path.Combine(ShaderDirectory, path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    private static ShaderDescription Compile(GraphicsBackend backend, string moduleFile, ShaderType type)
    {
        CompilationSession session = new();
        session.RegisterModule(s_modules[backend]());

        session.BeginSession([new DirectoryInfo(ShaderDirectory)], LoadFile);
        CompilationResult result = session.CompileShader(moduleFile, type);
        session.EndSession();

        return result.CompiledVariants[0].Backends[0].Description;
    }

    // Compiles the vertex + fragment entry points of a module into stage descriptions.
    public static ShaderStageDescription[] LoadGraphics(GraphicsBackend backend, string moduleFile)
        => Compile(backend, moduleFile, ShaderType.Rasterization).Stages;

    // Compiles a single compute entry point into a stage description.
    public static ShaderStageDescription LoadCompute(GraphicsBackend backend, string moduleFile)
        => Compile(backend, moduleFile, ShaderType.Compute).Stages[0];
}
