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
    }

    [Fact]
    public void SwitchingBackToAPlanKeepsWritesMadeAfterItsFullBlockSource()
    {
        UniformShadows shadows = new();
        UniformBindingPlan both = Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1), new UniformBlockField("B", 4, 4, UniformScalarType.Float1));
        UniformBindingPlan other = Plan(new UniformBlockField("B", 0, 4, UniformScalarType.Float1));
        shadows.UsePlan(both);
        PropertySet old = new();
        old.SetFloat("A", 1);
        old.SetFloat("B", 1);
        shadows.Apply(old);
        PropertySet full = new();
        full.SetFloat("A", 2);
        full.SetFloat("B", 2);
        shadows.Apply(full);
        PropertySet partial = new();
        partial.SetFloat("B", 7);
        shadows.Apply(partial);

        shadows.UsePlan(other);
        Assert.Equal(new float[] { 7 }, Floats(shadows[0]));

        shadows.UsePlan(both);
        Assert.Equal(new float[] { 2, 7 }, Floats(shadows[0]));
    }

    [Fact]
    public void ClearDropsAppliedValuesForLaterPlans()
    {
        UniformShadows shadows = new();
        shadows.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1)));
        PropertySet source = new();
        source.SetFloat("A", 5);
        shadows.Apply(source);

        shadows.Clear();
        shadows.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1)));

        Assert.Equal(new float[] { 0 }, Floats(shadows[0]));
    }

    private static float[] Floats(ShadowBlock block)
    {
        ReadOnlySpan<byte> bytes = block.Cache != null ? block.Cache.Bytes.AsSpan(0, (int)block.Size) : block.Data;
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes).ToArray();
    }

    [Fact]
    public void FullCoverageSetBecomesTheBlockSourceAndIsReusedUntilItChanges()
    {
        UniformShadows shadows = new();
        shadows.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1), new UniformBlockField("B", 4, 4, UniformScalarType.Float1)));
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
    }

    [Fact]
    public void DefaultsAreOverwrittenByLaterUserValuesInCallOrder()
    {
        UniformShadows shadows = new();
        shadows.UsePlan(Plan(new UniformBlockField("A", 0, 4, UniformScalarType.Float1)));
        PropertySet defaults = new();
        defaults.SetFloat("A", 1);
        PropertySet user = new();
        user.SetFloat("A", 2);

        shadows.ApplyDefaults(defaults);
        Assert.Equal(1f, System.BitConverter.ToSingle(shadows[0].Data));

        shadows.Apply(user);
        Assert.Equal(2f, System.BitConverter.ToSingle(shadows[0].Data));
    }
}
