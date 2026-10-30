using System.Collections.Generic;
using System.Linq;
using System.Text;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


internal static class Glsl
{
    public const string Fullscreen = """
        Vertex
        {
            void main() { gl_Position = vec4(0, 0, 0, 1); }
        }
        Fragment
        {
            out vec4 color;
            void main() { color = vec4(1); }
        }
        """;

    public static ShaderPass Pass(string source, int line = 1) => new() { State = new PassState(), Source = source, SourceName = "Test", SourceLine = line };

    public static GlslShaderCompiler Compiler(Dictionary<string, string>? files = null) => new(new GlslCompilerOptions
    {
        ResolveInclude = (name, _) => files != null && files.TryGetValue(name, out string? source) ? source : null,
    });

    public static GlslProgram Compile(string source, Dictionary<string, string>? files = null, params Keyword[] combo)
    {
        using GlslShaderCompiler compiler = Compiler(files);
        return compiler.CompileProgram(Pass(source), combo);
    }

    public static ShaderDescription Describe(string source, params Keyword[] combo) => Compile(source, null, combo).Description;

    public static ShaderCompileException Fails(string source, Dictionary<string, string>? files = null)
        => Assert.Throws<ShaderCompileException>(() => Compile(source, files));

    public static string[] Rewritten(string source, params Keyword[] combo)
    {
        using GlslShaderCompiler compiler = Compiler();
        (List<GlslStageSource> stages, GlslProgramLayout layout) = compiler.Prepare(Pass(source), combo, new StringBuilder());
        return [.. stages.Select(s => s.Rewrite(layout))];
    }

    public static IEnumerable<ResourceLayoutElementDescription> Elements(ShaderDescription d) => d.ResourceLayouts.SelectMany(l => l.Elements);

    public static ResourceLayoutElementDescription Element(ShaderDescription d, string name) => Elements(d).Single(e => PropertyID.ToString(e.Name) == name);

    public static uint SetOf(ShaderDescription d, string name) => d.ResourceLayouts.Single(l => l.Elements.Any(e => PropertyID.ToString(e.Name) == name)).Set;

    public static UniformBlockField Field(ShaderDescription d, string name, string block = "$Global") => Element(d, block).UniformFields.Single(f => PropertyID.ToString(f.Name) == name);
}


public class GlslCompilerTests
{
    [Fact]
    public void VertexAndFragment_CompileToTwoStagesNamedMain()
    {
        ShaderDescription d = Glsl.Describe(Glsl.Fullscreen);

        Assert.Equal([ShaderStages.Vertex, ShaderStages.Fragment], d.Stages.Select(s => s.Stage));
        Assert.All(d.Stages, s => Assert.Equal("main", s.EntryPoint));
        Assert.All(d.Stages, s => Assert.Equal(0x07230203u, System.BitConverter.ToUInt32(s.ShaderBytes, 0)));
    }


    [Fact]
    public void Compute_ReportsItsThreadGroupSize()
    {
        GlslProgram program = Glsl.Compile("""
            Compute
            {
                layout(local_size_x = 8, local_size_y = 4) in;
                layout(rgba8) uniform writeonly image2D _Output;
                void main() { imageStore(_Output, ivec2(gl_GlobalInvocationID.xy), vec4(1)); }
            }
            """);

        Assert.Equal((8u, 4u, 1u), program.ThreadGroupSize);
        Assert.Equal(ShaderStages.Compute, Assert.Single(program.Description.Stages).Stage);
        Assert.Equal(ResourceKind.TextureReadWrite, Glsl.Element(program.Description, "_Output").Kind);
        Assert.Equal(8u, program.ToCompute().ThreadGroupSizeX);
    }


    [Fact]
    public void Compute_WithAnotherStage_Fails()
    {
        string message = Glsl.Fails("Compute { layout(local_size_x = 1) in; void main() {} } Vertex { void main() {} }").Message;
        Assert.Contains("Compute section must be the only stage", message);
    }


    [Fact]
    public void FragmentWithoutVertex_Fails()
    {
        Assert.Contains("needs a Vertex section", Glsl.Fails("Fragment { out vec4 c; void main() { c = vec4(1); } }").Message);
    }


    [Fact]
    public void UnknownSection_FailsNamingIt()
    {
        Assert.Contains("'Geometry'", Glsl.Fails("Geometry { void main() {} }").Message);
    }


    [Fact]
    public void VersionDirective_Fails()
    {
        Assert.Contains("remove #version", Glsl.Fails("Vertex { #version 450\n void main() {} }").Message);
    }


