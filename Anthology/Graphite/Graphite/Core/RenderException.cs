using System;

namespace Prowl.Graphite;

/// <summary>A frame graph that cannot be built or run, naming the pass that broke it when one did.</summary>
public sealed class RenderGraphException(string message, string? pass) : InvalidOperationException(message)
{
    public string? Pass { get; } = pass;
}

/// <summary>Prowl.Graphite error.</summary>
public class RenderException : Exception
{
    /// <summary>Creates a new RenderException.</summary>
    public RenderException()
    {
    }

    /// <summary>Creates a new RenderException with a message.</summary>
    /// <param name="message">Error message.</param>
    public RenderException(string message) : base(message)
    {
    }

    /// <summary>Creates a new RenderException with a message and inner exception.</summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public RenderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
