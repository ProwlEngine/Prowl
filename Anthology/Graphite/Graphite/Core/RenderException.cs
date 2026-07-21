using System;

namespace Prowl.Graphite;

/// <summary>
/// Error from Prowl.Graphite.
/// </summary>
public class RenderException : Exception
{
    /// <summary>
    /// New RenderException.
    /// </summary>
    public RenderException()
    {
    }

    /// <summary>
    /// New RenderException with a message.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public RenderException(string message) : base(message)
    {
    }

    /// <summary>
    /// New RenderException with a message and inner exception.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">Inner exception.</param>
    public RenderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
