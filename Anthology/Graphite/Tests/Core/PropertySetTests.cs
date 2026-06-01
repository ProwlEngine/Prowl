using Xunit;

namespace Prowl.Veldrid.Tests;

// Covers the CPU-only surface of PropertySet: scalar uniform writes, entry de-duplication
// by name, the resource-version counter, and Clear. Resource setters (buffer/texture/sampler)
// require a live GraphicsDevice and are exercised by the GPU resource tests instead.
public class PropertySetTests
{
    [Fact]
    public void NewSet_IsEmpty()
    {
        PropertySet set = new();

        Assert.Equal(0, set.EntryCount);
        Assert.Equal(0u, set.ResourceVersion);
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
        set.SetDouble("d", 3.0);

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
    public void UniformWrite_DoesNotBumpResourceVersion()
    {
        PropertySet set = new();

        set.SetFloat("a", 1.0f);
        set.SetInt("b", 2);

        // Only resource (buffer/texture/sampler) writes advance the resource version.
        Assert.Equal(0u, set.ResourceVersion);
    }

    [Fact]
    public void Clear_RemovesEntries_AndBumpsResourceVersion()
    {
        PropertySet set = new();
        set.SetFloat("a", 1.0f);
        set.SetFloat("b", 2.0f);

        set.Clear();

        Assert.Equal(0, set.EntryCount);
        Assert.Equal(1u, set.ResourceVersion);
    }

    [Fact]
    public void Clear_OnEmptySet_StillBumpsResourceVersion()
    {
        PropertySet set = new();

        set.Clear();

        Assert.Equal(1u, set.ResourceVersion);
    }

    [Fact]
    public void CapacityCtor_BehavesLikeDefault()
    {
        PropertySet set = new(8);

        Assert.Equal(0, set.EntryCount);
        set.SetFloat("a", 1.0f);
        Assert.Equal(1, set.EntryCount);
    }
}
