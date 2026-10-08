using System;
using System.Collections.Generic;
using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger;

internal static class ProgramEquality
{
    public static bool Equal(EquatableArray<ResourceLayoutDescription> a, EquatableArray<ResourceLayoutDescription> b)
        => Same(a, b, (x, y) => x.Set == y.Set && SameElements(x.Elements, y.Elements));

    public static bool Equal(EquatableArray<VertexLayoutDescription> a, EquatableArray<VertexLayoutDescription> b)
        => Same(a, b, (x, y) => x.Location == y.Location && x.Stride == y.Stride && x.StepRate == y.StepRate && SameElements(x.Elements, y.Elements));

    public static int Hash(EquatableArray<ResourceLayoutDescription> layouts)
    {
        HashCode hash = new();
        foreach (ResourceLayoutDescription layout in layouts)
        {
            hash.Add(layout.Set);
            foreach (ResourceLayoutElementDescription element in layout.Elements ?? [])
                hash.Add(element);
        }

        return hash.ToHashCode();
    }

    public static int Hash(EquatableArray<VertexLayoutDescription> layouts)
    {
        HashCode hash = new();
        foreach (VertexLayoutDescription layout in layouts)
        {
            hash.Add(layout.Location);
            hash.Add(layout.Stride);
            hash.Add(layout.StepRate);
            foreach (VertexElementDescription element in layout.Elements ?? [])
                hash.Add(element);
        }

        return hash.ToHashCode();
    }

    private static bool Same<T>(EquatableArray<T> a, EquatableArray<T> b, Func<T, T, bool> equal)
    {
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (!equal(a[i], b[i]))
                return false;
        }

        return true;
    }

    private static bool SameElements<T>(T[]? a, T[]? b)
    {
        a ??= [];
        b ??= [];
        return a.AsSpan().SequenceEqual(b);
    }
}
