using System;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class ShaderSourceTests
{
    [Fact]
    public void ParsesFileAndEntrypoints()
    {
        ParsedShaderSource s = Parse.Source("""
            ShaderSource "Standard.slang"
            {
                Vertex "vsMain"
                Fragment "fsMain"
            }
            """);

        Assert.Equal("Standard.slang", s.ShaderSourceFile);
        Assert.Equal("vsMain", s.VertexEntrypoint);
        Assert.Equal("fsMain", s.FragmentEntrypoint);
    }


    [Fact]
    public void Keywords_AreCaseInsensitive()
    {
        ParsedShaderSource s = Parse.Source("""
            shadersource "s"
            {
                vertex "v"
                fragment "f"
            }
            """);

        Assert.Equal("s", s.ShaderSourceFile);
        Assert.Equal("v", s.VertexEntrypoint);
        Assert.Equal("f", s.FragmentEntrypoint);
    }


    [Fact]
    public void MissingFragment_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Parse.Source("""
            ShaderSource "s"
            {
                Vertex "v"
            }
            """));
    }


    [Fact]
    public void MissingShaderSourceKeyword_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Parse.Source("""
            "s"
            {
                Vertex "v"
                Fragment "f"
            }
            """));
    }
}
