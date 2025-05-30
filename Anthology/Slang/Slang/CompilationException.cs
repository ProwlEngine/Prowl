// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;


namespace Prowl.Slang;


/// <summary>
/// The exception that is thrown whenever a part of the Slang library encounters errors when compiling code.
/// </summary>
public class CompilationException : Exception
{
    /// <summary>
    /// The diagnostics information that contains the error messages from the compilation process.
    /// </summary>
    public DiagnosticInfo Diagnostics { get; internal set; }

    /// <summary>
    /// Creates a new <see cref="CompilationException"/>
    /// </summary>
    public CompilationException(DiagnosticInfo diagnostics, string message) : base(message)
    {
        Diagnostics = diagnostics;
    }
}