// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Encoders.Blocks;

/// <summary>Compresses one block of RGBA8 pixels, using scratch state owned by the calling thread.</summary>
internal interface IBlockEncoder
{
    object CreateScratch();

    void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch);
}
