using Prowl.Slang;

namespace Prowl.Graphite.Compiler;


public interface DiagnosticHandler
{
    public void HandleCompilationDiagnostics(DiagnosticInfo diagnostics);
}


public class DefaultDiagnosticHandler : DiagnosticHandler
{
    public void HandleCompilationDiagnostics(DiagnosticInfo diagnostics)
    {
        if (string.IsNullOrWhiteSpace(diagnostics.Message))
            return;

        System.Console.WriteLine(diagnostics.Message);
    }
}
