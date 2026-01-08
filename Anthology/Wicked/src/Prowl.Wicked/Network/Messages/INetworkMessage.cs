namespace Prowl.Wicked.Network.Messages;

using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Interface for all network messages.
/// </summary>
public interface INetworkMessage
{
    /// <summary>
    /// Serializes the message to a NetworkWriter.
    /// </summary>
    void Serialize(NetworkWriter writer);

    /// <summary>
    /// Deserializes the message from a NetworkReader.
    /// </summary>
    void Deserialize(NetworkReader reader);
}
