using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


/// <summary>
/// The WGSL compiler module for all WebGPU backends.
/// </summary>
public class WebGPUCompiler : CompilerModule
{
    private TargetDescription _target;

    /// <inheritdoc/>
    public TargetDescription Target => _target;

    /// <inheritdoc/>
    public GraphicsBackend Backend => throw new NotImplementedException("WebGPU backend does not exist.");

    /// <summary>
    /// Creates a new instance of <see cref="WebGPUCompiler"/>
    /// </summary>
    /// <param name="profileString"></param>
    public WebGPUCompiler(string profileString = "wgsl_1_0")
    {
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Wgsl
        };
    }

    /// <inheritdoc/>
    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler) =>
        throw new NotImplementedException();
}
