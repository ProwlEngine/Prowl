namespace Prowl.Wicked.Network.Messages;

using Prowl.Echo;

/// <summary>
/// Sent by client to indicate it's ready to receive game state.
/// </summary>
[FixedEchoStructure]
public class ReadyMessage : INetworkMessage
{
}
