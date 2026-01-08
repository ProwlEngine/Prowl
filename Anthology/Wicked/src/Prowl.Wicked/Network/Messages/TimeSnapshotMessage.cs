namespace Prowl.Wicked.Network.Messages;

using Prowl.Echo;

/// <summary>
/// Sent by the server to clients for time synchronization.
/// Used by snapshot interpolation to drive the client timeline.
/// </summary>
[FixedEchoStructure]
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
}
