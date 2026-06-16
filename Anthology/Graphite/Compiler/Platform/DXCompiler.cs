using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;



public class DXCompiler : CompilerModule
{
    private TargetDescription _target;
    public TargetDescription Target => _target;

    private GraphicsBackend _backend;
    public GraphicsBackend Backend => _backend;


    public DXCompiler(string profileString = "sm_6_6", GraphicsBackend backend = GraphicsBackend.Direct3D11)
    {
        _backend = backend;
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Hlsl
        };
    }

    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler) =>
        SlangReflector.BuildDescription(linkedComponent, layoutIndex, handler);
}
