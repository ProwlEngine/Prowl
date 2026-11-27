using System;
using System.IO;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace Prowl.Graphite.Tests;

// Compiles the test .glsl shaders to stage descriptions. Each test owns its ShaderDescription or
// ComputeDescription layouts, this loader only produces the compiled stages.
internal static class TestShaderLoader
{
    private static string ShaderDirectory => Path.Combine(AppContext.BaseDirectory, "Shaders");

    private static readonly GlslShaderCompiler s_compiler = new(new GlslCompilerOptions
    {
        ResolveInclude = (name, _) =>
        {
            string path = Path.Combine(ShaderDirectory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        },
    });

    private static ShaderDescription Compile(GraphicsBackend backend, string file)
    {
        string source = File.ReadAllText(Path.Combine(ShaderDirectory, file));
        ShaderPass pass = new() { State = new PassState(), Source = source, SourceName = file };
        return s_compiler.Compile(pass, [], backend);
    }

    public static ShaderStageDescription[] LoadGraphics(GraphicsBackend backend, string file)
        => Compile(backend, file).Stages;

    public static ShaderStageDescription LoadCompute(GraphicsBackend backend, string file)
        => Compile(backend, file).Stages[0];
}
