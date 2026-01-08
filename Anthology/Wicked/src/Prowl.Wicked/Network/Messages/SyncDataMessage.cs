namespace Prowl.Wicked.Network.Messages;

using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Sent by server to sync entity behaviour data to clients.
/// Each behaviour's sync data is sent separately.
/// </summary>
public class SyncDataMessage : INetworkMessage
{
    /// <summary>
    /// The network ID of the entity.
    /// </summary>
    public uint NetId { get; set; }

    /// <summary>
    /// The index of the behaviour on the entity.
    /// </summary>
    public byte BehaviourIndex { get; set; }

    /// <summary>
    /// Bitmask indicating which sync data slots are included (32-bit for 32 slots).
    /// </summary>
    public uint DirtyMask { get; set; }

    /// <summary>
    /// The changed sync data values (only dirty slots, in order).
    /// </summary>
    public object?[] SyncData { get; set; } = Array.Empty<object?>();

    public void Serialize(NetworkWriter writer)
    {
        writer.WriteUInt(NetId);
        writer.WriteByte(BehaviourIndex);
        writer.WriteUInt(DirtyMask);

        // Write only the dirty data values
        foreach (var data in SyncData)
        {
            writer.WriteTypedValue(data);
        }
    }

    public void Deserialize(NetworkReader reader)
    {
        NetId = reader.ReadUInt();
        BehaviourIndex = reader.ReadByte();
        DirtyMask = reader.ReadUInt();

        // Count the number of set bits to know how many values to read
        int count = BitCount(DirtyMask);
        SyncData = new object?[count];
        for (int i = 0; i < count; i++)
        {
            SyncData[i] = reader.ReadTypedValue();
        }
    }

    private static int BitCount(uint value)
    {
        int count = 0;
        while (value != 0)
        {
            count += (int)(value & 1);
            value >>= 1;
        }
        return count;
    }
}
