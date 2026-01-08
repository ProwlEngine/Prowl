namespace Prowl.Wicked.Core;

using Prowl.Wicked.Network;
using Prowl.Wicked.Network.Messages;
using Prowl.Wicked.Network.Rpc;
using Prowl.Wicked.Network.Serialization;

/// <summary>
/// Base class for all networked behaviours attached to entities.
/// Equivalent to Unity's NetworkBehaviour.
/// </summary>
public abstract class EntityBehaviour
{
    /// <summary>
    /// The index of this behaviour in the entity's behaviour list.
    /// Used for RPC routing and sync data identification.
    /// </summary>
    public byte BehaviourIndex { get; internal set; }

    /// <summary>
    /// The entity this behaviour is attached to.
    /// </summary>
    public Entity Entity { get; internal set; } = null!;

    /// <summary>
    /// Number of sync data slots per behaviour.
    /// </summary>
    public const int SyncSlotCount = 32;

    /// <summary>
    /// Sync data slots (32 slots for synchronized data per behaviour).
    /// Each behaviour has its own independent sync data.
    /// Protected - accessed by IL-weaved [SyncVar] code. Do not use directly.
    /// </summary>
    protected internal object?[] SyncData { get; } = new object?[SyncSlotCount];

    /// <summary>
    /// Bitmask indicating which sync data slots have changed (32-bit for 32 slots).
    /// Protected - managed automatically by [SyncVar] properties.
    /// </summary>
    protected internal uint DirtyMask { get; private set; }

    /// <summary>
    /// Marks a sync data slot as dirty (changed).
    /// Protected - called automatically by [SyncVar] property setters.
    /// </summary>
    protected internal void SetDirty(int index)
    {
        if (index < 0 || index >= SyncSlotCount)
            throw new ArgumentOutOfRangeException(nameof(index), $"Sync data index must be between 0 and {SyncSlotCount - 1}.");
        DirtyMask |= (1u << index);
    }

    /// <summary>
    /// Marks all sync data slots as dirty.
    /// </summary>
    protected internal void SetAllDirty() => DirtyMask = uint.MaxValue;

    /// <summary>
    /// Clears all dirty flags. Called after sync data is sent.
    /// </summary>
    protected internal void ClearDirty() => DirtyMask = 0;

    /// <summary>
    /// Returns true if any sync data slot is dirty.
    /// </summary>
    public bool IsDirty => DirtyMask != 0;

    // Convenience accessors that delegate to Entity

    /// <summary>
    /// The unique network ID of the entity.
    /// </summary>
    public uint NetId => Entity.NetId;

    /// <summary>
    /// True if this entity exists on the server.
    /// </summary>
    public bool IsServer => Entity.IsServer;

    /// <summary>
    /// True if this entity exists on a client.
    /// </summary>
    public bool IsClient => Entity.IsClient;

    /// <summary>
    /// True if running in host mode (server + client).
    /// </summary>
    public bool IsHost => Entity.IsHost;

    /// <summary>
    /// True if this entity exists only on the server.
    /// </summary>
    public bool IsServerOnly => Entity.IsServerOnly;

    /// <summary>
    /// True if this entity exists only on a client.
    /// </summary>
    public bool IsClientOnly => Entity.IsClientOnly;

    /// <summary>
    /// True if this entity represents the local player.
    /// </summary>
    public bool IsLocalPlayer => Entity.IsLocalPlayer;

    /// <summary>
    /// True if the local connection has authority over this entity.
    /// </summary>
    public bool IsOwned => Entity.IsOwned;

    /// <summary>
    /// The network connection that owns this entity.
    /// </summary>
    public NetworkConnection? Owner => Entity.Owner;

    /// <summary>
    /// The world this entity belongs to.
    /// </summary>
    public World? World => Entity.World;

    /// <summary>
    /// Applies sync data received from the network.
    /// </summary>
    internal void ApplySyncData(object?[] data)
    {
        for (int i = 0; i < 32 && i < data.Length; i++)
        {
            SyncData[i] = data[i];
        }
    }

