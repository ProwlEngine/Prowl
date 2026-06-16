using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


// Tags are only reachable through a pass, so they are driven via ParsedPass.Parse.
public class TagsTests
{
    static Dictionary<string, string>? TagsOf(string pass) => Parse.Pass(pass).Tags;


    [Fact]
    public void NoTagsBlock_TagsNull()
    {
        ParsedPass pass = Parse.Pass("""
            Pass
            {
                ShaderSource "s" { Vertex "v" Fragment "f" }
            }
            """);

        Assert.Null(pass.Tags);
    }


    [Fact]
    public void SingleTag_Parsed()
    {
        Dictionary<string, string>? tags = TagsOf("""
            Pass
            {
                Tags { "LightMode" = "ForwardBase" }
                ShaderSource "s" { Vertex "v" Fragment "f" }
            }
            """);

        Assert.NotNull(tags);
        Assert.Single(tags!);
        Assert.Equal("ForwardBase", tags!["LightMode"]);
    }


    [Fact]
    public void MultipleTags_Parsed()
    {
        Dictionary<string, string>? tags = TagsOf("""
            Pass
            {
                Tags { "LightMode" = "ForwardBase" "Queue" = "Transparent" "RenderType" = "Opaque" }
                ShaderSource "s" { Vertex "v" Fragment "f" }
            }
            """);

        Assert.Equal(3, tags!.Count);
        Assert.Equal("ForwardBase", tags["LightMode"]);
        Assert.Equal("Transparent", tags["Queue"]);
        Assert.Equal("Opaque", tags["RenderType"]);
    }


    [Fact]
    public void EmptyTagsBlock_EmptyDictionary()
    {
        Dictionary<string, string>? tags = TagsOf("""
            Pass
            {
                Tags { }
                ShaderSource "s" { Vertex "v" Fragment "f" }
            }
            """);

        Assert.NotNull(tags);
        Assert.Empty(tags!);
    }
}
