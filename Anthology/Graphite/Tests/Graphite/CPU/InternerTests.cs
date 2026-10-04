#nullable enable

using System.Threading;

using Xunit;

namespace Prowl.Graphite.Tests;

public class InternerTests
{
    private static Interner NewInterner() => new();

    [Fact]
    public void Intern_DistinctInstancesWithEqualContent_ReturnSameValue()
    {
        Interner interner = NewInterner();

        string first = new string(new[] { 'a', 'b', 'c' });
        string second = new string(new[] { 'a', 'b', 'c' });

        int a = interner.Intern(first);
        int b = interner.Intern(second);
        int c = interner.Intern(first);

        Assert.NotSame(first, second);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Intern_SameKey_ReturnsSameValue()
    {
        Interner interner = NewInterner();

        int a = interner.Intern("hello");
        int b = interner.Intern("hello");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Intern_DifferentKeys_ReturnDistinctValues()
    {
        Interner interner = NewInterner();

        int a = interner.Intern("a");
        int b = interner.Intern("b");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Intern_MintsMonotonically()
    {
        Interner interner = NewInterner();

        int a = interner.Intern("a");
        int b = interner.Intern("b");
        int c = interner.Intern("c");

        Assert.Equal(1, a);
        Assert.Equal(2, b);
        Assert.Equal(3, c);
    }

    [Fact]
    public void Intern_RepeatedKey_DoesNotMintNewValue()
    {
        Interner interner = NewInterner();

        int a = interner.Intern("a");
        interner.Intern("a");
        int b = interner.Intern("b");

        // "a" was only minted once, so "b" should be the second issued id.
        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void TryGetKey_KnownValue_ReturnsOriginalKey()
    {
        Interner interner = NewInterner();

        int id = interner.Intern("roundtrip");

        Assert.True(interner.TryGetKey(id, out string? key));
        Assert.Equal("roundtrip", key);
    }

    [Fact]
    public void TryGetKey_UnknownValue_ReturnsFalse()
    {
        Interner interner = NewInterner();
        interner.Intern("known");

        Assert.False(interner.TryGetKey(9999, out string? key));
        Assert.Null(key);
    }

    [Fact]
    public void Intern_Concurrent_SameKeyYieldsSingleValue()
    {
        Interner interner = NewInterner();
        const int threads = 16;

        int[] results = new int[threads];
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