    [Fact]
    public void Errors_ReportTheDocumentLine()
    {
        const string source = """
            Vertex
            {
                void main() { gl_Position = vec4(0); }
            }
            Fragment
            {
                out vec4 color;
                void main() { color = missing; }
            }
            """;

        using GlslShaderCompiler compiler = Glsl.Compiler();
        ShaderCompileException error = Assert.Throws<ShaderCompileException>(() => compiler.CompileProgram(Glsl.Pass(source, line: 10), []));

        Assert.Contains("Test:17:", error.Message);
        Assert.Contains("missing", error.Message);
    }


    [Fact]
    public void LooseUniforms_MergedFromAnInclude_ReportTheirOwnLines()
    {
        Dictionary<string, string> files = new() { ["Camera.glsl"] = "uniform vec3 eye;\nuniform float zoom;\n" };
        string message = Glsl.Fails("""
            Shared
            {
                #include "Camera.glsl"
                uniform float scale;
                uniform int active;
            }
            Vertex { void main() { gl_Position = vec4(eye * zoom * scale, float(active)); } }
            Fragment
            {
                out vec4 color;
                void main() { color = vec4(1); }
            }
            """, files).Message;

        Assert.Contains("Test:5:", message);
        Assert.Contains("active", message);
    }


    [Fact]
    public void Includes_ResolveThroughTheHost_AndReportTheirOwnLines()
    {
        Dictionary<string, string> files = new() { ["Broken.glsl"] = "float helper()\n{\n    return nope;\n}\n" };
        string message = Glsl.Fails("""
            Vertex { void main() { gl_Position = vec4(0); } }
            Fragment
            {
                #include "Broken.glsl"
                out vec4 color;
                void main() { color = vec4(helper()); }
            }
            """, files).Message;

        Assert.Contains("Broken.glsl:3:", message);
    }


    [Fact]
    public void MissingInclude_FailsNamingIt()
    {
        Assert.Contains("Nowhere.glsl", Glsl.Fails("Vertex { #include \"Nowhere.glsl\"\n void main() {} }").Message);
    }


    [Fact]
    public void InactiveInclude_IsNeverResolved()
    {
        Glsl.Compile("Vertex {\n#if 0\n#include \"Nowhere.glsl\"\n#endif\nvoid main() { gl_Position = vec4(0); } }");
    }


    [Fact]
    public void PragmaOnce_IncludesAFileOnlyOnce()
    {
        Dictionary<string, string> files = new() { ["Common.glsl"] = "#pragma once\nfloat helper() { return 1.0; }\n" };
        GlslProgram program = Glsl.Compile("""
            Shared
            {
                #include "Common.glsl"
            }
            Vertex
            {
                #include "Common.glsl"
                void main() { gl_Position = vec4(helper()); }
            }
            Fragment
            {
                #include "Common.glsl"
                out vec4 color;
                void main() { color = vec4(helper()); }
            }
            """, files);

        Assert.Equal("", program.Warnings);
    }


    [Fact]
    public void MissingLibraryInclude_Fails()
    {
        Assert.Contains("Graphite/Nothing.glsl", Glsl.Fails("Vertex { #include \"Graphite/Nothing.glsl\"\n void main() {} }").Message);
    }


    [Fact]
    public void Axes_ComeFromPragmasInOrder_IncludingIncludedFiles()
    {
        Dictionary<string, string> files = new() { ["Fog.glsl"] = "#pragma variant global FOG OFF LINEAR EXP\n" };
        using GlslShaderCompiler compiler = Glsl.Compiler(files);
        IReadOnlyList<VariantSpace> axes = compiler.GetAxes(Glsl.Pass("""
            Shared
            {
                #pragma variant _NORMALMAP
                #include "Fog.glsl"
            }
            Fragment
            {
                // #pragma variant COMMENTED
                #pragma variant _SURFACE OPAQUE CUTOUT
                #pragma variant _NORMALMAP
            }
            """));

        Assert.Equal(["_NORMALMAP", "FOG", "_SURFACE"], axes.Select(a => a.Name));
        Assert.True(axes[0].IsToggle);
        Assert.Equal(VariantSpace.ToggleValues, axes[0].Values);
        Assert.Equal(VariantScope.Global, axes[1].Scope);
        Assert.Equal(["OFF", "LINEAR", "EXP"], axes[1].Values);
        Assert.Equal(VariantScope.Material, axes[2].Scope);
        Assert.False(axes[2].IsToggle);
    }


