// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;

namespace Prowl.Clay;

/// <summary>
/// Thrown when import cannot produce a valid <see cref="Model"/>. The only exception type raised
/// by this library; consumers can catch this once and treat all import failures uniformly.
/// </summary>
public sealed class ImportException : Exception
{
    /// <summary>Source path or stream-hint identifying the file being imported.</summary>
    public string? SourcePath { get; }

    /// <summary>The model importer when known, otherwise <c>null</c>.</summary>
    public IModelFormat? Format { get; }

    /// <summary>Initializes a new exception.</summary>
    public ImportException(string message, string? sourcePath = null, IModelFormat? format = null, Exception? inner = null)
        : base(message, inner)
    {
        SourcePath = sourcePath;
        Format = format;
    }

    /// <summary>
    /// Returns this exception with <paramref name="sourcePath"/> and <paramref name="format"/> filled
    /// in, or itself when it already carries them.
    /// </summary>
    /// <remarks>
    /// Code deep in a reader or post-process step knows what is wrong but not which file it came
    /// from, so it throws without that context and the entry point attaches it on the way out.
    /// </remarks>
    internal ImportException WithContext(string? sourcePath, IModelFormat? format)
    {
        string? path = SourcePath ?? sourcePath;
        IModelFormat? fmt = Format ?? format;
        if (path == SourcePath && fmt == Format)
            return this;

        // Kept as the inner exception so the original throw site survives in the stack trace.
        return new ImportException(Message, path, fmt, InnerException ?? this);
    }
}
