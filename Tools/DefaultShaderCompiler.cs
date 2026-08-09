#!/usr/bin/env dotnet run

#:package Prowl.Echo@3.0.0
#:package Prowl.Graphite@3.0.0
#:package Prowl.Graphite.ShaderDef@3.0.0
#:package Prowl.Graphite.ShaderDef.Compiler@3.0.0

#:sdk Microsoft.NET.Sdk

#:property LangVersion=preview
#:property TargetFramework=net10.0
#:property AllowUnsafeBlocks=true

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

RegisterSerializationFormats();

// Run from Tools/ (e.g. `dotnet run DefaultShaderCompiler.cs`): Environment.ProcessPath points into the
// dotnet runfile cache when launched this way, not this script's directory, so use the CWD instead.
string scriptDir = Directory.GetCurrentDirectory();
string runtimeDir = Path.GetFullPath(Path.Combine(scriptDir, "..", "Prowl.Runtime"));

string shaderDir = args.Length > 0
    ? args[0]
    : Path.Combine(runtimeDir, "Assets", "Defaults");

string outputDir = args.Length > 1
    ? args[1]
    : Path.Combine(shaderDir, "Compiled");

Directory.CreateDirectory(outputDir);

// Headless Vulkan device: only used so the ShaderDef library has a device to bind passes to while
// compiling. GraphicsBackend only has one value (Vulkan) now, so this is also what runtime playback
// will target - no per-backend loop needed anymore.
GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions());

foreach (string shaderPath in Directory.EnumerateFiles(shaderDir, "*.shader"))
    Compile(shaderPath, Path.Combine(outputDir, Path.GetFileNameWithoutExtension(shaderPath) + ".shaderblob"));

device.Dispose();

return;

void Compile(string shaderPath, string outputPath)
{
    string shaderFile = Path.GetFileName(shaderPath);
    Console.WriteLine($"Compiling {shaderFile} ...");

    string source = File.ReadAllText(shaderPath);

    ShaderDefinition definition = ShaderParser.Parse(source);

    SlangShaderCompiler compiler = new();
    compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
    compiler.BeginSession([new DirectoryInfo(shaderDir)], FileLoader);

    definition.Create(device, compiler, new Variant(), CompileMode.All);
    ShaderSnapshot snapshot = definition.Snapshot();

    compiler.EndSession();

    DefaultShaderBlobData data = new() { Definition = definition, Snapshot = snapshot };

    EchoObject root = Serializer.Serialize(data, TypeMode.None);

    using (var writer = new BinaryWriter(File.Create(outputPath)))
        root.WriteToBinary(writer);

    VerifyRoundTrip(outputPath, data);

    Console.WriteLine($"  wrote {outputPath} ({snapshot.Passes?.Length ?? 0} pass(es))");

    Memory<byte>? FileLoader(string name)
    {
        string path = Path.Combine(shaderDir, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}

void VerifyRoundTrip(string outputPath, DefaultShaderBlobData original)
{
    using var reader = new BinaryReader(File.OpenRead(outputPath));

    EchoObject root = EchoObject.ReadFromBinary(reader);
    DefaultShaderBlobData restored = Serializer.Deserialize<DefaultShaderBlobData>(root);

    PassSnapshot[] originalPasses = original.Snapshot.Passes ?? [];
    PassSnapshot[] restoredPasses = restored.Snapshot.Passes ?? [];

    if (originalPasses.Length != restoredPasses.Length)
        throw new InvalidOperationException("Round-trip pass count mismatch.");

    for (int p = 0; p < originalPasses.Length; p++)
    {
        Variant[] originalVariants = originalPasses[p].Variants ?? [];
        Variant[] restoredVariants = restoredPasses[p].Variants ?? [];

        if (originalVariants.Length != restoredVariants.Length)
            throw new InvalidOperationException($"Round-trip variant count mismatch (pass {p}).");

        for (int i = 0; i < originalVariants.Length; i++)
        {
            if (!originalVariants[i].TryGetDescription(GraphicsBackend.Vulkan, out ShaderDescription a))
                continue;

            if (!restoredVariants[i].TryGetDescription(GraphicsBackend.Vulkan, out ShaderDescription b))
                throw new InvalidOperationException($"Round-trip lost the Vulkan variant at index {i} (pass {p}).");

            ShaderStageDescription[] sa = a.Stages;
            ShaderStageDescription[] sb = b.Stages;

            if (sa.Length != sb.Length)
                throw new InvalidOperationException($"Round-trip stage count mismatch (pass {p}, variant {i}).");

            for (int s = 0; s < sa.Length; s++)
            {
                if (sa[s].Stage != sb[s].Stage ||
                    sa[s].EntryPoint != sb[s].EntryPoint ||
                    !sa[s].ShaderBytes.AsSpan().SequenceEqual(sb[s].ShaderBytes))
                {
                    throw new InvalidOperationException($"Round-trip stage mismatch (pass {p}, variant {i}, stage {sa[s].Stage}).");
                }
            }
        }
    }
}

void RegisterSerializationFormats()
{
    Serializer.RegisterFormat(new PropertyIDFormat());
    Serializer.RegisterFormat(new VertexAttributeIDFormat());
    Serializer.RegisterFormat(new KeywordFormat());
    Serializer.RegisterFormat(new VariantSpaceFormat());
}

// A baked default shader: the parsed definition plus whichever variants were compiled ahead of time
// (Vulkan only). Field-for-field identical to Prowl.Runtime.Resources.DefaultShaderBlobData - Echo's
// TypeMode.None serializes by field name only, so the two independently-defined types round-trip
// through each other without either assembly referencing the other.
struct DefaultShaderBlobData
{
    public ShaderDefinition Definition;
    public ShaderSnapshot Snapshot;
}

// The following four formats are duplicated from Prowl.Runtime.SerializationFormats.RegisterDefaults()
// rather than referencing Prowl.Runtime directly, so this tool has no dependency on the runtime project
// (avoids a build-order cycle: the runtime assembly embeds this tool's output as a resource).

sealed class PropertyIDFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(PropertyID);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
        => new(PropertyID.ToString((PropertyID)value) ?? "");

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        string name = value.StringValue;
        return string.IsNullOrEmpty(name) ? default : (PropertyID)name;
    }
}

