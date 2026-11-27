using Prowl.Prism.Compiler;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// Phase 2 of the correctness story: Prism's lowering + optimizer must byte-match the pre-generated
/// optimized goldens (the reference compiler's output run through its performance pipeline). These
/// are the optimizer work tracker: a red test shows exactly what the reference optimizer does that
/// Prism does not yet reproduce. Filter out with
/// <c>--filter "Category!=OptimizedParity"</c> while working elsewhere; everything else in the suite
/// is expected green.
/// </summary>
[Trait("Category", "OptimizedParity")]
public sealed class ParityOptimizedTests
{
    public static IEnumerable<object[]> Cases() => TestCorpus.Parity.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesGlslangOptimized(Snippet snippet)
    {
        var golden = TestCorpus.GoldenOpt(snippet);
        byte[] ours = GlslCompiler.CompileToBytes(snippet.Source, snippet.Stage, snippet.Target, optimize: true);

        if (golden is null)
        {
            // spirv-opt refuses modules that are invalid for their target (the glslang-shared set:
            // PixelCenterInteger, SampledRect, scalar layout, DependencyLength, VMM scopes) - no
            // optimized golden can exist. The contract drops to "compiles to a structurally sound
            // module", which is what the manifest's optimizedMissing list records.
            Assert.True(TestCorpus.Manifest.OptimizedMissing.Contains(snippet.Name),
                $"{snippet.Name}: no optimized golden and not listed in the manifest's optimizedMissing - the golden set is stale; regenerate it.");
            StructValidator.Valid(ours);
            return;
        }

        Assert.True(golden.AsSpan().SequenceEqual(ours),
            $"{snippet.Name}: optimized SPIR-V differs from the optimized golden ({golden.Length} vs {ours.Length} words).");
    }

    [Fact]
    public void GoldenSetCoversTheCorpus()
    {
        int withGolden = TestCorpus.Parity.Count(s => TestCorpus.GoldenOpt(s) is not null);
        Assert.Equal(TestCorpus.Manifest.OptimizedCount, withGolden);
        Assert.Equal(TestCorpus.Parity.Count(s => TestCorpus.GoldenOpt(s) is null), TestCorpus.Manifest.OptimizedMissing.Count);
    }
}
