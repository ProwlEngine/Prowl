namespace Prowl.Wicked.Attributes;

/// <summary>
/// Marks a method as a Command (client to server RPC).
/// Commands are called on clients and executed on the server.
/// The entity must be owned by the calling client.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class CommandAttribute : Attribute
{
    /// <summary>
    /// If true, the command can be called by any client, not just the owner.
    /// </summary>
    public bool RequireOwnership { get; set; } = true;

    /// <summary>
    /// Optional: Skip the authority check (use with caution).
    /// </summary>
    public bool IgnoreAuthority { get; set; } = false;
}
