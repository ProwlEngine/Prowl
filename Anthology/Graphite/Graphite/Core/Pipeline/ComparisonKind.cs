namespace Prowl.Graphite;

/// <summary>
/// How new values compare against existing values in a depth/stencil test.
/// </summary>
public enum ComparisonKind : byte
{
    /// <summary>
    /// Never succeeds.
    /// </summary>
    Never,
    /// <summary>
    /// Succeeds if new &lt; existing.
    /// </summary>
    Less,
    /// <summary>
    /// Succeeds if new == existing.
    /// </summary>
    Equal,
    /// <summary>
    /// Succeeds if new &lt;= existing.
    /// </summary>
    LessEqual,
    /// <summary>
    /// Succeeds if new &gt; existing.
    /// </summary>
    Greater,
    /// <summary>
    /// Succeeds if new != existing.
    /// </summary>
    NotEqual,
    /// <summary>
    /// Succeeds if new &gt;= existing.
    /// </summary>
    GreaterEqual,
    /// <summary>
    /// Always succeeds.
    /// </summary>
    Always,
}
