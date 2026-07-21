using System;

namespace Prowl.Graphite;

/// <summary>
/// Layout of vertex data in a vertex buffer.
/// </summary>
public struct VertexLayoutDescription : IEquatable<VertexLayoutDescription>
{
    /// <summary>
    /// Shader attribute index of the first element; rest increment by 1 (Vulkan location). Not the buffer binding
    /// slot - that's the layout's index in VertexLayouts, passed as layoutSlot to ResolveSlot.
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
    {
        Location = location;
        Stride = stride;
        Elements = elements;
        InstanceStepRate = 0;
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
    /// Makes a VertexLayoutDescription. Stride is computed as the sum of element sizes.
    /// </summary>
    public VertexLayoutDescription(uint location, params VertexElementDescription[] elements)
    {
        Location = location;
        Elements = elements;
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

        Stride = computedStride;
        InstanceStepRate = 0;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    public readonly bool Equals(VertexLayoutDescription other)
    {
        return Location.Equals(other.Location)
            && Stride.Equals(other.Stride)
            && Util.ArrayEqualsEquatable(Elements, other.Elements)
            && InstanceStepRate.Equals(other.InstanceStepRate);
    }

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Location.GetHashCode(), Stride.GetHashCode(), Elements.ArrayHash(), InstanceStepRate.GetHashCode());
    }
}
