#!/usr/bin/env dotnet run

#:package Prowl.Echo@2.3.0
#:package Prowl.Graphite@2.3.0
#:package Prowl.Graphite.Compiler@2.3.0

#:sdk Microsoft.NET.Sdk

#:property LangVersion=preview
#:property TargetFramework=net10.0

#:project ../Prowl.Runtime/Prowl.Runtime.csproj

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.Compiler;
using Prowl.Graphite.Variants;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

string shaderDir;

var allBackends = new Dictionary<string, Func<CompilerModule>>(StringComparer.OrdinalIgnoreCase)
{
    ["OpenGL"] = () => new GLCompiler("glsl_450", GraphicsBackend.OpenGL),
    ["OpenGLES"] = () => new GLCompiler("glsl_es_310", GraphicsBackend.OpenGLES),
    ["Vulkan"] = () => new VulkanCompiler("spirv_1_4"),
    ["Direct3D11"] = () => new DXCompiler("sm_5_0", GraphicsBackend.Direct3D11),
};

SerializationFormats.RegisterDefaults();

string scriptDir = Path.GetDirectoryName(
    Environment.ProcessPath!)!;

string runtimeDir = Path.GetFullPath(
    Path.Combine(scriptDir, "..", "Prowl.Runtime"));

shaderDir = args.Length > 0
    ? args[0]
    : Path.Combine(runtimeDir, "GUI", "Shaders");

string outputDir = args.Length > 1
    ? args[1]
    : Path.Combine(runtimeDir, "Assets", "Shaders");

Directory.CreateDirectory(outputDir);

Compile("UI.slang", Path.Combine(outputDir, "UI.shaderblob"));
Compile("Blur.slang", Path.Combine(outputDir, "Blur.shaderblob"));

return;

(string Name, Func<CompilerModule> Factory)[] SelectedBackends()
{
    string env = Environment.GetEnvironmentVariable("UISHADER_BACKENDS");

    string[] names = string.IsNullOrWhiteSpace(env)
        ? ["OpenGL", "OpenGLES", "Vulkan"]
        : env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    return [.. names.Select(n => (n, allBackends[n]))];
}

void Compile(string shaderFile, string outputPath)
{
    Console.WriteLine($"Compiling {shaderFile} ...");

    List<VariantResult> merged = new();

    foreach ((string name, Func<CompilerModule> factory) in SelectedBackends())
    {
        Console.WriteLine($"  backend {name} ...");

        CompilationSession session = new();
        session.RegisterModule(factory());
        session.BeginSession([new DirectoryInfo(shaderDir)], FileLoader);

        CompilationResult result = session.CompileShader(shaderFile, ShaderType.Rasterization);

        session.EndSession();

        MergeBackend(merged, result);
    }

    UIShaderBlobData data = BuildBlobData(merged);

    EchoObject root = Serializer.Serialize(data, TypeMode.None);

    using (var writer = new BinaryWriter(File.Create(outputPath)))
        root.WriteToBinary(writer);

    VerifyRoundTrip(outputPath, data);

    Console.WriteLine($"  wrote {outputPath} ({data.Variants.Length} variant(s))");
}

UIShaderBlobData BuildBlobData(List<VariantResult> merged)
{
    return new UIShaderBlobData
    {
        Variants = [.. merged.Select(v => new UIShaderVariantData
        {
            Keywords = v.Variants,
            Backends = [.. v.Backends.Select(b => new UIShaderBackendData
            {
                Backend = b.Backend,
                Description = b.Description,
            })],
        })],
    };
}

void VerifyRoundTrip(string outputPath, UIShaderBlobData original)
{
    using var reader = new BinaryReader(File.OpenRead(outputPath));

    EchoObject root = EchoObject.ReadFromBinary(reader);
    UIShaderBlobData restored = Serializer.Deserialize<UIShaderBlobData>(root);

    if (restored.Variants.Length != original.Variants.Length)
        throw new InvalidOperationException("Round-trip variant count mismatch.");

    for (int i = 0; i < original.Variants.Length; i++)
    {
        UIShaderBackendData[] a = original.Variants[i].Backends;
        UIShaderBackendData[] b = restored.Variants[i].Backends;

        if (a.Length != b.Length)
            throw new InvalidOperationException("Round-trip backend count mismatch.");

        for (int j = 0; j < a.Length; j++)
            VerifyDescription(a[j], b[j], i);
    }
}

void VerifyDescription(UIShaderBackendData a, UIShaderBackendData b, int variant)
{
    if (a.Backend != b.Backend)
        throw new InvalidOperationException($"Round-trip backend mismatch (variant {variant}).");

    ShaderStageDescription[] sa = a.Description.Stages;
    ShaderStageDescription[] sb = b.Description.Stages;

    if (sa.Length != sb.Length)
        throw new InvalidOperationException($"Round-trip stage count mismatch (variant {variant}, {a.Backend}).");

    for (int i = 0; i < sa.Length; i++)
    {
        if (sa[i].Stage != sb[i].Stage ||
            sa[i].EntryPoint != sb[i].EntryPoint ||
            !sa[i].ShaderBytes.AsSpan().SequenceEqual(sb[i].ShaderBytes))
        {
            throw new InvalidOperationException(
                $"Round-trip stage mismatch (variant {variant}, {a.Backend}, stage {sa[i].Stage}).");
        }
    }

    ResourceLayoutDescription[] ra = a.Description.ResourceLayouts;
    ResourceLayoutDescription[] rb = b.Description.ResourceLayouts;

    if (ra.Length != rb.Length)
        throw new InvalidOperationException(
            $"Round-trip resource-layout count mismatch (variant {variant}, {a.Backend}).");

    for (int i = 0; i < ra.Length; i++)
    {
        if (ra[i].Elements.Length != rb[i].Elements.Length)
            throw new InvalidOperationException(
                $"Round-trip resource-element count mismatch (variant {variant}, {a.Backend}).");

        for (int j = 0; j < ra[i].Elements.Length; j++)
        {
            if (ra[i].Elements[j].Name != rb[i].Elements[j].Name)
            {
                throw new InvalidOperationException(
                    $"Round-trip resource-element name mismatch (variant {variant}, {a.Backend}).");
            }
        }
    }
}

void MergeBackend(List<VariantResult> merged, CompilationResult result)
{
    foreach (VariantResult variant in result.CompiledVariants)
    {
        int index = merged.FindIndex(v => SameKeywords(v.Variants, variant.Variants));

        if (index < 0)
        {
            merged.Add(variant);
            continue;
        }

        VariantResult existing = merged[index];
        existing.Backends = [.. existing.Backends, .. variant.Backends];
        merged[index] = existing;
    }
}

bool SameKeywords(Keyword[] a, Keyword[] b)
{
    if (a.Length != b.Length)
        return false;

    for (int i = 0; i < a.Length; i++)
    {
        if (a[i].Name != b[i].Name || a[i].Value != b[i].Value)
            return false;
    }

    return true;
}

Memory<byte>? FileLoader(string name)
{
    string path = Path.Combine(shaderDir, name);
    return File.Exists(path) ? File.ReadAllBytes(path) : null;
}