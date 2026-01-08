namespace Prowl.Wicked.Network.Messages;

/// <summary>
/// Sent to measure RTT (round trip time).
/// The receiver responds with a NetworkPongMessage.
/// </summary>
public struct NetworkPingMessage : INetworkMessage
{
    /// <summary>
    /// Local time when ping was sent.
    /// Used to calculate round trip time.
    /// </summary>
    public double LocalTime;

    /// <summary>
    /// Predicted time (adjusted) sent to compare final error, for debugging.
    /// </summary>
    public double PredictedTimeAdjusted;

    public NetworkPingMessage(double localTime, double predictedTimeAdjusted)
    {
        LocalTime = localTime;
        PredictedTimeAdjusted = predictedTimeAdjusted;
    }

    public void Serialize(Serialization.NetworkWriter writer)
    {
        writer.WriteDouble(LocalTime);
        writer.WriteDouble(PredictedTimeAdjusted);
    }

    public void Deserialize(Serialization.NetworkReader reader)
    {
        LocalTime = reader.ReadDouble();
        PredictedTimeAdjusted = reader.ReadDouble();
    }
}
