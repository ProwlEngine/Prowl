namespace Prowl.Wicked.Attributes;

/// <summary>
/// Marks a method as client-only.
/// The method will only execute on clients.
/// On the server, the method returns early without doing anything.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ClientAttribute : Attribute
{
    /// <summary>
    /// If true, logs a warning when called on the server.
    /// </summary>
    public bool Warn { get; set; } = false;
}
