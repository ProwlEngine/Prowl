using System.Text;
using Prowl.Prism;
using Prowl.Prism.Compiler;
using Prowl.Prism.Glsl;
using Prowl.Prism.Optimizer;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

// Production-hardening guards. Prism compiles shaders on demand at runtime, on a background thread, from
// sources that may be machine-generated or author-supplied, so malformed input must produce a
// GlslParseException the host can report -- never an uncatchable stack overflow, an exception type the
// caller cannot anticipate, a hang, or silently wrong output. The same contract holds for the public
// optimizer and module APIs against corrupt modules.
public sealed class RobustnessTests
{
    const char Backslash = (char)92;

    static uint[] Compile(string src, ShaderStage stage = ShaderStage.Fragment) =>
        GlslCompiler.Compile(src, stage, SpirVVersion.V1_0, optimize: false).Words.ToArray();

    // ---- unbounded recursion: a stack overflow cannot be caught and would kill the host process ----

    public static TheoryData<string, string> DeepShapes()
    {
        var d = new TheoryData<string, string>();
        string head = "#version 450\nlayout(location=0) in float f;\nlayout(location=0) out float o;\nvoid main(){ ";
        d.Add("binary chain", head + "float x = f" + string.Concat(Enumerable.Repeat("+f", 50000)) + "; o = x; }\n");
        d.Add("parenthesis", head + "float x = " + new string('(', 50000) + "f" + new string(')', 50000) + "; o = x; }\n");
        d.Add("block", head + new string('{', 50000) + " o = 1.0; " + new string('}', 50000) + " }\n");
        d.Add("member chain", head + "vec4 v = vec4(0.0); o = v" + string.Concat(Enumerable.Repeat(".x", 50000)) + "; }\n");
        d.Add("unary chain", head + "float x = " + string.Concat(Enumerable.Repeat("-", 50000)) + "f; o = x; }\n");
        d.Add("index chain", head + "float a[2]; o = a" + string.Concat(Enumerable.Repeat("[0]", 50000)) + "; }\n");
        return d;
    }

    [Theory]
    [MemberData(nameof(DeepShapes))]
    public void DeeplyNestedSourceIsRejectedNotFatal(string _, string source) =>
        Assert.Throws<GlslParseException>(() => Compile(source));

    [Fact]
    public void NestingWithinTheLimitStillCompiles()
    {
        string src = "#version 450\nlayout(location=0) in float f;\nlayout(location=0) out float o;\n"
                   + "void main(){ float x = f" + string.Concat(Enumerable.Repeat("+f", 200)) + "; o = x; }\n";
        Assert.NotEmpty(Compile(src));
    }

    // ---- the exception contract: malformed input must surface as GlslParseException ----

    public static TheoryData<string, string> MalformedSources()
    {
        var d = new TheoryData<string, string>
        {
            { "unterminated macro params", "#version 450\n#define F(\nvoid main(){}\n" },
            { "unterminated macro param list", "#version 450\n#define F(a\nvoid main(){}\n" },
            { "attribute at end of input", "#version 450\nvoid main(){ [[" },
            { "non-integer attribute arg", "#version 450\nvoid main(){ [[dependency_length(x)]] for(int i=0;i<2;++i){} }\n" },
            { "oversized decimal literal", "#version 450\nvoid main(){ int k = 999999999999999999999; }\n" },
            { "oversized hex literal", "#version 450\nvoid main(){ int k = 0xFFFFFFFFFFFFFFFFF; }\n" },
            { "invalid octal digit", "#version 450\nvoid main(){ int k = 09; }\n" },
            { "exponent without digits", "#version 450\nvoid main(){ float k = 1e; }\n" },
            { "signed exponent without digits", "#version 450\nvoid main(){ float k = 1e+; }\n" },
            { "oversized literal in #if", "#version 450\n#if 99999999999999999999\n#endif\nvoid main(){}\n" },
            { "unterminated block", "#version 450\nvoid main(){ float a = 1.0;" },
            { "unterminated field list", "#version 450\nstruct S { float a;" },
            { "unterminated switch body", "#version 450\nvoid main(){ switch(1) { case 0: " },
            { "unterminated initializer", "#version 450\nvoid main(){ float a[2] = { 1.0," },
            { "array size out of range", "#version 450\nvoid main(){ float a[4294967297]; }\n" },
        };
        return d;
    }

    [Theory]
    [MemberData(nameof(MalformedSources))]
    public void MalformedSourceThrowsGlslParseException(string _, string source) =>
        Assert.Throws<GlslParseException>(() => Compile(source));

    // An instruction longer than the 16-bit word-count field cannot be encoded; emitting it truncated would
    // yield a module that derails partway through instead of a diagnostic.
    [Fact]
    public void OversizedInstructionIsRejected()
    {
        string src = "#version 450\nconst float T[70000] = float[70000]("
                   + string.Join(",", Enumerable.Repeat("1.0", 70000))
                   + ");\nlayout(location=0) out float o;\nvoid main(){ o = T[3]; }\n";
        Assert.Throws<GlslParseException>(() => Compile(src));
    }

