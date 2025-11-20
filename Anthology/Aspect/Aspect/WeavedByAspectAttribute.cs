namespace Aspect;

/// <summary>
/// Marker attribute added to assemblies that have been processed by the Aspect weaver.
/// This prevents double-weaving of the same assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class WeavedByAspectAttribute : Attribute
{
    /// <summary>
    /// Version of the Aspect weaver that processed this assembly.
    /// </summary>
    public string Version { get; }

    public WeavedByAspectAttribute(string version)
    {
        Version = version;
    }
}
