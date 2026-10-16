using Prowl.Prism.Compiler;
using Prowl.Prism.Glsl;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// <c>#include</c> is resolved by the host through an <see cref="IncludeCallback"/>. These tests cover the callback
/// contract, include-once, skipping includes in dead <c>#if</c> blocks, error paths, and the preprocess-only output.
/// </summary>
public sealed class IncludeTests
{
    const string Frag = "#version 450\n#include \"common.glsl\"\nlayout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(Twice(0.5)); }\n";
    const string CommonGlsl = "float Twice(float x) { return 2.0 * x; }\n";

    static IncludeCallback Resolver(IReadOnlyDictionary<string, string> files, List<(string Name, bool IsSystem, string From)> requests) =>
        (name, isSystem, from) =>
        {
            requests.Add((name, isSystem, from));
            return files.TryGetValue(name, out var content) ? new IncludeResult(name, content) : null;
        };

    [Fact]
    public void ResolvesQuotedIncludeThroughTheCallback()
    {
        var requests = new List<(string, bool, string)>();
        var files = new Dictionary<string, string> { ["common.glsl"] = CommonGlsl };

        byte[] bytes = GlslCompiler.CompileToBytes(Frag, ShaderStage.Fragment, includes: Resolver(files, requests));

        StructValidator.Valid(bytes);
        Assert.Equal([("common.glsl", false, "")], requests);
    }

    [Fact]
    public void AngleBracketIncludePassesTheSystemFlag()
    {
        var requests = new List<(string, bool, string)>();
        var files = new Dictionary<string, string> { ["lib/common.glsl"] = CommonGlsl };
        const string src = "#version 450\n#include <lib/common.glsl>\nlayout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(Twice(0.5)); }\n";

        StructValidator.Valid(GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: Resolver(files, requests)));

