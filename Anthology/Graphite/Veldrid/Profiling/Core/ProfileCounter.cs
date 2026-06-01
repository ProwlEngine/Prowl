namespace Prowl.Veldrid;

/// <summary>
/// A single profiling counter value. Whether it represents per-frame activity (a flow)
/// or currently resident state (a gauge) is determined by which <see cref="ProfileSnapshot"/>
/// accessor it came from.
/// </summary>
public readonly struct ProfileCounter
{
    /// <summary>Number of events, or live object count for a gauge.</summary>
    public readonly long Count;

    /// <summary>Bytes moved this frame, or resident bytes for a gauge.</summary>
    public readonly long Bytes;

    internal ProfileCounter(long count, long bytes)
    {
        Count = count;
        Bytes = bytes;
    }
}
