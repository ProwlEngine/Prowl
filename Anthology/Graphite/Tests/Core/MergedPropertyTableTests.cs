using Xunit;

namespace Prowl.Veldrid.Tests;

// Covers the internal per-command-buffer merge of PropertySet entries. Uniform-only paths are
// exercised here (no GPU needed); the resource-version bump for buffer/texture/sampler entries
// is covered by the GPU resource tests. Reaches internal types via InternalsVisibleTo.
public class MergedPropertyTableTests
{
    [Fact]
    public void IngestFrom_Uniforms_CopiesEntries()
    {
        PropertySet src = new();
        src.SetFloat("a", 1.0f);
        src.SetInt("b", 2);

        MergedPropertyTable table = new();
        table.IngestFrom(src, ChangeMask.Uniforms);

        Assert.Equal(2, table.Entries.Count);
    }

    [Fact]
    public void IngestFrom_Uniforms_DoesNotBumpResourceVersion()
    {
        PropertySet src = new();
        src.SetFloat("a", 1.0f);

        MergedPropertyTable table = new();
        table.IngestFrom(src, ChangeMask.Uniforms);

        Assert.Equal(0u, table.ResourceVersion);
    }

    [Fact]
    public void IngestFrom_None_IsNoOp()
    {
        PropertySet src = new();
        src.SetFloat("a", 1.0f);

        MergedPropertyTable table = new();
        table.IngestFrom(src, ChangeMask.None);

        Assert.Empty(table.Entries);
    }

    [Fact]
    public void IngestFrom_ResourcesMask_SkipsUniformEntries()
    {
        PropertySet src = new();
        src.SetFloat("a", 1.0f);

        MergedPropertyTable table = new();
        table.IngestFrom(src, ChangeMask.Resources);

        Assert.Empty(table.Entries);
    }

    [Fact]
    public void IngestFrom_SameName_LastWriterWins()
    {
        PropertyID key = "shared";

        PropertySet first = new();
        first.SetFloat(key, 1.0f);

        PropertySet second = new();
        second.SetFloat(key, 2.0f);

        MergedPropertyTable table = new();
        table.IngestFrom(first, ChangeMask.Both);
        table.IngestFrom(second, ChangeMask.Both);

        Assert.Single(table.Entries);
        Assert.Same(second.RawEntries[key], table.Entries[key]);
    }

    [Fact]
    public void Clear_EmptiesEntries_AndBumpsResourceVersion()
    {
        PropertySet src = new();
        src.SetFloat("a", 1.0f);

        MergedPropertyTable table = new();
        table.IngestFrom(src, ChangeMask.Both);
        uint before = table.ResourceVersion;

        table.Clear();

        Assert.Empty(table.Entries);
        Assert.NotEqual(before, table.ResourceVersion);
    }
}
