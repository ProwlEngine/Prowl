using Prowl.Slang;

namespace Prowl.Graphite.Compiler;


public interface CompilerModule
{
    public TargetDescription Target { get; }
    public GraphicsBackend Backend { get; }


    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler);
}
