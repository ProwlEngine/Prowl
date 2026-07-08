using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Prowl.Graphite.Compiler.Tests;


// Shared scaffolding for the platform compilation tests: access to the on-disk shared shader suite,
// the session plumbing to compile a shader for one or more backends, and the checked-in known-good
// outputs. The shaders live as real .slang files under Shaders/ (copied next to the test binary) so
// they can import one another, the single source of truth every backend suite compiles against.
internal static class CompilerTestHarness
{
    static string ShadersDirectory => Path.Combine(AppContext.BaseDirectory, "Shaders");

    static string KnownGoodDirectory => Path.Combine(AppContext.BaseDirectory, "KnownGood");

    public static byte[] KnownGoodBytes(string fileName) => File.ReadAllBytes(Path.Combine(KnownGoodDirectory, fileName));

    public static string KnownGoodText(string fileName) => File.ReadAllText(Path.Combine(KnownGoodDirectory, fileName));


    // Reads the source of a shared shader module from the Shaders directory.
    public static string ShaderSource(string moduleName) => File.ReadAllText(Path.Combine(ShadersDirectory, $"{moduleName}.slang"));


    // Compiles the named shared shader for the given backends and returns the single (non-variant)
    // result. Use for shaders that declare no variant space.
    public static VariantResult CompileShared(string moduleName, params Func<CompilerModule>[] moduleFactories)
    {
        CompilationResult result = CompileSharedAll(moduleName, moduleFactories);

        // No variant attributes in the source, so exactly one (empty) variant is produced.
        return result.CompiledVariants[0];
    }


    // Compiles the named shared shader and returns the full result (every variant permutation).
    public static CompilationResult CompileSharedAll(string moduleName, params Func<CompilerModule>[] moduleFactories)
        => Compile(ShaderSource(moduleName), moduleName, moduleFactories);


    // Backwards-compatible shorthand for the baseline Graphics shader.
    public static VariantResult CompileGraphics(params Func<CompilerModule>[] moduleFactories)
        => CompileShared("Graphics", moduleFactories);


    public static CompilationResult Compile(string source, string moduleName, params Func<CompilerModule>[] moduleFactories)
        => SlangThread.Run(() =>
        {
            CompilationSession session = new();

            foreach (Func<CompilerModule> factory in moduleFactories)
                session.RegisterModule(factory());

            // The Shaders directory is on the search path so shaders can import sibling modules
            // (e.g. Modules imports Common); the base directory satisfies the native non-empty path.
            session.BeginSession([new DirectoryInfo(ShadersDirectory), new DirectoryInfo(AppContext.BaseDirectory)]);

            CompilationResult result = session.CompileShader(
                moduleName, $"{moduleName}.slang", Encoding.UTF8.GetBytes(source), ShaderType.Rasterization);

            session.EndSession();
            return result;
        });


    // The compiled bytes for one stage, decoded as UTF-8 text (HLSL targets).
    public static string StageText(ShaderDescription description, ShaderStages stage)
        => Encoding.UTF8.GetString(StageOf(description, stage).ShaderBytes);


    // HLSL embeds the source file path in every #line directive; that path varies by how the module
    // was loaded and the machine it ran on, so it is reduced to the bare file name before comparison.
    // The known-good generator (Tools/SlangQuickCompile) applies the identical reduction on write.
    static readonly System.Text.RegularExpressions.Regex s_lineDirective =
        new("^(?<pre>\\s*#line\\s+\\d+\\s+)\"(?<path>[^\"]*)\"", System.Text.RegularExpressions.RegexOptions.Multiline);

    public static string NormalizeSourcePaths(string hlsl)
        => s_lineDirective.Replace(hlsl, m => $"{m.Groups["pre"].Value}\"{Path.GetFileName(m.Groups["path"].Value)}\"");


    public static ShaderStageDescription StageOf(ShaderDescription description, ShaderStages stage)
    {
        foreach (ShaderStageDescription s in description.Stages)
            if (s.Stage == stage)
                return s;

        throw new InvalidOperationException($"No {stage} stage in description.");
    }


    // Each reflected vertex input becomes its own single-element layout, so these locate one by its
    // shader location (Vulkan) or by its semantic name (D3D11).
    public static VertexElementDescription ElementAtLocation(ShaderDescription description, uint location)
    {
        foreach (VertexLayoutDescription layout in description.VertexLayouts)
            if (layout.Location == location)
                return Single(layout);

        throw new InvalidOperationException($"No vertex layout at location {location}.");
    }


    // Locates a layout by its element's user-facing (blended) name, e.g. "UV0".
    public static VertexLayoutDescription LayoutWithName(ShaderDescription description, string blendedName)
    {
        foreach (VertexLayoutDescription layout in description.VertexLayouts)
            if (Single(layout).Name == blendedName)
                return layout;

        throw new InvalidOperationException($"No vertex element named '{blendedName}'.");
    }


    public static VertexElementDescription Single(VertexLayoutDescription layout)
    {
        if (layout.Elements.Length != 1)
            throw new InvalidOperationException("Expected exactly one element per reflected vertex layout.");

        return layout.Elements[0];
    }


    // Runs spirv-val on the given SPIR-V bytes. Returns null when the tool is unavailable so callers
    // can fall back to the byte-for-byte known-good comparison; otherwise the tool's stderr on failure.
    public static string TryValidateSpirv(byte[] spirv)
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
