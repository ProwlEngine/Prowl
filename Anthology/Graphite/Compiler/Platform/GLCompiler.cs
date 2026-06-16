using System;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


public class GLCompiler : CompilerModule
{
    private TargetDescription _target;
    public TargetDescription Target => _target;

    private readonly GraphicsBackend _backend;
    public GraphicsBackend Backend => _backend;


    public GLCompiler(string profileString = "glsl_450", GraphicsBackend backend = GraphicsBackend.OpenGL)
    {
        _backend = backend;
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Glsl,
        };
    }

    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler) =>
        throw new NotImplementedException();
}