    /// <summary>
    /// Applies delta sync data received from the network.
    /// </summary>
    internal void ApplyDeltaSyncData(uint mask, object?[] data)
    {
        int dataIndex = 0;
        for (int i = 0; i < SyncSlotCount; i++)
        {
            if ((mask & (1u << i)) != 0)
            {
                if (dataIndex < data.Length)
                {
                    SyncData[i] = data[dataIndex++];
                }
            }
        }
    }

    // Lifecycle callbacks - override these in derived classes

    /// <summary>
    /// Called when the behaviour is created (before Start).
    /// </summary>
    public virtual void OnAwake() { }

    /// <summary>
    /// Called once when the entity starts (after Awake, before first Update).
    /// </summary>
    public virtual void OnStart() { }

    /// <summary>
    /// Called every frame.
    /// </summary>
    public virtual void OnUpdate() { }

    /// <summary>
    /// Called at fixed time intervals (for physics).
    /// </summary>
    public virtual void OnFixedUpdate() { }

    /// <summary>
    /// Called after all Update calls.
    /// </summary>
    public virtual void OnLateUpdate() { }

    /// <summary>
    /// Called when the entity is destroyed.
    /// </summary>
    public virtual void OnDestroy() { }

    // Network lifecycle callbacks

    /// <summary>
    /// Called on the server when the entity is spawned.
    /// Called AFTER sync data is initialized.
    /// </summary>
    public virtual void OnStartServer() { }

    /// <summary>
    /// Called on the server when the entity is despawned.
    /// </summary>
    public virtual void OnStopServer() { }

    /// <summary>
    /// Called on clients when the entity is spawned.
    /// Called AFTER sync data is received and applied.
    /// </summary>
    public virtual void OnStartClient() { }

    /// <summary>
    /// Called on clients when the entity is despawned.
    /// </summary>
    public virtual void OnStopClient() { }

    /// <summary>
    /// Called on the owning client when this entity becomes the local player.
    /// Called AFTER sync data is received and applied.
    /// </summary>
    public virtual void OnStartLocalPlayer() { }

    /// <summary>
    /// Called on the owning client when this entity stops being the local player.
    /// </summary>
    public virtual void OnStopLocalPlayer() { }

    /// <summary>
    /// Called when this client gains authority over the entity.
    /// </summary>
    public virtual void OnStartAuthority() { }

    /// <summary>
    /// Called when this client loses authority over the entity.
    /// </summary>
    public virtual void OnStopAuthority() { }

    /// <summary>
    /// Called when ownership of this entity is transferred.
    /// </summary>
    /// <param name="previousOwner">The previous owner (null if was server-owned).</param>
    public virtual void OnOwnershipTransferred(NetworkConnection? previousOwner) { }

    // Component access helpers

    /// <summary>
    /// Gets another behaviour of the specified type from the same entity.
    /// </summary>
    public T? GetBehaviour<T>() where T : EntityBehaviour
    {
        return Entity.GetBehaviour<T>();
    }

    /// <summary>
    /// Gets all behaviours of the specified type from the same entity.
    /// </summary>
    public IEnumerable<T> GetBehaviours<T>() where T : EntityBehaviour
    {
        return Entity.GetBehaviours<T>();
    }

    // RPC sending methods (protected - called by IL-weaved code in derived classes)

    /// <summary>
    /// Sends a Command (client to server RPC).
    /// Called by IL-weaved code. Not intended for direct use.
    /// </summary>
    protected void SendCommand(string methodName, params object?[] args)
    {
        if (!IsClient)
        {
            Console.WriteLine($"EntityBehaviour.SendCommand: Not a client, cannot send command {methodName}");
            return;
        }

        var writer = new NetworkWriter();
        Console.WriteLine($"SendCommand: Writing {args.Length} args for {methodName}");
        foreach (var arg in args)
        {
            Console.WriteLine($"  Writing arg: {arg} (type: {arg?.GetType().Name ?? "null"})");
            writer.WriteTypedValue(arg);
        }
        Console.WriteLine($"  Total bytes: {writer.Position}");

        // In host mode, invoke command locally instead of sending through transport
        if (NetworkManager.IsHost)
        {
            var reader = new NetworkReader(writer.ToArray());
            Console.WriteLine($"SendCommand: Host mode, invoking locally. Reader has {reader.Length} bytes");
            InvokeCommand(methodName, reader, NetworkServer.LocalConnection!);
            return;
        }

        var message = new CommandMessage
        {
            NetId = NetId,
            BehaviourIndex = BehaviourIndex,
            MethodName = methodName,
            Arguments = writer.ToArray()
        };

        NetworkClient.Send(message);
    }

