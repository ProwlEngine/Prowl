using Xunit;

namespace Prowl.Graphite.Tests;

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
    public void ApplyOther_CopiesEntriesFromOther()
    {
        PropertySet target = new();
        target.SetFloat("a", 1.0f);

        PropertySet other = new();
        other.SetFloat("b", 2.0f);
        other.SetFloat("c", 3.0f);

        target.ApplyOther(other);

        Assert.Equal(3, target.EntryCount);
    }

    [Fact]
    public void ApplyOther_UniformOnly_DoesNotBumpResourceVersion()
    {
        PropertySet target = new();
        PropertySet other = new();
        other.SetFloat("a", 1.0f);

        target.ApplyOther(other);

        // Only resource entries dirty the version; a uniform-only merge must leave it alone.
        Assert.Equal(0u, target.ResourceVersion);
    }

    [Fact]
    public void ApplyOther_OverwritesExistingEntries()
    {
        PropertySet target = new();
        target.SetFloat("shared", 1.0f);
        target.SetFloat("kept", 9.0f);

        PropertySet other = new();
        other.SetFloat("shared", 2.0f);

        target.ApplyOther(other);

        // "shared" is replaced rather than duplicated, and "kept" survives the merge untouched.
        Assert.Equal(2, target.EntryCount);
    }

    [Fact]
    public void ApplyOther_EmptyOther_IsNoOp()
    {
        PropertySet target = new();
        target.SetFloat("a", 1.0f);

        target.ApplyOther(new PropertySet());

        Assert.Equal(1, target.EntryCount);
        Assert.Equal(0u, target.ResourceVersion);
    }

    [Fact]
    public void ApplyOther_DoesNotMutateSource()
    {
        PropertySet target = new();
        target.SetFloat("a", 1.0f);

        PropertySet other = new();
        other.SetFloat("b", 2.0f);

        target.ApplyOther(other);

        Assert.Equal(1, other.EntryCount);
        Assert.Equal(0u, other.ResourceVersion);
    }

    [Fact]
    public void ApplyOther_Self_LeavesSetUnchanged()
    {
        PropertySet set = new();
        set.SetFloat("a", 1.0f);
        set.SetFloat("b", 2.0f);

        set.ApplyOther(set);

        Assert.Equal(2, set.EntryCount);
    }

    [Fact]
    public void Clear_ThenReuse_AcceptsNewEntries()
    {
        PropertySet set = new();
        set.SetFloat("a", 1.0f);
        set.Clear();

        set.SetFloat("b", 2.0f);

        Assert.Equal(1, set.EntryCount);
        Assert.Equal(1u, set.ResourceVersion);
    }
}
