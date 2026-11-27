using Xunit;

namespace Prowl.Graphite.Tests;

public class UniformBindingPlanTests
{
    private static ResourceLayoutElementDescription Block(string name, int binding, params UniformBlockField[] fields)
        => new(name, ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment, binding, uniformFields: fields);

    private static UniformBlockField F(string name, uint offset, uint size, UniformScalarType type) => new(name, offset, size, type);

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
