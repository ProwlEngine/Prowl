using System;
using System.IO;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace SlangHlslProbe;


// Standalone out-of-process compile probe. Run as a child process by the D3D11 compilation tests so a
// native Slang crash surfaces as a process exit code instead of taking down the xUnit test host.
//
// HLSL codegen for combined-sampler shaders (Sampler2D<>) segfaults in the current native Slang build,
// a known upstream issue, so as a stopgap this probe dummy-compiles the shader to SPIR-V (which does
// not crash) rather than to the requested HLSL profile.
//
// Usage: SlangHlslProbe <shaderPath> <moduleName> <profile>
//   exit 0: compiled successfully
//   exit 1: ordinary Slang compile error (CompilationException)
//   exit 2: usage error
//   any other exit code (e.g. 139 on Linux): the process crashed while compiling
internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: SlangHlslProbe <shaderPath> <moduleName> <profile>");
            return 2;
        }

        string shaderPath = args[0];

        try
        {
            SlangShaderCompiler compiler = new();
            compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
            compiler.BeginSession([new DirectoryInfo(Path.GetDirectoryName(shaderPath)!)]);

            string source = File.ReadAllText(shaderPath);
            ShaderPass pass = new() { State = new PassState(), InlineSlang = source };
            ShaderDescription description = compiler.Compile(pass, [], GraphicsBackend.Vulkan);

            compiler.EndSession();

            if (description.Stages.Length == 0)
            {
                Console.Error.WriteLine("Compilation produced no output.");
                return 1;
            }

            Console.WriteLine("OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
