using System;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


// End-to-end coverage of ParsedShader.Parse composing all components together.
public class ShaderTests
{
    const string FullShader = """
        Shader "Example/Standard"
        {
            // line comment
            Properties
            {
                _Color("Tint", Color) = (1, 0.5, 0.25, 1)
                _Albedo("Albedo", Texture2D) = "white" {}
            }

            Pass 0
            {
                Name "Forward"
                Tags { "LightMode" = "ForwardBase" }

                Cull Back
                ZTest LessEqual
                Offset -1 -1   /* block comment */

                Stencil { Ref 2 Comp Equal Pass Keep }

                ShaderSource "Standard.slang"
                {
                    Vertex "vsMain"
                    Fragment "fsMain"
                }
            }

            Fallback "Hidden/Fallback"
        }
        """;


    [Fact]
    public void ParsesNamePropertiesPassesAndFallback()
    {
        ParsedShader shader = Parse.Shader(FullShader);

        Assert.Equal("Example/Standard", shader.Name);
        Assert.Equal("Hidden/Fallback", shader.Fallback);
        Assert.Equal(2, shader.Properties!.Length);
        Assert.Single(shader.Passes!);
        Assert.Equal("Forward", shader.Passes![0].Name);
    }


    [Fact]
    public void CommentsAreStripped()
    {
        // If line/block comments weren't stripped, parsing would have thrown long before this.
        ParsedShader shader = Parse.Shader(FullShader);

        Assert.Equal(new Float4(1, 0.5f, 0.25f, 1), shader.Properties![0].Value);
    }


    [Fact]
    public void OptionalPropertiesBlock_DefaultsToEmpty()
    {
        ParsedShader shader = Parse.Shader("""
            Shader "Min"
            {
                Pass { ShaderSource "s" { Vertex "v" Fragment "f" } }
                Fallback "fb"
            }
            """);

        Assert.Empty(shader.Properties!);
    }


    [Fact]
    public void MultiplePasses_AllParsedInOrder()
    {
        ParsedShader shader = Parse.Shader("""
            Shader "Multi"
            {
                Pass { Name "A" ShaderSource "s" { Vertex "v" Fragment "f" } }
                Pass { Name "B" ShaderSource "s" { Vertex "v" Fragment "f" } }
                Fallback "fb"
            }
            """);

        Assert.Equal(2, shader.Passes!.Length);
        Assert.Equal("A", shader.Passes![0].Name);
        Assert.Equal("B", shader.Passes![1].Name);
    }


    [Fact]
    public void MissingFallback_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Parse.Shader("""
            Shader "NoFallback"
            {
                Pass { ShaderSource "s" { Vertex "v" Fragment "f" } }
            }
            """));
    }
}
