namespace Prowl.Veldrid;

/// <summary>
/// An immutable, point-in-time copy of a <see cref="GraphicsDevice"/>'s profiling counters,
/// returned by <see cref="GraphicsDevice.GetProfile"/>. Safe to read from any thread.
///
/// Flow accessors report the last completed frame's activity and are zeroed each
/// <see cref="GraphicsDevice.BeginFrame"/>. Gauge accessors report state that is currently
/// resident and persists across frames until <see cref="GraphicsDevice.ResetProfile"/>.
/// </summary>
public readonly struct ProfileSnapshot
{
    // Flow - per-frame activity.

    /// <summary>Resources created during the last frame, by type.</summary>
    public ProfileBinGroup<AllocBin> Allocated { get; }

    /// <summary>Resources destroyed during the last frame, by type.</summary>
    public ProfileBinGroup<AllocBin> Freed { get; }

    /// <summary>Buffer data-transfer operations during the last frame.</summary>
    public ProfileBinGroup<BufferOpBin> BufferOps { get; }

    /// <summary>Swapchain events during the last frame.</summary>
    public ProfileBinGroup<SwapBin> Swaps { get; }

    // Gauge - live, resident state.

    /// <summary>Currently resident resources, by type.</summary>
    public ProfileBinGroup<AllocBin> Live { get; }

    /// <summary>Currently resident buffer memory, by usage role.</summary>
    public ProfileBinGroup<BufferRoleBin> BufferMem { get; }

    internal ProfileSnapshot(
        ProfileBinGroup<AllocBin> allocated,
        ProfileBinGroup<AllocBin> freed,
        ProfileBinGroup<BufferOpBin> bufferOps,
        ProfileBinGroup<SwapBin> swaps,
        ProfileBinGroup<AllocBin> live,
        ProfileBinGroup<BufferRoleBin> bufferMem)
    {
        Allocated = allocated;
        Freed = freed;
        BufferOps = bufferOps;
        Swaps = swaps;
        Live = live;
        BufferMem = bufferMem;
    }
}
