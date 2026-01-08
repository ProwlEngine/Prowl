namespace Prowl.Wicked.Network.Messages;

using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Sent by server to spawn an entity on clients.
/// </summary>
public class SpawnMessage : INetworkMessage
{
    /// <summary>
    /// The network ID of the entity.
    /// </summary>
    public uint NetId { get; set; }

    /// <summary>
    /// The connection ID of the owner (-1 for server-owned).
    /// </summary>
    public int OwnerId { get; set; } = -1;

    /// <summary>
    /// True if this entity is the local player for the receiving client.
    /// </summary>
    public bool IsLocalPlayer { get; set; }

    /// <summary>
    /// Behaviour type indices from BehaviourRegistry.
    /// </summary>
    public byte[] BehaviourIndices { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Initial sync data for each behaviour (array of arrays).
    /// BehaviourSyncData[i] = sync data array for behaviour at BehaviourIndices[i].
    /// </summary>
    public object?[][] BehaviourSyncData { get; set; } = Array.Empty<object?[]>();

    public void Serialize(NetworkWriter writer)
    {
        writer.WriteUInt(NetId);
        writer.WriteInt(OwnerId);
        writer.WriteBool(IsLocalPlayer);

        // Write behaviour indices
        writer.WriteByte((byte)BehaviourIndices.Length);
        foreach (var index in BehaviourIndices)
        {
            writer.WriteByte(index);
        }

        // Write sync data for each behaviour
        // Note: BehaviourSyncData.Length should match BehaviourIndices.Length
        foreach (var syncData in BehaviourSyncData)
        {
            // Write sync data count for this behaviour
            writer.WriteByte((byte)(syncData?.Length ?? 0));
            if (syncData != null)
            {
                foreach (var data in syncData)
                {
                    writer.WriteTypedValue(data);
                }
            }
        }
    }

    public void Deserialize(NetworkReader reader)
    {
        NetId = reader.ReadUInt();
        OwnerId = reader.ReadInt();
        IsLocalPlayer = reader.ReadBool();

        // Read behaviour indices
        var behaviourCount = reader.ReadByte();
        BehaviourIndices = new byte[behaviourCount];
        for (int i = 0; i < behaviourCount; i++)
        {
            BehaviourIndices[i] = reader.ReadByte();
        }

        // Read sync data for each behaviour
        BehaviourSyncData = new object?[behaviourCount][];
        for (int i = 0; i < behaviourCount; i++)
        {
            var itemCount = reader.ReadByte();
            BehaviourSyncData[i] = new object?[itemCount];
            for (int j = 0; j < itemCount; j++)
            {
                BehaviourSyncData[i][j] = reader.ReadTypedValue();
            }
        }
    }
}