sealed class VertexAttributeIDFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(VertexAttributeID);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
        => new(VertexAttributeID.ToString((VertexAttributeID)value) ?? "");

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        string name = value.StringValue;
        return string.IsNullOrEmpty(name) ? default : (VertexAttributeID)name;
    }
}

sealed class KeywordFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(Keyword);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
    {
        var keyword = (Keyword)value;
        EchoObject compound = EchoObject.NewCompound();
        compound.Add("Name", new EchoObject(keyword.Name ?? ""));
        compound.Add("Value", new EchoObject(keyword.Value ?? ""));
        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
        => new Keyword(value["Name"].StringValue, value["Value"].StringValue);
}

sealed class VariantSpaceFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => type == typeof(VariantSpace);

    public EchoObject Serialize(Type targetType, object value, SerializationContext context)
    {
        var space = (VariantSpace)value;
        EchoObject compound = EchoObject.NewCompound();
        compound.Add("Name", new EchoObject(space.Name ?? ""));
        compound.Add("DeclType", new EchoObject(space.DeclType ?? ""));

        EchoObject values = EchoObject.NewList();
        foreach (string v in space.Values ?? [])
            values.ListAdd(new EchoObject(v));
        compound.Add("Values", values);

        compound.Add("IsEnum", new EchoObject(space.IsEnum));
        compound.Add("TypeModule", new EchoObject(space.TypeModule ?? ""));
        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        List<string> values = [.. value["Values"].List.Select(v => v.StringValue)];
        string? typeModule = value["TypeModule"].StringValue;

        return new VariantSpace(
            value["Name"].StringValue,
            value["DeclType"].StringValue,
            values,
            value["IsEnum"].BoolValue,
            string.IsNullOrEmpty(typeModule) ? null : typeModule);
    }
}
