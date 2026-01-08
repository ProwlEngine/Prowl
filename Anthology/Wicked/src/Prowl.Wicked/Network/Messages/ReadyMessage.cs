namespace Prowl.Wicked.Network.Messages;

using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Sent by client to indicate it's ready to receive game state.
/// </summary>
public class ReadyMessage : INetworkMessage
{
    public void Serialize(NetworkWriter writer)
    {
        // No data needed
    }

    public void Deserialize(NetworkReader reader)
    {
        // No data needed
    }
}
