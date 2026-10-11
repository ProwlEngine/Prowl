using System.Collections.Generic;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

public class UniformShadowsTests
{
    private static UniformBindingPlan Plan(params UniformBlockField[] fields)
        => UniformBindingPlan.Build([new ResourceLayoutDescription(new ResourceLayoutElementDescription("B", ResourceKind.UniformBuffer, ShaderStages.Fragment, 0, uniformFields: fields))]);

    [Fact]
    public void BlitWritesAtFieldOffsetAndClearsTheRestOfAShorterValue()
    {
        UniformShadows shadows = new();
        PropertySet merged = new();
        shadows.UsePlan(Plan(new UniformBlockField("V", 16, 16, UniformScalarType.Float4)), merged);

        PropertySet wide = new();
        wide.SetFloat4("V", new Float4(1, 2, 3, 4));
        shadows.Apply(wide);
        PropertySet narrow = new();
        narrow.SetFloat("V", 9);
        shadows.Apply(narrow);

        float[] floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(shadows[0].Data).ToArray();
        Assert.Equal(new float[] { 0, 0, 0, 0, 9, 0, 0, 0 }, floats);
        Assert.True(shadows[0].Dirty);
    }

    [Fact]
    public void ChangingPlanRebuildsFromTheMergedTable()
    {
        UniformShadows shadows = new();
        PropertySet merged = new();
        PropertySet source = new();
        source.SetFloat("A", 5);
        merged.MergeFrom(source, new List<PropertyID>(), new HashSet<PropertyID>());

        shadows.UsePlan(Plan(new UniformBlockField("Other", 0, 4, UniformScalarType.Float1)), merged);
        shadows.UsePlan(Plan(new UniformBlockField("A", 4, 4, UniformScalarType.Float1)), merged);

        float[] floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(shadows[0].Data).ToArray();
        Assert.Equal(5f, floats[1]);
    }

    [Fact]
    public void FullCoverageSetBecomesTheBlockSourceAndIsReusedUntilItChanges()
    {
        UniformShadows shadows = new();
        shadows.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1), new UniformBlockField("B", 4, 4, UniformScalarType.Float1)), new PropertySet());
        PropertySet full = new();
        full.SetFloat("A", 1);
        full.SetFloat("B", 2);

        shadows.Apply(full);
        PackedBlock? first = shadows[0].Cache;
        shadows[0].Dirty = false;
        shadows.Apply(full);

        Assert.NotNull(first);
        Assert.Same(first, shadows[0].Cache);
        Assert.True(shadows[0].Dirty);

        full.SetFloat("B", 3);
        shadows.Apply(full);

        Assert.Equal(3f, System.BitConverter.ToSingle(shadows[0].Cache!.Bytes, 4));
    }

    [Fact]
    public void PartialWriteOnACachedBlockStartsFromTheCachedBytes()
    {
        UniformShadows shadows = new();
        shadows.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1), new UniformBlockField("B", 4, 4, UniformScalarType.Float1)), new PropertySet());
        PropertySet full = new();
        full.SetFloat("A", 1);
        full.SetFloat("B", 2);
        shadows.Apply(full);
        shadows.Apply(full);
        PropertySet partial = new();
        partial.SetFloat("B", 7);

        shadows.Apply(partial);

        Assert.Null(shadows[0].Cache);
        float[] floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(shadows[0].Data).ToArray();
        Assert.Equal(new float[] { 1, 7 }, floats);
    }
}