    /// <summary>
    /// Sends a ClientRpc (server to all clients RPC).
    /// Called by IL-weaved code. Not intended for direct use.
    /// </summary>
    protected void SendClientRpc(string methodName, bool includeHost, params object?[] args)
    {
        if (!IsServer)
        {
            Console.WriteLine($"EntityBehaviour.SendClientRpc: Not a server, cannot send RPC {methodName}");
            return;
        }

        var writer = new NetworkWriter();
        foreach (var arg in args)
        {
            writer.WriteTypedValue(arg);
        }

        var message = new RpcMessage
        {
            NetId = NetId,
            BehaviourIndex = BehaviourIndex,
            MethodName = methodName,
            Arguments = writer.ToArray()
        };

        NetworkServer.SendToReady(message);

        // If host mode and includeHost, also invoke locally
        if (includeHost && IsHost)
        {
            InvokeRpcLocally(methodName, args);
        }
    }

    /// <summary>
    /// Sends a TargetRpc (server to specific client RPC).
    /// Called by IL-weaved code. Not intended for direct use.
    /// </summary>
    protected void SendTargetRpc(NetworkConnection target, string methodName, params object?[] args)
    {
        if (!IsServer)
        {
            Console.WriteLine($"EntityBehaviour.SendTargetRpc: Not a server, cannot send RPC {methodName}");
            return;
        }

        if (target == null)
        {
            Console.WriteLine($"EntityBehaviour.SendTargetRpc: Target connection is null for {methodName}");
            return;
        }

        var writer = new NetworkWriter();
        foreach (var arg in args)
        {
            writer.WriteTypedValue(arg);
        }

        var message = new RpcMessage
        {
            NetId = NetId,
            BehaviourIndex = BehaviourIndex,
            MethodName = methodName,
            Arguments = writer.ToArray()
        };

        NetworkServer.Send(target, message);

        // If target is the local connection (host mode), invoke locally
        if (target == NetworkServer.LocalConnection)
        {
            InvokeRpcLocally(methodName, args);
        }
    }

    /// <summary>
    /// Invokes an RPC method locally (for host mode).
    /// </summary>
    private void InvokeRpcLocally(string methodName, object?[] args)
    {
        // Create a reader from the serialized args
        var writer = new NetworkWriter();
        foreach (var arg in args)
        {
            writer.WriteTypedValue(arg);
        }

        var reader = new NetworkReader(writer.ToArray());
        RpcRegistry.Invoke(this, methodName, reader);
    }

    /// <summary>
    /// Invokes an RPC method with the given reader.
    /// Called by the network system when a message is received.
    /// </summary>
    internal void InvokeRpc(string methodName, NetworkReader reader)
    {
        if (!RpcRegistry.Invoke(this, methodName, reader))
        {
            Console.WriteLine($"EntityBehaviour.InvokeRpc: No handler found for {GetType().Name}.{methodName}");
        }
    }

    /// <summary>
    /// Invokes a Command method on the server.
    /// Called by the network system when a command message is received.
    /// </summary>
    internal void InvokeCommand(string methodName, NetworkReader reader, NetworkConnection sender)
    {
        if (!RpcRegistry.Invoke(this, methodName, reader))
        {
            Console.WriteLine($"EntityBehaviour.InvokeCommand: No handler found for {GetType().Name}.{methodName}");
        }
    }
}
