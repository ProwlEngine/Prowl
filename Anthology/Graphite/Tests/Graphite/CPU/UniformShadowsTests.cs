using System;
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
        shadows.UsePlan(Plan(new UniformBlockField("V", 16, 16, UniformScalarType.Float4)));

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
    public void ChangingPlanReplaysAppliedValuesInCallOrder()
    {
        UniformShadows shadows = new();
        PropertySet early = new();
        early.SetFloat("A", 1);
        early.SetFloat("B", 5);
        shadows.Apply(early);
        shadows.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1)));
        PropertySet defaults = new();
        defaults.SetFloat("B", 2);
        shadows.ApplyDefaults(defaults);
        PropertySet late = new();
        late.SetFloat("A", 3);
        shadows.Apply(late);

        shadows.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1), new UniformBlockField("B", 4, 4, UniformScalarType.Float1)));

        Assert.Equal(new float[] { 3, 2 }, Floats(shadows[0]));

        UniformShadows switching = new();
        UniformBindingPlan both = Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1), new UniformBlockField("B", 4, 4, UniformScalarType.Float1));
        UniformBindingPlan other = Plan(new UniformBlockField("B", 0, 4, UniformScalarType.Float1));
        switching.UsePlan(both);
        PropertySet old = new();
        old.SetFloat("A", 1);
        old.SetFloat("B", 1);
        switching.Apply(old);
        PropertySet whole = new();
        whole.SetFloat("A", 2);
        whole.SetFloat("B", 2);
        switching.Apply(whole);
        PropertySet lastWrite = new();
        lastWrite.SetFloat("B", 7);
        switching.Apply(lastWrite);

        switching.UsePlan(other);
        Assert.Equal(new float[] { 7 }, Floats(switching[0]));

        switching.UsePlan(both);
        Assert.Equal(new float[] { 2, 7 }, Floats(switching[0]));

        UniformShadows layered = new();
        layered.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1)));
        PropertySet baseline = new();
        baseline.SetFloat("A", 1);
        PropertySet userValue = new();
        userValue.SetFloat("A", 2);

        layered.ApplyDefaults(baseline);
        Assert.Equal(1f, System.BitConverter.ToSingle(layered[0].Data));

        layered.Apply(userValue);
        Assert.Equal(2f, System.BitConverter.ToSingle(layered[0].Data));
    }

    private static float[] Floats(ShadowBlock block)
    {
        ReadOnlySpan<byte> bytes = block.Cache != null ? block.Cache.Bytes.AsSpan(0, (int)block.Size) : block.Data;
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes).ToArray();
    }

    [Fact]
    public void PartialWriteOnACachedBlockStartsFromTheCachedBytes()
    {
        UniformShadows shadows = new();
        shadows.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1), new UniformBlockField("B", 4, 4, UniformScalarType.Float1)));
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

        UniformShadows reused = new();
        reused.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1), new UniformBlockField("B", 4, 4, UniformScalarType.Float1)));
        PropertySet whole = new();
        whole.SetFloat("A", 1);
        whole.SetFloat("B", 2);

        reused.Apply(whole);
        PackedBlock? firstCache = reused[0].Cache;
        reused[0].Dirty = false;
        reused.Apply(whole);

        Assert.NotNull(firstCache);
        Assert.Same(firstCache, reused[0].Cache);
        Assert.True(reused[0].Dirty);

        whole.SetFloat("B", 3);
        reused.Apply(whole);

        Assert.Equal(3f, System.BitConverter.ToSingle(reused[0].Cache!.Bytes, 4));
    }
}
