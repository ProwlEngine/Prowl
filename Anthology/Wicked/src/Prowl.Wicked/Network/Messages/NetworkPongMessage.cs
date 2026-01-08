namespace Prowl.Wicked.Network.Messages;

/// <summary>
/// Response to NetworkPingMessage.
/// Used to calculate RTT and prediction error.
/// </summary>
public struct NetworkPongMessage : INetworkMessage
{
    /// <summary>
    /// The local time from the original ping message.
    /// Used to calculate round trip time.
    /// </summary>
    public double LocalTime;

    /// <summary>
    /// Prediction error (unadjusted) - the offset the client needs to apply.
    /// </summary>
    public double PredictionErrorUnadjusted;

    /// <summary>
    /// Prediction error (adjusted) - for debug purposes.
    /// </summary>
    public double PredictionErrorAdjusted;

    public NetworkPongMessage(double localTime, double predictionErrorUnadjusted, double predictionErrorAdjusted)
    {
        LocalTime = localTime;
        PredictionErrorUnadjusted = predictionErrorUnadjusted;
        PredictionErrorAdjusted = predictionErrorAdjusted;
    }

    public void Serialize(Serialization.NetworkWriter writer)
    {
        writer.WriteDouble(LocalTime);
        writer.WriteDouble(PredictionErrorUnadjusted);
        writer.WriteDouble(PredictionErrorAdjusted);
    }

    public void Deserialize(Serialization.NetworkReader reader)
    {
        LocalTime = reader.ReadDouble();
        PredictionErrorUnadjusted = reader.ReadDouble();
        PredictionErrorAdjusted = reader.ReadDouble();
    }
}
