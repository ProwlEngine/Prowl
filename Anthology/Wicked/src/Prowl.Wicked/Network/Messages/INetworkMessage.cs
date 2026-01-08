namespace Prowl.Wicked.Network.Messages;

/// <summary>
/// Marker interface for all network messages.
/// Messages are serialized using Prowl.Echo - just define public fields/properties.
/// Use [FixedEchoStructure] attribute on message classes for better performance.
/// </summary>
public interface INetworkMessage
{
}
