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

    public static IEnumerable<object[]> Cases() =>
        TestCorpus.Parity.Where(s => s.Target == SpirVVersion.V1_0)
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
