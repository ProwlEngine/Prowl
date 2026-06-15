using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Slang;

namespace Prowl.Graphite.Tests;

// Compiles the test .slang shaders to per-backend bytecode at runtime, mirroring
// Samples/Shared/ShaderLoader. Each test owns its ShaderDescription/ComputeDescription
// (vertex + resource layouts); this loader only produces the compiled stage bytes.
//
// Slang entry-point names are not carried over on Vulkan, so every produced stage reports
// its entry point as "main".
internal static class TestShaderLoader
{
    private static string ShaderDirectory => Path.Combine(AppContext.BaseDirectory, "Shaders");

    private sealed class ShaderFileProvider : IFileProvider
    {
        public Memory<byte>? LoadFile(string path)
        {
            string full = Path.IsPathRooted(path) ? path : Path.Combine(ShaderDirectory, path);
            return File.Exists(full) ? File.ReadAllBytes(full) : null;
        }
    }

    private static readonly Dictionary<GraphicsBackend, TargetDescription> s_targets = new()
    {
        [GraphicsBackend.OpenGL] = new() { Format = CompileTarget.Glsl, Profile = GlobalSession.FindProfile("glsl_410") },
        [GraphicsBackend.OpenGLES] = new() { Format = CompileTarget.Glsl, Profile = GlobalSession.FindProfile("glsl_es_310") },
        [GraphicsBackend.Vulkan] = new() { Format = CompileTarget.Spirv, Profile = GlobalSession.FindProfile("spirv_1_4") },
        [GraphicsBackend.Direct3D11] = new() { Format = CompileTarget.Hlsl, Profile = GlobalSession.FindProfile("sm_5_0") },
    };

    private static Session CreateSession(GraphicsBackend backend) => GlobalSession.CreateSession(new SessionDescription
    {
        DefaultMatrixLayoutMode = MatrixLayoutMode.ColumnMajor,
        FileProvider = new ShaderFileProvider(),
        SearchPaths = [ShaderDirectory],
        Targets = [s_targets[backend]],
    });

    // Compiles the vertex + fragment entry points of a module into stage descriptions.
    public static ShaderStageDescription[] LoadGraphics(
        GraphicsBackend backend, string moduleFile, string vertexEntry = "vertex", string fragmentEntry = "fragment")
    {
        Session session = CreateSession(backend);
        Module module = session.LoadModule(moduleFile, out _);

        EntryPoint vert = module.FindEntryPointByName(vertexEntry);
        EntryPoint frag = module.FindEntryPointByName(fragmentEntry);
        ComponentType composite = session.CreateCompositeComponentType([module, vert, frag], out _);

        return
        [
            new ShaderStageDescription(ShaderStages.Vertex, composite.GetEntryPointCode(0, 0, out _).ToArray(), "main"),
            new ShaderStageDescription(ShaderStages.Fragment, composite.GetEntryPointCode(1, 0, out _).ToArray(), "main"),
        ];
    }

    // Compiles a single compute entry point into a stage description.
    public static ShaderStageDescription LoadCompute(
        GraphicsBackend backend, string moduleFile, string entry = "compute")
    {
        Session session = CreateSession(backend);
        Module module = session.LoadModule(moduleFile, out _);

        EntryPoint comp = module.FindEntryPointByName(entry);
        ComponentType composite = session.CreateCompositeComponentType([module, comp], out _);

        return new ShaderStageDescription(ShaderStages.Compute, composite.GetEntryPointCode(0, 0, out _).ToArray(), "main");
    }
}
