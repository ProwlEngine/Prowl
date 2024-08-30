#r "nuget: DirectXShaderCompiler.NET, 1.0.0"  // Reference a NuGet package

using System;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;

using DirectXShaderCompiler.NET;

public static string GetScriptFolder([CallerFilePath] string path = null) => Path.GetDirectoryName(path);

// Step 1: Determine the script's directory
string scriptDirectory = GetScriptFolder();

string srcVertex = File.ReadAllText(Path.Combine(scriptDirectory, "HLSL", "gui-vertex.hlsl"));
string srcFragment = File.ReadAllText(Path.Combine(scriptDirectory, "HLSL", "gui-frag.hlsl"));

string outputVertex = Path.Combine(scriptDirectory, "SPIR-V", "gui-vertex.spv");
string outputFragment = Path.Combine(scriptDirectory, "SPIR-V", "gui-frag.spv");

// Ensure the destination directory exists
Directory.CreateDirectory(Path.Combine(scriptDirectory, "SPIR-V"));

CompilerOptions options = new(ShaderType.Vertex.ToProfile(6, 0));

options.generateAsSpirV = true;
options.useOpenGLMemoryLayout = true;

options.entryPoint = "VS";

CompilationResult vsResult = ShaderCompiler.Compile(srcVertex, options, (x) => "");

if (vsResult.compilationErrors != null)
    throw new Exception($"Compilation errors encountered for vertex stage:\n\n{vsResult.compilationErrors}");

File.WriteAllBytes(outputVertex, vsResult.objectBytes);

options.entryPoint = "FS";
options.profile = ShaderType.Fragment.ToProfile(6, 0);

CompilationResult fsResult = ShaderCompiler.Compile(srcFragment, options, (x) => "");

if (fsResult.compilationErrors != null)
    throw new Exception($"Compilation errors encountered for vertex stage:\n\n{fsResult.compilationErrors}");

File.WriteAllBytes(outputFragment, fsResult.objectBytes);


Console.WriteLine("Compilation complete.");
