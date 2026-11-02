using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>Builds the renderer's own shaders from its part of the Graphite shader library.</summary>
internal static class InternalShaders
{
    static InternalShaders() => ShaderLibrary.Register(typeof(InternalShaders).Assembly);

    /// <summary>Makes sure includes can reach the renderer's library files.</summary>
    public static void Register() { }

    // Graphite_PresentMode 0 copies, 1 decodes sRGB to linear, 2 encodes linear to sRGB
    public const string Present = """
        uniform int Graphite_PresentMode;
        void main()
        {
            vec4 c = Graphite_SampleSource(vec2(vUV.x, 1.0 - vUV.y));
            vec3 v = max(c.rgb, vec3(0.0));
            if (Graphite_PresentMode == 1)
                c.rgb = mix(v / 12.92, pow((v + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), v));
            else if (Graphite_PresentMode == 2)
                c.rgb = mix(v * 12.92, 1.055 * pow(v, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), v));
            outColor = c;
        }
        """;

    public const string CopyBack = "void main() { outColor = Graphite_SampleSource(vUV); }";

    public static string PostFragment(string file) => $"#include \"Graphite/Internal/{file}.glsl\"\n";

    /// <summary>A library file holding its own stages as one pass.</summary>
    public static ShaderPass FilePass(string file)
    {
        string name = ShaderLibrary.Prefix + file;
        string source = ShaderLibrary.Get(name) ?? throw new InvalidOperationException($"The shader library has no {name}.");
        return new ShaderPass { State = new PassState(), Source = source, SourceName = name };
    }

    /// <summary>A compute program of a library file, compiled under the policy.</summary>
    public static InternalCompute Compute(GraphicsDevice device, IShaderCompiler? compiler, string file, CompilePolicy policy = CompilePolicy.Sync)
        => new(device, FilePass(file).Variants(compiler, device.BackendType), ShaderLibrary.Prefix + file, policy);
}

/// <summary>
/// A compute program of the renderer's library. Async compiles it on a worker at Fallback priority and its first use
/// waits for that, the other policies compile it when made.
/// </summary>
internal sealed class InternalCompute : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly PassVariants _variants;
    private readonly string _name;
    private ComputeProgram? _program;

    public InternalCompute(GraphicsDevice device, PassVariants variants, string name, CompilePolicy policy)
    {
        _device = device;
        _variants = variants;
        _name = name;
        if (policy == CompilePolicy.Async)
            variants.QueueCompute(0, CompilePriority.Fallback);
        else
            _program = Create();
    }

    /// <summary>The program, waiting for its compile the first time. Render thread.</summary>
    public ComputeProgram Program => _program ??= Create();

    private ComputeProgram Create()
    {
        ComputeProgram program = _device.ResourceFactory.CreateComputeProgram(_variants.RequireCompute());
        program.Name = _name;
        return program;
    }

    public void Dispose() => _program?.Dispose();
}
