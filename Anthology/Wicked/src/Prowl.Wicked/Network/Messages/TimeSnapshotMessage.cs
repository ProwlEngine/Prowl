namespace Prowl.Wicked.Network.Messages;

/// <summary>
/// Sent by the server to clients for time synchronization.
/// Used by snapshot interpolation to drive the client timeline.
/// </summary>
public struct TimeSnapshotMessage : INetworkMessage
{
    /// <summary>
    /// The server's time when this message was sent.
    /// </summary>
    public double ServerTime;

    public TimeSnapshotMessage(double serverTime)
    {
        ServerTime = serverTime;
    }

    public void Serialize(Serialization.NetworkWriter writer)
    {
        writer.WriteDouble(ServerTime);
    }

    public void Deserialize(Serialization.NetworkReader reader)
    {
        ServerTime = reader.ReadDouble();
    }
}
