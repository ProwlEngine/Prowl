using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Collects the frame's writes to persistent GPU buffers and records them as one compute scatter per destination,
/// instead of a copy and a barrier per write. Writes land in a host visible upload buffer owned by the execution's frame
/// in flight slot, which is free to reuse because a slot is only handed out again once its last execution completed.
/// </summary>
internal sealed class Uploader : IDisposable
{
    /// <summary>Words one scatter item copies at most, so big writes spread over many workgroups.</summary>
    public const uint MaxItemWords = 4096;

    private const uint MaxGroupsPerDispatch = 65535;

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private struct Item
    {
        public uint DestinationWord;
        public uint SourceWord;
        public uint Words;
    }

    private sealed class Destination
    {
        public required DeviceBuffer Buffer;
        public readonly List<Item> Items = new();
    }

    private sealed class SlotBuffers
    {
        public DeviceBuffer? Items;
        public DeviceBuffer? Source;
    }

    private readonly GraphicsDevice _device;
    private readonly ComputeProgram _scatter;
    private readonly Dictionary<DeviceBuffer, Destination> _destinations = new();
    private readonly List<Destination> _order = new();
    private readonly List<SlotBuffers> _slots = new();
    private readonly PropertySet _properties = new();
    private uint[] _words = new uint[1 << 16];
    private uint _wordCount;
    private int _itemCount;

    public Uploader(GraphicsDevice device, ComputeProgram scatter)
    {
        _device = device;
        _scatter = scatter;
    }

    /// <summary>Words queued this frame.</summary>
    public uint QueuedWords => _wordCount;

    /// <summary>Queues words to land at a word offset of a buffer created with StructuredBufferReadWrite usage.</summary>
    public void Write(DeviceBuffer destination, uint destinationWord, ReadOnlySpan<uint> words)
    {
        if (words.IsEmpty)
            return;

        if (!_destinations.TryGetValue(destination, out Destination? target))
        {
            target = new Destination { Buffer = destination };
            _destinations[destination] = target;
            _order.Add(target);
        }

        EnsureWords(_wordCount + (uint)words.Length);
        words.CopyTo(_words.AsSpan((int)_wordCount));
        for (uint done = 0; done < words.Length; done += MaxItemWords)
        {
            uint count = Math.Min(MaxItemWords, (uint)words.Length - done);
            target.Items.Add(new Item { DestinationWord = destinationWord + done, SourceWord = _wordCount + done, Words = count });
            _itemCount++;
        }

        _wordCount += (uint)words.Length;
    }

    /// <summary>Queues a value, its size a multiple of 4 bytes.</summary>
    public void Write<T>(DeviceBuffer destination, uint destinationWord, in T value) where T : unmanaged
        => Write(destination, destinationWord, MemoryMarshal.Cast<byte, uint>(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value))));

    /// <summary>
    /// Records the queued writes into the command buffer and clears the queue. Run in a pass that declares every
    /// destination as an imported buffer with ShaderWrite access, so later readers get a barrier.
    /// </summary>
    public void Record(CommandBuffer cmd, ExecutionTask task)
    {
        if (_itemCount == 0)
            return;

        SlotBuffers slot = Slot(task.RingSlot);
        EnsureCapacity(ref slot.Items, (uint)_itemCount * 4, "Upload items");
        EnsureCapacity(ref slot.Source, _wordCount, "Upload words");

        Span<uint> source = _device.Map<uint>(slot.Source!);
        _words.AsSpan(0, (int)_wordCount).CopyTo(source);
        _device.Unmap(slot.Source!);

        Span<Item> items = _device.Map<Item>(slot.Items!);
        int written = 0;
        foreach (Destination destination in _order)
        {
            CollectionsMarshal.AsSpan(destination.Items).CopyTo(items[written..]);
            written += destination.Items.Count;
        }
        _device.Unmap(slot.Items!);

        cmd.SetComputeShader(_scatter);
        _properties.SetBuffer("Items", slot.Items!);
        _properties.SetBuffer("Source", slot.Source!);

        uint first = 0;
        foreach (Destination destination in _order)
        {
            _properties.SetBuffer("Destination", destination.Buffer);
            cmd.SetProperties(_properties);
            uint remaining = (uint)destination.Items.Count;
            while (remaining > 0)
            {
                uint groups = Math.Min(remaining, MaxGroupsPerDispatch);
                cmd.SetPushConstants(first);
                cmd.Dispatch(groups, 1, 1);
                first += groups;
                remaining -= groups;
            }
            destination.Items.Clear();
        }

        _wordCount = 0;
        _itemCount = 0;
    }

    public void Dispose()
    {
        foreach (SlotBuffers slot in _slots)
        {
            slot.Items?.Dispose();
            slot.Source?.Dispose();
        }
        _slots.Clear();
    }

    private SlotBuffers Slot(uint ringSlot)
    {
        while (_slots.Count <= ringSlot)
            _slots.Add(new SlotBuffers());
        return _slots[(int)ringSlot];
    }

    private void EnsureCapacity(ref DeviceBuffer? buffer, uint words, string name)
    {
        uint bytes = Math.Max(words * 4, 256u);
        if (buffer != null && buffer.SizeInBytes >= bytes)
            return;

        buffer?.Dispose();
        uint size = 256;
        while (size < bytes)
            size *= 2;
        buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(size, BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic));
        buffer.Name = name;
    }

    private void EnsureWords(uint words)
    {
        if (words <= _words.Length)
            return;

        uint size = (uint)_words.Length;
        while (size < words)
            size *= 2;
        Array.Resize(ref _words, (int)size);
    }
}

