using System;

namespace Prowl.Veldrid;

/// <summary>
/// Represents errors that occur in the Prowl.Veldrid library.
/// </summary>
public class VeldridException : Exception
{
    /// <summary>
    /// Constructs a new VeldridException.
    /// </summary>
    public VeldridException()
    {
    }

    /// <summary>
    /// Constructs a new VeldridException with the given message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public VeldridException(string message) : base(message)
    {
    }

    /// <summary>
    /// Constructs a new VeldridException with the given message and inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public VeldridException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
