#nullable enable

using System.Threading;

using Xunit;

namespace Prowl.Veldrid.Tests;

public class InternerTests
{
    private static Interner<string, IntId> NewInterner()
    {
        int counter = 0;
        return new Interner<string, IntId>(_ => new IntId(Interlocked.Increment(ref counter)));
    }

    private readonly struct IntId : System.IEquatable<IntId>
    {
        public readonly int Value;
        public IntId(int value) { Value = value; }
        public bool Equals(IntId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is IntId o && Equals(o);
        public override int GetHashCode() => Value;
    }

    [Fact]
    public void Intern_SameKey_ReturnsSameValue()
    {
        Interner<string, IntId> interner = NewInterner();

        IntId a = interner.Intern("hello");
        IntId b = interner.Intern("hello");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Intern_DifferentKeys_ReturnDistinctValues()
    {
        Interner<string, IntId> interner = NewInterner();

        IntId a = interner.Intern("a");
        IntId b = interner.Intern("b");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Intern_MintsMonotonically()
    {
        Interner<string, IntId> interner = NewInterner();

        IntId a = interner.Intern("a");
        IntId b = interner.Intern("b");
        IntId c = interner.Intern("c");

        Assert.Equal(1, a.Value);
        Assert.Equal(2, b.Value);
        Assert.Equal(3, c.Value);
    }

    [Fact]
    public void Intern_RepeatedKey_DoesNotMintNewValue()
    {
        Interner<string, IntId> interner = NewInterner();

        IntId a = interner.Intern("a");
        interner.Intern("a");
        IntId b = interner.Intern("b");

        // "a" was only minted once, so "b" should be the second issued id.
        Assert.Equal(1, a.Value);
        Assert.Equal(2, b.Value);
    }

    [Fact]
    public void TryGetKey_KnownValue_ReturnsOriginalKey()
    {
        Interner<string, IntId> interner = NewInterner();

        IntId id = interner.Intern("roundtrip");

        Assert.True(interner.TryGetKey(id, out string? key));
        Assert.Equal("roundtrip", key);
    }

    [Fact]
    public void TryGetKey_UnknownValue_ReturnsFalse()
    {
        Interner<string, IntId> interner = NewInterner();
        interner.Intern("known");

        Assert.False(interner.TryGetKey(new IntId(9999), out string? key));
        Assert.Null(key);
    }

    [Fact]
    public void Ctor_NullIncrement_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => new Interner<string, IntId>(null!));
    }

    [Fact]
    public void Intern_Concurrent_SameKeyYieldsSingleValue()
    {
        Interner<string, IntId> interner = NewInterner();
        const int threads = 16;

        IntId[] results = new IntId[threads];
        using Barrier barrier = new(threads);

        System.Threading.Tasks.Parallel.For(0, threads, i =>
        {
            barrier.SignalAndWait();
            results[i] = interner.Intern("contended");
        });

        for (int i = 1; i < threads; i++)
            Assert.Equal(results[0], results[i]);
    }
}
