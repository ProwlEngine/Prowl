// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Intermediate;
using Prowl.Clay.Internal.IO;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// IModelFormat implementation for <c>.glb</c> (chunked binary), and <c>.vrm</c> (a <c>.glb</c> with
/// VRM extensions exposed via raw JSON)
/// </summary>
internal sealed class GlbFormat : IModelFormat
{
    /// <summary>
    /// Determines whether a file with the given file extension can be read by this importer.
    /// </summary>
    /// <param name="extension"> The lowercase file extension WITHOUT the leading period ('.'). </param>
    /// <returns><see langword="true"/> if the file extension is readable by this importer.</returns>
    public bool CanRead(string extension) => extension is "glb" or "vrm";

    /// <summary>
    /// Determines via the initial file content whether the file can be read by this importer.
    /// </summary>
    /// <param name="initialBytes"> The initial 32 bytes of data from the file, if the file is at least that size. </param>
    /// <returns><see langword="true"/> if the file data is readable by this importer.</returns>
    public bool CanRead(ReadOnlySpan<byte> initialBytes)
    {
        // "glTF" magic from the GLB header.
        return initialBytes.Length >= 4 && initialBytes[0] == 0x67 && initialBytes[1] == 0x6C && initialBytes[2] == 0x54 && initialBytes[3] == 0x46;
    }

    /// <summary>
    /// Reads the source into an <see cref="IntermediateScene"/>. The source may be either
    /// <see cref="ImportContext.SourcePath"/> (when not null) or <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="context">Importer context.</param>
    public IntermediateScene Read(Stream stream, ImportContext context)
    {
        GlbContent glb = Glb.Read(stream);
        return GltfImporter.Read(glb.Json, glb.Bin, context);
    }
}
