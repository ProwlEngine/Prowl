using System;

namespace Prowl.Graphite;

/// <summary>
/// Layout of vertex data in a vertex buffer.
/// </summary>
public struct VertexLayoutDescription
{
    /// <summary>
    /// Shader attribute index; rest increment by 1 (Vulkan location).
    /// </summary>
    public uint Location;
    /// <summary>
    /// Bytes between successive elements in the buffer.
    /// </summary>
    public uint Stride;
    /// <summary>
    /// One entry per vertex element.
    /// </summary>
    public VertexElementDescription[] Elements;
    /// <summary>
    /// Instance advance rate. 0 for per-vertex elements.
    /// </summary>
    public uint InstanceStepRate;

    /// <summary>
    /// Makes a VertexLayoutDescription.
    /// </summary>
    public VertexLayoutDescription(uint location, uint stride, params VertexElementDescription[] elements)
        : this(location, stride, 0, elements)
    {
    }

    /// <summary>
    /// Makes a VertexLayoutDescription.
    /// </summary>
    public VertexLayoutDescription(uint location, uint stride, uint instanceStepRate, params VertexElementDescription[] elements)
    {
        Location = location;
        Stride = stride;
        Elements = elements;
        InstanceStepRate = instanceStepRate;
    }

    /// <summary>
    /// Makes a VertexLayoutDescription; stride computed from element sizes.
    /// </summary>
    public VertexLayoutDescription(uint location, params VertexElementDescription[] elements)
        : this(location, ComputeStride(elements), 0, elements)
    {
    }

    private static uint ComputeStride(VertexElementDescription[] elements)
    {
        uint computedStride = 0;
        for (int i = 0; i < elements.Length; i++)
        {
            uint elementSize = elements[i].Format.GetSizeInBytes();
            if (elements[i].Offset != 0)
            {
                computedStride = elements[i].Offset + elementSize;
            }
            else
            {
                computedStride += elementSize;
            }
        }

        return computedStride;
    }
}
