namespace Prowl.Wicked.Network.Messages;

using Prowl.Echo;

/// <summary>
/// Sent by server to notify clients of ownership changes.
/// Equivalent to Mirror's ChangeOwnerMessage.
/// </summary>
[FixedEchoStructure]
public class OwnershipMessage : INetworkMessage
{
    /// <summary>
    /// The network ID of the entity.
    /// </summary>
    public uint NetId;

    /// <summary>
    /// True if the receiving client now has authority over this entity.
    /// </summary>
    public bool IsOwner;

    /// <summary>
    /// True if this entity is now the local player for the receiving client.
    /// </summary>
    public bool IsLocalPlayer;
}
