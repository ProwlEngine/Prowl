using System;
using System.Numerics;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Hands out ranges of a fixed size space in constant time with a two level segregated fit: free blocks sit in lists
/// by size class, a bitmap per level finds a fitting class in a few instructions, and a freed block merges with free
/// neighbours. Sizes and offsets are in caller defined units.
/// </summary>
internal sealed class RangeAllocator
{
    private const int SecondLevelBits = 4;
    private const int SecondLevelCount = 1 << SecondLevelBits;
    private const int FirstLevelCount = 32;
    private const int None = -1;

    private struct Block
    {
        public uint Offset;
        public uint Size;
        public int PreviousPhysical;
        public int NextPhysical;
        public int PreviousFree;
        public int NextFree;
        public bool Free;
        public bool Used;
    }

    private Block[] _blocks = new Block[64];
    private int _blockCount;
    private int _recycledBlocks = None;
    private readonly int[] _freeHeads = new int[FirstLevelCount * SecondLevelCount];
    private readonly uint[] _secondLevelMaps = new uint[FirstLevelCount];
    private uint _firstLevelMap;

    /// <summary>Total units the allocator manages.</summary>
    public uint Capacity { get; }

    /// <summary>Units currently handed out.</summary>
    public uint Used { get; private set; }

    public RangeAllocator(uint capacity)
    {
        if (capacity == 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "A range allocator needs a capacity.");

        Capacity = capacity;
        Array.Fill(_freeHeads, None);
        int first = NewBlock();
        _blocks[first] = new Block { Offset = 0, Size = capacity, PreviousPhysical = None, NextPhysical = None, Free = true, Used = true };
        InsertFree(first);
    }

