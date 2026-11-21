// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Runtime.Resources;

using Xunit;

using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Test;

/// <summary>
/// GPU backed resources must work without a device (headless or dedicated server): their CPU state works and the
/// Graphite objects stay absent.
/// </summary>
public class HeadlessGraphicsTests
{
    [Fact]
    public void Texture2D_LargeSize_DoesNotFailValidationHeadless()
    {
        var tex = new Texture2D(4096, 4096);
        Assert.Equal(4096u, tex.Width);
    }

    [Fact]
    public void Material_CreatesHeadless_WithoutThrowing()
    {
        var mat = new Material();
        Assert.NotNull(mat.Shader);
        Assert.Null(mat.GpuMaterial);
    }

    public static TheoryData<DefaultShader> AllDefaultShaders()
    {
        var data = new TheoryData<DefaultShader>();
        foreach (DefaultShader s in System.Enum.GetValues<DefaultShader>())
            data.Add(s);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllDefaultShaders))]
    public void DefaultShader_ParsesHeadless(DefaultShader shader)
    {
        Shader loaded = Shader.LoadDefault(shader);

        Assert.Null(loaded.Error);
        Assert.NotNull(loaded.GpuShader);
        Assert.NotEmpty(Passes(loaded));
    }

    [Theory]
    [MemberData(nameof(AllDefaultShaders))]
    public void DefaultShader_EveryPassCompiles(DefaultShader shader)
    {
        CompileEveryPass(Shader.LoadDefault(shader));
    }

    private const string GridInclude = "float GridShade(vec3 p) { return fract(p.x); }";
    private const string MultiLineGridInclude = "float GridA() { return 1.0; }\nfloat GridB() { return 2.0; }\nfloat GridC() { return 3.0; }\nfloat GridShade(vec3 p) { return fract(p.x); }\n";

    [Theory]
    [InlineData("#include \"Grid\"", "#include \"Grid\"")]
    [InlineData("#include \"Grid\"\n#include \"Grid\"", "#include \"Grid.glsl\"")]
    [InlineData("#include \"Grid\"\n#include \"Grid.glsl\"", "#include \"Grid\"")]
    [InlineData("#if 0\n#include \"Grid\"\n#endif\n#include \"Grid\"", "#include \"Grid\"")]
    [InlineData("#pragma variant FOO\n#ifdef FOO\n#include \"Grid\"\n#endif\n#include \"Grid\"", "#include \"Grid\"")]
    public void AnInclude_ReachesEveryStageOnce(string vertexIncludes, string fragmentIncludes)
    {
        CompileEveryVariant(WithGridIncludes($$"""
            Shader "Test/Includes"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        {{vertexIncludes}}
                        void main() { gl_Position = vec4(GridShade(vec3(0.5))); }
                    }
                    Fragment
                    {
                        {{fragmentIncludes}}
                        layout(location = 0) out vec4 color;
                        void main() { color = vec4(GridShade(vec3(0.5))); }
                    }
                    ENDGLSL
                }
            }
            """));
    }

    [Fact]
    public void ASurfaceShader_IncludesOneFileInItsSurfaceAndVertexBlocks()
    {
        CompileEveryPass(WithGridIncludes("""
            Shader "Test/Surface Includes"
            {
                Surface "Lit"
                {
                    GLSLPROGRAM
                    #include "Grid"
                    void Surface(inout PBR_Params p, Graphite_SurfaceData i) { p.baseColor = vec3(GridShade(i.vertexColor.rgb)); }
                    ENDGLSL

                    Vertex
                    {
                        GLSLPROGRAM
                        #include "Grid"
                        void SurfaceVertex(inout Graphite_SurfaceVertex v) { v.color.rgb *= GridShade(v.position); }
                        ENDGLSL
                    }
                }
            }
            """));
    }

    [Fact]
    public void AnIncludePastedOnce_CanHoldAWholeSection()
    {
        const string vertex = "Vertex\n{\n    void main() { gl_Position = vec4(0.0); }\n}";
        CompileEveryPass(Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Section Include"
            {
                Pass
                {
                    GLSLPROGRAM
                    #include "Vertex Section"
                    Fragment
                    {
                        layout(location = 0) out vec4 color;
                        void main() { color = vec4(1.0); }
                    }
                    ENDGLSL
                }
            }
            """, name => name == "Vertex Section" ? vertex : null)));
    }

    [Fact]
    public void IncludesThatIncludeEachOther_ExpandEachFileOnce()
    {
        // Y names X with .glsl, so the loop comes back to X under another name
        Dictionary<string, string> files = new() { ["X"] = "#include \"Y\"\nfloat FromX() { return 1.0; }", ["Y"] = "#include \"X.glsl\"\nfloat FromY() { return 2.0; }" };

        string expanded = Shader.ExpandIncludes("#include \"X\"", name => files.GetValueOrDefault(name.Replace(".glsl", "")));

        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(expanded, "FromX"));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(expanded, "FromY"));
    }

    [Fact]
    public void AnErrorInsideAProjectIncludePastedOnce_ReportsItsFileAndLine()
    {
        const string broken = "float Ok() { return 1.0; }\nfloat Broken() { return nope; }\n";
        Shader shader = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Broken Include Once"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Grid.glsl"
                        void main() { gl_Position = vec4(Ok()); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """, name => name == "Grid.glsl" ? broken : null));

        ShaderCompileException ex = CompileError(shader);
        Assert.Equal("Grid.glsl", ex.File);
        Assert.Equal(2, ex.Line);
    }

    [Fact]
    public void AnErrorInsideAProjectIncludePastedInMultipleStages_ReportsItsFileAndLine()
    {
        const string broken = "float Ok() { return 1.0; }\n#ifdef GRAPHITE_FRAGMENT\nfloat Broken() { return nope; }\n#endif\n";
        Shader shader = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Broken Include Guarded"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Grid.glsl"
                        void main() { gl_Position = vec4(Ok()); }
                    }
                    Fragment
                    {
                        #include "Grid.glsl"
                        layout(location = 0) out vec4 color;
                        void main() { color = vec4(Ok()); }
                    }
                    ENDGLSL
                }
            }
            """, name => name == "Grid.glsl" ? broken : null));

        ShaderCompileException ex = CompileError(shader);
        Assert.Equal("Grid.glsl", ex.File);
        Assert.Equal(3, ex.Line);
    }

    [Fact]
    public void AnErrorInTheMainShaderAfterAnInclude_ReportsTheOriginalLine()
    {
        Shader single = WithMultiLineGridIncludes("""
            Shader "Test/Main After Single"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Grid"
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException singleEx = CompileError(single);
        Assert.Equal("Test/Main After Single", singleEx.File);
        Assert.Equal(9, singleEx.Line);

        Shader duplicate = WithMultiLineGridIncludes("""
            Shader "Test/Main After Duplicate"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Grid"
                        #include "Grid.glsl"
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException duplicateEx = CompileError(duplicate);
        Assert.Equal("Test/Main After Duplicate", duplicateEx.File);
        Assert.Equal(10, duplicateEx.Line);

        Shader namedPass = WithMultiLineGridIncludes("""
            Shader "Test/Named Pass"
            {
                Pass
                {
                    Name "Forward"
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Grid"
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException namedEx = CompileError(namedPass);
        Assert.Equal("Test/Named Pass/Forward", namedEx.File);
        Assert.Equal(10, namedEx.Line);
    }

    [Fact]
    public void AnErrorInALaterStageOrPassAfterAnExpandedInclude_ReportsTheOriginalLine()
    {
        Shader laterStage = WithMultiLineGridIncludes("""
            Shader "Test/Later Stage"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Grid"
                        void main() { gl_Position = vec4(GridShade(vec3(0.5))); }
                    }
                    Fragment
                    {
                        layout(location = 0) out vec4 color;
                        void main() { color = vec4(nope); }
                    }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException stageEx = CompileError(laterStage);
        Assert.Equal("Test/Later Stage", stageEx.File);
        Assert.Equal(14, stageEx.Line);

        Shader laterPass = WithMultiLineGridIncludes("""
            Shader "Test/Later Pass"
            {
                Pass
                {
                    Name "First"
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Grid"
                        void main() { gl_Position = vec4(GridShade(vec3(0.5))); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
                Pass
                {
                    Name "SecondPass"
                    GLSLPROGRAM
                    Vertex
                    {
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException passEx = CompileError(laterPass);
        Assert.Equal("Test/Later Pass/SecondPass", passEx.File);
        Assert.Equal(21, passEx.Line);
    }

    [Fact]
    public void NestedIncludes_ReportEachFilesOwnLineNumbers()
    {
        const string shaderTemplate = """
            Shader "Test/Nested"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Outer.glsl"
                        void main() { gl_Position = vec4(OuterOk()); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """;

        Dictionary<string, string> innerBrokenFiles = new()
        {
            ["Inner.glsl"] = "float InnerOk() { return 1.0; }\nfloat InnerBad() { return nope; }\n",
            ["Outer.glsl"] = "#include \"Inner.glsl\"\nfloat OuterOk() { return InnerOk(); }\n",
        };
        ShaderCompileException innerEx = CompileError(Shader.FromSource(Shader.ExpandIncludes(shaderTemplate, name => innerBrokenFiles.GetValueOrDefault(name))));
        Assert.Equal("Inner.glsl", innerEx.File);
        Assert.Equal(2, innerEx.Line);

        Dictionary<string, string> outerBrokenFiles = new()
        {
            ["Inner.glsl"] = "float InnerA() { return 1.0; }\nfloat InnerB() { return 2.0; }\n",
            ["Outer.glsl"] = "#include \"Inner.glsl\"\nfloat OuterOk() { return InnerA() + InnerB(); }\nfloat OuterBad() { return nope; }\n",
        };
        ShaderCompileException outerEx = CompileError(Shader.FromSource(Shader.ExpandIncludes(shaderTemplate, name => outerBrokenFiles.GetValueOrDefault(name))));
        Assert.Equal("Outer.glsl", outerEx.File);
        Assert.Equal(3, outerEx.Line);

        Dictionary<string, string> mainBrokenFiles = new()
        {
            ["Inner.glsl"] = "float InnerA() { return 1.0; }\nfloat InnerB() { return 2.0; }\n",
            ["Outer.glsl"] = "#include \"Inner.glsl\"\nfloat OuterOk() { return InnerA() + InnerB(); }\n",
        };
        Shader mainBroken = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Nested"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Outer.glsl"
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """, name => mainBrokenFiles.GetValueOrDefault(name)));
        ShaderCompileException mainEx = CompileError(mainBroken);
        Assert.Equal("Test/Nested", mainEx.File);
        Assert.Equal(9, mainEx.Line);
    }

    [Fact]
    public void ConditionalInclude_PreservesLinesInElseAndAfterEndif()
    {
        Shader inElse = WithMultiLineGridIncludes("""
            Shader "Test/Conditional Else"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #if 0
                        #include "Grid"
                        #else
                        void helper() { float bad = nope; }
                        #endif
                        void main() { gl_Position = vec4(0.0); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException elseEx = CompileError(inElse);
        Assert.Equal("Test/Conditional Else", elseEx.File);
        Assert.Equal(11, elseEx.Line);

        Shader afterEndif = WithMultiLineGridIncludes("""
            Shader "Test/Conditional After"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #if 0
                        #include "Grid"
                        #endif
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException afterEx = CompileError(afterEndif);
        Assert.Equal("Test/Conditional After", afterEx.File);
        Assert.Equal(11, afterEx.Line);

        Shader variant = WithMultiLineGridIncludes("""
            Shader "Test/Variant Conditional"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #pragma variant FOO
                        #ifdef FOO
                        #include "Grid"
                        #endif
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """);

        ShaderPass pass = Passes(variant)[0];
        foreach (string val in new[] { "false", "true" })
        {
            ShaderCompileException variantEx = Assert.Throws<ShaderCompileException>(() =>
                Graphics.Compiler.CompileProgram(pass, [new Keyword("FOO", val)]));
            Assert.Equal("Test/Variant Conditional", variantEx.File);
            Assert.Equal(12, variantEx.Line);
        }
    }

    [Fact]
    public void SurfaceShaderWithInclude_ReportsIncludeAndMainShaderLines()
    {
        const string broken = "float Ok() { return 1.0; }\nfloat Broken() { return nope; }\n";
        Shader brokenInclude = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Surface Broken Include"
            {
                Surface "Lit"
                {
                    GLSLPROGRAM
                    #include "Grid.glsl"
                    void Surface(inout PBR_Params p, Graphite_SurfaceData i) { p.baseColor = vec3(Ok()); }
                    ENDGLSL
                }
            }
            """, name => name == "Grid.glsl" ? broken : null));

        ShaderCompileException includeEx = CompileError(brokenInclude);
        Assert.Equal("Grid.glsl", includeEx.File);
        Assert.Equal(2, includeEx.Line);

        Shader brokenSurface = WithMultiLineGridIncludes("""
            Shader "Test/Surface Broken"
            {
                Surface "Lit"
                {
                    GLSLPROGRAM
                    #include "Grid"
                    void Surface(inout PBR_Params p, Graphite_SurfaceData i)
                    {
                        p.baseColor = vec3(nope);
                    }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException surfaceEx = CompileError(brokenSurface);
        Assert.Equal("Test/Surface Broken", surfaceEx.File);
        Assert.Equal(9, surfaceEx.Line);
    }

    [Fact]
    public void WholeSectionInclude_ReportsSectionIncludeAndLaterStageLines()
    {
        const string brokenVertex = "Vertex\n{\n    void main() { gl_Position = vec4(nope); }\n}";
        Shader brokenSection = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Broken Section Include"
            {
                Pass
                {
                    GLSLPROGRAM
                    #include "Vertex Section"
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """, name => name == "Vertex Section" ? brokenVertex : null));

        ShaderCompileException sectionEx = CompileError(brokenSection);
        Assert.Equal("Vertex Section", sectionEx.File);
        Assert.Equal(3, sectionEx.Line);

        const string validVertex = "Vertex\n{\n    void main()\n    {\n        gl_Position = vec4(0.0);\n    }\n}";
        Shader laterStage = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Section Later"
            {
                Pass
                {
                    GLSLPROGRAM
                    #include "Vertex Section"
                    Fragment
                    {
                        layout(location = 0) out vec4 color;
                        void main() { color = vec4(nope); }
                    }
                    ENDGLSL
                }
            }
            """, name => name == "Vertex Section" ? validVertex : null));

        ShaderCompileException laterEx = CompileError(laterStage);
        Assert.Equal("Test/Section Later", laterEx.File);
        Assert.Equal(10, laterEx.Line);
    }

    [Fact]
    public void ComputeShaderWithInclude_ReportsIncludeAndMainLines()
    {
        const string broken = "float Ok() { return 1.0; }\nfloat Broken() { return nope; }\n";
        string expandedBroken = Shader.ExpandIncludes("""
            #pragma kernel Main 64
            #include "Grid.glsl"
            void Main() { float x = Ok(); }
            """, name => name == "Grid.glsl" ? broken : null, "MyCompute");

        ComputeShader brokenCompute = ComputeShader.FromSource("MyCompute", expandedBroken);
        ShaderPass brokenPass = new() { State = new PassState(), Source = brokenCompute.KernelSource(0), SourceName = "MyCompute/Main" };
        ShaderCompileException includeEx = Assert.Throws<ShaderCompileException>(() => Graphics.Compiler.CompileProgram(brokenPass, []));
        Assert.Equal("Grid.glsl", includeEx.File);
        Assert.Equal(2, includeEx.Line);

        string expandedMain = Shader.ExpandIncludes("""
            #pragma kernel Main 64
            #include "Grid.glsl"
            void Main() { float x = nope; }
            """, name => name == "Grid.glsl" ? MultiLineGridInclude : null, "MyCompute");

        ComputeShader mainCompute = ComputeShader.FromSource("MyCompute", expandedMain);
        ShaderPass mainPass = new() { State = new PassState(), Source = mainCompute.KernelSource(0), SourceName = "MyCompute/Main" };
        ShaderCompileException mainEx = Assert.Throws<ShaderCompileException>(() => Graphics.Compiler.CompileProgram(mainPass, []));
        Assert.Equal("MyCompute", mainEx.File);
        Assert.Equal(3, mainEx.Line);
    }

    [Fact]
    public void IncludeNamesWithSpacesOrBackslashesAndNoOpIncludes_WorkProperly()
    {
        const string broken = "float Ok() { return 1.0; }\nfloat Broken() { return nope; }\n";
        Shader escaped = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Escaped Include"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Sub\Grid.glsl"
                        void main() { gl_Position = vec4(Ok()); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """, name => name == @"Sub\Grid.glsl" ? broken : null));

        ShaderCompileException ex = CompileError(escaped);
        Assert.Equal("Sub/Grid.glsl", ex.File);
        Assert.Equal(2, ex.Line);

        Shader withSpaces = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Spaced Include"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Occlusion Grid.glsl"
                        void main() { gl_Position = vec4(Ok()); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """, name => name == "Occlusion Grid.glsl" ? broken : null));

        ShaderCompileException spacesEx = CompileError(withSpaces);
        Assert.Equal("Occlusion Grid.glsl", spacesEx.File);
        Assert.Equal(2, spacesEx.Line);

        const string unchangedSource = """
            Shader "Test/No Project Includes"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #include "Graphite/Vertex.glsl"
                        void main() { gl_Position = vec4(0.0); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """;
        Assert.Same(unchangedSource, Shader.ExpandIncludes(unchangedSource, _ => null));
    }

    [Fact]
    public void ConditionalInclude_WithMultilineCommentOnClosingDirective_PreservesLines()
    {
        Shader endifComment = WithMultiLineGridIncludes("""
            Shader "Test/Endif Block Comment"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #if 0
                        #include "Grid"
                        #endif /* multiline
                        comment
                        */
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException endifEx = CompileError(endifComment);
        Assert.Equal("Test/Endif Block Comment", endifEx.File);
        Assert.Equal(13, endifEx.Line);

        Shader elseComment = WithMultiLineGridIncludes("""
            Shader "Test/Else Block Comment"
            {
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        #if 0
                        #include "Grid"
                        #else /* multiline
                        comment
                        */
                        void helper() { float bad = nope; }
                        #endif
                        void main() { gl_Position = vec4(0.0); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """);

        ShaderCompileException elseEx = CompileError(elseComment);
        Assert.Equal("Test/Else Block Comment", elseEx.File);
        Assert.Equal(13, elseEx.Line);
    }

    [Fact]
    public void ShaderDefLevelIncludes_ExpandOutsideGlslProgram_AndPreserveLines()
    {
        const string props = """
            Properties
            {
                _Tint("Tint", Float) = 1.0
                _Scale("Scale", Float) = 2.0
            }
            """;
        Shader withProps = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Included Props"
            {
                #include "Props.shaderpart"
                Pass
                {
                    GLSLPROGRAM
                    Vertex
                    {
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """, name => name == "Props.shaderpart" ? props : null));

        Assert.Null(withProps.Error);
        Assert.Contains("_Tint", PropertyNames(withProps));
        ShaderCompileException propsEx = CompileError(withProps);
        Assert.Equal("Test/Included Props", propsEx.File);
        Assert.Equal(9, propsEx.Line);

        const string brokenPass = """
            Pass
            {
                Name "IncludedPass"
                GLSLPROGRAM
                Vertex
                {
                    void main() { gl_Position = vec4(nope); }
                }
                Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                ENDGLSL
            }
            """;
        Shader includedBrokenPass = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Included Pass Broken"
            {
                #include "IncludedPass.shaderpart"
                Pass
                {
                    Name "Explicit"
                    GLSLPROGRAM
                    Vertex { void main() { gl_Position = vec4(0.0); } }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """, name => name == "IncludedPass.shaderpart" ? brokenPass : null));

        Assert.Null(includedBrokenPass.Error);
        ShaderCompileException includedPassEx = CompileError(includedBrokenPass);
        Assert.Equal("IncludedPass.shaderpart", includedPassEx.File);
        Assert.Equal(7, includedPassEx.Line);

        const string validPass = """
            Pass
            {
                Name "IncludedPass"
                GLSLPROGRAM
                Vertex
                {
                    void main() { gl_Position = vec4(0.0); }
                }
                Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                ENDGLSL
            }
            """;
        Shader afterIncludedPass = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/After Included Pass"
            {
                #include "IncludedPass.shaderpart"
                Pass
                {
                    Name "Explicit"
                    GLSLPROGRAM
                    Vertex
                    {
                        void main() { gl_Position = vec4(nope); }
                    }
                    Fragment { layout(location = 0) out vec4 color; void main() { color = vec4(1.0); } }
                    ENDGLSL
                }
            }
            """, name => name == "IncludedPass.shaderpart" ? validPass : null));

        Assert.Null(afterIncludedPass.Error);
        ShaderCompileException explicitPassEx = CompileError(afterIncludedPass);
        Assert.Equal("Test/After Included Pass/Explicit", explicitPassEx.File);
        Assert.Equal(10, explicitPassEx.Line);

        Shader onlyIncludedPass = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Only Included Pass"
            {
                #include "IncludedPass.shaderpart"
            }
            """, name => name == "IncludedPass.shaderpart" ? brokenPass : null));

        Assert.Null(onlyIncludedPass.Error);
        ShaderCompileException onlyPassEx = CompileError(onlyIncludedPass);
        Assert.Equal("IncludedPass.shaderpart", onlyPassEx.File);
        Assert.Equal(7, onlyPassEx.Line);

        Shader surfaceWithProps = Shader.FromSource(Shader.ExpandIncludes("""
            Shader "Test/Surface Included Props"
            {
                #include "Props.shaderpart"
                Surface "Lit"
                {
                    GLSLPROGRAM
                    void Surface(inout PBR_Params p, Graphite_SurfaceData i)
                    {
                        p.baseColor = vec3(nope);
                    }
                    ENDGLSL
                }
            }
            """, name => name == "Props.shaderpart" ? props : null));

        Assert.Null(surfaceWithProps.Error);
        Assert.Contains("_Tint", PropertyNames(surfaceWithProps));
        ShaderCompileException surfaceEx = CompileError(surfaceWithProps);
        Assert.Equal("Test/Surface Included Props", surfaceEx.File);
        Assert.Equal(9, surfaceEx.Line);
    }

    [Fact]
    public void CustomTonemapShaders_CompileInsideTheDisplayEncode()
    {
        string shader = Rendering.PostEffect.TonemapShader("void main() { vec4 c = Graphite_SampleSource(vUV); outColor = vec4(c.rgb / (1.0 + c.rgb), c.a); }");
        ShaderPass pass = FullscreenProgram.Pass(shader, "CustomTonemap");
        List<Keyword> combo = [];
        foreach (VariantSpace axis in Graphics.Compiler.GetAxes(pass))
            combo.Add(new Keyword(axis.Name, axis.Name == "GRAPHITE_NO_SOURCE" ? "false" : axis.Values[0]));
        Graphics.Compiler.CompileProgram(pass, [.. combo]);
    }

    [Fact]
    public void Lit_ExposesPbrFactors()
    {
        // The model importer sets these factors, so every glTF material without textures depends on them
        HashSet<string> names = PropertyNames(Shader.LoadDefault(DefaultShader.Lit));

        Assert.Contains("base_color", names);
        Assert.Contains("base_metalness", names);
        Assert.Contains("specular_roughness", names);
        Assert.Contains("ambient_occlusion_map", names);
        Assert.Contains("normal_scale", names);
        Assert.Contains("emission_color", names);
        Assert.Contains("emission_luminance", names);
        Assert.Contains("alpha_cutoff", names);
        Assert.Contains("geometry_opacity", names);
    }

    [Theory]
    [InlineData(DefaultShader.Lit)]
    [InlineData(DefaultShader.Unlit)]
    public void StandardShaders_TakeTheirSurfaceFromTheMaterial(DefaultShader shader)
    {
        Shader loaded = Shader.LoadDefault(shader);
        Assert.Contains(loaded.Keywords, axis => axis.Name == "_SURFACE");
        Assert.True(loaded.HasPass(PassType.Forward));
        Assert.True(loaded.HasPass(PassType.Prepass));
        Assert.True(loaded.HasPass(PassType.ShadowCaster));
        foreach (ShaderPass pass in Passes(loaded))
            Assert.False(pass.State.Set.HasFlag(PassStateFields.BlendEnabled));
    }

    [Theory]
    [MemberData(nameof(AllDefaultShaders))]
    public void DefaultShader_DeclaresAMenuPath(DefaultShader shader)
    {
        // The material inspector's shader picker builds its menu tree from these declarations
        string? path = Shader.ReadDeclaredPath(Shader.GetDefaultSource(shader));

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.DoesNotContain("//", path);
        Assert.False(path!.StartsWith('/') || path.EndsWith('/'));
    }

    [Fact]
    public void DefaultShaderMenuPaths_AreUnique()
    {
        var byPath = new Dictionary<string, DefaultShader>();

        foreach (DefaultShader s in System.Enum.GetValues<DefaultShader>())
        {
            string path = Shader.ReadDeclaredPath(Shader.GetDefaultSource(s))!;
            Assert.False(byPath.TryGetValue(path, out DefaultShader existing),
                $"'{s}' and '{existing}' both declare the path '{path}'.");
            byPath[path] = s;
        }
    }

    [Theory]
    [InlineData("Shader \"Default/Standard\"\n\nProperties {}", "Default/Standard")]
    [InlineData("\uFEFFShader \"Default/Standard\"", "Default/Standard")]
    [InlineData("// a header comment\n// another\nShader \"A/B C\"", "A/B C")]
    [InlineData("/* block\n comment */\nShader \"X\"", "X")]
    [InlineData("   \n\t Shader   \"Spaced/Out\"", "Spaced/Out")]
    [InlineData("Properties { }", null)]
    [InlineData("", null)]
    [InlineData("Shader Default/Standard", null)]
    public void ReadDeclaredPath_HandlesRealSourceShapes(string source, string? expected)
    {
        Assert.Equal(expected, Shader.ReadDeclaredPath(source));
    }

    private static ShaderPass[] Passes(Shader shader) => shader.GpuShader!.Definition.Passes ?? [];

    // Resolves like the shader importer, as written or with .glsl added
    private static Shader WithGridIncludes(string source)
        => Shader.FromSource(Shader.ExpandIncludes(source, name => name is "Grid" or "Grid.glsl" ? GridInclude : null));

    private static Shader WithMultiLineGridIncludes(string source)
        => Shader.FromSource(Shader.ExpandIncludes(source, name => name is "Grid" or "Grid.glsl" ? MultiLineGridInclude : null));

    private static ShaderCompileException CompileError(Shader shader)
        => Assert.Throws<ShaderCompileException>(() => CompileEveryPass(shader));

    private static void CompileEveryPass(Shader shader) => Compile(shader, 1);

    private static void CompileEveryVariant(Shader shader) => Compile(shader, int.MaxValue);

    // Every pass with each axis's first valuesPerAxis values, in every combination
    private static void Compile(Shader shader, int valuesPerAxis)
    {
        Assert.Null(shader.Error);
        foreach (ShaderPass pass in Passes(shader))
        {
            List<Keyword[]> combos = [[]];
            foreach (VariantSpace axis in Graphics.Compiler.GetAxes(pass))
                combos = [.. combos.SelectMany(combo => axis.Values.Take(valuesPerAxis).Select(value => (Keyword[])[.. combo, new Keyword(axis.Name, value)]))];
            foreach (Keyword[] combo in combos)
                Graphics.Compiler.CompileProgram(pass, combo);
        }
    }

    private static HashSet<string> PropertyNames(Shader shader)
    {
        HashSet<string> names = [];
        foreach (Prowl.Graphite.Rendering.MaterialProperty p in shader.Properties)
            names.Add(p.Name.Text());
        return names;
    }
}
