namespace Prowl.Graphite.Vk;

// Fixed-size "array" types, useful for constructing inputs
// to some Vulkan functions without allocating and pinning a real array.

internal struct FixedArray2<T> where T : struct
{
    public T First;
    public T Second;

    public FixedArray2(T first, T second) { First = first; Second = second; }

    public readonly uint Count => 2;
}

internal struct FixedArray3<T> where T : struct
{
    public T First;
    public T Second;
    public T Third;

    public FixedArray3(T first, T second, T third) { First = first; Second = second; Third = third; }

    public readonly uint Count => 3;
}

internal struct FixedArray4<T> where T : struct
{
    public T First;
    public T Second;
    public T Third;
    public T Fourth;

    public FixedArray4(T first, T second, T third, T fourth) { First = first; Second = second; Third = third; Fourth = fourth; }

    public readonly uint Count => 4;
}

internal struct FixedArray5<T> where T : struct
{
    public T First;
    public T Second;
    public T Third;
    public T Fourth;
    public T Fifth;

    public FixedArray5(T first, T second, T third, T fourth, T fifth) { First = first; Second = second; Third = third; Fourth = fourth; Fifth = fifth; }

    public readonly uint Count => 5;
}

internal struct FixedArray6<T> where T : struct
{
    public T First;
    public T Second;
    public T Third;
    public T Fourth;
    public T Fifth;
    public T Sixth;

    public FixedArray6(T first, T second, T third, T fourth, T fifth, T sixth) { First = first; Second = second; Third = third; Fourth = fourth; Fifth = fifth; Sixth = sixth; }

    public readonly uint Count => 6;
}
