namespace Prowl.Wicked.Network.Messages;

using Prowl.Echo;

/// <summary>
/// Sent by client to invoke a command on the server.
/// </summary>
[FixedEchoStructure]
public class CommandMessage : INetworkMessage
{
    /// <summary>
    /// The network ID of the entity.
    /// </summary>
    public uint NetId;

    /// <summary>
    /// The index of the behaviour on the entity.
    /// </summary>
    public byte BehaviourIndex;

    /// <summary>
    /// The 16-bit hash of the function name.
    /// Computed as FunctionHash.ComputeHash(behaviourType, methodName).
    /// </summary>
    public ushort FunctionHash;

    /// <summary>
    /// Serialized arguments for the method (Echo-serialized object[]).
    /// </summary>
    public byte[] Arguments = Array.Empty<byte>();
}