        Assert.Equal([("lib/common.glsl", true, "")], requests);
    }

    [Fact]
    public void NestedIncludeReportsItsRequestingFile()
    {
        var requests = new List<(string, bool, string)>();
        var files = new Dictionary<string, string>
        {
            ["outer.glsl"] = "#include \"inner.glsl\"\n",
            ["inner.glsl"] = CommonGlsl,
        };

        const string src = "#version 450\n#include \"outer.glsl\"\nlayout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(Twice(0.5)); }\n";

        StructValidator.Valid(GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: Resolver(files, requests)));

        Assert.Equal([("outer.glsl", false, ""), ("inner.glsl", false, "outer.glsl")], requests);
    }

    [Fact]
    public void EachFileIsEnteredOnce()
    {
        var files = new Dictionary<string, string>
        {
            ["common.glsl"] = CommonGlsl,
            ["a.glsl"] = "#include \"common.glsl\"\n",
            ["b.glsl"] = "#include \"common.glsl\"\n",
        };
        // common.glsl defines a function. Entering it twice would be a redefinition error.
        const string src = "#version 450\n#include \"common.glsl\"\n#include \"a.glsl\"\n#include \"b.glsl\"\n"
            + "layout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(Twice(0.5)); }\n";

        var result = GlslCompiler.Preprocess(src, includes: Resolver(files, []));

        StructValidator.Valid(GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: Resolver(files, [])));
        Assert.Equal(["common.glsl", "a.glsl", "b.glsl"], result.IncludedFiles);
    }

    [Fact]
    public void IncludeInsideAFalseIfIsNotResolved()
    {
        var requests = new List<(string, bool, string)>();
        const string src = "#version 450\n#if 0\n#include \"missing.glsl\"\n#endif\n"
            + "layout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(1.0); }\n";

        StructValidator.Valid(GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: Resolver(new Dictionary<string, string>(), requests)));

        Assert.Empty(requests);
    }

    [Fact]
    public void IncludeInADeadBranchDoesNotMarkTheFileAsIncluded()
    {
        // The dead include must not count as an entry. The later live include still has to enter the file.
        var files = new Dictionary<string, string> { ["common.glsl"] = CommonGlsl };
        const string src = "#version 450\n#if 0\n#include \"common.glsl\"\n#endif\n#include \"common.glsl\"\n"
            + "layout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(Twice(0.5)); }\n";

        var result = GlslCompiler.Preprocess(src, includes: Resolver(files, []));

        Assert.Equal(["common.glsl"], result.IncludedFiles);
        StructValidator.Valid(GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: Resolver(files, [])));
    }

    [Fact]
    public void IncludedFileSeesTheHostsDefines()
    {
        var files = new Dictionary<string, string>
        {
            ["scale.glsl"] = "#ifdef FANCY\nfloat Scale() { return 3.0; }\n#else\nfloat Scale() { return 1.0; }\n#endif\n",
        };
        const string src = "#version 450\n#include \"scale.glsl\"\nlayout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(Scale()); }\n";
        var defines = new Dictionary<string, string> { ["FANCY"] = "1" };

        var result = GlslCompiler.Preprocess(src, defines, Resolver(files, []));

        Assert.Contains("return 3.0", result.Text);
        Assert.DoesNotContain("return 1.0", result.Text);
    }

    [Fact]
    public void MacrosDefinedInAHeaderAreVisibleAfterIt()
    {
        var files = new Dictionary<string, string> { ["defs.glsl"] = "#define OFFSET 1\n" };
        const string src = "#version 450\n#include \"defs.glsl\"\nlayout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(OFFSET); }\n";

        var result = GlslCompiler.Preprocess(src, includes: Resolver(files, []));

        Assert.DoesNotContain("OFFSET", result.Text);
        Assert.Contains(result.Defines, d => d.Name == "OFFSET" && d.Body == "1");
        StructValidator.Valid(GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: Resolver(files, [])));
    }

    [Fact]
    public void MissingIncludeWithoutACallbackIsAnError()
    {
        const string src = "#version 450\n#include \"missing.glsl\"\nvoid main() {}\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(src, ShaderStage.Fragment));

        Assert.StartsWith("line 2:", ex.Message);
    }

    [Fact]
    public void CallbackReturningNullIsAnError()
    {
        const string src = "#version 450\n#include \"missing.glsl\"\nvoid main() {}\n";

        var ex = Assert.Throws<GlslParseException>(() =>
            GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: Resolver(new Dictionary<string, string>(), [])));

        Assert.StartsWith("line 2:", ex.Message);
        Assert.Contains("cannot open include file 'missing.glsl'", ex.Message);
    }

    [Fact]
    public void IncludeWithoutAHeaderNameIsAnError()
    {
        const string src = "#version 450\n#include common.glsl\nvoid main() {}\n";

        var ex = Assert.Throws<GlslParseException>(() =>
            GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: Resolver(new Dictionary<string, string>(), [])));

        Assert.Contains("must be followed by a header name", ex.Message);
    }

    [Fact]
    public void CyclicIncludesTerminate()
    {
        var files = new Dictionary<string, string>
        {
            ["a.glsl"] = "#include \"b.glsl\"\nfloat fa() { return 1.0; }\n",
            ["b.glsl"] = "#include \"a.glsl\"\nfloat fb() { return 2.0; }\n",
        };
        const string src = "#version 450\n#include \"a.glsl\"\nlayout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(fa() + fb()); }\n";

        var result = GlslCompiler.Preprocess(src, includes: Resolver(files, []));

        Assert.Equal(["a.glsl", "b.glsl"], result.IncludedFiles);
        StructValidator.Valid(GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: Resolver(files, [])));
    }

    [Fact]
    public void IncludeNestingIsBounded()
    {
        var files = new Dictionary<string, string>();
        for (int i = 0; i < 100; i++) files[$"h{i}.glsl"] = $"#include \"h{i + 1}.glsl\"\n";
        const string src = "#version 450\n#include \"h0.glsl\"\nvoid main() {}\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.Preprocess(src, includes: Resolver(files, [])));

        Assert.Contains("nested too deeply", ex.Message);
    }

    [Fact]
    public void UnterminatedIfInIncludedFileIsAnError()
    {
        var files = new Dictionary<string, string> { ["open.glsl"] = "#if 1\nfloat x = 1.0;\n" };
        const string src = "#version 450\n#include \"open.glsl\"\nvoid main() {}\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.Preprocess(src, includes: Resolver(files, [])));

        Assert.Contains("unterminated #if", ex.Message);
    }

    [Fact]
    public void IncludedFileCannotCloseAnOuterIf()
    {
        var files = new Dictionary<string, string> { ["close.glsl"] = "#endif\n" };
        const string src = "#version 450\n#if 1\n#include \"close.glsl\"\n#endif\nvoid main() {}\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.Preprocess(src, includes: Resolver(files, [])));

        Assert.Contains("#endif without #if", ex.Message);
    }

    [Fact]
    public void FileMacroNumbersIncludedFilesInOrder()
    {
        var files = new Dictionary<string, string> { ["file.glsl"] = "float file_id = __FILE__;\n" };
        const string src = "#version 450\n#include \"file.glsl\"\nint main_id = __FILE__;\n";

        var result = GlslCompiler.Preprocess(src, includes: Resolver(files, []));

        Assert.Contains("file_id = 1", result.Text);
        Assert.Contains("main_id = 0", result.Text);
    }

    [Fact]
    public void PreprocessReportsTheStateItFound()
    {
        const string src = "#version 450\n#extension GL_EXT_scalar_block_layout : enable\n#extension GL_EXT_demo : enable\n#pragma optimize(off)\n"
            + "#define SCALE 2\n#define ADD(a, b) ((a) + (b))\n#define DROP 1\n#undef DROP\n"
            + "layout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(ADD(SCALE, 3)); }\n";

        var result = GlslCompiler.Preprocess(src);

        Assert.Equal(450, result.Version);
        Assert.False(result.IsEs);
        Assert.Equal(["GL_EXT_scalar_block_layout"], result.Extensions);
        Assert.Equal(["optimize"], result.Pragmas);
        Assert.Contains(result.Defines, d => d.Name == "SCALE" && d.Parameters is null && d.Body == "2");
        Assert.Contains(result.Defines, d => d.Name == "ADD" && d.Parameters is not null && d.Parameters.SequenceEqual(["a", "b"]));
        Assert.DoesNotContain(result.Defines, d => d.Name == "DROP");
        Assert.Empty(result.IncludedFiles);
    }

    [Fact]
    public void PreprocessedTextHasNoDirectivesAndStillCompiles()
    {
        var files = new Dictionary<string, string> { ["common.glsl"] = CommonGlsl };
        const string src = "#version 450\n#define HALF 0.5\n#include \"common.glsl\"\n#ifdef MISSING\nthis is not glsl\n#endif\n"
            + "layout(location = 0) out vec4 outColor;\nvoid main() { outColor = vec4(Twice(HALF)); }\n";

        var result = GlslCompiler.Preprocess(src, includes: Resolver(files, []));

        Assert.StartsWith("#version 450\n", result.Text);
        Assert.DoesNotContain("#define", result.Text);
        Assert.DoesNotContain("#include", result.Text);
        Assert.DoesNotContain("this is not glsl", result.Text);
        StructValidator.Valid(GlslCompiler.CompileToBytes(result.Text, ShaderStage.Fragment));
    }
}
