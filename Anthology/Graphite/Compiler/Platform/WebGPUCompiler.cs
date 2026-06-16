using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


// Will use slang so no need for abstraction
public class WebGPUCompiler : CompilerModule
{
    private TargetDescription _target;
    public TargetDescription Target => _target;

    public GraphicsBackend Backend => throw new NotImplementedException("WebGPU backend does not exist.");

    public WebGPUCompiler(string profileString = "wgsl_1_0")
    {
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Wgsl
        };
    }

    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler) =>
        throw new NotImplementedException();
}
