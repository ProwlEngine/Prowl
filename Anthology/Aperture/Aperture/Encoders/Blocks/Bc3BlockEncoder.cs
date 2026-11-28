// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Encoders.Blocks;

/// <summary>BC3: a BC4 block for alpha followed by a four colour BC1 block for colour.</summary>
internal sealed class Bc3BlockEncoder(TextureEncodeQuality quality) : IBlockEncoder
{
    private readonly Bc4BlockEncoder _alpha = new(quality, 1, 3);
    private readonly Bc1BlockEncoder _colour = new(quality, fourColour: true);

    public object CreateScratch() => new[] { _alpha.CreateScratch(), _colour.CreateScratch() };

    public void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch)
    {
        object[] scratches = (object[])scratch;
        _alpha.EncodeBlock(pixels, output[..8], scratches[0]);
        _colour.EncodeBlock(pixels, output[8..], scratches[1]);
    }
}
