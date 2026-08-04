namespace Prowl.Graphite;

/// <summary>Resource CPU mapping mode.</summary>
public enum MapMode : byte
{
    /// <summary>Read-only, staging resources only.</summary>
    Read,

    /// <summary>Write-only, transferred back on Unmap, full replace only.</summary>
    Write,

    /// <summary>Read and write, staging resources only.</summary>
    ReadWrite,
}
