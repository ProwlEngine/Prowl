using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

internal sealed class ShadowBlock
{
    public byte[] Bytes = Array.Empty<byte>();
    public uint Size;
    public bool Dirty;
    public bool Foreign;
    public PackedBlock? Cache;
    public DeviceBufferRange Range;
    public ulong Stamp;

    public Span<byte> Data => Bytes.AsSpan(0, (int)Size);
}

internal sealed class UniformShadows
{
    private UniformBindingPlan? _plan;
    private ShadowBlock[] _blocks = Array.Empty<ShadowBlock>();
    private bool[] _blit = Array.Empty<bool>();
    private PackedBlock?[] _pack = Array.Empty<PackedBlock?>();
    private int[] _hits = Array.Empty<int>();

    public ShadowBlock this[int block] => _blocks[block];

    public void Reset() => _plan = null;

    public void UsePlan(UniformBindingPlan plan, PropertySet merged)
    {
        if (ReferenceEquals(plan, _plan))
            return;

        _plan = plan;
        int count = plan.BlockCount;
        if (_blocks.Length < count)
        {
            int old = _blocks.Length;
            Array.Resize(ref _blocks, count);
            for (int i = old; i < count; i++)
                _blocks[i] = new ShadowBlock();

            _blit = new bool[count];
            _pack = new PackedBlock?[count];
            _hits = new int[count];
        }

        for (int i = 0; i < count; i++)
        {
            ShadowBlock block = _blocks[i];
            block.Size = plan.Layouts[i].Size;
            if (block.Bytes.Length < block.Size)
                block.Bytes = new byte[block.Size];
            else
                block.Data.Clear();

            block.Dirty = true;
            block.Foreign = false;
            block.Cache = null;
            block.Range = default;
            block.Stamp = 0;
        }

        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in merged.Entries)
            Write(kv.Key, kv.Value);
    }

    public void Apply(PropertySet source)
    {
        if (_plan == null)
            return;

        bool pass = false;
        for (int b = 0; b < _plan.BlockCount; b++)
        {
            UniformBlockLayout layout = _plan.Layouts[b];
            ShadowBlock block = _blocks[b];
            PackedBlock? cache = layout.Cacheable ? source.FindPacked(layout) : null;
            bool valid = cache != null && cache.Version == source.Version;
            _blit[b] = false;
            _pack[b] = null;

            if (valid && cache!.Full)
            {
                block.Cache = cache;
                block.Dirty = true;
                continue;
            }

            Materialize(block);
            _blit[b] = true;
            pass = true;
            if (layout.Cacheable && !valid)
            {
                cache ??= source.AddPacked(layout);
                Array.Clear(cache.Bytes);
                _pack[b] = cache;
                _hits[b] = 0;
            }
        }

        if (!pass)
            return;

        foreach (KeyValuePair<PropertyID, PropertyEntry> kv in source.Entries)
        {
            PropertyEntry entry = kv.Value;
            if (entry.Kind != PropertyEntryKind.Uniform || !_plan.TryFind(kv.Key, out ReadOnlySpan<UniformFieldTarget> targets))
                continue;

            int length = (int)UniformSizes.Of(entry.UniformType);
            ReadOnlySpan<byte> src = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref entry.Uniform), length);
            foreach (UniformFieldTarget target in targets)
            {
                if (_blit[target.Block])
                {
                    ShadowBlock block = _blocks[target.Block];
                    Copy(src, block.Data, target);
                    block.Dirty = true;
                }

                if (_pack[target.Block] is { } packed)
                {
                    Copy(src, packed.Bytes, target);
                    _hits[target.Block]++;
                }
            }
        }

        for (int b = 0; b < _plan.BlockCount; b++)
        {
            if (_pack[b] is not { } cache)
                continue;

            cache.Version = source.Version;
            cache.Full = _hits[b] == cache.Layout.FieldCount;
            cache.Stamp = 0;
            if (cache.Full)
                _blocks[b].Cache = cache;
        }
    }

    public void Apply(List<PropertyID> keys, int from, PropertySet merged)
    {
        if (_plan == null)
            return;

        for (int i = from; i < keys.Count; i++)
        {
            if (merged.Entries.TryGetValue(keys[i], out PropertyEntry? entry))
                Write(keys[i], entry);
        }
    }

    private void Write(PropertyID id, PropertyEntry entry)
    {
        if (entry.Kind != PropertyEntryKind.Uniform || _plan == null || !_plan.TryFind(id, out ReadOnlySpan<UniformFieldTarget> targets))
            return;

        int length = (int)UniformSizes.Of(entry.UniformType);
        ReadOnlySpan<byte> src = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref entry.Uniform), length);
        foreach (UniformFieldTarget target in targets)
        {
            ShadowBlock block = _blocks[target.Block];
            Materialize(block);
            Copy(src, block.Data, target);
            block.Dirty = true;
        }
    }

    private static void Materialize(ShadowBlock block)
    {
        if (block.Cache == null)
            return;

        block.Cache.Bytes.AsSpan(0, (int)block.Size).CopyTo(block.Data);
        block.Cache = null;
    }

    private static void Copy(ReadOnlySpan<byte> src, Span<byte> block, UniformFieldTarget target)
    {
        Span<byte> dst = block.Slice((int)target.Offset, (int)target.Size);
        int count = Math.Min(dst.Length, src.Length);
        src.Slice(0, count).CopyTo(dst);
        dst.Slice(count).Clear();
    }
}

internal static class UniformSizes
{
    public static uint Of(UniformScalarType type) => type switch
    {
        UniformScalarType.Float1 or UniformScalarType.Int1 or UniformScalarType.UInt1 => 4,
        UniformScalarType.Float2 or UniformScalarType.Int2 or UniformScalarType.UInt2 or UniformScalarType.Double1 => 8,
        UniformScalarType.Float3 or UniformScalarType.Int3 or UniformScalarType.UInt3 => 12,
        UniformScalarType.Float4 or UniformScalarType.Int4 or UniformScalarType.UInt4 or UniformScalarType.Double2 or UniformScalarType.Float2x2 => 16,
        UniformScalarType.Double3 => 24,
        UniformScalarType.Double4 => 32,
        UniformScalarType.Float3x3 => 36,
        _ => 64,
    };
}
