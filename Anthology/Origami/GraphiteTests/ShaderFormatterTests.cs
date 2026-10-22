// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Graphite;

using Xunit;
using Xunit.Abstractions;

namespace Prowl.Origami.Graphite.Tests;

public class ShaderFormatterTests
{
    private readonly ITestOutputHelper _output;

    public ShaderFormatterTests(ITestOutputHelper output) => _output = output;

    private static string Apply(string text, IEnumerable<CodeTextEdit> edits)
    {
        foreach (var e in edits.OrderByDescending(e => e.Start))
            text = text.Remove(e.Start, e.Length).Insert(e.Start, e.NewText);
        return text;
    }

    private static string Format(string text, bool shader = true) => Apply(text, ShaderFormatter.Format(text, shader));

    private static string Visible(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    [Fact]
    public void IndentsWrapperAndGlslByBraces()
    {
        const string input = """
            Shader "Test"
            {
            Pass
            {
            Name "Main"
                    Cull   Off
            GLSLPROGRAM
            Vertex
            {
            void main()
            {
            gl_Position=vec4(0.0);
            }
            }
            ENDGLSL
            }
            }
            """;
        const string expected = """
            Shader "Test"
            {
                Pass
                {
                    Name "Main"
                    Cull Off
                    GLSLPROGRAM
                    Vertex
                    {
                        void main()
                        {
                            gl_Position = vec4(0.0);
                        }
                    }
                    ENDGLSL
                }
            }
            """;
        Assert.Equal(expected, Format(input));
    }

    [Fact]
    public void SpacesGlslOperatorsAndPunctuation()
    {
        const string input = "void main()\n{\n    float x=a*-b+c [ 1 ]  ;\n    if(x>0.0&&!flag){ y++ ; }\n    for(int i=0;i<4;i++) sum+=f( i ,2 );\n    vec2 v = vec2(-1.0, uv .x);\n    return -x;\n}\n";
        const string expected = "void main()\n{\n    float x = a * -b + c[1];\n    if (x > 0.0 && !flag) { y++; }\n    for (int i = 0; i < 4; i++) sum += f(i, 2);\n    vec2 v = vec2(-1.0, uv.x);\n    return -x;\n}\n";
        Assert.Equal(expected, Format(input, shader: false));
    }

    [Fact]
    public void NormalizesPropertySpacing()
    {
        const string input = """
            Shader "Test"
            {
                Properties
                {
                    _MainTex("Texture", Texture2D)="white" {}
                    _AlphaCutoff ( "Alpha Cutoff" , Float ) = 0.5 [Range(0,1)]   [ShowIf(_SURFACE, CUTOUT)]
                    _Color("Tint", Color) = (1.0,1.0, 1.0, 1.0)
                }
            }
            """;
        const string expected = """
            Shader "Test"
            {
                Properties
                {
                    _MainTex("Texture", Texture2D) = "white" {}
                    _AlphaCutoff("Alpha Cutoff", Float) = 0.5 [Range(0, 1)] [ShowIf(_SURFACE, CUTOUT)]
                    _Color("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
                }
            }
            """;
        Assert.Equal(expected, Format(input));
    }

    [Fact]
    public void IndentsSwitchCases()
    {
        const string input = "void main()\n{\nswitch (mode)\n{\ncase 0:\nx = 1;\nbreak;\ndefault:\nx = 2;\n}\n}\n";
        const string expected = "void main()\n{\n    switch (mode)\n    {\n        case 0:\n            x = 1;\n            break;\n        default:\n            x = 2;\n    }\n}\n";
        Assert.Equal(expected, Format(input, shader: false));
    }

    [Fact]
    public void KeepsCaseLabelsLevelWithTheSwitchBraces()
    {
        const string input = "void main()\n{\n    switch (mode)\n    {\n    case 0:\n    x = 1;\n        break;\n    }\n}\n";
        const string expected = "void main()\n{\n    switch (mode)\n    {\n    case 0:\n        x = 1;\n        break;\n    }\n}\n";
        Assert.Equal(expected, Format(input, shader: false));
    }

    [Fact]
    public void ContinuedLinesKeepTheirLeadingOperator()
    {
        const string input = "void main()\n{\n    float s = a\n        + b;\n    vec4 e = {};\n    if (x) { }\n}\n";
        Assert.Equal(input, Format(input, shader: false));
    }

    [Fact]
    public void KeepsContinuationAlignmentAndComments()
    {
        const string input = "void main()\n{\n  vec4 c = mix(a,\n               b, t);   // blend\n  /* keep\n     this */\n  #ifdef FOG\n  c = fog(c);\n  #endif\n}\n";
        const string expected = "void main()\n{\n    vec4 c = mix(a,\n                 b, t);   // blend\n    /* keep\n     this */\n    #ifdef FOG\n    c = fog(c);\n    #endif\n}\n";
        Assert.Equal(expected, Format(input, shader: false));
    }

    [Fact]
    public void FormatOnSemicolonTouchesOnlyThatLine()
    {
        const string text = "void main()\n{\nfloat a=1.0;\nfloat b=2.0;\n}\n";
        int offset = text.IndexOf("b=2.0;", StringComparison.Ordinal) + "b=2.0;".Length;
        string result = Apply(text, ShaderFormatter.FormatOnType(text, false, offset, ';'));
        Assert.Equal("void main()\n{\nfloat a=1.0;\n    float b = 2.0;\n}\n", result);
    }

    [Fact]
    public void FormatOnBraceFormatsTheBlock()
    {
        const string text = "float a=1.0;\nvoid main()\n{\nif(a>0.0)\n{\na=2.0;\n}\n}\n";
        int offset = text.LastIndexOf('}') + 1;
        string result = Apply(text, ShaderFormatter.FormatOnType(text, false, offset, '}'));
        Assert.Equal("float a=1.0;\nvoid main()\n{\n    if (a > 0.0)\n    {\n        a = 2.0;\n    }\n}\n", result);
    }

    [Fact]
    public void FormatOnTypeIgnoresCommentsAndStrings()
    {
        const string text = "void main()\n{\n    // a=b;\n}\n";
        int offset = text.IndexOf(';') + 1;
        Assert.Empty(ShaderFormatter.FormatOnType(text, false, offset, ';'));
    }

    // Every shader in the repository: formatting changes only whitespace, and formatting again changes nothing.
    [Fact]
    public void RepositoryShadersFormatSafely()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Anthology.slnx"))) root = Path.GetDirectoryName(root)!;

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();
        Assert.NotEmpty(files);

        int changed = 0;
        foreach (var file in files)
        {
            string text = File.ReadAllText(file).Replace("\r\n", "\n");
            bool shader = file.EndsWith(".shader", StringComparison.OrdinalIgnoreCase);
            string once = Format(text, shader);
            Assert.True(Visible(text) == Visible(once), "non whitespace changed in " + file);
            string twice = Format(once, shader);
            Assert.True(once == twice, "not idempotent: " + file);
            if (once != text) changed++;
        }
        _output.WriteLine($"{files.Count} files, {changed} would change");
    }
}
