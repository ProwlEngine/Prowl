using Xunit;

namespace Prowl.Graphite.Tests;

public class UniformBindingPlanTests
{
    private static ResourceLayoutElementDescription Block(string name, int binding, params UniformBlockField[] fields)
        => new(name, ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment, binding, uniformFields: fields);

    private static UniformBlockField F(string name, uint offset, uint size, UniformScalarType type) => new(name, offset, size, type);

    [Fact]
    public void FieldLookupFindsEveryBlockDeclaringTheName()
    {
        ResourceLayoutDescription[] layouts =
        [
            new(0, Block("A", 1, F("Shared", 0, 16, UniformScalarType.Float4), F("OnlyA", 16, 4, UniformScalarType.Float1))),
            new(1, Block("B", 0, F("Shared", 32, 16, UniformScalarType.Float4))),
        ];

        UniformBindingPlan plan = UniformBindingPlan.Build(layouts);

        Assert.True(plan.TryFind("Shared", out var shared));
        Assert.Equal(2, shared.Length);
        Assert.True(plan.TryFind("OnlyA", out var onlyA));
        Assert.Equal(new UniformFieldTarget(0, 16, 4), onlyA[0]);
        Assert.False(plan.TryFind("Missing", out _));
        Assert.Equal(new uint[] { 20, 48 }, plan.BlockSizes);
    }

    [Fact]
    public void DynamicOffsetOrderFollowsBindingIndex()
    {
        ResourceLayoutDescription[] layouts =
        [
            new(0, Block("A", 3, F("X", 0, 4, UniformScalarType.Float1)), Block("B", 1, F("Y", 0, 4, UniformScalarType.Float1))),
        ];

        UniformBindingPlan plan = UniformBindingPlan.Build(layouts);

        Assert.Equal(new[] { 1, 0 }, plan.DynamicOffsetOrder[0]);
    }
}
