using System;

namespace Prowl.Veldrid;

/// <summary>
/// A bitmask describing the permitted uses of a <see cref="DeviceBuffer"/> object.
/// </summary>
[Flags]
public enum BufferUsage : byte
{
    /// <summary>
    /// Indicates that a <see cref="DeviceBuffer"/> can be used as the source of vertex data for drawing commands.
    /// This flag enables the use of a Buffer as a vertex buffer returned from an <see cref="IVertexSource.ResolveSlot"/> implementation.
    /// </summary>
    VertexBuffer = 1 << 0,
    /// <summary>
    /// Indicates that a <see cref="DeviceBuffer"/> can be used as the source of index data for drawing commands.
    /// This flag enables the use of a Buffer as an index buffer returned from an <see cref="IVertexSource.TryGetIndexBuffer"/> implementation.
    /// </summary>
    IndexBuffer = 1 << 1,
    /// <summary>
    /// Indicates that a <see cref="DeviceBuffer"/> can be used as a uniform Buffer.
    /// This flag enables the use of a Buffer in a <see cref="ResourceSet"/> as a uniform Buffer.
    /// </summary>
    UniformBuffer = 1 << 2,
    /// <summary>
    /// Can be combined with <see cref="VertexBuffer"/>, <see cref="IndexBuffer"/>, or <see cref="IndirectBuffer"/>
    /// so a compute shader can fill that buffer. This combination requires
    /// <see cref="BufferDescription.UseTypedHlslBinding"/> to be <see langword="false"/> (its default).
    /// </summary>
    StructuredBufferReadOnly = 1 << 3,
    /// <summary>
    /// Can be combined with <see cref="VertexBuffer"/>, <see cref="IndexBuffer"/>, or <see cref="IndirectBuffer"/>
    /// so a compute shader can fill that buffer. This combination requires
    /// <see cref="BufferDescription.UseTypedHlslBinding"/> to be <see langword="false"/> (its default).
    /// </summary>
    StructuredBufferReadWrite = 1 << 4,
    /// <summary>
    /// Indicates that a <see cref="DeviceBuffer"/> can be used as the source of indirect drawing information.
    /// This flag enables the use of a Buffer in the *Indirect methods of <see cref="CommandBuffer"/>.
    /// This flag cannot be combined with <see cref="Dynamic"/>.
    /// </summary>
    IndirectBuffer = 1 << 5,
    /// <summary>
    /// Indicates that a <see cref="DeviceBuffer"/> will be updated with new data very frequently. Dynamic Buffers can be
    /// mapped with <see cref="MapMode.Write"/>. This flag cannot be combined with <see cref="StructuredBufferReadWrite"/>
    /// or <see cref="IndirectBuffer"/>.
    /// </summary>
    Dynamic = 1 << 6,
    /// <summary>
    /// Indicates that a <see cref="DeviceBuffer"/> will be used as a staging Buffer. Staging Buffers can be used to transfer data
    /// to-and-from the CPU using <see cref="GraphicsDevice.Map(MappableResource, MapMode)"/>. Staging Buffers can use all
    /// <see cref="MapMode"/> values.
    /// This flag cannot be combined with any other flag.
    /// </summary>
    Staging = 1 << 7,
}