    [Fact]
    public void Globals_ComeFromPragmas_IncludingIncludedFiles_AndAreRemovedBeforeCompiling()
    {
        Dictionary<string, string> files = new() { ["Wind.glsl"] = "#pragma global vec4 _Wind _Gust\n" };
        using GlslShaderCompiler compiler = Glsl.Compiler(files);
        ShaderPass pass = Glsl.Pass("""
            Shared
            {
                #pragma global float _Noise
                #include "Wind.glsl"
                #pragma global vec4 _Wind
            }
            Compute
            {
                layout(local_size_x = 1) in;
                void main() {}
            }
            """);

        Assert.Equal([new ShaderGlobal("_Noise", "float"), new ShaderGlobal("_Wind", "vec4"), new ShaderGlobal("_Gust", "vec4")], compiler.GetGlobals(pass));
        compiler.CompileProgram(pass, []);
    }


    [Fact]
    public void Globals_OutsideAMaterial_AreLooseUniformsOfTheStagesReadingThem()
    {
        ShaderDescription d = Glsl.Describe("""
            Shared
            {
                #pragma global vec4 _Tint
                #pragma global ivec3 _Cell
                #pragma global float _Unread
            }
            Vertex
            {
                void main() { gl_Position = vec4(vec3(_Cell), 1.0); }
            }
            Fragment
            {
                out vec4 color;
                void main() { color = _Tint; }
            }
            """);

        Assert.Equal(UniformScalarType.Float4, Glsl.Field(d, "_Tint").Type);
        Assert.Equal(UniformScalarType.Int3, Glsl.Field(d, "_Cell").Type);
        Assert.DoesNotContain(Glsl.Element(d, "$Global").UniformFields, f => PropertyID.ToString(f.Name) == "_Unread");
    }


    [Fact]
    public void Axes_DeclaredTwiceDifferently_Fail()
    {
        using GlslShaderCompiler compiler = Glsl.Compiler();
        Assert.Throws<ShaderCompileException>(() => compiler.GetAxes(Glsl.Pass("Vertex { #pragma variant A X Y\n#pragma variant A X Z\n }")));
    }


    [Fact]
    public void Axes_WithOneValue_Fail()
    {
        using GlslShaderCompiler compiler = Glsl.Compiler();
        Assert.Contains("single value", Assert.Throws<ShaderCompileException>(() => compiler.GetAxes(Glsl.Pass("Vertex { #pragma variant A X\n }"))).Message);
    }


    const string VariantShader = """
        Vertex { void main() { gl_Position = vec4(0); } }
        Fragment
        {
            #pragma variant _TINT
            #pragma variant _MODE A B
            out vec4 color;
            #ifdef _TINT
            uniform vec4 _Tint;
            #endif
            #ifdef _MODE_B
            uniform vec4 _ModeB;
            #endif
            void main()
            {
                color = vec4(0);
            #ifdef _TINT
                color += _Tint;
            #endif
            #ifdef _MODE_B
                color += _ModeB;
            #endif
            }
        }
        """;


    [Fact]
    public void Variants_DefineTogglesAndEnumValues()
    {
        ShaderDescription d = Glsl.Describe(VariantShader, new Keyword("_TINT", "true"), new Keyword("_MODE", "B"));

        Assert.Equal(0u, Glsl.Field(d, "_Tint").Offset);
        Assert.Equal(16u, Glsl.Field(d, "_ModeB").Offset);
    }


    [Fact]
    public void Variants_OffByDefault_LeaveTheirCodeOut()
    {
        ShaderDescription d = Glsl.Describe(VariantShader, new Keyword("_TINT", "false"), new Keyword("_MODE", "A"));

        Assert.Empty(Glsl.Elements(d));
    }


    [Fact]
    public void StageDefines_SelectPerStageCode()
    {
        ShaderDescription d = Glsl.Describe("""
            Shared
            {
                #ifdef GRAPHITE_FRAGMENT
                uniform vec4 _FragmentOnly;
                #endif
            }
            Vertex
            {
                uniform vec4 _VertexOnly;
                void main() { gl_Position = _VertexOnly; }
            }
            Fragment
            {
                out vec4 color;
                void main() { color = _FragmentOnly; }
            }
            """);

        ResourceLayoutElementDescription global = Glsl.Element(d, "$Global");
        Assert.Equal(ShaderStages.Vertex | ShaderStages.Fragment, global.Stages);
        Assert.Equal(["_VertexOnly", "_FragmentOnly"], global.UniformFields.Select(f => PropertyID.ToString(f.Name)));
    }


    [Fact]
    public void VariantPragmas_AreNotSeenByTheGlslCompiler()
    {
        Assert.Equal("", Glsl.Compile(VariantShader).Warnings);
    }
}


