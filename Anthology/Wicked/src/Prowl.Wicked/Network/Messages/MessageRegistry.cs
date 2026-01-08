namespace Prowl.Wicked.Network.Messages;

/// <summary>
/// Registry for mapping message types to IDs.
/// </summary>
public static class MessageRegistry
{
    private static readonly Dictionary<Type, ushort> _typeToId = new();
    private static readonly Dictionary<ushort, Type> _idToType = new();
    private static ushort _nextId = 1;

    static MessageRegistry()
    {
        // Register built-in messages
        Register<ReadyMessage>();
        Register<SpawnMessage>();
        Register<DespawnMessage>();
        Register<SyncDataMessage>();
        Register<RpcMessage>();
        Register<CommandMessage>();
        Register<OwnershipMessage>();
    }

    /// <summary>
    /// Registers a message type.
    /// </summary>
    public static void Register<T>() where T : INetworkMessage
    {
        var type = typeof(T);
        if (_typeToId.ContainsKey(type))
            return;

        var id = _nextId++;
        _typeToId[type] = id;
        _idToType[id] = type;
    }

    /// <summary>
    /// Gets the message ID for a type.
    /// </summary>
    public static ushort GetMessageId<T>() where T : INetworkMessage
    {
        return _typeToId.TryGetValue(typeof(T), out var id) ? id : (ushort)0;
    }

    /// <summary>
    /// Gets the message ID for a type.
    /// </summary>
    public static ushort GetMessageId(Type type)
    {
        return _typeToId.TryGetValue(type, out var id) ? id : (ushort)0;
    }

    /// <summary>
    /// Gets the message type for an ID.
    /// </summary>
    public static Type? GetMessageType(ushort id)
    {
        return _idToType.TryGetValue(id, out var type) ? type : null;
    }
}
