namespace Prowl.Wicked.Network.Messages;

using Prowl.Echo;

/// <summary>
/// Sent by server to sync entity behaviour data to clients.
/// Each behaviour's sync data is sent separately.
/// </summary>
[FixedEchoStructure]
public class SyncDataMessage : INetworkMessage
{
    /// <summary>
    /// The network ID of the entity.
    /// </summary>
    public uint NetId;

    /// <summary>
    /// The index of the behaviour on the entity.
    /// </summary>
    public byte BehaviourIndex;

    /// <summary>
    /// Bitmask indicating which sync data slots are included (32-bit for 32 slots).
    /// </summary>
    public uint DirtyMask;

    /// <summary>
    /// The changed sync data values (only dirty slots, in order).
    /// Echo serializes this automatically including type info.
    /// </summary>
    public object?[] SyncData = Array.Empty<object?>();
}
