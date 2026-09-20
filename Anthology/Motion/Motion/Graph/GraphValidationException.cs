namespace Prowl.Motion;

/// <summary>
/// Thrown when a graph cannot be instantiated because a node references a bad child index or the
/// wrong kind of node (e.g. a value pin wired to a pose node). Carries the offending node index so
/// an editor can highlight it.
/// </summary>
public sealed class GraphValidationException : Exception
{
    public GraphValidationException(int nodeIndex, string? nodeName, string message, Exception? inner = null)
        : base($"Graph node {nodeIndex}{(string.IsNullOrEmpty(nodeName) ? "" : $" ('{nodeName}')")}: {message}", inner)
    {
        NodeIndex = nodeIndex;
        NodeName = nodeName;
    }

    public int NodeIndex { get; }
    public string? NodeName { get; }
}
