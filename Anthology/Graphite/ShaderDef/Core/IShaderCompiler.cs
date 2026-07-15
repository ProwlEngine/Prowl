using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// The seam a shader compiler slots into. A <see cref="ShaderDefinition"/> drives an implementation
/// of this interface to discover a pass's variant axes and to compile individual variants for a
/// requested backend, on demand or all at once.
/// </summary>
public interface IShaderCompiler
{
    /// <summary>
    /// Returns the variant axes discovered for the given pass.
    /// </summary>
    IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass);

    /// <summary>
    /// Compiles a single variant of the given pass for a single backend, returning its reflected
    /// <see cref="ShaderDescription"/> (without fixed-function render state, which the pass applies
    /// when binding).
    /// </summary>
    ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend);
}
