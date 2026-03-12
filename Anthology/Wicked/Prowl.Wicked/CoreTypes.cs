namespace Prowl.Wicked;

/// <summary>
/// Specifies which clients receive a ClientRpc.
/// </summary>
public enum RpcTarget
{
    /// <summary>
    /// All clients observing the entity's map.
    /// </summary>
    Observers,

    /// <summary>
    /// Only the entity's owner. Invalid on Map (throws at runtime).
    /// </summary>
    Owner,

    /// <summary>
    /// A specific client. The first method parameter must be a RemoteClient,
    /// used server-side for routing and null on the client (not serialized).
    /// </summary>
    Player
}

/// <summary>
/// Identifies whether a NetworkObject instance lives on the server or client.
/// Defaults to Unspecified — IsServer and IsClient both return false until
/// the networking system explicitly assigns a side, preventing stale reads
/// during construction.
/// </summary>
public enum NetworkSide
{
    /// <summary>Not yet assigned. IsServer and IsClient both return false.</summary>
    Unspecified = 0,

    /// <summary>This instance lives on the server.</summary>
    Server,

    /// <summary>This instance lives on the client.</summary>
    Client
}

/// <summary>
/// Client-side transport interface. Maintains a single connection to a server.
/// All transports must use length-prefixed framing: each message is preceded by
/// a 4-byte little-endian int32 indicating the payload size in bytes.
/// </summary>
public interface IClientTransport
{
    /// <summary>Initiates a connection to a server at the specified host and port.</summary>
    void Connect(string host, int port);

    /// <summary>Disconnects from the server.</summary>
    void Disconnect();

    /// <summary>Sends data to the server.</summary>
    void Send(ArraySegment<byte> data);

    /// <summary>Processes pending network I/O. Fires events for state changes and data.</summary>
    void Tick();

    /// <summary>Fires when the connection to the server is established.</summary>
    event Action OnConnected;

    /// <summary>Fires when the connection is lost or closed. Parameter: reason (null if unknown).</summary>
    event Action<string?> OnDisconnected;

    /// <summary>Fires when data is received from the server.</summary>
    event Action<ArraySegment<byte>> OnDataReceived;
}

/// <summary>
/// Server-side transport interface. Manages multiple client connections.
/// All transports must use length-prefixed framing: each message is preceded by
/// a 4-byte little-endian int32 indicating the payload size in bytes.
/// </summary>
public interface IServerTransport
{
    /// <summary>Begins listening for incoming connections on the specified port.</summary>
    void Listen(int port);

    /// <summary>Stops listening and closes all connections.</summary>
    void Stop();

    /// <summary>Sends data to a specific connected client.</summary>
    void Send(uint connectionId, ArraySegment<byte> data);

    /// <summary>Forcefully disconnects a specific client with an optional reason.</summary>
    void Disconnect(uint connectionId, string? reason = null);

    /// <summary>Processes pending network I/O. Fires events for connections, disconnections, and data.</summary>
    void Tick();

    /// <summary>Fires when a new client connects. Parameter is the connectionId.</summary>
    event Action<uint> OnClientConnected;

    /// <summary>Fires when a client disconnects. Parameters: connectionId, reason (null if unknown).</summary>
    event Action<uint, string?> OnClientDisconnected;

    /// <summary>Fires when data is received from a client. Parameters: connectionId, data.</summary>
    event Action<uint, ArraySegment<byte>> OnDataReceived;
}

/// <summary>
/// Contract for custom types that can be serialized over the network.
/// Implement on any type used as an RPC parameter or return value.
/// </summary>
public interface INetworkSerializable
{
    /// <summary>Writes this object's data to the writer.</summary>
    void Serialize(NetworkWriter writer);

    /// <summary>Reads this object's data from the reader.</summary>
    void Deserialize(NetworkReader reader);
}

/// <summary>
/// Marks a static method as a static RPC (server-to-client).
/// The first parameter must be RemoteClient or RemoteClient[] to specify the target(s).
/// That parameter is not serialized — it is null on the client side.
/// The IL weaver intercepts calls on the server and routes them to targeted clients.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class StaticRpcAttribute : Attribute
{
}

/// <summary>
/// Marks a static method as a static command (client-to-server).
/// The Sender property on NetworkObject is set during execution.
/// The IL weaver intercepts calls on the client and routes them to the server.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class StaticCommandAttribute : Attribute
{
}

/// <summary>
/// Marks a method as a map RPC (server-to-client) on a Map.
/// The IL weaver intercepts calls on the server and routes them to targeted clients.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class MapRpcAttribute : Attribute
{
    /// <summary>
    /// Determines which clients receive the RPC. Default is Observers.
    /// Valid values: Observers, Player.
    /// </summary>
    public RpcTarget Target { get; set; } = RpcTarget.Observers;
}

/// <summary>
/// Marks a method as an entity RPC (server-to-client) on a NetworkEntity.
/// The IL weaver intercepts calls on the server and routes them to targeted clients.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EntityRpcAttribute : Attribute
{
    /// <summary>
    /// Determines which clients receive the RPC. Default is Observers.
    /// </summary>
    public RpcTarget Target { get; set; } = RpcTarget.Observers;

    /// <summary>
    /// When true, excludes the entity's owner from Observer broadcasts.
    /// Only valid with RpcTarget.Observers.
    /// </summary>
    public bool ExcludeOwner { get; set; } = false;
}

/// <summary>
/// Marks a method as an entity command (client-to-server) on a NetworkEntity.
/// The IL weaver intercepts calls on the client and routes them to the server.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EntityCommandAttribute : Attribute
{
    /// <summary>
    /// When true (default), only the entity's owner can call this command.
    /// Set to false for "query" commands any observer can call.
    /// </summary>
    public bool RequireOwner { get; set; } = true;
}