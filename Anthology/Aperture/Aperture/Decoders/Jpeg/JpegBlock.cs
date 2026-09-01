// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Jpeg;

/// <summary>Constants shared by everything that handles an eight by eight coefficient block.</summary>
internal static class JpegBlock
{
    public const int Coefficients = 64;

    /// <summary>
    /// Maps a position in the entropy coded zig zag sequence to its index in row major order.
    /// The four extra entries are a guard: a corrupt stream can run the coefficient index past
    /// the end of a block, and landing on a scratch slot is cheaper than testing every write.
    /// </summary>
    public static ReadOnlySpan<byte> ZigZag =>
    [
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
        63, 63, 63, 63,
    ];
}
