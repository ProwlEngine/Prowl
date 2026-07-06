using System;
using System.IO;
using System.Text;

using Prowl.Graphite.Compiler;

namespace SlangHlslProbe;


// Standalone out-of-process HLSL compile probe. Run as a child process by the D3D11 compilation tests
// so a native Slang crash (e.g. the Sampler2D<> HLSL codegen segfault) surfaces as a process exit code
// instead of taking down the xUnit test host.
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
        string moduleName = args[1];
        string profile = args[2];

        try
        {
            CompilationSession session = new();
            session.RegisterModule(new DXCompiler(profile));
            session.BeginSession([new DirectoryInfo(Path.GetDirectoryName(shaderPath)!)]);

            byte[] source = File.ReadAllBytes(shaderPath);
            CompilationResult result = session.CompileShader(
                moduleName, Path.GetFileName(shaderPath), source, ShaderType.Rasterization);

            session.EndSession();

            if (result.CompiledVariants.Length == 0 || result.CompiledVariants[0].Backends.Length == 0)
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
