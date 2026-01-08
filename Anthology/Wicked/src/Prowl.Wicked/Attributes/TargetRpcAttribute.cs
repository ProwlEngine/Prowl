namespace Prowl.Wicked.Attributes;

using Prowl.Wicked.Network;

/// <summary>
/// Marks a method as a TargetRpc (server to specific client RPC).
/// TargetRpcs are called on the server and executed on a specific client.
/// The first parameter should be NetworkConnection specifying the target.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class TargetRpcAttribute : Attribute
{
    /// <summary>
    /// If true, the RPC is only sent if the target connection is ready.
    /// </summary>
    public bool RequireReady { get; set; } = true;
}
