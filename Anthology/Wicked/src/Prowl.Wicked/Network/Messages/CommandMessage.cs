namespace Prowl.Wicked.Network.Messages;

using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Sent by client to invoke a command on the server.
/// </summary>
public class CommandMessage : INetworkMessage
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
    /// The name or hash of the method to invoke.
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Serialized arguments for the method.
    /// </summary>
    public byte[] Arguments { get; set; } = Array.Empty<byte>();

    public void Serialize(NetworkWriter writer)
    {
        writer.WriteUInt(NetId);
        writer.WriteByte(BehaviourIndex);
        writer.WriteString(MethodName);
        writer.WriteBytes(Arguments);
    }

    public void Deserialize(NetworkReader reader)
    {
        NetId = reader.ReadUInt();
        BehaviourIndex = reader.ReadByte();
        MethodName = reader.ReadString() ?? string.Empty;
        Arguments = reader.ReadBytes();
    }
}
