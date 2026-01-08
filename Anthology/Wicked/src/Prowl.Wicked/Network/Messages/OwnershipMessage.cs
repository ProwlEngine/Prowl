namespace Prowl.Wicked.Network.Messages;

using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Sent by server to notify clients of ownership changes.
/// Equivalent to Mirror's ChangeOwnerMessage.
/// </summary>
public class OwnershipMessage : INetworkMessage
{
    /// <summary>
    /// The network ID of the entity.
    /// </summary>
    public uint NetId { get; set; }

    /// <summary>
    /// True if the receiving client now has authority over this entity.
    /// </summary>
    public bool IsOwner { get; set; }

    /// <summary>
    /// True if this entity is now the local player for the receiving client.
    /// </summary>
    public bool IsLocalPlayer { get; set; }

    public void Serialize(NetworkWriter writer)
    {
        writer.Write(NetId);
        // Pack both bools into single byte
        byte flags = 0;
        if (IsOwner) flags |= 1;
        if (IsLocalPlayer) flags |= 2;
        writer.Write(flags);
    }

    public void Deserialize(NetworkReader reader)
    {
        NetId = reader.ReadUInt();
        byte flags = reader.ReadByte();
        IsOwner = (flags & 1) != 0;
        IsLocalPlayer = (flags & 2) != 0;
    }
}
