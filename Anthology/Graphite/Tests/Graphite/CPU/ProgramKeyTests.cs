using Xunit;

namespace Prowl.Graphite.Tests;

public class ProgramKeyTests
{
    private static ShaderStageDescription Stage(ShaderStages stage, string entry, params byte[] bytes) =>
        new(stage, bytes, entry);

    [Fact]
    public void Compute_MatchesFixedDigest()
    {
        ProgramKey key = ProgramKey.Compute([Stage(ShaderStages.Compute, "main", 1, 2, 3, 4)]);

        Assert.Equal("3004609469D02DECFF51C6543D962D81E9C7B50C80F32AB90B83F59BDE5779C0", key.ToString());

        ProgramKey first = ProgramKey.Compute([Stage(ShaderStages.Vertex, "vs", 1, 2)]);
        ProgramKey second = ProgramKey.Compute([Stage(ShaderStages.Vertex, "vs", 1, 2)]);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Compute_DifferentStageEntryBytesOrOrder_Differ()
    {
        ShaderStageDescription vs = Stage(ShaderStages.Vertex, "vs", 1, 2);
        ShaderStageDescription fs = Stage(ShaderStages.Fragment, "fs", 3, 4);
        ProgramKey baseKey = ProgramKey.Compute([vs, fs]);

        Assert.NotEqual(baseKey, ProgramKey.Compute([fs, vs]));
        Assert.NotEqual(baseKey, ProgramKey.Compute([Stage(ShaderStages.Geometry, "vs", 1, 2), fs]));
        Assert.NotEqual(baseKey, ProgramKey.Compute([Stage(ShaderStages.Vertex, "vs2", 1, 2), fs]));
        Assert.NotEqual(baseKey, ProgramKey.Compute([Stage(ShaderStages.Vertex, "vs", 1, 9), fs]));

        ShaderStageDescription[] plain = [Stage(ShaderStages.Vertex, "vs", 1, 2)];

        Assert.Equal(ProgramKey.Compute(plain), ProgramKey.Compute(plain, []));
        Assert.NotEqual(ProgramKey.Compute(plain), ProgramKey.Compute(plain, [new SpecializationConstant(0, 1)]));
        Assert.NotEqual(ProgramKey.Compute(plain, [new SpecializationConstant(0, 1)]), ProgramKey.Compute(plain, [new SpecializationConstant(0, 2)]));
    }
}
