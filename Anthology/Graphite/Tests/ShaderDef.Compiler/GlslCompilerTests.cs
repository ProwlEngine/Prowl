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

        Dictionary<string, string> merged = new() { ["Camera.glsl"] = "uniform vec3 eye;\nuniform float zoom;\n" };
        string mergedMessage = Glsl.Fails("""
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
            """, merged).Message;
        Assert.Contains("Test:5:", mergedMessage);
        Assert.Contains("active", mergedMessage);

        Dictionary<string, string> broken = new() { ["Broken.glsl"] = "float helper()\n{\n    return nope;\n}\n" };
        string includeMessage = Glsl.Fails("""
            Vertex { void main() { gl_Position = vec4(0); } }
            Fragment
            {
                #include "Broken.glsl"
                out vec4 color;
                void main() { color = vec4(helper()); }
            }
            """, broken).Message;
        Assert.Contains("Broken.glsl:3:", includeMessage);
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

    const string ScanVertex = "Vertex { void main() { gl_Position = vec4(0); } }\nFragment { out vec4 color; void main() { color = vec4(1); } }\n";

    static readonly Dictionary<string, string> s_surface = new()
    {
        ["Lights.glsl"] = "#pragma variant global LIGHT_MASKS\n#pragma global vec4 _LightTint\n",
        ["Surface.glsl"] = """
            #pragma variant _SURFACE OPAQUE CUTOUT
            #if defined(ROLE_FORWARD) && !defined(ROLE_UNLIT)
                #define ROLE_LIT
            #endif
            #ifdef ROLE_LIT
                #include "Lights.glsl"
            #endif
            """,
    };

    [Fact]
    public void Axes_BehindAFalsePassDefine_AreLeftOut()
    {
        using GlslShaderCompiler compiler = Glsl.Compiler(s_surface);
        ShaderPass depth = Glsl.Pass("Shared\n{\n#define ROLE_DEPTH\n#include \"Surface.glsl\"\n}\n" + ScanVertex);
        ShaderPass unlit = Glsl.Pass("Shared\n{\n#define ROLE_FORWARD\n#define ROLE_UNLIT\n#include \"Surface.glsl\"\n}\n" + ScanVertex);
        ShaderPass lit = Glsl.Pass("Shared\n{\n#define ROLE_FORWARD\n#include \"Surface.glsl\"\n}\n" + ScanVertex);

        Assert.Equal(["_SURFACE"], compiler.GetAxes(depth).Select(a => a.Name));
        Assert.Empty(compiler.GetGlobals(depth));
        Assert.Equal(["_SURFACE"], compiler.GetAxes(unlit).Select(a => a.Name));
        Assert.Equal(["_SURFACE", "LIGHT_MASKS"], compiler.GetAxes(lit).Select(a => a.Name));
        Assert.Equal([new ShaderGlobal("_LightTint", "vec4")], compiler.GetGlobals(lit));

        Dictionary<string, string> variantFiles = new()
        {
            ["Clip.glsl"] = "#pragma variant CLIP_FADE\n",
            ["Surface.glsl"] = """
                #pragma variant _SURFACE OPAQUE CUTOUT
                #ifdef _SURFACE_CUTOUT
                    #define ALPHA
                    #include "Clip.glsl"
                #endif
                #ifdef ALPHA
                    #pragma variant DITHER
                #elif defined(ROLE_DEPTH)
                    #pragma variant DEPTH_BIAS
                #else
                    #pragma variant NEVER
                #endif
                """,
        };
        using GlslShaderCompiler variants = Glsl.Compiler(variantFiles);
        ShaderPass variantPass = Glsl.Pass("Shared\n{\n#define ROLE_DEPTH\n#include \"Surface.glsl\"\n}\n" + ScanVertex);
        Assert.Equal(["_SURFACE", "CLIP_FADE", "DITHER", "DEPTH_BIAS"], variants.GetAxes(variantPass).Select(a => a.Name));
        variants.CompileProgram(variantPass, [new Keyword("_SURFACE", "CUTOUT"), new Keyword("CLIP_FADE", "true"), new Keyword("DITHER", "true")]);

        Assert.Equal(["HIDDEN", "LATE"], compiler.GetAxes(Glsl.Pass("Shared\n{\n#ifdef LATE\n#pragma variant HIDDEN\n#endif\n#pragma variant LATE\n}\n" + ScanVertex)).Select(a => a.Name));

        const string stages = "Shared\n{\n#ifdef GRAPHITE_FRAGMENT\n#pragma variant FRAGMENT_ONLY\n#endif\n#ifndef GRAPHITE_COMPUTE\n#pragma variant NOT_COMPUTE\n#endif\n}\n";
        Assert.Equal(["NOT_COMPUTE", "FRAGMENT_ONLY"], compiler.GetAxes(Glsl.Pass(stages + ScanVertex)).Select(a => a.Name));
        Assert.Empty(compiler.GetAxes(Glsl.Pass(stages + "Compute { layout(local_size_x = 1) in; void main() {} }")));

        IReadOnlyList<VariantSpace> unreadable = compiler.GetAxes(Glsl.Pass("""
            Shared
            {
                #define LEVEL 2
                #if LEVEL == 2
                    #pragma variant EQUALS
                #else
                    #pragma variant OTHERWISE
                #endif
                #if FUNCTION(1)
                    #pragma variant CALLED
                #endif
                #ifdef GL_EXT_null_initializer
                    #pragma variant EXTENSION
                #endif
                #if 0
                    #pragma variant ZERO
                #elif LEVEL && !defined(MISSING)
                    #pragma variant LEVEL_SET
                #else
                    #pragma variant ELSE
                #endif
            }
            """ + ScanVertex));
        Assert.Equal(["EQUALS", "OTHERWISE", "CALLED", "EXTENSION", "LEVEL_SET"], unreadable.Select(a => a.Name));

        using GlslShaderCompiler hosted = new(new GlslCompilerOptions { Defines = { ["HOST_FLAG"] = "" } });
        Assert.Equal(["ON_HOST"], hosted.GetAxes(Glsl.Pass("Shared\n{\n#ifdef HOST_FLAG\n#pragma variant ON_HOST\n#endif\n#ifndef HOST_FLAG\n#pragma variant OFF_HOST\n#endif\n}\n" + ScanVertex)).Select(a => a.Name));
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

        string plain = System.IO.Path.Combine(_directory, "plain");
        using (GlslShaderCompiler compiler = Cached(plain))
        {
            compiler.CompileProgram(Glsl.Pass(Glsl.Fullscreen), []);
            compiler.CompileProgram(Glsl.Pass(Glsl.Fullscreen.Replace("vec4(1)", "vec4(0.25)")), []);
        }

        Assert.Equal(2, Stored(plain).Length);
    }
}
