using Prowl.Slang;

namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// A handler for any error, warning or log message produced when compiling shaders.
/// </summary>
public delegate void DiagnosticHandler(DiagnosticInfo diagnostics);
