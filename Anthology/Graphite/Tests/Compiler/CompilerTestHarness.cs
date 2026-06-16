using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Prowl.Graphite.Compiler.Tests;


// Shared scaffolding for the platform compilation tests: a single graphics shader, the session
// plumbing to compile it for one or more backends, and access to the checked-in known-good outputs.
internal static class CompilerTestHarness
{
    // A self-contained graphics module: a three-attribute vertex input feeding a vertex + fragment
    // stage. Kept deliberately tiny so the known-good GLSL/HLSL/SPIR-V stay readable.
    public const string GraphicsSource = """
        module TestShader;

        struct VertexInput
        {
            float3 position : POSITION;
            float2 uv       : UV0;
            float4 color    : COLOR;
        }

        struct VertexOutput
        {
            float4 clipPosition : SV_Position;
            float2 uv : UV;
            float4 color : COLOR;
        }

        [shader("vertex")]
        VertexOutput vertex(VertexInput input)
        {
            VertexOutput output;
            output.clipPosition = float4(input.position, 1);
            output.uv = input.uv;
            output.color = input.color;
            return output;
        }

        [shader("fragment")]
        float4 fragment(VertexOutput input) : SV_Target
        {
            return input.color;
        }
        """;


    static string KnownGoodDirectory => Path.Combine(AppContext.BaseDirectory, "KnownGood");

    public static byte[] KnownGoodBytes(string fileName) => File.ReadAllBytes(Path.Combine(KnownGoodDirectory, fileName));

    public static string KnownGoodText(string fileName) => File.ReadAllText(Path.Combine(KnownGoodDirectory, fileName));


    // Compiles GraphicsSource for the registered modules and returns the single (non-variant) result.
    public static VariantResult CompileGraphics(params CompilerModule[] modules)
    {
        CompilationResult result = Compile(GraphicsSource, "TestShader", modules);

        // No variant attributes in the source, so exactly one (empty) variant is produced.
        return result.CompiledVariants[0];
    }


    public static CompilationResult Compile(string source, string moduleName, params CompilerModule[] modules)
    {
        CompilationSession session = new();

        foreach (CompilerModule module in modules)
            session.RegisterModule(module);

        // The inline module has no imports, but the native session requires a non-empty search path.
        session.BeginSession([new DirectoryInfo(AppContext.BaseDirectory)]);

        CompilationResult result = session.CompileShader(
            moduleName, $"{moduleName}.slang", Encoding.UTF8.GetBytes(source), ShaderType.Rasterization);

        session.EndSession();
        return result;
    }


    // The compiled bytes for one stage, decoded as UTF-8 text (GLSL / HLSL targets).
    public static string StageText(ShaderDescription description, ShaderStages stage)
        => Encoding.UTF8.GetString(StageOf(description, stage).ShaderBytes);


    public static ShaderStageDescription StageOf(ShaderDescription description, ShaderStages stage)
    {
        foreach (ShaderStageDescription s in description.Stages)
            if (s.Stage == stage)
                return s;

        throw new InvalidOperationException($"No {stage} stage in description.");
    }


    // Each reflected vertex input becomes its own single-element layout, so these locate one by its
    // shader location (GL / Vulkan) or by its semantic name (D3D11).
    public static VertexElementDescription ElementAtLocation(ShaderDescription description, uint location)
    {
        foreach (VertexLayoutDescription layout in description.VertexLayouts)
            if (layout.Location == location)
                return Single(layout);

        throw new InvalidOperationException($"No vertex layout at location {location}.");
    }


    public static VertexElementDescription ElementWithSemantic(ShaderDescription description, string semantic)
    {
        foreach (VertexLayoutDescription layout in description.VertexLayouts)
        {
            VertexElementDescription element = Single(layout);
            if (element.Name == semantic)
                return element;
        }

        throw new InvalidOperationException($"No vertex element with semantic '{semantic}'.");
    }


    static VertexElementDescription Single(VertexLayoutDescription layout)
    {
        if (layout.Elements.Length != 1)
            throw new InvalidOperationException("Expected exactly one element per reflected vertex layout.");

        return layout.Elements[0];
    }


    // Runs spirv-val on the given SPIR-V bytes. Returns null when the tool is unavailable so callers
    // can fall back to the byte-for-byte known-good comparison; otherwise the tool's stderr on failure.
    public static string? TryValidateSpirv(byte[] spirv)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"graphite_val_{Guid.NewGuid():N}.spv");
        File.WriteAllBytes(temp, spirv);

        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo("spirv-val", temp)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            process.Start();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0 ? "" : stderr;
        }
        catch (Exception)
        {
            // spirv-val not installed in this environment; signal "unknown" so the test does not fail.
            return null;
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
