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
    }

    [Fact]
    public void Compute_EqualInputs_AreEqual()
    {
        ProgramKey a = ProgramKey.Compute([Stage(ShaderStages.Vertex, "vs", 1, 2)]);
        ProgramKey b = ProgramKey.Compute([Stage(ShaderStages.Vertex, "vs", 1, 2)]);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
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
    }
}
