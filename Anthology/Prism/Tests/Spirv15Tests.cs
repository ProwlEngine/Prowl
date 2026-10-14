using Prowl.Prism.Compiler;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// SPIR-V 1.5 (Vulkan 1.2) has no reference goldens yet, so these tests do not byte-match an external compiler.
/// The 1.5 output must equal the 1.4 output everywhere except the header's version word, since every version gate
/// Prism has is a 1.4-or-later check and 1.5 adds no lowering changes of its own.
/// </summary>
public sealed class Spirv15Tests
{
    const int VersionWordOffset = 4;   // bytes 4..8 hold the module header's version word

    public static IEnumerable<object[]> Cases() =>
        TestCorpus.Parity.Where(s => s.Target == SpirVVersion.V1_4).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesThe14OutputExceptForTheVersionWord(Snippet snippet)
    {
        byte[] v14 = GlslCompiler.CompileToBytes(snippet.Source, snippet.Stage, SpirVVersion.V1_4);
        byte[] v15 = GlslCompiler.CompileToBytes(snippet.Source, snippet.Stage, SpirVVersion.V1_5);

        Assert.Equal(v14.Length, v15.Length);
        Assert.True(v14.AsSpan(0, VersionWordOffset).SequenceEqual(v15.AsSpan(0, VersionWordOffset)), $"{snippet.Name}: magic differs");
        Assert.True(v14.AsSpan(VersionWordOffset + 4).SequenceEqual(v15.AsSpan(VersionWordOffset + 4)),
            $"{snippet.Name}: 1.5 output differs from 1.4 beyond the version word.");
        StructValidator.Valid(v15);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void HeaderCarriesVersion15(Snippet snippet)
    {
        byte[] bytes = GlslCompiler.CompileToBytes(snippet.Source, snippet.Stage, SpirVVersion.V1_5);

        Assert.Equal(0x00010500u, BitConverter.ToUInt32(bytes, VersionWordOffset));
    }

    [Fact]
    public void OptimizerAcceptsA15Module()
    {
        const string src = "#version 450\nlayout(location = 0) out vec4 o;\nvoid main() { o = vec4(1.0); }\n";
        byte[] bytes = GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, SpirVVersion.V1_5, optimize: true);

        StructValidator.Valid(bytes);
        Assert.Equal(0x00010500u, BitConverter.ToUInt32(bytes, VersionWordOffset));
    }
}
