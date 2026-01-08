namespace Prowl.Wicked.Network.Messages;

using Prowl.Echo;

/// <summary>
/// Sent by server to spawn an entity on clients.
/// </summary>
[FixedEchoStructure]
public class SpawnMessage : INetworkMessage
{
    /// <summary>
    /// The network ID of the entity.
    /// </summary>
    public uint NetId;

    /// <summary>
    /// The connection ID of the owner (-1 for server-owned).
    /// </summary>
    public int OwnerId = -1;

    /// <summary>
    /// True if the receiving client owns this entity (has authority).
    /// Server computes this directly rather than client deriving from OwnerId.
    /// </summary>
    public bool IsOwner;

    /// <summary>
    /// True if this entity is the local player for the receiving client.
    /// </summary>
    public bool IsLocalPlayer;

    /// <summary>
    /// Behaviour type indices from BehaviourRegistry.
    /// </summary>
    public byte[] BehaviourIndices = Array.Empty<byte>();

    /// <summary>
    /// Initial sync data for each behaviour (array of arrays).
    /// BehaviourSyncData[i] = sync data array for behaviour at BehaviourIndices[i].
    /// Echo serializes this automatically including type info.
    /// </summary>
    public object?[][] BehaviourSyncData = Array.Empty<object?[]>();
}
