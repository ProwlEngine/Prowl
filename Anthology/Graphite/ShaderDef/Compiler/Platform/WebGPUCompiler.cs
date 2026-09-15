using System;

using Prowl.Slang;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// WGSL compiler for WebGPU backends.
/// </summary>
public class WebGPUCompiler : CompilerModule
{
    public TargetDescription Target { get; }

    /// <inheritdoc/>
    public GraphicsBackend Backend => throw new NotImplementedException("WebGPU backend does not exist.");

    /// <summary>
    /// New WebGPUCompiler.
    /// </summary>
    /// <param name="profileString"></param>
    public WebGPUCompiler(string profileString = "wgsl_1_0")
    {
        Target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Wgsl
        };
    }

    /// <inheritdoc/>
    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler) =>
        throw new NotImplementedException();
}
