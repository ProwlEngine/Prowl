// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Graphite;

using Xunit;
using Xunit.Abstractions;

namespace Prowl.Origami.Graphite.Tests;

public class ShaderSessionTests
{
    private readonly ITestOutputHelper _output;

    public ShaderSessionTests(ITestOutputHelper output) => _output = output;

    private const string Blur = """
        Shader "Test/Blur"
        {
            Properties
            {
                _Strength("Strength", Float) = 1.0
                _MainTex("Main", Texture2D) = "white" {}
            }

            Pass
            {
                Name "Blur"
                Cull Off
                Blend One InverseSourceAlpha

                GLSLPROGRAM
                Shared
                {
                    #pragma variant Upsample

                    struct Sample { vec4 color; float weight; };

                    uniform sampler2D src;
                    uniform vec2 halfpixel;
                }

                Vertex
                {
                    out vec2 vUV;

                    void main()
                    {
                        vec2 p = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
                        vUV = p;
                        gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
                    }
                }

                Fragment
                {
                    in vec2 vUV;
                    out vec4 target;

                    vec4 Down(vec2 uv, float spread)
                    {
                        Sample s;
                        s.color = texture(src, uv - halfpixel * spread);
                        return s.color;
                    }

                    void main()
                    {
                        target = Down(vUV, 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private static ShaderSession Session(string text, ShaderFileKind kind = ShaderFileKind.Shader, ShaderWorkspace? workspace = null)
        => new(workspace ?? new ShaderWorkspace(), kind, kind == ShaderFileKind.Shader ? "Test.shader" : "Test.glsl", text);

    private static int At(string text, string marker, int skip = 0)
    {
        int at = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, "missing " + marker);
        return at + skip;
    }

    private static async Task<List<string>> Labels(ShaderSession session, int offset)
    {
        var list = await session.GetCompletionsAsync(offset, CodeCompletionTrigger.Invoke, default);
        return list?.Items.Select(i => i.Label).ToList() ?? new List<string>();
    }

    [Fact]
    public async Task CompletesLocalsUniformsAndBuiltinsInGlsl()
    {
        int caret = At(Blur, "return s.color;");
        var labels = await Labels(Session(Blur), caret);
        Assert.Contains("uv", labels);
        Assert.Contains("spread", labels);
        Assert.Contains("halfpixel", labels);
        Assert.Contains("Down", labels);
        Assert.Contains("texture", labels);
        Assert.Contains("vec4", labels);
        Assert.Contains("Upsample", labels);
        Assert.DoesNotContain("vUV2", labels);
    }

    [Fact]
    public async Task CompletesStructMembersAndSwizzles()
    {
        string text = Blur.Replace("return s.color;", "return s.;");
        var members = await Labels(Session(text), At(text, "return s.;", 9));
        Assert.Contains("color", members);
        Assert.Contains("weight", members);

        text = Blur.Replace("return s.color;", "return uv.;");
        var swizzles = await Labels(Session(text), At(text, "return uv.;", 10));
        Assert.Contains("x", swizzles);
        Assert.Contains("xy", swizzles);
        Assert.DoesNotContain("z", swizzles);
    }

    [Fact]
    public async Task CompletesWrapperByBlock()
    {
        string text = Blur.Replace("        Cull Off", "        Cull \n        Cull Off");
        var values = await Labels(Session(text), At(text, "Cull \n", 5));
        Assert.Contains("Off", values);
        Assert.Contains("Back", values);

        var commands = await Labels(Session(Blur), At(Blur, "Cull Off"));
        Assert.Contains("ZTest", commands);
        Assert.Contains("GLSLPROGRAM", commands);
        Assert.DoesNotContain("texture", commands);
    }

    [Fact]
    public async Task CompletesPragmaNames()
    {
        string text = Blur.Replace("#pragma variant Upsample", "#pragma ");
        var labels = await Labels(Session(text), At(text, "#pragma ", 8));
        Assert.Contains("variant", labels);
        Assert.Contains("global", labels);
    }

    [Fact]
    public async Task CompletesIncludesFromLibraryAndHost()
    {
        var workspace = new ShaderWorkspace((name, from) => name == "Common.glsl" ? new ShaderSource("Common.glsl", "float Helper(float x) { return x; }\n") : null,
            from => new[] { "Common.glsl" });
        string text = "#include \"\"\nvoid main() { }\n";
        var labels = await Labels(Session(text, ShaderFileKind.Glsl, workspace), 10);
        Assert.Contains("Common.glsl", labels);
        Assert.Equal(labels.Count, labels.Count(l => l == "Common.glsl" || l.StartsWith("Graphite/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task IncludedFunctionsAreVisibleAndNavigable()
    {
        var workspace = new ShaderWorkspace((name, from) => name == "Common.glsl" ? new ShaderSource("Common.glsl", "float Helper(float x) { return x; }\n") : null);
        string text = "#include \"Common.glsl\"\nvoid main() { float y = Helper(1.0); }\n";
        var session = Session(text, ShaderFileKind.Glsl, workspace);

        Assert.Contains("Helper", await Labels(session, At(text, "Helper")));
        var definition = await session.GetDefinitionAsync(At(text, "Helper", 2), default);
        Assert.Single(definition);
        Assert.Equal("Common.glsl", definition[0].Path);
    }

    [Fact]
    public async Task DefinitionAndReferencesOfUniform()
    {
        var session = Session(Blur);
        int use = At(Blur, "halfpixel * spread", 2);
        var definition = await session.GetDefinitionAsync(use, default);
        Assert.Single(definition);
        Assert.Equal(At(Blur, "halfpixel;"), definition[0].Start);

        var references = await session.FindReferencesAsync(use, default);
        Assert.Equal(2, references.Count);
    }

    [Fact]
    public async Task PropertyReferencesIncludeUniformUses()
    {
        string text = Blur.Replace("uniform vec2 halfpixel;", "uniform vec2 halfpixel;\n uniform float _Strength;").Replace("return s.color;", "return s.color * _Strength;");
        var session = Session(text);
        var references = await session.FindReferencesAsync(At(text, "_Strength(", 2), default);
        _output.WriteLine(string.Join(", ", references.Select(r => r.Start)));
        Assert.Equal(3, references.Count);
    }

    [Fact]
    public async Task RenameLocalStaysInItsFunction()
    {
        var session = Session(Blur);
        var result = await session.RenameAsync(At(Blur, "uv - halfpixel"), "coord", default);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Edits.Count);
    }

    [Fact]
    public async Task HoverShowsBuiltinAndUserSignatures()
    {
        var session = Session(Blur);
        var builtin = await session.GetQuickInfoAsync(At(Blur, "texture(src", 2), default);
        Assert.NotNull(builtin);
        Assert.Contains("texture", string.Concat(builtin!.Signature.Select(p => p.Text)));

        var user = await session.GetQuickInfoAsync(At(Blur, "Down(vUV", 1), default);
        Assert.NotNull(user);
        Assert.Contains("vec4 Down(vec2 uv, float spread)", string.Concat(user!.Signature.Select(p => p.Text)));

        var command = await session.GetQuickInfoAsync(At(Blur, "Cull Off", 1), default);
        Assert.NotNull(command);
    }

    [Fact]
    public async Task SignatureHelpTracksArgument()
    {
        string text = Blur.Replace("target = Down(vUV, 1.0);", "target = Down(vUV, ");
        var session = Session(text);
        var help = await session.GetSignatureHelpAsync(At(text, "Down(vUV, ", 10), default);
        Assert.NotNull(help);
        Assert.Equal(1, help!.ActiveParameter);
        Assert.Contains(help.Signatures, s => string.Concat(s.Parts.Select(p => p.Text)) == "vec4 Down(vec2 uv, float spread)");
    }

    [Fact]
    public async Task ValidShaderHasNoErrors()
    {
        var diagnostics = await Session(Blur).GetDiagnosticsAsync(default);
        foreach (var d in diagnostics) _output.WriteLine($"{d.Severity} {d.Start}: {d.Message}");
        Assert.DoesNotContain(diagnostics, d => d.Severity == CodeDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task GlslErrorUnderlinesTheMember()
    {
        string text = Blur.Replace("return s.color;", "return s.colour;");
        var diagnostics = await Session(text).GetDiagnosticsAsync(default);
        foreach (var d in diagnostics) _output.WriteLine($"{d.Severity} {d.Start} {d.Length}: {d.Message}");
        var error = Assert.Single(diagnostics, d => d.Severity == CodeDiagnosticSeverity.Error);
        Assert.Equal(At(text, "colour"), error.Start);
        Assert.Equal(6, error.Length);
    }

    [Fact]
    public async Task GlslErrorUnderlinesTheUnknownName()
    {
        string text = Blur.Replace("target = Down(vUV, 1.0);", "target = Down(vUV, strength);");
        var diagnostics = await Session(text).GetDiagnosticsAsync(default);
        foreach (var d in diagnostics) _output.WriteLine($"{d.Severity} {d.Start} {d.Length}: {d.Message}");
        var error = Assert.Single(diagnostics, d => d.Severity == CodeDiagnosticSeverity.Error);
        Assert.Equal(At(text, "strength"), error.Start);
        Assert.Equal(8, error.Length);
    }

    [Fact]
    public async Task IncludeErrorLandsOnTheInclude()
    {
        var workspace = new ShaderWorkspace((name, from) => name == "Broken.glsl" ? new ShaderSource("Broken.glsl", "float Helper()\n{\n    return nope;\n}\n") : null);
        string text = Blur.Replace("#pragma variant Upsample", "#include \"Broken.glsl\"");
        var diagnostics = await Session(text, workspace: workspace).GetDiagnosticsAsync(default);
        foreach (var d in diagnostics) _output.WriteLine($"{d.Severity} {d.Start} {d.Length}: {d.Message}");
        var error = Assert.Single(diagnostics, d => d.Severity == CodeDiagnosticSeverity.Error);
        Assert.Equal(At(text, "Broken.glsl"), error.Start);
        Assert.StartsWith("Broken.glsl(3,12): ", error.Message);
    }

    [Fact]
    public async Task WrapperErrorIsReported()
    {
        string text = Blur.Replace("Cull Off", "Cull Sideways");
        var diagnostics = await Session(text).GetDiagnosticsAsync(default);
        foreach (var d in diagnostics) _output.WriteLine($"{d.Severity} {d.Start}: {d.Message}");
        Assert.Contains(diagnostics, d => d.Severity == CodeDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task MissingIncludeIsReported()
    {
        string text = "#include \"Nope.glsl\"\nvoid main() { }\n";
        var diagnostics = await Session(text, ShaderFileKind.Glsl).GetDiagnosticsAsync(default);
        Assert.Contains(diagnostics, d => d.Id == "include" && d.Start == 10);
    }

    [Fact]
    public async Task GlslFileSyntaxError()
    {
        string text = "void main()\n{\n    float x = ;\n}\n";
        var diagnostics = await Session(text, ShaderFileKind.Glsl).GetDiagnosticsAsync(default);
        foreach (var d in diagnostics) _output.WriteLine($"{d.Severity} {d.Start}: {d.Message}");
        var error = Assert.Single(diagnostics, d => d.Severity == CodeDiagnosticSeverity.Error);
        Assert.Equal(text.IndexOf(" ;", StringComparison.Ordinal) + 1, error.Start);
        Assert.Equal(1, error.Length);
    }

    [Fact]
    public void LexerSwitchesBetweenWrapperAndGlsl()
    {
        var lexer = ShaderLexer.ShaderFile;
        var tokens = new List<CodeToken>();
        int state = lexer.Tokenize("    GLSLPROGRAM", 0, tokens);
        Assert.NotEqual(0, state);
        tokens.Clear();
        int glsl = lexer.Tokenize("    vec4 x = texture(src, uv);", state, tokens);
        Assert.Equal(state, glsl);
        Assert.Contains(tokens, t => t.Kind == CodeTokenKind.Struct);
        Assert.Equal(0, lexer.Tokenize("    ENDGLSL", glsl, null));
    }
}
