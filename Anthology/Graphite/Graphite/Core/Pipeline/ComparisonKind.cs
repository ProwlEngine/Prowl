namespace Prowl.Graphite;

/// <summary>
/// Depth/stencil comparison function.
/// </summary>
public enum ComparisonKind : byte
{
    /// <summary>
    /// Never succeeds.
    /// </summary>
    Never,
    /// <summary>
    /// New &lt; existing.
    /// </summary>
    Less,
    /// <summary>
    /// New == existing.
    /// </summary>
    Equal,
    /// <summary>
    /// New &lt;= existing.
    /// </summary>
    LessEqual,
    /// <summary>
    /// New &gt; existing.
    /// </summary>
    Greater,
    /// <summary>
    /// New != existing.
    /// </summary>
    NotEqual,
    /// <summary>
    /// New &gt;= existing.
    /// </summary>
    GreaterEqual,
    /// <summary>
    /// Always succeeds.
    /// </summary>
    Always,
}
