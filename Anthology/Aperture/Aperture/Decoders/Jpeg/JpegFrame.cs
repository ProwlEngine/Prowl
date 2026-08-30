// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Jpeg;

/// <summary>One colour component of a frame, with the geometry every later stage derives from it.</summary>
internal sealed class JpegComponent
{
    public byte Id;
    public int HorizontalFactor;
    public int VerticalFactor;
    public int QuantizationTableId;

    public int DcTableId;
    public int AcTableId;

    /// <summary>Blocks the component occupies once the frame is padded out to whole MCUs.</summary>
    public int BlocksPerLine;
    public int BlocksPerColumn;

    /// <summary>Blocks the component's own dimensions actually reach, ignoring MCU padding.</summary>
    public int UsedBlocksPerLine;
    public int UsedBlocksPerColumn;

    /// <summary>Samples that carry meaning. The plane is wider, because blocks pad it out.</summary>
    public int SampledWidth;
    public int SampledHeight;

    public short[]? Coefficients;

    public byte[]? Plane;
    public int PlaneStride;

    public int DcPredictor;

    public long CoefficientLength => (long)BlocksPerLine * BlocksPerColumn * JpegBlock.Coefficients;

    /// <summary>Start of a block within <see cref="Coefficients"/>, in shorts.</summary>
    public int BlockOffset(int blockX, int blockY) =>
        ((blockY * BlocksPerLine) + blockX) * JpegBlock.Coefficients;
}

/// <summary>The frame header, expanded into the geometry the scan and output stages need.</summary>
internal sealed class JpegFrame
{
    public int Width;
    public int Height;
    public int Precision;
    public bool Progressive;

    /// <summary>Whether the scans are coded by learned probabilities rather than by a table.</summary>
    public bool Arithmetic;

    public JpegComponent[] Components = [];

    public int MaxHorizontalFactor = 1;
    public int MaxVerticalFactor = 1;
    public int McusPerLine;
    public int McusPerColumn;

    /// <summary>
    /// Fills in everything derived from the sampling factors. The padded block counts are what
    /// the entropy decoder addresses, and the sampled sizes are what the upsampler may read.
    /// </summary>
    public void Prepare()
    {
        MaxHorizontalFactor = 1;
        MaxVerticalFactor = 1;
        foreach (JpegComponent component in Components)
        {
            if (component.HorizontalFactor > MaxHorizontalFactor)
                MaxHorizontalFactor = component.HorizontalFactor;
            if (component.VerticalFactor > MaxVerticalFactor)
                MaxVerticalFactor = component.VerticalFactor;
        }

        McusPerLine = CeilingDivide(Width, MaxHorizontalFactor * 8);
        McusPerColumn = CeilingDivide(Height, MaxVerticalFactor * 8);

        foreach (JpegComponent component in Components)
        {
            component.SampledWidth = CeilingDivide(Width * component.HorizontalFactor, MaxHorizontalFactor);
            component.SampledHeight = CeilingDivide(Height * component.VerticalFactor, MaxVerticalFactor);
            component.UsedBlocksPerLine = CeilingDivide(component.SampledWidth, 8);
            component.UsedBlocksPerColumn = CeilingDivide(component.SampledHeight, 8);
            component.BlocksPerLine = McusPerLine * component.HorizontalFactor;
            component.BlocksPerColumn = McusPerColumn * component.VerticalFactor;
            component.PlaneStride = component.BlocksPerLine * 8;
        }
    }

    public static int CeilingDivide(int value, int divisor) => (value + divisor - 1) / divisor;
}

/// <summary>A scan header: which components it carries, over which spectral band, at which precision.</summary>
internal struct JpegScan
{
    public int ComponentCount;
    public int SpectralStart;
    public int SpectralEnd;
    public int ApproximationHigh;
    public int ApproximationLow;
}
