namespace Prowl.Wicked.Network.Messages;

using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Sent by server to despawn an entity on clients.
/// </summary>
public class DespawnMessage : INetworkMessage
{
    /// <summary>
    /// The network ID of the entity to despawn.
    /// </summary>
    public uint NetId { get; set; }

    public void Serialize(NetworkWriter writer)
    {
        writer.WriteUInt(NetId);
    }

    public void Deserialize(NetworkReader reader)
    {
        NetId = reader.ReadUInt();
    }
}
