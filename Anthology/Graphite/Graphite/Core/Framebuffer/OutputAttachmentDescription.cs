using System;

namespace Prowl.Graphite;

/// <summary>One output attachment's format.</summary>
public struct OutputAttachmentDescription : IEquatable<OutputAttachmentDescription>
{
    /// <summary>Attachment's texture format.</summary>
    public PixelFormat Format;

    /// <summary>Makes a description.</summary>
    /// <param name="format">Attachment format.</param>
    public OutputAttachmentDescription(PixelFormat format)
    {
        Format = format;
    }

    /// <summary>Field equality.</summary>
    /// <param name="other">Instance to compare.</param>
    /// <returns>True if equal.</returns>
    public readonly bool Equals(OutputAttachmentDescription other)
    {
        return Format == other.Format;
    }

    /// <summary>Hash code.</summary>
    /// <returns>Hash code.</returns>
    public override readonly int GetHashCode()
    {
        return (int)Format;
    }
}
