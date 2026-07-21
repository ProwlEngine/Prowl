namespace Prowl.Graphite;

/// <summary>
/// How a resource gets mapped into CPU address space.
/// </summary>
public enum MapMode : byte
{
    /// <summary>
    /// Read-only. Not writable, no transfer back. Staging resources only.
    /// </summary>
    Read,

    /// <summary>
    /// Write-only. Transferred back on Unmap. Erases prior contents, full replace only.
    /// </summary>
    Write,

    /// <summary>
    /// Read and write. Staging resources only.
    /// </summary>
    ReadWrite,
}
