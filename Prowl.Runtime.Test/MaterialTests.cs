// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public class MaterialTests
{
    [Fact]
    public void StateHash_MatchesForIdenticalMaterials()
    {
        var a = new Material(Shader.LoadDefault(DefaultShader.Standard));
        var b = new Material(Shader.LoadDefault(DefaultShader.Standard));
        a.SetFloat("_Metallic", 0.5f);
        b.SetFloat("_Metallic", 0.5f);

        Assert.Equal(a.GetStateHash(), b.GetStateHash());
    }

    [Fact]
    public void StateHash_DiffersByShader()
    {
        var standard = new Material(Shader.LoadDefault(DefaultShader.Standard));
        var unlit = new Material(Shader.LoadDefault(DefaultShader.Unlit));

        Assert.NotEqual(standard.GetStateHash(), unlit.GetStateHash());
    }

    [Fact]
    public void StateHash_DiffersByEnabledKeyword()
    {
        var a = new Material(Shader.LoadDefault(DefaultShader.Standard));
        var b = new Material(Shader.LoadDefault(DefaultShader.Standard));
        ulong before = a.GetStateHash();

        a.SetKeyword("SOME_FEATURE", true);

        Assert.NotEqual(before, a.GetStateHash());
        Assert.NotEqual(a.GetStateHash(), b.GetStateHash());

        a.SetKeyword("SOME_FEATURE", false);
        Assert.Equal(b.GetStateHash(), a.GetStateHash());
    }
}