    /// <summary>
    /// Allocates a range of at least <paramref name="size"/> units starting at a multiple of <paramref name="alignment"/>.
    /// Returns false when no free block fits.
    /// </summary>
    public bool TryAllocate(uint size, uint alignment, out RangeAllocation allocation)
    {
        allocation = default;
        if (size == 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Cannot allocate an empty range.");
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a power of two.");

        uint search = size + alignment - 1;
        if (search < size)
            return false;

        int block = FindFree(search);
        if (block == None)
            block = FindInOwnClass(search);
        if (block == None)
            return false;

        RemoveFree(block);
        uint offset = _blocks[block].Offset;
        uint aligned = (offset + alignment - 1) & ~(alignment - 1);
        if (aligned != offset)
        {
            int front = Split(block, aligned - offset);
            InsertFree(block);
            block = front;
        }

        if (_blocks[block].Size > size)
        {
            int rest = Split(block, size);
            InsertFree(rest);
        }

        _blocks[block].Free = false;
        Used += _blocks[block].Size;
        allocation = new RangeAllocation(block, _blocks[block].Offset, _blocks[block].Size);
        return true;
    }

    /// <summary>Returns a range, merging it with free neighbours.</summary>
    public void Free(RangeAllocation allocation)
    {
        int block = allocation.Block;
        if ((uint)block >= (uint)_blockCount || !_blocks[block].Used || _blocks[block].Free || _blocks[block].Offset != allocation.Offset)
            throw new InvalidOperationException("The range is not an allocation of this allocator, or was already freed.");

        Used -= _blocks[block].Size;
        _blocks[block].Free = true;

        int previous = _blocks[block].PreviousPhysical;
        if (previous != None && _blocks[previous].Free)
        {
            RemoveFree(previous);
            block = Merge(previous, block);
        }

        int next = _blocks[block].NextPhysical;
        if (next != None && _blocks[next].Free)
        {
            RemoveFree(next);
            block = Merge(block, next);
        }

        InsertFree(block);
    }

    /// <summary>Size of the largest range a single allocation could still get.</summary>
    public uint LargestFree
    {
        get
        {
            if (_firstLevelMap == 0)
                return 0;

            int fl = 31 - BitOperations.LeadingZeroCount(_firstLevelMap);
            int sl = 31 - BitOperations.LeadingZeroCount(_secondLevelMaps[fl]);
            uint largest = 0;
            for (int block = _freeHeads[fl * SecondLevelCount + sl]; block != None; block = _blocks[block].NextFree)
                largest = Math.Max(largest, _blocks[block].Size);
            return largest;
        }
    }

    private static void Classify(uint size, out int fl, out int sl)
    {
        if (size < SecondLevelCount)
        {
            fl = 0;
            sl = (int)size;
            return;
        }

        fl = 31 - BitOperations.LeadingZeroCount(size);
        sl = (int)(size >> (fl - SecondLevelBits)) & (SecondLevelCount - 1);
        fl -= SecondLevelBits - 1;
    }

    // Rounds the request up to the next class boundary first, so any block of the found class is large enough.
    private int FindFree(uint size)
    {
        uint rounded = size;
        if (size >= SecondLevelCount)
        {
            int log = 31 - BitOperations.LeadingZeroCount(size);
            uint step = (1u << (log - SecondLevelBits)) - 1;
            rounded = size + step;
            if (rounded < size)
                return None;
        }

        Classify(rounded, out int fl, out int sl);
        uint secondMap = _secondLevelMaps[fl] & (~0u << sl);
        if (secondMap == 0)
        {
            uint firstMap = fl + 1 < 32 ? _firstLevelMap & (~0u << (fl + 1)) : 0;
            if (firstMap == 0)
                return None;

            fl = BitOperations.TrailingZeroCount(firstMap);
            secondMap = _secondLevelMaps[fl];
        }

        sl = BitOperations.TrailingZeroCount(secondMap);
        return _freeHeads[fl * SecondLevelCount + sl];
    }

    // The class a size falls in also holds smaller blocks, so the constant time search skips it. On a miss, scan it.
    private int FindInOwnClass(uint size)
    {
        Classify(size, out int fl, out int sl);
        for (int block = _freeHeads[fl * SecondLevelCount + sl]; block != None; block = _blocks[block].NextFree)
        {
            if (_blocks[block].Size >= size)
                return block;
        }
        return None;
    }

        private void InsertFree(int block)
    {
        Classify(_blocks[block].Size, out int fl, out int sl);
        int list = fl * SecondLevelCount + sl;
        int head = _freeHeads[list];
        _blocks[block].PreviousFree = None;
        _blocks[block].NextFree = head;
        if (head != None)
            _blocks[head].PreviousFree = block;
        _freeHeads[list] = block;
        _firstLevelMap |= 1u << fl;
        _secondLevelMaps[fl] |= 1u << sl;
    }

    private void RemoveFree(int block)
    {
        Classify(_blocks[block].Size, out int fl, out int sl);
        int list = fl * SecondLevelCount + sl;
        int previous = _blocks[block].PreviousFree;
        int next = _blocks[block].NextFree;
        if (previous != None)
            _blocks[previous].NextFree = next;
        else
            _freeHeads[list] = next;
        if (next != None)
            _blocks[next].PreviousFree = previous;

        if (_freeHeads[list] == None)
        {
            _secondLevelMaps[fl] &= ~(1u << sl);
            if (_secondLevelMaps[fl] == 0)
                _firstLevelMap &= ~(1u << fl);
        }
    }

    // Splits off the units past firstSize into a new block placed after this one, and returns it.
    private int Split(int block, uint firstSize)
    {
        int rest = NewBlock();
        ref Block b = ref _blocks[block];
        _blocks[rest] = new Block
        {
            Offset = b.Offset + firstSize,
            Size = b.Size - firstSize,
            PreviousPhysical = block,
            NextPhysical = b.NextPhysical,
            Free = true,
            Used = true,
        };
        if (b.NextPhysical != None)
            _blocks[b.NextPhysical].PreviousPhysical = rest;
        b.NextPhysical = rest;
        b.Size = firstSize;
        return rest;
    }

    private int Merge(int first, int second)
    {
        _blocks[first].Size += _blocks[second].Size;
        _blocks[first].NextPhysical = _blocks[second].NextPhysical;
        if (_blocks[second].NextPhysical != None)
            _blocks[_blocks[second].NextPhysical].PreviousPhysical = first;
        RecycleBlock(second);
        return first;
    }

    private int NewBlock()
    {
        if (_recycledBlocks != None)
        {
            int recycled = _recycledBlocks;
            _recycledBlocks = _blocks[recycled].NextFree;
            return recycled;
        }

        if (_blockCount == _blocks.Length)
            Array.Resize(ref _blocks, _blocks.Length * 2);
        return _blockCount++;
    }

    private void RecycleBlock(int block)
    {
        _blocks[block] = new Block { NextFree = _recycledBlocks };
        _recycledBlocks = block;
    }
}

/// <summary>A range handed out by a <see cref="RangeAllocator"/>.</summary>
internal readonly record struct RangeAllocation(int Block, uint Offset, uint Size);
