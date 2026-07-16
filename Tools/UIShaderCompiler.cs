#!/usr/bin/env dotnet run

#:package Prowl.Echo@2.6.3
#:package Prowl.Graphite@2.6.3
#:package Prowl.Graphite.ShaderDef@2.6.3
#:package Prowl.Graphite.ShaderDef.Compiler@2.6.3

#:sdk Microsoft.NET.Sdk

#:property LangVersion=preview
#:property TargetFramework=net10.0
#:property AllowUnsafeBlocks=true

#:project ../Prowl.Runtime/Prowl.Runtime.csproj

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

SerializationFormats.RegisterDefaults();

// Run from Tools/ (e.g. `dotnet run UIShaderCompiler.cs`): Environment.ProcessPath points into the
// dotnet runfile cache when launched this way, not this script's directory, so use the CWD instead.
string scriptDir = Directory.GetCurrentDirectory();
string runtimeDir = Path.GetFullPath(Path.Combine(scriptDir, "..", "Prowl.Runtime"));

string shaderDir = args.Length > 0
    ? args[0]
    : Path.Combine(runtimeDir, "GUI", "Shaders");

string outputDir = args.Length > 1
    ? args[1]
    : Path.Combine(runtimeDir, "Assets", "Shaders");

Directory.CreateDirectory(outputDir);

// Headless Vulkan device: only used so the ShaderDef library has a device to bind passes to while
// compiling. GraphicsBackend only has one value (Vulkan) now, so this is also what runtime playback
// will target - no per-backend loop needed anymore.
GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions());

Compile("UI.slang", Path.Combine(outputDir, "UI.shaderblob"));
Compile("Blur.slang", Path.Combine(outputDir, "Blur.shaderblob"));

device.Dispose();

return;

void Compile(string shaderFile, string outputPath)
{
    Console.WriteLine($"Compiling {shaderFile} ...");

    string source = File.ReadAllText(Path.Combine(shaderDir, shaderFile));

    ShaderPass pass = new() { State = new PassState(), InlineSlang = source };
    ShaderDefinition definition = new() { Name = shaderFile, Passes = [pass] };

    SlangShaderCompiler compiler = new();
    compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
    compiler.BeginSession([new DirectoryInfo(shaderDir)], FileLoader);

    definition.Create(device, compiler, CompileMode.All);
    ShaderSnapshot snapshot = definition.Snapshot();

    compiler.EndSession();

    UIShaderBlobData data = new() { Definition = definition, Snapshot = snapshot };

    EchoObject root = Serializer.Serialize(data, TypeMode.None);

    using (var writer = new BinaryWriter(File.Create(outputPath)))
        root.WriteToBinary(writer);

    VerifyRoundTrip(outputPath, data);

    Console.WriteLine($"  wrote {outputPath} ({snapshot.Passes[0].Variants?.Length ?? 0} variant(s))");

    Memory<byte>? FileLoader(string name)
    {
        string path = Path.Combine(shaderDir, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}

void VerifyRoundTrip(string outputPath, UIShaderBlobData original)
{
    using var reader = new BinaryReader(File.OpenRead(outputPath));

    EchoObject root = EchoObject.ReadFromBinary(reader);
    UIShaderBlobData restored = Serializer.Deserialize<UIShaderBlobData>(root);

    Variant[] originalVariants = original.Snapshot.Passes[0].Variants ?? [];
    Variant[] restoredVariants = restored.Snapshot.Passes[0].Variants ?? [];

    if (originalVariants.Length != restoredVariants.Length)
        throw new InvalidOperationException("Round-trip variant count mismatch.");

    for (int i = 0; i < originalVariants.Length; i++)
    {
        if (!originalVariants[i].TryGetDescription(GraphicsBackend.Vulkan, out ShaderDescription a))
            continue;

        if (!restoredVariants[i].TryGetDescription(GraphicsBackend.Vulkan, out ShaderDescription b))
            throw new InvalidOperationException($"Round-trip lost the Vulkan variant at index {i}.");

        ShaderStageDescription[] sa = a.Stages;
        ShaderStageDescription[] sb = b.Stages;

        if (sa.Length != sb.Length)
            throw new InvalidOperationException($"Round-trip stage count mismatch (variant {i}).");

        for (int s = 0; s < sa.Length; s++)
        {
            if (sa[s].Stage != sb[s].Stage ||
                sa[s].EntryPoint != sb[s].EntryPoint ||
                !sa[s].ShaderBytes.AsSpan().SequenceEqual(sb[s].ShaderBytes))
            {
                throw new InvalidOperationException($"Round-trip stage mismatch (variant {i}, stage {sa[s].Stage}).");
            }
        }
    }
}
