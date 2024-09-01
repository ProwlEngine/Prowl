#r "nuget: DirectXShaderCompiler.NET, 1.0.0"  // Reference a NuGet package

using System;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;

using DirectXShaderCompiler.NET;

public static string GetScriptFolder([CallerFilePath] string path = null) => Path.GetDirectoryName(path);

// Step 1: Determine the script's directory
string scriptDirectory = GetScriptFolder();

string srcVertex = File.ReadAllText(Path.Combine(scriptDirectory, "src", "gui-vertex.hlslv"));
string srcVertexLinear = File.ReadAllText(Path.Combine(scriptDirectory, "src", "gui-vertex-linear.hlslv"));
string srcFragment = File.ReadAllText(Path.Combine(scriptDirectory, "src", "gui-frag.hlslv"));

string outputVertex = Path.Combine(scriptDirectory, "gui-vertex.spv");
string outputVertexLinear = Path.Combine(scriptDirectory, "gui-vertex-linear.spv");
string outputFragment = Path.Combine(scriptDirectory, "gui-frag.spv");

CompilerOptions options = new(ShaderType.Vertex.ToProfile(6, 0));

options.generateAsSpirV = true;
options.useOpenGLMemoryLayout = true;

options.entryPoint = "VS";

CompilationResult vsResult = ShaderCompiler.Compile(srcVertex, options, (x) => "");

if (vsResult.compilationErrors != null)
    Console.WriteLine($"Messages encountered for vertex stage:\n\n{vsResult.compilationErrors}");

File.WriteAllBytes(outputVertex, vsResult.objectBytes);


CompilationResult vsLinearResult = ShaderCompiler.Compile(srcVertexLinear, options, (x) => "");

if (vsLinearResult.compilationErrors != null)
    Console.WriteLine($"Messages encountered for linear vertex stage:\n\n{vsLinearResult.compilationErrors}");

File.WriteAllBytes(outputVertexLinear, vsLinearResult.objectBytes);


options.entryPoint = "FS";
options.profile = ShaderType.Fragment.ToProfile(6, 0);

CompilationResult fsResult = ShaderCompiler.Compile(srcFragment, options, (x) => "");

if (fsResult.compilationErrors != null)
    Console.WriteLine($"Messages encountered for fragment stage:\n\n{fsResult.compilationErrors}");

File.WriteAllBytes(outputFragment, fsResult.objectBytes);


Console.WriteLine("Compilation complete.");
