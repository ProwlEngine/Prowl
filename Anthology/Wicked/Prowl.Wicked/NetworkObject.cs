namespace Prowl.Wicked;

/// <summary>
/// Abstract base class for all instance RPC-capable types: NetworkEntity and Map.
/// </summary>
public abstract class NetworkObject
{
    /// <summary>
    /// Which side of the network this instance lives on.
    /// Defaults to Unspecified — IsServer and IsClient both return false until set.
    /// </summary>
    public NetworkSide Side { get; internal set; } = NetworkSide.Unspecified;

    /// <summary>
    /// True if this instance lives on the server side.
    /// </summary>
    public bool IsServer => Side == NetworkSide.Server;

    /// <summary>
    /// True if this instance lives on the client side.
    /// </summary>
    public bool IsClient => Side == NetworkSide.Client;

    /// <summary>
    /// Identifies which client invoked the currently executing ServerRpc.
    /// Backed by [ThreadStatic] — safe for multi-threaded RPC dispatch.
    /// Set before RPC execution, cleared after.
    /// WARNING: If you use async/await inside an RPC handler, execution may resume
    /// on a different thread and Sender will be null. Capture it in a local variable
    /// before any await.
    /// </summary>
    [ThreadStatic]
    private static RemoteClient? _sender;

    public static RemoteClient? Sender
    {
        get => _sender;
        internal set => _sender = value;
    }

    // Weaver overrides these per-type with switch dispatch for RPC methods
    public virtual void __DispatchServerRpc(ushort methodId, NetworkReader reader, uint connectionId, ushort promiseId) { }
    public virtual void __DispatchClientRpc(ushort methodId, NetworkReader reader) { }
}