/// <summary>
/// A GPU array of records with a CPU mirror. Records change on the CPU and only the dirty ones reach the GPU on
/// <see cref="Flush"/>, merged into runs. Growing moves the table to a new buffer and hands the old one back to retire.
/// </summary>
internal sealed class GpuTable<T> : IDisposable where T : unmanaged
{
    private readonly GraphicsDevice _device;
    private readonly string _name;
    private T[] _records;
    private bool[] _dirtyFlags;
    private readonly List<uint> _dirty = new();
    private uint _count;

    public GpuTable(GraphicsDevice device, string name, uint initialCapacity = 256)
    {
        if (Unsafe.SizeOf<T>() % 4 != 0)
            throw new ArgumentException($"{typeof(T).Name} is not a whole number of words.");

        _device = device;
        _name = name;
        _records = new T[Math.Max(initialCapacity, 1)];
        _dirtyFlags = new bool[_records.Length];
        Buffer = CreateBuffer((uint)_records.Length);
    }

    public DeviceBuffer Buffer { get; private set; }

    /// <summary>One past the highest index ever set.</summary>
    public uint Count => _count;

    public ref readonly T this[uint index] => ref _records[index];

    public void Set(uint index, in T value)
    {
        if (index >= _records.Length)
        {
            uint size = (uint)_records.Length;
            while (size <= index)
                size *= 2;
            Array.Resize(ref _records, (int)size);
            Array.Resize(ref _dirtyFlags, (int)size);
        }

        _records[index] = value;
        _count = Math.Max(_count, index + 1);
        if (!_dirtyFlags[index])
        {
            _dirtyFlags[index] = true;
            _dirty.Add(index);
        }
    }

    /// <summary>
    /// Queues the dirty records. Returns the replaced buffer when the table outgrew its buffer, which the caller retires
    /// once the GPU is done with it, or null.
    /// </summary>
    public DeviceBuffer? Flush(Uploader uploader)
    {
        uint words = (uint)Unsafe.SizeOf<T>() / 4;
        DeviceBuffer? replaced = null;
        if (_count * words * 4 > Buffer.SizeInBytes)
        {
            replaced = Buffer;
            Buffer = CreateBuffer((uint)_records.Length);
            uploader.Write(Buffer, 0, Words(0, _count));
        }
        else if (_dirty.Count > 0)
        {
            _dirty.Sort();
            int start = 0;
            for (int i = 1; i <= _dirty.Count; i++)
            {
                if (i < _dirty.Count && _dirty[i] == _dirty[i - 1] + 1)
                    continue;

                uploader.Write(Buffer, _dirty[start] * words, Words(_dirty[start], _dirty[i - 1] - _dirty[start] + 1));
                start = i;
            }
        }

        foreach (uint index in _dirty)
            _dirtyFlags[index] = false;
        _dirty.Clear();
        return replaced;
    }

    public void Dispose() => Buffer.Dispose();

    private ReadOnlySpan<uint> Words(uint first, uint count) => MemoryMarshal.Cast<T, uint>(_records.AsSpan((int)first, (int)count));

    private DeviceBuffer CreateBuffer(uint capacity)
    {
        DeviceBuffer buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(capacity * (uint)Unsafe.SizeOf<T>(), BufferUsage.StructuredBufferReadWrite));
        buffer.Name = _name;
        return buffer;
    }
}
