using Xunit;

namespace Prowl.Graphite.Tests;

// Covers the CPU-only surface of PropertySet: scalar uniform writes, entry de-duplication
// by name, and Clear. Resource setters (buffer/texture/sampler)
// require a live GraphicsDevice and are exercised by the GPU resource tests instead.
public class PropertySetTests
{
    [Fact]
    public void NewSet_IsEmpty()
    {
        PropertySet set = new();

        Assert.Equal(0, set.EntryCount);
    }

    [Fact]
    public void SetFloat_AddsEntry()
    {
        PropertySet set = new();

        set.SetFloat("a", 1.0f);

        Assert.Equal(1, set.EntryCount);
    }

    [Fact]
    public void SetScalar_DistinctNames_AddDistinctEntries()
    {
        PropertySet set = new();

        set.SetFloat("f", 1.0f);
        set.SetInt("i", 2);
        set.SetFloat("d", 3.0f);

        Assert.Equal(3, set.EntryCount);
    }

    [Fact]
    public void SetFloat_SameName_OverwritesInPlace()
    {
        PropertySet set = new();

        set.SetFloat("dup", 1.0f);
        set.SetFloat("dup", 2.0f);

        Assert.Equal(1, set.EntryCount);
    }

    [Fact]
    public void Clear_RemovesEntries()
    {
        PropertySet set = new();
        set.SetFloat("a", 1.0f);
        set.SetFloat("b", 2.0f);

        set.Clear();

        Assert.Equal(0, set.EntryCount);
    }

    [Fact]
    public void CapacityCtor_BehavesLikeDefault()
    {
        PropertySet set = new(8);

        Assert.Equal(0, set.EntryCount);
        set.SetFloat("a", 1.0f);
        Assert.Equal(1, set.EntryCount);
    }

    [Fact]
    public void SetScalar_SameName_DifferentType_StaysOneEntry()
    {
        PropertySet set = new();

        set.SetFloat("v", 1.0f);
        set.SetInt("v", 2);

        // The entry is rewritten in place with the new scalar type rather than duplicated.
        Assert.Equal(1, set.EntryCount);
    }

    [Fact]
    public void Clear_ThenReuse_AcceptsNewEntries()
    {
        PropertySet set = new();
        set.SetFloat("a", 1.0f);
        set.Clear();

        set.SetFloat("b", 2.0f);

        Assert.Equal(1, set.EntryCount);
    }
}
