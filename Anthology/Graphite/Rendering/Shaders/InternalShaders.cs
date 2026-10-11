using System;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>Builds the renderer's own shaders from its part of the Graphite shader library.</summary>
internal static class InternalShaders
{
    static InternalShaders() => ShaderLibrary.Register(typeof(InternalShaders).Assembly);

    /// <summary>Makes sure includes can reach the renderer's library files.</summary>
    public static void Register() { }

    public static ComputeProgram Compute(GraphicsDevice device, IShaderCompiler compiler, string file)
    {
        string name = ShaderLibrary.Prefix + file;
        string source = ShaderLibrary.Get(name) ?? throw new InvalidOperationException($"The shader library has no {name}.");
        ShaderPass pass = new() { State = new PassState(), Source = source, SourceName = name };
        ComputeProgram program = device.ResourceFactory.CreateComputeProgram(compiler.CompileCompute(pass, [], device.BackendType));
        program.Name = name;
        return program;
    }
}
