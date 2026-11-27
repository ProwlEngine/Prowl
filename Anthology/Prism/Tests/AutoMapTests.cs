using Prowl.Prism.Compiler;
using Prowl.Prism.Glsl;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// Opt-in auto-location (Prism's AutoMapLocations) must byte-match the reference compiler's
/// auto-mapped output. The shaders live in Corpus/AutoMap/ and their goldens in Golden/AutoMap
/// (generated with locations auto-assigned), so the test itself only runs Prism. The reference
/// compiler's own auto-map is buggy on colliding mixes; Prism stays valid there and is not expected
/// to match, so those cases live in Corpus/AutoMapColliding and are only checked for sound output.
/// </summary>
public sealed class AutoMapTests
{
    public static IEnumerable<object[]> Cases() =>
        TestCorpus.All.Where(s => s.Name.StartsWith("AutoMap/", StringComparison.OrdinalIgnoreCase))
            .Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesGlslangAutoMap(Snippet snippet)
    {
        var golden = TestCorpus.GoldenAutoMap(snippet)
            ?? throw new Xunit.Sdk.XunitException($"{snippet.Name}: no auto-map golden - the golden set is stale; regenerate it.");
        byte[] ours = GlslCompiler.CompileToBytes(snippet.Source, snippet.Stage, snippet.Target, autoMapLocations: true);
        Assert.True(golden.AsSpan().SequenceEqual(ours),
            $"{snippet.Name}: auto-mapped SPIR-V differs from the auto-map golden ({golden.Length} vs {ours.Length} words).");
    }

    [Fact]
    public void GoldenSetCoversTheCorpus()
    {
        int expected = TestCorpus.All.Count(s => s.Name.StartsWith("AutoMap/", StringComparison.OrdinalIgnoreCase) && TestCorpus.GoldenAutoMap(s) is not null);
        Assert.Equal(TestCorpus.Manifest.AutoMapCount, expected);
    }

    // The colliding mixes (explicit location + auto fill) are not parity cases - glslang's own
    // auto-map emits invalid SPIR-V for them - but Prism's output must stay structurally sound.
    public static IEnumerable<object[]> Colliding() =>
        TestCorpus.All.Where(s => s.Name.StartsWith("AutoMapColliding/", StringComparison.OrdinalIgnoreCase))
            .Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Colliding))]
    public void CollidingMixesStaySound(Snippet snippet)
    {
        byte[] bytes = GlslCompiler.CompileToBytes(snippet.Source, snippet.Stage, snippet.Target, autoMapLocations: true);
        StructValidator.Valid(bytes);
    }

    [Fact]
    public void DefaultRejectsMissingLocationWithASpecMessage()
    {
        const string src = "#version 450\nin vec3 a;\nout vec4 o;\nvoid main(){ o = vec4(a, 1.0); }\n";
        // Prism's default (no auto-map) must reject with a spec-framed message (not "not implemented").
        var ex = Assert.Throws<GlslParseException>(() => GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, SpirVVersion.V1_0));
        Assert.Contains("requires location", ex.Message);
    }
}
