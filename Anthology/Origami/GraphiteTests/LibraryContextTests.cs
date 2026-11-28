// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.ShaderDef;
using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Graphite;

using Xunit;

namespace Prowl.Origami.Graphite.Tests;

/// <summary>What the Graphite library gives a shader: Surface block context and the includes Graphite ships.</summary>
public class LibraryContextTests
{
    private readonly ITestOutputHelper _output;

    public LibraryContextTests(ITestOutputHelper output)
    {
        _output = output;
        ShaderLibrary.Register(typeof(Prowl.Graphite.Rendering.Renderer).Assembly);
    }

    private const string Rim = """
        Shader "Test/Rim"
        {
            Properties
            {
                _RimColor("Rim Color", Color) = (0.3, 0.6, 1.0, 1.0)
            }

            Surface "Lit"
            {
                GLSLPROGRAM
                uniform vec4 _RimColor;

                float Fresnel(float d) { return 1.0 - max(d, 0.0); }

                void Surface(inout PBR_Params p, Graphite_SurfaceData i)
                {
                    float rim = Fresnel(dot(i.geometryNormal, i.viewDir));
                    p.baseColor += _RimColor.rgb * rim;
                }
                ENDGLSL

                Lighting
                {
                    GLSLPROGRAM
                    float lit = 1.0;
                    ENDGLSL
                }

                Vertex
                {
                    GLSLPROGRAM
                    float moved = 1.0;
                    ENDGLSL
                }
            }
        }
        """;

    private static ShaderSession Session(string text) => new(new ShaderWorkspace(), ShaderFileKind.Shader, "Test.shader", text);

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
    public async Task SurfaceCode_CompletesTheModelsTypesAndFunctions()
    {
        var labels = await Labels(Session(Rim), At(Rim, "float rim"));
        Assert.Contains("Graphite_SurfaceData", labels);
        Assert.Contains("PBR_Params", labels);
        Assert.Contains("Graphite_StandardUV", labels);
        Assert.Contains("GRAPHITE_STANDARD_FORWARD", labels);
        Assert.Contains("base_color", labels);
    }

    [Fact]
    public async Task SurfaceCode_CompletesSurfaceDataMembers()
    {
        string text = Rim.Replace("p.baseColor +=", "p. +=");
        var labels = await Labels(Session(text), At(text, "p. +=", 2));
        Assert.Contains("baseColor", labels);
        Assert.Contains("specularWeight", labels);

        text = Rim.Replace("i.viewDir", "i.");
        labels = await Labels(Session(text), At(text, "i.)", 2));
        Assert.Contains("viewDir", labels);
        Assert.Contains("uv", labels);
    }

    [Fact]
    public async Task SurfaceCode_HoverAndDefinitionReachTheLibrary()
    {
        var session = Session(Rim);
        int at = At(Rim, "Graphite_SurfaceData i", 3);

        var hover = await session.GetQuickInfoAsync(at, TestContext.Current.CancellationToken);
        Assert.NotNull(hover);
        Assert.Contains(hover!.Documentation, d => d.Contains("Graphite/Standard/Standard.glsl"));

        var definition = await session.GetDefinitionAsync(at, TestContext.Current.CancellationToken);
        var location = Assert.Single(definition);
        _output.WriteLine($"{location.Path} {location.Line}");
        Assert.Equal("Graphite/Standard/Standard.glsl", location.Path);
        Assert.True(location.Line > 0);
    }

    [Fact]
    public async Task SurfaceCode_IsLexicallyTheFragmentStage()
    {
        var labels = await Labels(Session(Rim), At(Rim, "float rim"));
        Assert.Contains("dFdx", labels);
        Assert.Contains("gl_FragCoord", labels);
        Assert.DoesNotContain("gl_VertexIndex", labels);
    }

    [Fact]
    public async Task LightingBlock_SeesTheSurfaceCodeAndTheModel()
    {
        var labels = await Labels(Session(Rim), At(Rim, "float lit"));
        Assert.Contains("Fresnel", labels);
        Assert.Contains("PBR_Params", labels);
        Assert.Contains("gl_FragCoord", labels);
        Assert.DoesNotContain("rim", labels);
    }

    [Fact]
    public async Task VertexBlock_IsTheVertexStageOfTheModel()
    {
        var labels = await Labels(Session(Rim), At(Rim, "float moved"));
        Assert.Contains("gl_VertexIndex", labels);
        Assert.DoesNotContain("dFdx", labels);
        Assert.Contains("PBR_Params", labels);
        Assert.DoesNotContain("Fresnel", labels);
    }

    [Fact]
    public async Task UnknownSurfaceModel_CompletesOnlyTheFile()
    {
        string text = Rim.Replace("Surface \"Lit\"", "Surface \"Nope\"");
        var labels = await Labels(Session(text), At(text, "float rim"));
        Assert.Contains("_RimColor", labels);
        Assert.DoesNotContain("Graphite_SurfaceData", labels);
    }

    [Fact]
    public async Task IncludedLibraryFile_CompletesItsDeclarations()
    {
        const string text = "#include \"Graphite/Standard/Standard.glsl\"\nvoid main() { Graphite_SurfaceData s; s. }\n";
        var session = new ShaderSession(new ShaderWorkspace(), ShaderFileKind.Glsl, "Test.glsl", text);
        var labels = await Labels(session, text.IndexOf("s. ", StringComparison.Ordinal) + 2);
        Assert.Contains("viewDir", labels);
    }
}
