using System;

namespace Prowl.Graphite.Debugging;

/// <summary>How a pass command uses a resource. Combine flags when one span of use does several things.</summary>
[Flags]
public enum ResourceUsage : ushort
{
    None = 0,
    Vertex = 1 << 0,
    Index = 1 << 1,
    Uniform = 1 << 2,
    Storage = 1 << 3,
    Sampled = 1 << 4,
    Attachment = 1 << 5,
    CopySource = 1 << 6,
    CopyDestination = 1 << 7,
    Indirect = 1 << 8,
}

/// <summary>Whether a graph resource is a texture or a buffer.</summary>
public enum GraphResourceKind : byte
{
    Texture,
    Buffer,
}
