// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Intermediate;

namespace Prowl.Clay.Importer;

/// <summary>
/// Per-format importer: produces an <see cref="IntermediateScene"/> from a byte source, which the
/// post-process pipeline then transforms into the public <see cref="Model"/>.
/// </summary>
public interface IModelFormat
{
    /// <summary>
    /// Determines whether a file with the given file extension can be read by this importer.
    /// </summary>
    /// <param name="extension"> The lowercase file extension WITHOUT the leading period ('.'). </param>
    /// <returns><see langword="true"/> if the file extension is readable by this importer.</returns>
    bool CanRead(string extension);

    /// <summary>
    /// Determines via the initial file content whether the file can be read by this importer.
    /// </summary>
    /// <param name="initialBytes"> The initial 32 bytes of data from the file, if the file is at least that size. </param>
    /// <returns><see langword="true"/> if the file data is readable by this importer.</returns>
    bool CanRead(ReadOnlySpan<byte> initialBytes);

    /// <summary>
    /// Reads the source into an <see cref="IntermediateScene"/>. The source may be either
    /// <see cref="ImportContext.SourcePath"/> (when not null) or <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="context">Importer context.</param>
    IntermediateScene Read(Stream stream, ImportContext context);
}
