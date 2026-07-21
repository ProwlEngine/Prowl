using Prowl.Slang;

namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// Handles error/warning/log messages from shader compilation.
/// </summary>
public delegate void DiagnosticHandler(DiagnosticInfo diagnostics);
