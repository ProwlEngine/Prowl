using Prowl.Prism.Compiler;
using Prowl.Prism.Glsl;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// Diagnostics follow #line directives and includes the way glslang does, and the preprocessed text keeps the
/// source's layout with #line markers, so it can be compiled again with the same diagnostics.
/// </summary>
public sealed class LocationTests
{
    [Fact]
    public void LineDirectiveSetsTheFileAndLineOfErrors()
    {
        const string src = "#version 450\n#line 17 \"Test\"\nvoid main() { x = 1; }\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(src, ShaderStage.Fragment));

        Assert.StartsWith("Test:17: ", ex.Message);
        Assert.Equal("Test", ex.File);
        Assert.Equal(17, ex.Line);
        Assert.Contains("unresolved identifier 'x'", ex.Message);
    }

    [Fact]
    public void BareLineDirectiveKeepsTheFileName()
    {
        const string src = "#version 450\n#line 40 \"Shader\"\n#line 9\nvoid main() { x = 1; }\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(src, ShaderStage.Fragment));

        Assert.Equal("Shader", ex.File);
        Assert.Equal(9, ex.Line);
    }

    [Fact]
    public void ErrorsWithoutAFileNameStillReportTheLine()
    {
        const string src = "#version 450\n#line 5\nvoid main() { x = 1; }\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(src, ShaderStage.Fragment));

        Assert.StartsWith("line 5: ", ex.Message);
        Assert.Null(ex.File);
    }

    [Fact]
    public void IncludedFileErrorsReportTheIncludedFileAndItsLine()
    {
        var files = new Dictionary<string, string> { ["Broken.glsl"] = "float helper()\n{\n    return nope;\n}\n" };
        const string src = "#version 450\n#include \"Broken.glsl\"\nlayout(location = 0) out vec4 color;\nvoid main() { color = vec4(helper()); }\n";

        var ex = Assert.Throws<GlslParseException>(() =>
            GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: (name, _, _) => files.TryGetValue(name, out var text) ? new IncludeResult(name, text) : null));

        Assert.StartsWith("Broken.glsl:3: ", ex.Message);
        Assert.Equal("Broken.glsl", ex.File);
        Assert.Equal(3, ex.Line);
    }

    [Fact]
    public void MainFileLinesAfterAnIncludeAreReportedAgainInTheMainFile()
    {
        var files = new Dictionary<string, string> { ["Empty.glsl"] = "\n\n" };
        const string src = "#version 450\n#include \"Empty.glsl\"\nvoid main() { x = 1; }\n";

        var ex = Assert.Throws<GlslParseException>(() =>
            GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: (name, _, _) => files.TryGetValue(name, out var text) ? new IncludeResult(name, text) : null));

        Assert.Null(ex.File);
        Assert.Equal(3, ex.Line);
    }

    [Fact]
    public void PreprocessedTextKeepsTheSourceSpacing()
    {
        const string src = "#version 450\nlayout(location = 0) in vec2 aPosition;\nout vec2 uv;\n    float   scale = 2.0;\n";

        string text = GlslCompiler.Preprocess(src).Text;

        Assert.Contains("out vec2 uv;", text);
        Assert.Contains("layout(location = 0) in vec2 aPosition;", text);
        Assert.Contains("    float   scale = 2.0;", text);
    }

    [Fact]
    public void PreprocessedTextMarksLineChangesWithLineDirectives()
    {
        const string src = "#version 450\n#line 17 \"Test\"\nfloat x;\n";

        string text = GlslCompiler.Preprocess(src).Text;

        Assert.Contains("#line 17 \"Test\"\nfloat x;", text);
    }

    [Fact]
    public void PreprocessedTextKeepsIncludeLocations()
    {
        var files = new Dictionary<string, string> { ["Common.glsl"] = "float helper() { return 1.0; }\n" };
        const string src = "#version 450\n#include \"Common.glsl\"\nfloat after;\n";

        string text = GlslCompiler.Preprocess(src, includes: (name, _, _) => files.TryGetValue(name, out var content) ? new IncludeResult(name, content) : null).Text;

        Assert.Contains("#line 1 \"Common.glsl\"\nfloat helper() { return 1.0; }", text);
        Assert.Contains("#line 3\nfloat after;", text);
    }

    [Fact]
    public void PreprocessedTextCompilesWithTheSameDiagnostics()
    {
        const string src = "#version 450\n#line 17 \"Test\"\nvoid main() { x = 1; }\n";
        string text = GlslCompiler.Preprocess(src).Text;

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(text, ShaderStage.Fragment));

        Assert.StartsWith("Test:17: ", ex.Message);
    }

    [Fact]
    public void UnresolvedNameErrorsPointAtTheName()
    {
        const string src = "#version 450\nvoid main()\n{\n    float y = 1.0 + nope;\n}\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(src, ShaderStage.Fragment));

        Assert.Equal(4, ex.Line);
        Assert.Equal(21, ex.Column);
        Assert.Equal(4, ex.Length);
    }

    [Fact]
    public void MemberErrorsPointAtTheMember()
    {
        const string src = "#version 450\nstruct S { vec4 color; };\nvoid main()\n{\n    S s;\n    vec4 c = s.colour;\n}\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(src, ShaderStage.Fragment));

        Assert.Equal(6, ex.Line);
        Assert.Equal(16, ex.Column);
        Assert.Equal(6, ex.Length);
    }

    [Fact]
    public void SyntaxErrorsPointAtTheToken()
    {
        const string src = "#version 450\nvoid main()\n{\n    float x = ;\n}\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(src, ShaderStage.Fragment));

        Assert.Equal(4, ex.Line);
        Assert.Equal(15, ex.Column);
    }

    [Fact]
    public void IncludedFileErrorsKeepTheirColumn()
    {
        var files = new Dictionary<string, string> { ["Broken.glsl"] = "float helper()\n{\n    return nope;\n}\n" };
        const string src = "#version 450\n#include \"Broken.glsl\"\nlayout(location = 0) out vec4 color;\nvoid main() { color = vec4(helper()); }\n";

        var ex = Assert.Throws<GlslParseException>(() =>
            GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, includes: (name, _, _) => files.TryGetValue(name, out var text) ? new IncludeResult(name, text) : null));

        Assert.Equal("Broken.glsl", ex.File);
        Assert.Equal(3, ex.Line);
        Assert.Equal(12, ex.Column);
    }

    [Fact]
    public void MacroErrorsHaveNoColumn()
    {
        const string src = "#version 450\n#define BAD nope\nvoid main()\n{\n    float y = BAD;\n}\n";

        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(src, ShaderStage.Fragment));

        Assert.Equal(5, ex.Line);
        Assert.Equal(0, ex.Column);
    }
}
