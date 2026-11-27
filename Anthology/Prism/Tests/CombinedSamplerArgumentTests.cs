using Prowl.Prism.Compiler;
using Xunit;

namespace Prowl.Prism.Tests;

// A combined sampler built on the spot, `f(sampler2D(tex, smp))`, passed to a user function. glslang rejects it,
// since SPIR-V passes opaque values by pointer, but bindless renderers build every material texture this way.
public sealed class CombinedSamplerArgumentTests
{
    const string Separate = """
        #version 450
        layout(set=0, binding=0) uniform texture2D tex;
        layout(set=0, binding=1) uniform sampler smp;
        layout(location=0) in vec2 uv;
        layout(location=0) out vec4 o;
        vec4 Read(sampler2D s, vec2 p) { vec2 d = dFdx(p); return textureGrad(s, p, d, d); }
        vec4 Twice(sampler2D s, vec2 p) { return Read(s, p) + Read(s, p * 2.0); }
        void main() { o = Twice(sampler2D(tex, smp), uv); }
        """;

    const string Bindless = """
        #version 450
        #extension GL_EXT_nonuniform_qualifier : require
        layout(set=0, binding=0) uniform texture2D texs[];
        layout(set=0, binding=1) uniform sampler smps[4];
        layout(location=0) flat in uint idx;
        layout(location=1) in vec2 uv;
        layout(location=0) out vec4 o;
        vec4 Read(sampler2D s, vec2 p) { return texture(s, p); }
        void main() { o = Read(sampler2D(texs[nonuniformEXT(idx)], smps[nonuniformEXT(idx & 3u)]), uv); }
        """;

    [Theory]
    [InlineData(Separate, false)]
    [InlineData(Separate, true)]
    [InlineData(Bindless, false)]
    [InlineData(Bindless, true)]
    public void ConstructedSamplerPassesToAFunction(string source, bool optimize)
    {
        byte[] bytes = GlslCompiler.CompileToBytes(source, ShaderStage.Fragment, optimize: optimize);
        StructValidator.Valid(bytes);
    }

    // The shorthand compiles to exactly what glslang makes of the same program written out by hand, a copy of each
    // function taking the texture and sampler apart. Those hand written programs are in the parity corpus, where
    // glslang made their goldens and spirv-val checked them.
    [Theory]
    [InlineData(Separate, "combined_sampler_split_separate", false)]
    [InlineData(Separate, "combined_sampler_split_separate", true)]
    [InlineData(Bindless, "combined_sampler_split_bindless", false)]
    [InlineData(Bindless, "combined_sampler_split_bindless", true)]
    public void MatchesGlslangOnTheHandWrittenSplit(string source, string corpus, bool optimize)
    {
        Snippet split = TestCorpus.Parity.Single(s => s.Name == $"Spirv 1.0/{corpus}.frag");
        byte[] golden = (optimize ? TestCorpus.GoldenOpt(split) : TestCorpus.GoldenUnopt(split))!;
        byte[] ours = GlslCompiler.CompileToBytes(source, ShaderStage.Fragment, optimize: optimize);
        Assert.True(golden.AsSpan().SequenceEqual(ours), $"{corpus}: {ours.Length} bytes against the golden's {golden.Length}");
    }

    // Inside the function the texture and sampler are still read as non uniform, as the caller picked them: the image
    // and sampler a sampled image is made from carry the decoration, as glslang leaves them for an inline lookup.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonUniformSurvivesTheCall(bool optimize)
    {
        uint[] words = Words(GlslCompiler.CompileToBytes(Bindless, ShaderStage.Fragment, optimize: optimize));
        HashSet<uint> nonUniform = [];
        List<uint> operands = [];
        for (int i = 5; i < words.Length;)
        {
            uint op = words[i] & 0xFFFF, count = words[i] >> 16;
            if (op == 71 && words[i + 2] == 5300) nonUniform.Add(words[i + 1]);   // OpDecorate NonUniform
            if (op == 86) operands.AddRange([words[i + 3], words[i + 4]]);       // OpSampledImage image, sampler
            i += (int)count;
        }
        Assert.NotEmpty(operands);
        Assert.All(operands, id => Assert.Contains(id, nonUniform));
    }

    // A local that reuses the parameter's name in an inner scope keeps its own meaning in the copy.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalsShadowingTheParameterStayLocal(bool optimize)
    {
        const string source = """
            #version 450
            layout(set=0, binding=0) uniform texture2D tex;
            layout(set=0, binding=1) uniform sampler smp;
            layout(location=0) in vec2 uv;
            layout(location=0) out vec4 o;
            float Read(sampler2D s, vec2 p)
            {
                float a = texture(s, p).a;
                { float s = a * 2.0; a += s; }
                for (int s = 0; s < 2; s++) a += float(s);
                { float b = 1.0, s = b + texture(s, p).r; a += s; }
                return a + texture(s, p).r;
            }
            void main() { o = vec4(Read(sampler2D(tex, smp), uv)); }
            """;
        StructValidator.Valid(GlslCompiler.CompileToBytes(source, ShaderStage.Fragment, optimize: optimize));
    }

    // Programs that already compiled are left exactly as they were.
    [Fact]
    public void VariablesStillPassDirectly()
    {
        const string source = """
            #version 450
            layout(set=0, binding=0) uniform sampler2D s;
            layout(location=0) in vec2 uv;
            layout(location=0) out vec4 o;
            vec4 Read(sampler2D t, vec2 p) { return texture(t, p); }
            void main() { o = Read(s, uv); }
            """;
        byte[] bytes = GlslCompiler.CompileToBytes(source, ShaderStage.Fragment);
        StructValidator.Valid(bytes);
        Assert.DoesNotContain("_split", System.Text.Encoding.ASCII.GetString(bytes));
    }

    static uint[] Words(byte[] bytes)
    {
        uint[] words = new uint[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, words, 0, words.Length * 4);
        return words;
    }
}
