using System;

namespace Prowl.Graphite;

/// <summary>
/// Represents errors that occur in the Prowl.Graphite library.
/// </summary>
public class RenderException : Exception
{
    /// <summary>
    /// Constructs a new RenderException.
    /// </summary>
    public RenderException()
    {
    }

    /// <summary>
    /// Constructs a new RenderException with the given message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public RenderException(string message) : base(message)
    {
    }

    /// <summary>
    /// Constructs a new RenderException with the given message and inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public RenderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
