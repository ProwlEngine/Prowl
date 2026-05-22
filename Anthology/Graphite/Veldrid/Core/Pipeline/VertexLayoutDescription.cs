using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes the layout of vertex data in a single <see cref="DeviceBuffer"/> used as a vertex buffer.
/// </summary>
public struct VertexLayoutDescription : IEquatable<VertexLayoutDescription>
{
    /// <summary>
    /// The shader attribute index assigned to the first element of this layout. Subsequent
    /// elements are assigned <c>Location + 1</c>, <c>Location + 2</c>, ... — so on OpenGL
    /// this is the value used in <c>layout(location = N)</c>, on Vulkan it is the
    /// <c>VkVertexInputAttributeDescription.location</c>, and on D3D11 it has no shader
    /// effect (semantics are name-based). This does <b>not</b> identify the vertex buffer
    /// binding slot; the slot is determined by the layout's index in
    /// <see cref="ShaderSetDescription.VertexLayouts"/> and is what
    /// <see cref="CommandBuffer.SetVertexBuffer(uint, DeviceBuffer)"/>'s <c>index</c>
    /// parameter refers to.
    /// </summary>
    public uint Location;
    /// <summary>
    /// The number of bytes in between successive elements in the <see cref="DeviceBuffer"/>.
    /// </summary>
    public uint Stride;
    /// <summary>
    /// An array of <see cref="VertexElementDescription"/> objects, each describing a single element of vertex data.
    /// </summary>
    public VertexElementDescription[] Elements;
    /// <summary>
    /// A value controlling how often data for instances is advanced for this layout. For per-vertex elements, this value
    /// should be 0.
    /// </summary>
    public uint InstanceStepRate;

    /// <summary>
    /// Constructs a new VertexLayoutDescription.
    /// </summary>
    public VertexLayoutDescription(uint location, uint stride, params VertexElementDescription[] elements)
    {
        Location = location;
        Stride = stride;
        Elements = elements;
        InstanceStepRate = 0;
    }

    /// <summary>
    /// Constructs a new VertexLayoutDescription.
    /// </summary>
    public VertexLayoutDescription(uint location, uint stride, uint instanceStepRate, params VertexElementDescription[] elements)
    {
        Location = location;
        Stride = stride;
        Elements = elements;
        InstanceStepRate = instanceStepRate;
    }

    /// <summary>
    /// Constructs a new VertexLayoutDescription. The stride is assumed to be the sum of the size of all elements.
    /// </summary>
    public VertexLayoutDescription(uint location, params VertexElementDescription[] elements)
    {
        Location = location;
        Elements = elements;
        uint computedStride = 0;
        for (int i = 0; i < elements.Length; i++)
        {
            uint elementSize = FormatSizeHelpers.GetSizeInBytes(elements[i].Format);
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
    public bool Equals(VertexLayoutDescription other)
    {
        return Location.Equals(other.Location)
            && Stride.Equals(other.Stride)
            && Util.ArrayEqualsEquatable(Elements, other.Elements)
            && InstanceStepRate.Equals(other.InstanceStepRate);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Location.GetHashCode(), Stride.GetHashCode(), Elements.ArrayHash(), InstanceStepRate.GetHashCode());
    }
}
