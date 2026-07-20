#nullable enable

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.RenderGraph.Tests;

public class RenderResourceIDTests
{
    [Fact]
    public void Intern_SameName_ReturnsSameId()
    {
        RenderResourceID a = RenderResourceID.Intern("SceneColor");
        RenderResourceID b = RenderResourceID.Intern("SceneColor");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Intern_DifferentNames_ReturnDistinctIds()
    {
        RenderResourceID a = RenderResourceID.Intern("DepthBufferA");
        RenderResourceID b = RenderResourceID.Intern("DepthBufferB");

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void ImplicitConversion_MatchesIntern()
    {
        RenderResourceID fromString = "GBufferNormals";
        RenderResourceID fromIntern = RenderResourceID.Intern("GBufferNormals");

        Assert.Equal(fromIntern, fromString);
    }

    [Fact]
    public void InternedId_IsValid()
    {
        RenderResourceID id = RenderResourceID.Intern("ValidResource");

        Assert.True(id.IsValid);
    }

    [Fact]
    public void Default_IsInvalid()
    {
        RenderResourceID id = default;

        Assert.False(id.IsValid);
    }

    [Fact]
    public void ToStringOverload_RoundTripsOriginalName()
    {
        RenderResourceID id = RenderResourceID.Intern("RoundTripName");

        Assert.Equal("RoundTripName", RenderResourceID.ToString(id));
    }

    [Fact]
    public void ToStringOverload_DefaultId_ReturnsNull()
    {
        Assert.Null(RenderResourceID.ToString(default));
    }

    [Fact]
    public void ToString_IsHotPathSafeAndDoesNotReturnTheName()
    {
        RenderResourceID id = RenderResourceID.Intern("PrettyName");

        Assert.Equal($"RenderResourceID({id.Value})", id.ToString());
    }
}