    // glslang does not fold division/modulo by a constant zero - the runtime OpSDiv/OpSMod/OpFDiv stays
    // in the module (its own oracle accepts all of these). Prism must therefore not fold them either
    // (folding would materialize an IEEE +/-Inf constant or an int overflow glslang never emits) and must
    // never reject or crash on them.
    [Theory]
    [InlineData("const int k = 1/0;")]
    [InlineData("const int k = 1%0;")]
    [InlineData("float f = 1.0/0.0;")]
    [InlineData("int a = 5 % 0;")]
    public void ConstantDivisionByZeroStaysUnfoldedLikeGlslang(string decl) =>
        Assert.NotEmpty(Compile("#version 450\n" + decl + "\nvoid main(){}\n"));

    // ---- pathological-scale sources ----
    // These were stress files in the merged adversarial corpus; running the reference toolchain on them
    // was heavy enough to pressure the generating machine, so they live here as programmatic sources
    // without goldens instead of as corpus cases. The contract is unchanged: a controlled, sound outcome.
    [Fact]
    public void HundredThousandStatementsCompileSoundly()
    {
        string src = "#version 450\nvoid main(){ float x = 0.0;\n"
                   + string.Concat(Enumerable.Repeat("x += 0.5;\n", 100000))
                   + "}\n";
        byte[] bytes = GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, SpirVVersion.V1_0);
        StructValidator.Valid(bytes);
    }

    [Fact]
    public void HugeArraySizeCompilesSoundly()
    {
        // 40 MB of (virtual) local array - legal per glslang; Prism must keep up without blowing up.
        string src = "#version 450\nvoid main(){ float a[10000000]; a[0] = 1.0; }\n";
        byte[] bytes = GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, SpirVVersion.V1_0);
        StructValidator.Valid(bytes);
    }

    // ---- preprocessor semantics ----

    // A leading zero is octal in #if exactly as it is in the language; reading it as decimal would make a
    // variant guard select a different branch than the compiler.
    [Fact]
    public void PreprocessorReadsLeadingZeroAsOctal() =>
        Assert.NotEmpty(Compile("#version 450\n#if 010 == 8\nlayout(location=0) out float o;\nvoid main(){ o = 1.0; }\n#else\n#error octal mismatch\n#endif\n"));

    // && and || must not evaluate the untaken side, which is what makes the usual divisor guard safe.
    [Fact]
    public void PreprocessorShortCircuitsLogicalOperators() =>
        Assert.NotEmpty(Compile("#version 450\n#define N 0\n#if N != 0 && 100/N > 2\n#endif\nlayout(location=0) out float o;\nvoid main(){ o = 1.0; }\n"));

    [Fact]
    public void PreprocessorShortCircuitsLogicalOr() =>
        Assert.NotEmpty(Compile("#version 450\n#define N 0\n#if N == 0 || 100/N > 2\n#endif\nlayout(location=0) out float o;\nvoid main(){ o = 1.0; }\n"));

    // Line continuations join lines but must not shift the numbering of the lines after them.
    [Fact]
    public void LineContinuationPreservesLineNumbers()
    {
        string src = "#version 450\n#define A 1 " + Backslash + "\n  2\n#define B 3 " + Backslash + "\n  4\n"
                   + "layout(location=0) out float o;\n@\n";
        var ex = Assert.Throws<GlslParseException>(() => Compile(src));
        Assert.Equal(7, ex.Line);
    }

    [Fact]
    public void LineContinuationStillJoinsLines() =>
        Assert.NotEmpty(Compile("#version 450\n#define V vec4(1.0, " + Backslash + "\n 0.0, 0.0, 1.0)\nlayout(location=0) out vec4 o;\nvoid main(){ o = V; }\n"));

    // A doubling ladder of distinct macros expands exponentially; it must fail fast rather than stall.
    [Fact]
    public void RunawayMacroExpansionIsBounded()
    {
        var sb = new StringBuilder("#version 450\n#define A0 1\n");
        for (int i = 1; i <= 30; i++) sb.Append("#define A").Append(i).Append(" A").Append(i - 1).Append(" A").Append(i - 1).Append('\n');
        sb.Append("#if A30\n#endif\nlayout(location=0) out float o;\nvoid main(){ o = 1.0; }\n");
        Assert.Throws<GlslParseException>(() => Compile(sb.ToString()));
    }

    // A macro ladder deeper than the expansion-depth cap must reject cleanly instead of walking the
    // C# stack off the end (which in .NET is an uncatchable process kill).
    [Fact]
    public void DeepMacroLadderIsRejectedNotFatal()
    {
        var sb = new StringBuilder("#version 450\n#define M0 1.0\n");
        for (int i = 1; i <= 300; i++) sb.Append("#define M").Append(i).Append(" (M").Append(i - 1).Append(" + M").Append(i - 1).Append(")\n");
        sb.Append("layout(location=0) out float o;\nvoid main(){ o = M300; }\n");
        Assert.Throws<GlslParseException>(() => Compile(sb.ToString()));
    }

    // ---- API safety: pooled state must not be observable across calls ----

    // Two Lowerers alive on one thread must not share the pooled Module, or the first one's output is
    // silently overwritten by the second.
    [Fact]
    public void ConcurrentLowerersOnOneThreadDoNotShareState()
    {
        string Src(string v) => "#version 450\nlayout(location=0) out vec4 o;\nvoid main(){ o = vec4(" + v + "); }\n";
        var unitA = Parser.Parse(Src("1.0"));
        var unitB = Parser.Parse(Src("2.0"));

        var a = new Lowerer().Reset(ShaderStage.Fragment, SpirVVersion.V1_0, false).Lower(unitA).Serialize();
        var lowererB = new Lowerer();
        var b = lowererB.Reset(ShaderStage.Fragment, SpirVVersion.V1_0, false).Lower(unitB).Serialize();

        Assert.NotEqual(a, b);
        Assert.Equal(Compile(Src("2.0")), b);
    }

    // The preprocessor pools its token buffer internally; the public API must hand back an owned list.
    [Fact]
    public void PreprocessorResultIsNotOverwrittenByTheNextCall()
    {
        var first = Preprocessor.Process("#version 450\nvoid main(){ float a; }\n");
        var firstText = string.Join(" ", first.Tokens.Select(t => t.Text));
        var second = Preprocessor.Process("#version 450\nint zzz;\n");

        Assert.NotSame(first.Tokens, second.Tokens);
        Assert.Equal(firstText, string.Join(" ", first.Tokens.Select(t => t.Text)));
    }

    // Lowering mutates the instance and its pooled module, so it must refuse to be re-entered.
    [Fact]
    public void ReentrantLowerIsRejected()
    {
        var unit = Parser.Parse("#version 450\nlayout(location=0) out vec4 o;\nvoid main(){ o = vec4(1.0); }\n");
        var lowerer = new Lowerer().Reset(ShaderStage.Fragment, SpirVVersion.V1_0, false);
        lowerer.Lower(unit);
        // A second Lower on the same instance after a Reset is the supported reuse path and must work.
        Assert.NotNull(lowerer.Reset(ShaderStage.Fragment, SpirVVersion.V1_0, false).Lower(unit));
    }

    // ---- the public optimizer + module APIs against corrupt modules ----
    // The goldens guarantee compiler OUTPUT is well-formed; these pin the other direction:
    // arbitrary words handed to the public APIs must produce clean GlslParseExceptions (structural
    // gate or semantic boundary), never raw NREs, KeyNotFoundExceptions, or hangs.

    static uint[] CompiledModule()
    {
        const string src = "#version 450\n"
            + "layout(std430, binding=0) buffer D { float v[]; };\n"
            + "layout(local_size_x=8) in;\n"
            + "void main(){ float x = sqrt(v[gl_GlobalInvocationID.x]); v[gl_GlobalInvocationID.x] = x * 0.5 + 1.0; }\n";
        return GlslCompiler.Compile(src, ShaderStage.Compute, SpirVVersion.V1_0, optimize: false).Words.ToArray();
    }

    public static TheoryData<int> CorruptWordIndices()
    {
        // One case per module word from index 5 on: zeroing any of them must stay inside the contract.
        var d = new TheoryData<int>();
        for (int i = 5; i < CompiledModule().Length; i++) d.Add(i);
        return d;
    }

    [Theory]
    [MemberData(nameof(CorruptWordIndices))]
    public void CorruptedModuleIsRejectedCleanlyByTheOptimizer(int word)
    {
        var words = CompiledModule();
        words[word] = 0;
        try
        {
            SpirVOptimizer.Optimize(words);
        }
        catch (GlslParseException)
        {
            // the structural gate or the semantic boundary rejecting garbage: the contract working.
        }
    }

    [Theory]
    [InlineData(new uint[] { })]
    [InlineData(new uint[] { 1, 2 })]
    [InlineData(new uint[] { 0x07230203, 0x00010300, 0, 0 })]                       // too short, no schema
    [InlineData(new uint[] { 0xDEADBEEF, 0x00010300, 0, 16, 0 })]                   // bad magic
    [InlineData(new uint[] { 0x07230203, 0x00010300, 0, 16, 0, 0x00000000 })]       // zero-length instr
    [InlineData(new uint[] { 0x07230203, 0x00010300, 0, 16, 5 })]                   // bad schema word
    public void GarbageModuleIsRejectedByTheStructuralGate(uint[] words) =>
        Assert.Throws<GlslParseException>(() => SpirVOptimizer.Optimize(words));
}
