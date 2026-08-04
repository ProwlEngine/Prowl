using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

/// <summary>
/// Per-ring-slot transient uniform memory
/// </summary>
internal unsafe sealed class VkUniformArena
{
    internal sealed class Block
    {
        public ulong ExecutionId;
        public DeviceBufferRange Range;
        public readonly UniformBlockField[] Fields;
        public readonly PropertyEntry?[] Sources;
        public readonly uint[] Versions;
        public byte[] Packed;
        public byte[] Scratch;

        public DeviceBuffer? ExplicitBuffer;
        public uint ExplicitOffset;
        public uint ExplicitContentVersion;
        public readonly PropertyEntry?[] ExplicitSources;
        public readonly uint[] ExplicitVersions;

        public Block(UniformBlockField[] fields, uint size)
        {
            Fields = fields;
            Sources = new PropertyEntry?[fields.Length];
            Versions = new uint[fields.Length];
            Packed = new byte[size];
            Scratch = new byte[size];
            ExplicitSources = new PropertyEntry?[fields.Length];
            ExplicitVersions = new uint[fields.Length];
        }

        public void CommitScratch() => (Packed, Scratch) = (Scratch, Packed);
    }

    private readonly VkGraphicsDevice _gd;
    private readonly VkBuffer _primary;
    private readonly byte* _primaryMapped;
    private readonly List<VkBuffer> _overflow = [];

    private VkBuffer _active;
    private byte* _activeMapped;
    private uint _activeSize;
    private uint _head;

    private Block?[] _blocks = Array.Empty<Block>();

    public VkUniformArena(VkGraphicsDevice gd, VkBuffer primary)
    {
        _gd = gd;
        _primary = primary;
        _primaryMapped = MappedPointer(primary);
        _active = primary;
        _activeMapped = _primaryMapped;
        _activeSize = primary.SizeInBytes;
    }

    public VkBuffer PrimaryBuffer => _primary;

    public List<VkBuffer> OverflowBuffers => _overflow;

    public ulong CumulativeBytes
    {
        get
        {
            ulong cumulative = _primary.SizeInBytes;
            foreach (VkBuffer buf in _overflow)
                cumulative += buf.SizeInBytes;
            return cumulative;
        }
    }

    /// <summary>Rewinds the bump allocator.</summary>
    public void BeginExecution()
    {
        _active = _primary;
        _activeMapped = _primaryMapped;
        _activeSize = _primary.SizeInBytes;
        _head = 0;
    }

    /// <summary>
    /// Bump-allocates a range and returns a span over its mapped memory.
    /// </summary>
    public Span<byte> Allocate(uint sizeInBytes, out DeviceBufferRange range, out bool grew)
    {
        uint alignment = _gd.UniformBufferMinOffsetAlignment;
        uint alignedHead = (_head + alignment - 1) & ~(alignment - 1);

        if (alignedHead + sizeInBytes > _activeSize)
        {
            Grow(sizeInBytes);
            alignedHead = 0;
            grew = true;
        }
        else
        {
            grew = false;
        }

        _head = alignedHead + sizeInBytes;
        range = new DeviceBufferRange(_active, alignedHead, sizeInBytes);
        return new Span<byte>(_activeMapped + alignedHead, (int)sizeInBytes);
    }

    // Slots are recycled when a program is disposed, so a block only belongs to the caller if it was
    // built for this exact field array.
    public Block GetBlock(int slot, UniformBlockField[] fields, uint size)
    {
        if (_blocks.Length <= slot)
            Array.Resize(ref _blocks, Math.Max(slot + 1, _blocks.Length * 2));

        Block? block = _blocks[slot];
        if (block == null || !ReferenceEquals(block.Fields, fields))
            _blocks[slot] = block = new Block(fields, size);

        return block;
    }

    private void Grow(uint sizeInBytes)
    {
        uint requiredSize = Math.Max(sizeInBytes, _primary.SizeInBytes * 2);
        VkBuffer overflowBuffer = _gd.CreateTransientBuffer(requiredSize);
        _overflow.Add(overflowBuffer);

        _active = overflowBuffer;
        _activeMapped = MappedPointer(overflowBuffer);
        _activeSize = overflowBuffer.SizeInBytes;
        _head = 0;
    }

    private static byte* MappedPointer(VkBuffer buffer)
    {
        if (!buffer.Memory.IsPersistentMapped)
            throw new RenderException("Transient uniform buffers must be persistently mapped.");
        return (byte*)buffer.Memory.BlockMappedPointer;
    }
}
