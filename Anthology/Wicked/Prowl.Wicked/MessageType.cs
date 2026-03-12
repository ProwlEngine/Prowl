namespace Prowl.Wicked;

/// <summary>
/// Wire protocol message type constants.
/// </summary>
internal static class MessageType
{
    // RPC messages
    internal const byte RpcCall = 0x01;
    internal const byte RpcResponse = 0x02;
    internal const byte RpcError = 0x03;
    internal const byte ClientRpcCall = 0x04;

    // Connection
    internal const byte AssignClientId = 0x05;

    // Player entity assignment
    internal const byte PlayerEntityAssign = 0x06;
    internal const byte PlayerEntityUnassign = 0x07;

    // Entity replication
    internal const byte EntitySpawn = 0x10;
    internal const byte EntityDespawn = 0x11;
    internal const byte OwnerChange = 0x13;

    // Map replication
    internal const byte MapCreate = 0x20;
    internal const byte MapDestroy = 0x21;

    // Latency measurement
    internal const byte Ping = 0x30;
    internal const byte Pong = 0x31;

    // Authentication
    internal const byte Authenticate = 0x32;
    internal const byte AuthResult   = 0x33;
}