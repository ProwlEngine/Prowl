using Prowl.Prism.Compiler;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// SPIR-V 1.1 and 1.2 have no golden folders: neither version changed anything glslang lowers differently
/// (1.1 added OpModuleProcessed and named barriers, 1.2 the OpExecutionModeId/OpDecorateId forms, none of which
/// glslang emits for these targets). Compiling the whole corpus with the reference toolchain at spirv1.1 and
/// spirv1.2 under vulkan1.0 produced the 1.0 modules with only the header's version word changed, so that is
/// the contract here. Every other version (1.0, 1.3, 1.4, 1.5) is byte-matched against its own goldens.
/// </summary>
public sealed class Spirv11And12Tests
{
    const int VersionWordOffset = 4;   // bytes 4..8 hold the module header's version word

    // Shaders Prism does not compile yet. They fail the 1.0 compile this test starts from, so there is no module
    // to compare. Remove an entry once its finding in FINDINGS.md is fixed.
    static readonly HashSet<string> NotYetCompiling = new(StringComparer.Ordinal)
    {
        "Spirv 1.0/exprs_r12-imagesamples.frag",
        "Spirv 1.0/iface_cap-c17-shader-clock.comp",
        "Spirv 1.0/iface_fb-fb01-bary.frag",
        "Spirv 1.0/iface_fb-fb05-fragsize.frag",
        "Spirv 1.0/iface_fb-fb08-stencil-ref-layout.frag",
        "Spirv 1.0/iface_fb-fb09-bary-nv.frag",
        "Spirv 1.0/iface_fb-fb10-viewport-mask-vert.vert",
        "Spirv 1.0/iface_nv-n1.mesh",
        "Spirv 1.0/iface_nv-n4.task",
        "Spirv 1.0/iface_nv-n5.mesh",
        "Spirv 1.0/iface_rq-rq1.comp",
        "Spirv 1.0/iface_rq-rq5.comp",
        "Spirv 1.0/iface_rq-rq7.comp",
        "Spirv 1.0/iface_rq-rq8-func-param.comp",
        "Spirv 1.0/iface_sg-sg09-uint64-ballot.comp",
        "Spirv 1.0/iface_sg-sg11-amd-ballot.comp",
        "Spirv 1.0/iface_st2-t08-geom-viewport-mask.geom",
        "Spirv 1.0/review_t18-sp1.frag",
    };

    public static IEnumerable<object[]> Cases() =>
        TestCorpus.Parity.Where(s => s.Target == SpirVVersion.V1_0 && !NotYetCompiling.Contains(s.Name))
            .SelectMany(s => new[] { new object[] { s, SpirVVersion.V1_1 }, new object[] { s, SpirVVersion.V1_2 } });

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesThe10OutputExceptForTheVersionWord(Snippet snippet, SpirVVersion target)
    {
        byte[] v10 = GlslCompiler.CompileToBytes(snippet.Source, snippet.Stage, SpirVVersion.V1_0);
        byte[] ours = GlslCompiler.CompileToBytes(snippet.Source, snippet.Stage, target);

        Assert.Equal((uint)target, BitConverter.ToUInt32(ours, VersionWordOffset));
        Assert.Equal(v10.Length, ours.Length);
        Assert.True(v10.AsSpan(0, VersionWordOffset).SequenceEqual(ours.AsSpan(0, VersionWordOffset)), $"{snippet.Name}: magic differs");
        Assert.True(v10.AsSpan(VersionWordOffset + 4).SequenceEqual(ours.AsSpan(VersionWordOffset + 4)),
            $"{snippet.Name}: {target} output differs from 1.0 beyond the version word.");
    }
}
