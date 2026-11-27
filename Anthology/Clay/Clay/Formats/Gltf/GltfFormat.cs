// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Intermediate;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// IModelFormat implementation for <c>.gltf</c> (JSON + external resources).
/// </summary>
internal sealed class GltfFormat : IModelFormat
{
    /// <summary>
    /// Determines whether a file with the given file extension can be read by this importer.
    /// </summary>
    /// <param name="extension"> The lowercase file extension WITHOUT the leading period ('.'). </param>
    /// <returns><see langword="true"/> if the file extension is readable by this importer.</returns>
    public bool CanRead(string extension) => extension is "gltf";

    /// <summary>
    /// Determines via the initial file content whether the file can be read by this importer.
    /// </summary>
    /// <param name="initialBytes"> The initial 32 bytes of data from the file, if the file is at least that size. </param>
    /// <returns><see langword="true"/> if the file data is readable by this importer.</returns>
    public bool CanRead(ReadOnlySpan<byte> initialBytes)
    {
        // Likely a JSON document; could be .gltf.
        return initialBytes.Length >= 1 && (initialBytes[0] == (byte)'{' || initialBytes[0] == (byte)' ' || initialBytes[0] == (byte)'\r' || initialBytes[0] == (byte)'\n' || initialBytes[0] == (byte)'\t');
    }

    /// <summary>
    /// Reads the source into an <see cref="IntermediateScene"/>. The source may be either
    /// <see cref="ImportContext.SourcePath"/> (when not null) or <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="context">Importer context.</param>
    public IntermediateScene Read(Stream stream, ImportContext context)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return GltfImporter.Read(ms.ToArray(), binChunk: null, context);
    }
}
