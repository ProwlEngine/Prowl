using System;

namespace Prowl.Graphite;

/// <summary>One output attachment's format.</summary>
public record struct OutputAttachmentDescription
{
    /// <summary>Attachment's texture format.</summary>
    public PixelFormat Format;

    /// <summary>Makes a description.</summary>
    /// <param name="format">Attachment format.</param>
    public OutputAttachmentDescription(PixelFormat format)
    {
        Format = format;
    }
}
