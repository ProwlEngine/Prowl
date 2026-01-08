namespace Prowl.Wicked.Attributes;

/// <summary>
/// Marks a field as a synchronized variable.
/// SyncVars are automatically synchronized from server to all clients.
/// The IL weaver transforms the field into a property with dirty tracking.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class SyncVarAttribute : Attribute
{
    /// <summary>
    /// Optional: Name of the method to call when the value changes on clients.
    /// The method signature should be: void MethodName(T oldValue, T newValue)
    /// where T is the type of the SyncVar.
    /// </summary>
    public string? hook { get; set; }

    /// <summary>
    /// The sync slot index. Automatically assigned by the IL weaver.
    /// Do not set manually.
    /// </summary>
    internal int SlotIndex { get; set; } = -1;
}
