namespace Prowl.Wicked.Network.Messages;

using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Sent before a batch of spawn messages.
/// Allows clients to defer processing until all spawns are received.
/// This prevents issues with cross-references between entities.
/// </summary>
public class SpawnStartedMessage : INetworkMessage
{
    public void Serialize(NetworkWriter writer)
    {
        // Empty message - just a signal
    }

    public void Deserialize(NetworkReader reader)
    {
        // Empty message - just a signal
    }
}

/// <summary>
/// Sent after a batch of spawn messages.
/// Signals that all initial spawns have been sent and clients can process them.
/// </summary>
public class SpawnFinishedMessage : INetworkMessage
{
    public void Serialize(NetworkWriter writer)
    {
        // Empty message - just a signal
    }

    public void Deserialize(NetworkReader reader)
    {
        // Empty message - just a signal
    }
}
