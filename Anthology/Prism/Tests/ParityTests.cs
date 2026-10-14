using Prowl.Prism.Compiler;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// The single source of truth for correctness: every valid shader in the corpus is compiled by Prism
/// and the SPIR-V must be word-for-word identical to the pre-generated golden reference. The golden
/// set is the reference of record - "faithful" means byte-identical, not merely valid. Tests execute
/// only Prism and never touch the external toolchain.
/// </summary>
public sealed class ParityTests
{
    public static IEnumerable<object[]> Cases() => TestCorpus.Parity.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesGlslang(Snippet snippet)
    {
        var golden = TestCorpus.GoldenUnopt(snippet)
            ?? throw new Xunit.Sdk.XunitException($"{snippet.Name}: no unoptimized golden - the golden set is stale; regenerate it.");

        byte[] ours = GlslCompiler.CompileToBytes(snippet.Source, snippet.Stage, snippet.Target);
        Assert.True(golden.AsSpan().SequenceEqual(ours),
            $"{snippet.Name}: SPIR-V differs from the golden ({golden.Length} vs {ours.Length} words).");
    }

    [Fact]
    public void GoldenSetCoversTheCorpus()
    {
        // Corpus/golden drift guard: the manifest must agree with what is actually embedded.
        int expected = TestCorpus.Parity.Count(s => TestCorpus.GoldenUnopt(s) is not null);
        Assert.Equal(TestCorpus.Manifest.UnoptimizedCount, expected);
        Assert.Equal(TestCorpus.Errors.Count, TestCorpus.Manifest.ErrorCorpusCount);
    }
}
