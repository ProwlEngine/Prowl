using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Builds the renderer's own shaders from its part of the Graphite shader library, and lists every pass they can make so
/// a build compiles all of them ahead of time.
/// </summary>
internal static class InternalShaders
{
    static InternalShaders()
    {
        ShaderLibrary.Register(typeof(InternalShaders).Assembly);
        ShaderSources.Register(Sources);
    }

    /// <summary>Makes sure includes can reach the renderer's library files.</summary>
    public static void Register() { }

    public const string Present = "void main() { outColor = Graphite_SampleSource(vec2(vUV.x, 1.0 - vUV.y)); }";

    public const string Decode = """
        void main()
        {
            vec4 c = Graphite_SampleSource(vec2(vUV.x, 1.0 - vUV.y));
            outColor = vec4(pow((max(c.rgb, vec3(0.0)) + 0.055) / 1.055, vec3(2.4)), c.a);
        }
        """;

    public const string Clear = "uniform vec4 Graphite_ClearColor; void main() { outColor = Graphite_ClearColor; }";
    public const string CopyBack = "void main() { outColor = Graphite_SampleSource(vUV); }";

    // The library files the stock effects draw with, each one fullscreen pass with its stages as variants
    private static readonly string[] s_postFiles = ["Tonemap", "AutoExposure", "Bloom", "Cinematic", "DepthOfField", "Fxaa", "GodRays", "Gtao", "MotionBlur", "Smaa", "Ssr", "Taa"];
    private static readonly string[] s_computeFiles = ["Internal/Probes.glsl", "Internal/Deform.glsl", "Internal/SkyAmbient.glsl", "Internal/Scatter.glsl"];

    public static string PostFragment(string file) => $"#include \"Graphite/Internal/{file}.glsl\"\n";

    /// <summary>A library file holding its own stages as one pass.</summary>
    public static ShaderPass FilePass(string file)
    {
        string name = ShaderLibrary.Prefix + file;
        string source = ShaderLibrary.Get(name) ?? throw new InvalidOperationException($"The shader library has no {name}.");
        return new ShaderPass { State = new PassState(), Source = source, SourceName = name };
    }

    public static ComputeProgram Compute(GraphicsDevice device, IShaderCompiler? compiler, string file)
    {
        ComputeProgram program = device.ResourceFactory.CreateComputeProgram(FilePass(file).Variants(compiler, device.BackendType).RequireCompute());
        program.Name = ShaderLibrary.Prefix + file;
        return program;
    }

    private static IEnumerable<ShaderSources.Source> Sources()
    {
        (string Fragment, string Name)[] fullscreen =
        [
            (Present, "Graphite/Present"), (Present, "Graphite/PresentOver"), (Decode, "Graphite/PresentDecode"),
            (Decode, "Graphite/PresentDecodeOver"), (Clear, "Graphite/Clear"), (CopyBack, "Graphite/CopyBack"),
        ];
        foreach ((string fragment, string name) in fullscreen)
            yield return new(FullscreenProgram.Pass(fragment, name));
        foreach (string file in s_postFiles)
            yield return new(FullscreenProgram.Pass(PostFragment(file), $"Graphite/{file}"));
        yield return new(FilePass("Internal/Prefilter.glsl"));
        foreach (string file in s_computeFiles)
            yield return new(FilePass(file), Compute: true);
        yield return new(ShadowTarget.TileClearPass());
    }
}
