namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Controls whether variants are compiled lazily as they are requested, or all at once.
/// </summary>
public enum CompileMode
{
    /// <summary>
    /// Compile each variant the first time it is requested.
    /// </summary>
    OnDemand,

    /// <summary>
    /// Compile every variant up front.
    /// </summary>
    All
}


/// <summary>
/// A serialization-friendly snapshot of a created shader: the per-pass axes and whatever variants
/// have been compiled/cached. Supports partial ("spotty") capture - only populated variants are kept.
/// </summary>
public struct ShaderSnapshot
{
    /// <summary>
    /// The per-pass snapshots, index-aligned with the shader's passes.
    /// </summary>
    public PassSnapshot[] Passes;
}


/// <summary>
/// A snapshot of a single pass: its variant axes and the currently-known variants.
/// </summary>
public struct PassSnapshot
{
    /// <summary>
    /// The variant axes of the pass.
    /// </summary>
    public VariantSpace[] Axes;

    /// <summary>
    /// The variants currently populated for the pass. May be a subset of the full axis space.
    /// </summary>
    public Variant[] Variants;
}
