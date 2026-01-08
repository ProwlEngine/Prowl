namespace Prowl.Wicked.Network.Messages;

using Prowl.Echo;

/// <summary>
/// Sent by server to despawn an entity on clients.
/// </summary>
[FixedEchoStructure]
public class DespawnMessage : INetworkMessage
{
    /// <summary>
    /// The network ID of the entity to despawn.
    /// </summary>
    public uint NetId;
}
