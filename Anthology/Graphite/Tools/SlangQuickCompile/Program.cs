using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Graphite;
using Prowl.Graphite.Compiler;
using Prowl.Graphite.Variants;

namespace SlangQuickCompile;


// Known-good generator for the compiler test suite.
//
// Usage (from the repo root, or any subdirectory):
//   dotnet run Tools/SlangQuickCompile/Program.cs -- [--dump] [--write] [shaderName ...]
//     --dump    list the files each shader would produce (default when no flag given)
//     --write   (re)write the KnownGood output files
internal static class Program
{
    // Every shared shader the suite locks down. Variant permutations are discovered from each shader's
    // own [VariantAxis] attributes, so they are not listed here.
    static readonly string[] s_manifest =
    [
        "Graphics",
        "Modules",
        "ConstantBuffers",
        "ParameterBlocks",
        "Variants",
        "UVOriginUsage",
    ];


    static int Main(string[] args)
    {
        bool write = args.Contains("--write");
        bool dump = args.Contains("--dump") || !write;
        string[] names = [.. args.Where(a => !a.StartsWith("--"))];

        string shaderDir = LocateDirectory("Tests/Compiler/Shaders");
        string knownGoodDir = LocateDirectory("Tests/Compiler/KnownGood");

        Console.WriteLine($"Shaders:   {shaderDir}");
        Console.WriteLine($"KnownGood: {knownGoodDir}\n");

        foreach (string module in s_manifest)
        {
            if (names.Length > 0 && !names.Contains(module))
                continue;

            Process(module, shaderDir, knownGoodDir, write, dump);
        }

        return 0;
    }


    static void Process(string module, string shaderDir, string knownGoodDir, bool write, bool dump)
    {
        CompilationSession session = new();

        session.RegisterModule(new GLCompiler());
        session.RegisterModule(new DXCompiler());
        session.RegisterModule(new VulkanCompiler());

        session.BeginSession([new DirectoryInfo(shaderDir), new DirectoryInfo(AppContext.BaseDirectory)]);

        CompilationResult result = session.CompileShader(module, ShaderType.Rasterization);

        session.EndSession();

        foreach (VariantResult variant in result.CompiledVariants)
        {
            string suffix = VariantSuffix(variant.Variants);

            foreach ((ShaderDescription description, GraphicsBackend backend) in variant.Backends)
            {
                string extension = Extension(backend);

                foreach (ShaderStageDescription stage in description.Stages)
                {
                    string fileName = $"{module}.{StageName(stage.Stage)}{suffix}.{extension}";
                    byte[] bytes = OutputBytes(extension, stage.ShaderBytes);

                    if (write)
                    {
                        File.WriteAllBytes(Path.Combine(knownGoodDir, fileName), bytes);
                        Console.WriteLine($"  wrote {fileName} ({bytes.Length} bytes)");
                    }
                    else if (dump)
                    {
                        Console.WriteLine($"  {fileName} ({bytes.Length} bytes)");
                    }
                }
            }
        }
    }


    static string Extension(GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.OpenGL => "glsl",
        GraphicsBackend.Direct3D11 => "hlsl",
        GraphicsBackend.Vulkan => "spv",
        _ => throw new NotSupportedException($"No known-good extension for backend {backend}."),
    };


    static string VariantSuffix(Keyword[] variants)
        => variants.Length == 0 ? "" : "_" + string.Join("_", variants.Select(v => v.Value));


    static string StageName(ShaderStages stage) => stage switch
    {
        ShaderStages.Vertex => "vertex",
        ShaderStages.Fragment => "fragment",
        ShaderStages.Compute => "compute",
        _ => stage.ToString().ToLowerInvariant(),
    };

    static byte[] OutputBytes(string extension, byte[] shaderBytes)
        => extension == "hlsl"
            ? Encoding.UTF8.GetBytes(NormalizeSourcePaths(Encoding.UTF8.GetString(shaderBytes)))
            : shaderBytes;


    // Remove HLSL line directives for portability.
    static readonly Regex s_lineDirective = new("^(?<pre>\\s*#line\\s+\\d+\\s+)\"(?<path>[^\"]*)\"", RegexOptions.Multiline);

    static string NormalizeSourcePaths(string hlsl)
        => s_lineDirective.Replace(hlsl, m => $"{m.Groups["pre"].Value}\"{Path.GetFileName(m.Groups["path"].Value)}\"");


    static string LocateDirectory(string relative)
    {
        DirectoryInfo? dir = new(SourceDirectory());

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate '{relative}' from {SourceDirectory()}.");
    }

    static string SourceDirectory([CallerFilePath] string filePath = "") => Path.GetDirectoryName(filePath)!;
}
