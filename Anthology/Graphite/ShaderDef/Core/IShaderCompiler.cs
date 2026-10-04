using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Seam a shader compiler plugs into. Drives variant discovery and per-variant compile for a pass.
/// </summary>
public interface IShaderCompiler
{
    /// <summary>
    /// Changes whenever the compiler session restarts, so passes know a failed compile may now succeed.
    /// </summary>
    int SessionVersion => 0;

    /// <summary>Variant axes for the given pass.</summary>
    IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass);

    /// <summary>
    /// Compiles one variant of a pass for one backend. Result has no fixed-function
    /// render state, that's applied by the pass at bind time.
    /// </summary>
    ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend);
}
