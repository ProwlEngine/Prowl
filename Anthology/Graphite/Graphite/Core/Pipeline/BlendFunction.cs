namespace Prowl.Graphite;

/// <summary>Blend operation for source and destination factors.</summary>
public enum BlendFunction : byte
{
    /// <summary>src + dst.</summary>
    Add,
    /// <summary>src - dst.</summary>
    Subtract,
    /// <summary>dst - src.</summary>
    ReverseSubtract,
    /// <summary>min(src, dst).</summary>
    Minimum,
    /// <summary>max(src, dst).</summary>
    Maximum,
}
