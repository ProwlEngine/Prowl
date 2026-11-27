using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Prowl.Recast.Core.Buffers;

public static class RcCyclicBuffers
{
    public static long Sum(this ReadOnlySpan<long> source)
    {
        ReadOnlySpan<long> buffer = source;
        long result = 0L;
        if (Vector.IsHardwareAccelerated)
        {
            ReadOnlySpan<Vector<long>> vectors = MemoryMarshal.Cast<long, Vector<long>>(buffer);
            Vector<long> vecSum = Vector<long>.Zero;
            foreach (Vector<long> vec in vectors)
                vecSum += vec;

            result = Vector.Dot(vecSum, Vector<long>.One);
            int remainder = source.Length % Vector<long>.Count;
            buffer = buffer[^remainder..];
        }
        
        foreach (long val in buffer)
            result += val;
        
        return result;
    }

    public static double Average(this ReadOnlySpan<long> source)
    {
        if (0 >= source.Length)
            return 0;

        return source.Sum() / (double)source.Length;
    }
    
    private static long Min(this ReadOnlySpan<long> source)
    {
        ReadOnlySpan<long> buffer = source;
        long result = long.MaxValue;
        
        if (Vector.IsHardwareAccelerated)
        {
            ReadOnlySpan<Vector<long>> vectors = MemoryMarshal.Cast<long, Vector<long>>(buffer);
            Vector<long> vecMin = Vector<long>.One * result;
            
            foreach (Vector<long> vec in vectors)
                vecMin = Vector.Min(vecMin, vec);

            for (int i = 0; i < Vector<long>.Count; i++)
                result = Math.Min(result, vecMin[i]);

            int remainder = source.Length % Vector<long>.Count;
            buffer = buffer[^remainder..];
        }
        
        foreach (long val in buffer)
            result = Math.Min(result, val);

        return result;
    }
    
    private static long Max(this ReadOnlySpan<long> source)
    {
        ReadOnlySpan<long> buffer = source;
        long result = long.MinValue;
        
        if (Vector.IsHardwareAccelerated)
        {
            ReadOnlySpan<Vector<long>> vectors = MemoryMarshal.Cast<long, Vector<long>>(buffer);
            Vector<long> vecMax = Vector<long>.One * result;
            
            foreach (Vector<long> vec in vectors)
                vecMax = Vector.Max(vecMax, vec);

            for (int i = 0; i < Vector<long>.Count; i++)
                result = Math.Max(result, vecMax[i]);

            int remainder = source.Length % Vector<long>.Count;
            buffer = buffer[^remainder..];
        }
        
        foreach (long val in buffer)
            result = Math.Max(result, val);

        return result;
    }

    public static long Sum(this RcCyclicBuffer<long> source)
    {
        return Sum(source.ArrayOne()) + Sum(source.ArrayTwo());
    }

    public static double Average(this RcCyclicBuffer<long> source)
    {
        return Sum(source) / (double)source.Size;
    }

    public static long Min(this RcCyclicBuffer<long> source)
    {
        Span<long> firstHalf = source.ArrayOne();
        Span<long> secondHalf = source.ArrayTwo();
        long a = firstHalf.Length > 0 ? Min(firstHalf) : long.MaxValue;
        long b = secondHalf.Length > 0 ? Min(secondHalf) : long.MaxValue;
        return Math.Min(a, b);
    }

    public static long Max(this RcCyclicBuffer<long> source)
    {
        Span<long> firstHalf = source.ArrayOne();
        Span<long> secondHalf = source.ArrayTwo();
        long a = firstHalf.Length > 0 ? Max(firstHalf) : long.MinValue;
        long b = secondHalf.Length > 0 ? Max(secondHalf) : long.MinValue;
        return Math.Max(a, b);
    }
}