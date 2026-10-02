using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Ready-made IVertexSource. Buffers are matched to shader layout slots by the name of the slot's first element.
/// Build once and reuse the instance; VertexSource.None is the shared source for draws with no vertex data.
/// </summary>
public sealed class VertexSource : IVertexSource
{
    /// <summary>
    /// Triangle list with no buffers. For fullscreen triangles and other SV_VertexID draws.
    /// </summary>
    public static readonly VertexSource None = new(PrimitiveTopology.TriangleList, frozen: true);

    private readonly bool _frozen;
    private readonly List<VertexAttributeID> _names = [];
    private readonly List<VertexBinding> _bindings = [];
    private DeviceBuffer? _indexBuffer;
    private IndexFormat _indexFormat;
    private uint _indexCount;

    /// <summary>
    /// Topology for draws using this source.
    /// </summary>
    public PrimitiveTopology Topology { get; }

    /// <summary>
    /// Makes an empty source with the given topology.
    /// </summary>
    /// <param name="topology">Primitive topology.</param>
    public VertexSource(PrimitiveTopology topology = PrimitiveTopology.TriangleList) : this(topology, false)
    {
    }

    private VertexSource(PrimitiveTopology topology, bool frozen)
    {
        Topology = topology;
        _frozen = frozen;
    }

    /// <summary>
    /// Supplies the buffer for the layout slot whose first element has this name.
    /// </summary>
    /// <param name="semantic">Name of the slot's first element, e.g. POSITION0.</param>
    /// <param name="buffer">Vertex buffer.</param>
    /// <param name="offset">Byte offset into the buffer.</param>
    /// <returns>This source.</returns>
    public VertexSource SetBuffer(VertexAttributeID semantic, DeviceBuffer buffer, uint offset = 0)
    {
        ThrowIfFrozen();
        VertexBinding binding = new(buffer, offset);
        int index = _names.IndexOf(semantic);
        if (index >= 0)
        {
            _bindings[index] = binding;
        }
        else
        {
            _names.Add(semantic);
            _bindings.Add(binding);
        }
        return this;
    }

    /// <summary>
    /// Supplies the index buffer for indexed draws.
    /// </summary>
    /// <param name="buffer">Index buffer.</param>
    /// <param name="format">Index format.</param>
    /// <param name="indexCount">Index count.</param>
    /// <returns>This source.</returns>
    public VertexSource SetIndexBuffer(DeviceBuffer buffer, IndexFormat format, uint indexCount)
    {
        ThrowIfFrozen();
        _indexBuffer = buffer;
        _indexFormat = format;
        _indexCount = indexCount;
        return this;
    }

    /// <inheritdoc/>
    public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
    {
        VertexAttributeID wanted = layout.Elements[0].Name;
        int index = _names.IndexOf(wanted);
        if (index < 0)
            throw new InvalidOperationException($"VertexSource has no buffer for vertex attribute '{VertexAttributeID.ToString(wanted)}'.");

        binding = _bindings[index];
    }

    /// <inheritdoc/>
    public bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)
    {
        buffer = _indexBuffer!;
        format = _indexFormat;
        indexCount = _indexCount;
        return _indexBuffer != null;
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("VertexSource.None cannot be modified.");
    }
}
