namespace Prowl.Wicked.Attributes;

/// <summary>
/// Marks a method as a ClientRpc (server to all clients RPC).
/// ClientRpcs are called on the server and executed on all clients.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ClientRpcAttribute : Attribute
{
    /// <summary>
    /// If true, the RPC is only sent to ready clients.
    /// </summary>
    public bool RequireReady { get; set; } = true;

    /// <summary>
    /// If true, the RPC is also executed on the host's local client.
    /// </summary>
    public bool IncludeHost { get; set; } = true;
}
