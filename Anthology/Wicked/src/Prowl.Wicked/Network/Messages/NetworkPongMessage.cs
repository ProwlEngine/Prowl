namespace Prowl.Wicked.Network.Messages;

using Prowl.Echo;

/// <summary>
/// Response to NetworkPingMessage.
/// Used to calculate RTT and prediction error.
/// </summary>
[FixedEchoStructure]
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
}
