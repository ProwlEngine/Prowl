using Prowl.Graphite.Variants;

namespace Prowl.Graphite.Compiler;


/// <summary>
/// A container for all the compiled variant spaces and shader variants for each backend.
/// </summary>
public struct CompilationResult
{
    /// <summary>
    /// The variant spaces for this compilation unit (all keywords + a list of the found values for each keyword)
    /// </summary>
    public VariantSpace[] VariantSpaces;

    /// <summary>
    /// Every compiled variant permutation for this compilation unit.
    /// </summary>
    public VariantResult[] CompiledVariants;
}


/// <summary>
/// A container for a compiled variant, and all the shader sources for each of the backends registered for this compilation module.
/// </summary>
public struct VariantResult
{
    /// <summary>
    /// The fixed variant keywords used for this compiled variant.
    /// </summary>
    public Keyword[] Variants;

    /// <summary>
    /// The set of each compiled backend shader.
    /// </summary>
    public (ShaderDescription Description, GraphicsBackend Backend)[] Backends;
}
