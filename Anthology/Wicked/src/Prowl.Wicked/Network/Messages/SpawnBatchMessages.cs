namespace Prowl.Wicked.Network.Messages;

using Prowl.Echo;

/// <summary>
/// Sent before a batch of spawn messages.
/// Allows clients to defer processing until all spawns are received.
/// This prevents issues with cross-references between entities.
/// </summary>
[FixedEchoStructure]
public class SpawnStartedMessage : INetworkMessage
{
}

/// <summary>
/// Sent after a batch of spawn messages.
/// Signals that all initial spawns have been sent and clients can process them.
/// </summary>
[FixedEchoStructure]
public class SpawnFinishedMessage : INetworkMessage
{
}
