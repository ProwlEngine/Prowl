using Prowl.Slang;

namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// Provides modular compilation for specific target backends/platforms with differing reflection and binding rules.
/// </summary>
public interface CompilerModule
{
    internal TargetDescription Target { get; }

    /// <summary>
    /// The graphics backend this compiler targets.
    /// </summary>
    public GraphicsBackend Backend { get; }

    internal ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler);
}
