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

    private static readonly PropertyID s_items = "Items", s_source = "Source", s_destination = "Destination";

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
        public int Epoch;
    }

    private sealed class SlotBuffers
    {
        public DeviceBuffer? Items;
        public DeviceBuffer? Source;
    }

    private readonly GraphicsDevice _device;
    private readonly InternalCompute _scatter;
    private readonly Dictionary<DeviceBuffer, Destination> _destinations = new();
    private readonly List<Destination> _order = new();
    private readonly Stack<Destination> _spare = new();
    private readonly List<SlotBuffers> _slots = new();
    private readonly PropertySet _properties = new();
    private uint[] _words = new uint[1 << 16];
    private uint _wordCount;
    private int _itemCount;
    private int _epoch;

    public Uploader(GraphicsDevice device, InternalCompute scatter)
    {
        _device = device;
        _scatter = scatter;
    }

    /// <summary>Buffers with writes queued.</summary>
    internal int DestinationCount => _order.Count;

    /// <summary>Words queued this frame.</summary>
    public uint QueuedWords => _wordCount;

    /// <summary>Queues words to land at a word offset of a buffer created with StructuredBufferReadWrite usage.</summary>
    public void Write(DeviceBuffer destination, uint destinationWord, ReadOnlySpan<uint> words)
    {
        if (words.IsEmpty)
            return;

        if (!_destinations.TryGetValue(destination, out Destination? target))
        {
            target = _spare.Count > 0 ? _spare.Pop() : new Destination { Buffer = destination };
            target.Buffer = destination;
            target.Epoch = _epoch;
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

    /// <summary>
    /// Orders the writes queued from here after the ones before, so a word written on both sides ends as the later
    /// write. Writes between two fences land in any order.
    /// </summary>
    public void Fence()
    {
        if (_destinations.Count == 0)
            return;
        _destinations.Clear();
        _epoch++;
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

        cmd.SetComputeShader(_scatter.Program);
        _properties.SetBuffer(s_items, slot.Items!);
        _properties.SetBuffer(s_source, slot.Source!);

        uint first = 0;
        int epoch = _order[0].Epoch;
        foreach (Destination destination in _order)
        {
            if (destination.Epoch != epoch)
            {
                cmd.ComputeBarrier();
                epoch = destination.Epoch;
            }
            if (destination.Buffer.IsDisposed)
            {
                first += (uint)destination.Items.Count;
                destination.Items.Clear();
                continue;
            }
            _properties.SetBuffer(s_destination, destination.Buffer);
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

        Clear();
    }

    /// <summary>Drops everything queued. Destinations are forgotten until written again.</summary>
    public void Clear()
    {
        foreach (Destination destination in _order)
        {
            destination.Items.Clear();
            _spare.Push(destination);
        }
        _order.Clear();
        _destinations.Clear();
        _wordCount = 0;
        _itemCount = 0;
        _epoch = 0;
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
/// A GPU array of fixed size records with a CPU mirror in a persistent graph buffer. Only dirty records reach the GPU
/// on <see cref="Flush"/>, and growing resizes the buffer keeping its contents.
/// </summary>
internal sealed class GpuTable
{
    private readonly GraphicsDevice _device;
    private readonly RenderStore _store;
    private readonly PersistentBuffer _persistent;
    private uint[] _words;
    private bool[] _dirtyFlags;
    private readonly List<uint> _dirty = new();
    private uint _count;

    public GpuTable(GraphicsDevice device, RenderStore store, string name, uint strideWords, uint initialCapacity = 256)
    {
        if (strideWords == 0)
            throw new ArgumentOutOfRangeException(nameof(strideWords), "A record needs at least one word.");

        _device = device;
        _store = store;
        StrideWords = strideWords;
        uint capacity = Math.Max(initialCapacity, 1);
        _words = new uint[capacity * strideWords];
        _dirtyFlags = new bool[capacity];
        _persistent = new PersistentBuffer(RenderResourceID.Intern(name), BufferUsage.StructuredBufferReadWrite);
        Buffer = _persistent.Resolve(device, store, capacity * strideWords * 4);
    }

    /// <summary>Makes a table whose records are <typeparamref name="T"/>.</summary>
    public static GpuTable Of<T>(GraphicsDevice device, RenderStore store, string name, uint initialCapacity = 256) where T : unmanaged
    {
        if (Unsafe.SizeOf<T>() % 4 != 0)
            throw new ArgumentException($"{typeof(T).Name} is not a whole number of words.");
        return new GpuTable(device, store, name, (uint)Unsafe.SizeOf<T>() / 4, initialCapacity);
    }

    public DeviceBuffer Buffer { get; private set; }

    /// <summary>The graph buffer to declare in a pass.</summary>
    public PersistentBuffer Persistent => _persistent;

    public uint StrideWords { get; }

    /// <summary>One past the highest index ever written.</summary>
    public uint Count => _count;

    public ReadOnlySpan<uint> Read(uint index) => _words.AsSpan((int)(index * StrideWords), (int)StrideWords);

    public ref readonly T Get<T>(uint index) where T : unmanaged => ref MemoryMarshal.Cast<uint, T>(Read(index))[0];

    /// <summary>Marks a record dirty and returns its words to write.</summary>
    public Span<uint> Edit(uint index)
    {
        uint capacity = (uint)_dirtyFlags.Length;
        if (index >= capacity)
        {
            while (capacity <= index)
                capacity *= 2;
            Array.Resize(ref _words, (int)(capacity * StrideWords));
            Array.Resize(ref _dirtyFlags, (int)capacity);
        }

        _count = Math.Max(_count, index + 1);
        if (!_dirtyFlags[index])
        {
            _dirtyFlags[index] = true;
            _dirty.Add(index);
        }
        return _words.AsSpan((int)(index * StrideWords), (int)StrideWords);
    }

    public void Set<T>(uint index, in T value) where T : unmanaged
        => MemoryMarshal.Cast<byte, uint>(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value))).CopyTo(Edit(index));

    /// <summary>Queues the dirty records. Returns true when the table outgrew its buffer, so graphs declaring it need a rebuild.</summary>
    public bool Flush(Uploader uploader)
    {
        bool resized = false;
        if (_count * StrideWords * 4 > Buffer.SizeInBytes)
        {
            Buffer = _persistent.Resolve(_device, _store, (uint)_dirtyFlags.Length * StrideWords * 4);
            uploader.Write(Buffer, 0, _words.AsSpan(0, (int)(_count * StrideWords)));
            resized = true;
        }
        else if (_dirty.Count > 0)
        {
            _dirty.Sort();
            int start = 0;
            for (int i = 1; i <= _dirty.Count; i++)
            {
                if (i < _dirty.Count && _dirty[i] == _dirty[i - 1] + 1)
                    continue;

                uint first = _dirty[start];
                uint count = _dirty[i - 1] - first + 1;
                uploader.Write(Buffer, first * StrideWords, _words.AsSpan((int)(first * StrideWords), (int)(count * StrideWords)));
                start = i;
            }
        }

        foreach (uint index in _dirty)
            _dirtyFlags[index] = false;
        _dirty.Clear();
        return resized;
    }
}
