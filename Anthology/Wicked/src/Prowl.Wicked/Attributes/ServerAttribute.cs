namespace Prowl.Wicked.Attributes;

/// <summary>
/// Marks a method as server-only.
/// The method will only execute on the server.
/// On clients, the method returns early without doing anything.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ServerAttribute : Attribute
{
    /// <summary>
    /// If true, logs a warning when called on a client.
    /// </summary>
    public bool Warn { get; set; } = false;
}
