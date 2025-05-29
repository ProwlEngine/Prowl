// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;


namespace Prowl.Slang;


/// <summary>
/// The exception that is thrown when a <see cref="ComponentType"/> is used on a non-parent <see cref="Session"/>.
/// </summary>
public class InvalidComponentException : Exception
{
    /// <summary>
    /// Creates a new <see cref="InvalidComponentException"/>
    /// </summary>
    public InvalidComponentException() { }

    /// <summary>
    /// Creates a new <see cref="InvalidComponentException"/> with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public InvalidComponentException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="InvalidComponentException"/> with a message and an inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="inner">The inner exception.</param>
    public InvalidComponentException(string message, Exception inner) : base(message, inner) { }
}
