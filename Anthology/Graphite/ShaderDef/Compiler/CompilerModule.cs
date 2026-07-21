using Prowl.Slang;

namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// Per-backend compilation with its own reflection/binding rules.
/// </summary>
public interface CompilerModule
{
    internal TargetDescription Target { get; }

    /// <summary>
    /// Target backend.
    /// </summary>
    public GraphicsBackend Backend { get; }

    internal ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler);
}
