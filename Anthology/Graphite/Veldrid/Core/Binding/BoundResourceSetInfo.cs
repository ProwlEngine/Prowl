using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Prowl.Veldrid;


internal struct BoundResourceSetInfo : IEquatable<BoundResourceSetInfo>
{
    public ResourceSet Set;
    public SmallFixedOrDynamicArray Offsets;

    public BoundResourceSetInfo(ResourceSet set, uint offsetsCount, ref uint offsets)
    {
        Set = set;
        Offsets = new SmallFixedOrDynamicArray(offsetsCount, ref offsets);
    }

    public bool Equals(ResourceSet set, uint offsetsCount, ref uint offsets)
    {
        if (set != Set || offsetsCount != Offsets.Count) { return false; }

        for (uint i = 0; i < Offsets.Count; i++)
        {
            if (Unsafe.Add(ref offsets, (int)i) != Offsets.Get(i)) { return false; }
        }

        return true;
    }

    public bool Equals(BoundResourceSetInfo other)
    {
        if (Set != other.Set || Offsets.Count != other.Offsets.Count)
        {
            return false;
        }

        for (uint i = 0; i < Offsets.Count; i++)
        {
            if (Offsets.Get(i) != other.Offsets.Get(i))
            {
                return false;
            }
        }

        return true;
    }
}


internal unsafe struct SmallFixedOrDynamicArray : IDisposable
{
    private const int MaxFixedValues = 5;

    public readonly uint Count;
    private fixed uint FixedData[MaxFixedValues];
    public readonly uint[] Data;

    public uint Get(uint i) => Count > MaxFixedValues ? Data[i] : FixedData[i];

    public SmallFixedOrDynamicArray(uint count, ref uint data)
    {
        if (count > MaxFixedValues)
        {
            Data = ArrayPool<uint>.Shared.Rent((int)count);
            for (int i = 0; i < count; i++)
            {
                Data[i] = Unsafe.Add(ref data, i);
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                FixedData[i] = Unsafe.Add(ref data, i);
            }

            Data = null;
        }

        Count = count;
    }

    public void Dispose()
    {
        if (Data != null) { ArrayPool<uint>.Shared.Return(Data); }
    }
}
