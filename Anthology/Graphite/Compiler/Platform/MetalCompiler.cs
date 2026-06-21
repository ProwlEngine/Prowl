using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


/// <summary>
/// The MSL compiler module for the Metal backend.
/// </summary>
public class MetalCompiler : CompilerModule
{
    private TargetDescription _target;

    /// <inheritdoc/>
    public TargetDescription Target => _target;

    /// <inheritdoc/>
    public GraphicsBackend Backend => throw new NotImplementedException("Metal backend does not exist (yet)");


    /// <summary>
    /// Creates a new instance of <see cref="MetalCompiler"/>.
    /// </summary>
    public MetalCompiler(string profileString = "metal_2_0")
    {
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Metal
        };
    }


    /// <inheritdoc/>
    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler) =>
        throw new NotImplementedException();
}
