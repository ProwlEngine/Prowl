namespace Prowl.Motion;

/// <summary>Thrown when a graph cannot be instantiated, carrying the offending node index.</summary>
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
