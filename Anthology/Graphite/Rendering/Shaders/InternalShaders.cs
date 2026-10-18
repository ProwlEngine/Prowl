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

    public const string Present = "void main() { outColor = Graphite_SampleSource(vec2(vUV.x, 1.0 - vUV.y)); }";

    public const string Decode = """
        void main()
        {
            vec4 c = Graphite_SampleSource(vec2(vUV.x, 1.0 - vUV.y));
            vec3 e = max(c.rgb, vec3(0.0));
            outColor = vec4(mix(e / 12.92, pow((e + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), e)), c.a);
        }
        """;

    public const string Encode = """
        void main()
        {
            vec4 c = Graphite_SampleSource(vec2(vUV.x, 1.0 - vUV.y));
            vec3 l = max(c.rgb, vec3(0.0));
            outColor = vec4(mix(l * 12.92, 1.055 * pow(l, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), l)), c.a);
        }
        """;

    public const string Clear ="uniform vec4 Graphite_ClearColor; void main() { outColor = Graphite_ClearColor; }";
    public const string CopyBack = "void main() { outColor = Graphite_SampleSource(vUV); }";

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
}
