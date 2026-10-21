namespace Prowl.Warp;

/// <summary>Controls send timing independently of delivery guarantees.</summary>
[System.Flags]
public enum SendFlags : byte
{
    None = 0,
    /// <summary>Flush queued messages without waiting for the next regular Flush. Callbacks still require Receive.</summary>
    Immediate = 1,
}
