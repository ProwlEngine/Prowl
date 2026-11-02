namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Serializable snapshot of a shader: per-pass axes plus whatever variants are cached. Partial capture ok, only populated variants kept.
/// </summary>
public struct ShaderSnapshot
{
    /// <summary>
    /// Per-pass snapshots, aligned to shader's passes.
    /// </summary>
    public PassSnapshot[] Passes;
}


/// <summary>
/// Snapshot of one pass: its variant axes and known variants.
/// </summary>
public struct PassSnapshot
{
    /// <summary>
    /// Variant axes for the pass.
    /// </summary>
    public VariantSpace[] Axes;

    /// <summary>
    /// Populated variants. May be a subset of the full axis space.
    /// </summary>
    public Variant[] Variants;
}