public class GlslCompilerCacheTests : System.IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GraphiteVariantCache", System.Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_directory))
            System.IO.Directory.Delete(_directory, true);
    }

    private GlslShaderCompiler Cached(string? directory = null, Dictionary<string, string>? files = null) => new(new GlslCompilerOptions
    {
        CacheDirectory = directory ?? _directory,
        ResolveInclude = (name, _) => files != null && files.TryGetValue(name, out string? source) ? source : null,
    });

    private string[] Stored(string? directory = null) => System.IO.Directory.GetFiles(directory ?? _directory, "*.variant");

    private static byte[] Fragment(GlslProgram program) => program.Description.Stages.Single(s => s.Stage == ShaderStages.Fragment).ShaderBytes;

    private const string Toggled = """
        Shared
        {
            #pragma variant _UNUSED
            #pragma variant _HALF
        }
        Vertex
        {
            void main() { gl_Position = vec4(0, 0, 0, 1); }
        }
        Fragment
        {
            out vec4 color;
            void main()
            {
            #ifdef _HALF
                color = vec4(0.5);
            #else
                color = vec4(1);
            #endif
            }
        }
        """;

    [Fact]
    public void FinishedVariants_AreStored_AndAnotherCompilerReadsThemBack()
    {
        using (GlslShaderCompiler first = Cached())
            first.CompileProgram(Glsl.Pass(Glsl.Fullscreen), []);
        string stored = Assert.Single(Stored());

        // The stored entry swapped for another shader's shows the second compiler took it from the folder
        string other = System.IO.Path.Combine(_directory, "other");
        GlslProgram half;
        using (GlslShaderCompiler elsewhere = Cached(other))
            half = elsewhere.CompileProgram(Glsl.Pass(Glsl.Fullscreen.Replace("vec4(1)", "vec4(0.5)")), []);
        System.IO.File.Copy(Assert.Single(Stored(other)), stored, overwrite: true);

        using GlslShaderCompiler second = Cached();
        Assert.Equal(Fragment(half), Fragment(second.CompileProgram(Glsl.Pass(Glsl.Fullscreen), [])));
    }

    [Fact]
    public void DifferentSources_GetDifferentEntries()
    {
        using GlslShaderCompiler compiler = Cached();
        compiler.CompileProgram(Glsl.Pass(Glsl.Fullscreen), []);
        compiler.CompileProgram(Glsl.Pass(Glsl.Fullscreen.Replace("vec4(1)", "vec4(0.25)")), []);

        Assert.Equal(2, Stored().Length);
    }

    [Fact]
    public void ChangedIncludes_GetDifferentEntries()
    {
        const string source = """
            Vertex
            {
                void main() { gl_Position = vec4(0, 0, 0, 1); }
            }
            Fragment
            {
                #include "Shade.glsl"
                out vec4 color;
                void main() { color = Shade(); }
            }
            """;
        using (GlslShaderCompiler compiler = Cached(files: new() { ["Shade.glsl"] = "vec4 Shade() { return vec4(1); }" }))
            compiler.CompileProgram(Glsl.Pass(source), []);
        using (GlslShaderCompiler compiler = Cached(files: new() { ["Shade.glsl"] = "vec4 Shade() { return vec4(0.5); }" }))
            compiler.CompileProgram(Glsl.Pass(source), []);

        Assert.Equal(2, Stored().Length);
    }

    [Fact]
    public void VariantsPreprocessingTheSame_CompileOnce()
    {
        using GlslShaderCompiler compiler = Glsl.Compiler();
        ShaderPass pass = Glsl.Pass(Toggled);
        GlslProgram plain = compiler.CompileProgram(pass, []);

        Assert.Same(Fragment(plain), Fragment(compiler.CompileProgram(pass, [new Keyword("_UNUSED", "true")])));
        Assert.NotSame(Fragment(plain), Fragment(compiler.CompileProgram(pass, [new Keyword("_HALF", "true")])));
    }
}


public class VariantKeyTests
{
    [Fact]
    public void AnUnknownValue_TakesItsAxisFirstValue_AndKeepsTheOtherAxes()
    {
        const string source = """
            Shared
            {
                #pragma variant global _VIEW ALBEDO NORMALS
                #pragma variant _CUTOUT
            }
            Vertex
            {
                void main() { gl_Position = vec4(0, 0, 0, 1); }
            }
            """;
        using GlslShaderCompiler compiler = Glsl.Compiler();
        PassVariants variants = Glsl.Pass(source).Variants(compiler, GraphicsBackend.Vulkan);
        int cutout = variants.KeyOf([new Keyword("_CUTOUT", "true")]);

        Assert.NotEqual(0, cutout);
        Assert.Equal(cutout, variants.KeyOf([new Keyword("_VIEW", "FURNACE"), new Keyword("_CUTOUT", "true")]));
    }
}
