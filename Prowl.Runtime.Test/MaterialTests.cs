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

    // A reimport reads new values straight into the existing material.
    [Fact]
    public void StateHash_RefreshesAfterDeserializeInPlace()
    {
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        ulong before = material.GetStateHash();

        material._properties.SetFloat("_Metallic", 0.75f);
        material.OnAfterDeserialize();

        Assert.NotEqual(before, material.GetStateHash());
    }

    [Fact]
    public void KeywordHash_TracksKeywordChanges_AndMatchesAFreshMaterial()
    {
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        ulong none = material.KeywordHash;

        material.SetKeyword("HAS_NORMALS", true);
        material.SetKeyword("GPU_INSTANCING", true);
        ulong both = material.KeywordHash;
        Assert.NotEqual(none, both);

        // The variant cache keys on this, so it must not depend on the order keywords were turned on
        var other = new Material(Shader.LoadDefault(DefaultShader.Standard));
        other.SetKeyword("GPU_INSTANCING", true);
        other.SetKeyword("HAS_NORMALS", true);
        Assert.Equal(both, other.KeywordHash);

        material.SetKeyword("GPU_INSTANCING", false);
        material.SetKeyword("HAS_NORMALS", false);
        Assert.Equal(none, material.KeywordHash);

        material.SetKeyword("HAS_NORMALS", true);
        var copy = new Material(material);
        Assert.Equal(material.KeywordHash, copy.KeywordHash);
    }
}
