using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


// Will use slang so no need for abstraction
public class MetalCompiler : CompilerModule
{
    private TargetDescription _target;
    public TargetDescription Target => _target;

    public GraphicsBackend Backend => throw new NotImplementedException("Metal backend does not exist (yet)");


    public MetalCompiler(string profileString = "metal_2_0")
    {
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Metal
        };
    }

    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler) =>
        throw new NotImplementedException();
}
