// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Encoders.Blocks;

/// <summary>Compresses one block of RGBA8 pixels, using scratch state owned by the calling thread.</summary>
internal interface IBlockEncoder
{
    /// <summary>
    /// Returns a per-thread state object.
    /// </summary>
    object CreateScratch();

    /// <summary>
    /// Performs block compression
    /// </summary>
    /// <param name="pixels">The uncompressed 4x4 input pixels.</param>
    /// <param name="output">A span to write the block-compressed data to.</param>
    /// <param name="scratch">The per-thread state.</param>
    void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch);
}
