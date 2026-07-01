// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;


namespace Prowl.Slang;


/// <summary>
/// The exception that is thrown when a <see cref="GlobalSession"/> encounters errors while initalizing internal variables.
/// </summary>
public class InitializationException : Exception
{
    /// <summary>
    /// Creates a new <see cref="InitializationException"/>
    /// </summary>
    public InitializationException() { }

    /// <summary>
    /// Creates a new <see cref="InitializationException"/> with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    internal InitializationException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="InitializationException"/> with a message and an inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="inner">The inner exception.</param>
    internal InitializationException(string message, Exception inner) : base(message, inner) { }
}
