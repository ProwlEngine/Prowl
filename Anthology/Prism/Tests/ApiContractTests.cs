using System.Diagnostics;
using System.Reflection;
using System.Text;
using Prowl.Prism.Compiler;
using Prowl.Prism.Glsl;
using Prowl.Prism.Optimizer;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

// Tests for the public API, the host process, and resource use. Shader-level behavior is covered by the corpora
// (Corpus/ for valid GLSL, byte-matched against its goldens; Error Validation/ for invalid GLSL, which must be
// rejected where glslang rejects it). These tests cover what a corpus shader cannot express. Each test is pinned
// to a numbered entry in FINDINGS.md, given in the section comment above it.
//
// Tests that would take the whole test host down while the bug exists (an uncatchable
// StackOverflowException, or an endless loop) are marked Skip with the reason; drop the Skip once the
// fix is in so they guard against regressions.

[CollectionDefinition("SerialTests", DisableParallelization = true)]
public sealed class SerialTestCollection { }

public sealed class ApiContractTests
{
    const string Frag = "#version 450\nlayout(location=0) in float f;\nlayout(location=0) out float o;\n";

    static uint[] Words(byte[] bytes)
    {
        var w = new uint[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, w, 0, w.Length * 4);
        return w;
    }

    static byte[] Input(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("inputs/" + name)
            ?? throw new InvalidOperationException($"missing embedded input {name}");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    // ---- #5: the preprocessor must run on the 64 MB compile lane like Compile does ----

    [Fact]
    public void PreprocessRunsOnTheCompileLane()
    {
        // Compile() runs on the lane thread; Preprocess() runs on the caller's stack, so ~800 nested
        // parentheses in an #if overflow an ordinary 1 MB thread (uncatchable, kills the process).
        string? thread = null;
        GlslCompiler.Preprocess("#version 450\n#include \"a.glsl\"\nvoid main(){}\n", includes: (name, _, _) =>
        {
            thread = Thread.CurrentThread.Name;
            return new IncludeResult(name, "");
        });
        Assert.Equal("Prism compile", thread);
    }

    [Fact]
    public void ParseRunsOnTheCompileLane()
    {
        // The recursive-descent parser overflows a 1 MB thread on deep expressions unless it runs on the lane.
        string src = "#version 450\nlayout(location=0) out float o;\nvoid main(){ o = " + new string('(', 3000) + "1.0" + new string(')', 3000) + "; }\n";
        Exception? error = null;
        var t = new Thread(() => { try { Parser.Parse(src); } catch (Exception e) { error = e; } }, 1024 * 1024);
        t.Start(); t.Join();
        Assert.True(error is null, error?.ToString());
    }

    // ---- #6: SpirVOptimizer.Optimize must not recurse on the caller's stack ----

    [Fact]
    public void OptimizeOfDeepControlFlowIsNotFatalOnAnOrdinaryThread()
    {
        // Nesting is capped at SPIR-V's 1023 limit, so depth comes from a long chain of branches instead: the
        // recursive CFG walks overflow a 1.5 MB stack on it unless Optimize moves to the compile lane.
        string src = Frag + "void main(){ float x = 0.0; " + string.Concat(Enumerable.Repeat("if (x < f) x += 1.0; ", 8000)) + " o = x; }\n";
        uint[] words = GlslCompiler.Compile(src, ShaderStage.Fragment).Words.ToArray();
        Exception? error = null;
        var t = new Thread(() => { try { SpirVOptimizer.Optimize(words); } catch (Exception e) { error = e; } }, 1536 * 1024);
        t.Start(); t.Join();
        Assert.True(error is null or GlslParseException, error?.ToString());
    }

    // ---- #7: corrupt modules must be a GlslParseException, never a stack overflow or a hang ----

    [Theory]
    [InlineData("corrupt_selfref_access_chain.spv")]      // AdceFunction.AddStores recursion
    [InlineData("corrupt_bound_one.spv")]                 // valid module with id bound forced to 1
    [InlineData("corrupt_bound_max_feedsastore.spv")]     // LocalSingleStoreElim.FeedsAStore recursion
    [InlineData("corrupt_selfref_constant.spv")]          // Folder.FloatConstKind recursion
    [InlineData("corrupt_selfref_simplification_hang.spv")] // Simplification never terminates
    public void CorruptModulesAreRejectedCleanly(string input)
    {
        uint[] words = Words(Input(input));
        var task = Task.Run(() => { try { SpirVOptimizer.Optimize(words); return null; } catch (Exception e) { return e; } });
        Assert.True(task.Wait(TimeSpan.FromSeconds(30)), $"{input}: Optimize did not finish");
        Assert.True(task.Result is null or GlslParseException, task.Result?.ToString());
    }

    // ---- #35: optimizing a module with NonSemantic.Shader.DebugInfo must keep it valid ----

    [Fact]
    public void DebugInfoNeverReferencesARemovedFunction()
    {
        // glslang -V -gVS output (valid per spirv-val). After inlining, Prism deletes the callee but keeps
        // its DebugFunctionDefinition, which then names an undefined id; spirv-opt -O keeps it valid.
        uint[] w = SpirVOptimizer.Optimize(Words(Input("debuginfo_gVS_functions.spv")));
        var functions = new HashSet<uint>();
        var debugSets = new HashSet<uint>();
        var defs = new List<uint>();
        for (int p = 5; p < w.Length; p += (int)(w[p] >> 16))
        {
            int op = (int)(w[p] & 0xFFFF);
            if (op == 54) functions.Add(w[p + 2]);                                     // OpFunction
            if (op == 11 && Encoding.ASCII.GetString(MemoryMarshalBytes(w, p + 2, (int)(w[p] >> 16) - 2)).StartsWith("NonSemantic.Shader.DebugInfo"))
                debugSets.Add(w[p + 1]);                                                 // OpExtInstImport
            if (op == 12 && debugSets.Contains(w[p + 3]) && w[p + 4] == 101) defs.Add(w[p + 6]);   // DebugFunctionDefinition
        }
        Assert.NotEmpty(defs);
        foreach (uint fn in defs) Assert.True(functions.Contains(fn), $"DebugFunctionDefinition names %{fn}, which is not a function in the output");
    }

    static byte[] MemoryMarshalBytes(uint[] w, int start, int count)
    {
        var b = new byte[count * 4];
        Buffer.BlockCopy(w, start * 4, b, 0, b.Length);
        return b;
    }

    // ---- #36: the optimizer must never allocate ids past the module's maximum bound ----

    const string LoopShader = Frag + "float g(float a){ float r = 0.0; for (int i = 0; i < 4; i++) r += a * float(i); return r; }\n"
                                   + "void main(){ float x = f; if (f > 0.5) x = g(f); else x = g(x + 1.0); o = x; }\n";

    [Fact]
    public void OptimizerStaysWithinTheMaximumIdBound()
    {
        uint[] words = GlslCompiler.Compile(LoopShader, ShaderStage.Fragment).Words.ToArray();
        words[3] = 0x3FFFFF;   // the largest bound SPIR-V allows; the optimizer needs fresh ids beyond it
        uint[] outWords;
        try { outWords = SpirVOptimizer.Optimize(words); }
        catch (GlslParseException) { return; }   // refusing is fine; overflowing the bound is not
        Assert.True(outWords[3] <= 0x3FFFFF, $"output id bound {outWords[3]} exceeds the SPIR-V maximum 4194303");
    }

    // ---- #31: structured control flow deeper than SPIR-V's limit must not become invalid output ----

    [Fact]
    public void ControlFlowNestingBeyondTheSpirvLimitIsRejected()
    {
        // SPIR-V's universal limit is 1023 nested control-flow constructs; spirv-val rejects deeper modules.
        // (glslang emits the invalid module too - Prism's contract is a GlslParseException instead.)
        string src = Frag + "void main(){ o = 0.0; " + string.Concat(Enumerable.Repeat("if (f > 0.0) { ", 1100))
                   + "o = 1.0;" + string.Concat(Enumerable.Repeat(" }", 1100)) + " }\n";
        Assert.Throws<GlslParseException>(() => GlslCompiler.Compile(src, ShaderStage.Fragment));
    }

    // ---- #72: input encodings glslang accepts ----

    const string Simple = "#version 450\nlayout(location=0) out float o;\nvoid main(){ o = 1.0; }\n";

    [Fact]
    public void Utf8ByteOrderMarkIsAccepted() =>
        Assert.Equal(GlslCompiler.CompileToBytes(Simple, ShaderStage.Fragment),
                     GlslCompiler.CompileToBytes("﻿" + Simple, ShaderStage.Fragment));

    [Fact]
    public void CarriageReturnOnlyLineEndingsAreAccepted() =>
        Assert.Equal(GlslCompiler.CompileToBytes(Simple, ShaderStage.Fragment),
                     GlslCompiler.CompileToBytes(Simple.Replace('\n', '\r'), ShaderStage.Fragment));

    // ---- #75: deep macro nesting glslang accepts ----

    [Fact]
    public void DeeplyNestedMacroCallsCompile()
    {
        // glslang accepts F(F(...F(1.0)...)) 2000 deep; Prism's rescan is quadratic and gives up ("too large").
        const int depth = 2000;
        string src = "#version 450\n#define F(x) (x)\nlayout(location=0) out float o;\nvoid main(){ o = "
                   + string.Concat(Enumerable.Repeat("F(", depth)) + "1.0" + new string(')', depth) + "; }\n";
        Assert.NotEmpty(GlslCompiler.CompileToBytes(src, ShaderStage.Fragment));
    }

    // ---- #76: host-supplied include results must not escape as a NullReferenceException ----

    [Fact]
    public void IncludeCallbackReturningNullContentIsAParseError() =>
        Assert.Throws<GlslParseException>(() => GlslCompiler.Compile(
            "#version 450\n#extension GL_GOOGLE_include_directive : require\n#include \"a.glsl\"\nvoid main(){}\n",
            ShaderStage.Fragment, includes: (_, _, _) => new IncludeResult(null!, null!)));
}

// Process-wide measurements (thread count, total allocation, wall time) - run alone so other tests'
// compiles don't pollute them.
[Collection("SerialTests")]
public sealed class ResourceUsageTests
{
    const string Frag = "#version 450\nlayout(location=0) in float f;\nlayout(location=0) out float o;\n";

    // ---- #73: one compile lane per calling thread, never released ----

    [Fact]
    public void ShortLivedCallerThreadsDoNotLeakCompileLanes()
    {
        const string src = "#version 450\nlayout(location=0) out float o;\nvoid main(){ o = 1.0; }\n";
        GlslCompiler.Compile(src, ShaderStage.Fragment);   // warm this thread's lane
        GC.Collect(); GC.WaitForPendingFinalizers();
        int before = Process.GetCurrentProcess().Threads.Count;

        const int callers = 24;
        for (int i = 0; i < callers; i++)
        {
            var t = new Thread(() => GlslCompiler.Compile(src, ShaderStage.Fragment));
            t.Start(); t.Join();
        }
        GC.Collect(); GC.WaitForPendingFinalizers();
        Thread.Sleep(500);
        int grown = Process.GetCurrentProcess().Threads.Count - before;
        // Each caller thread currently leaves behind a "Prism compile" thread (64 MB stack reservation and its
        // thread-static pools) that lives for the life of the process.
        Assert.True(grown < callers / 2, $"{grown} threads were left behind by {callers} finished caller threads");
    }

    // ---- #74: optimizer memory must stay proportionate on nested loops ----

    [Fact]
    [Trait("Category", "Perf")]
    public void NestedLoopsOptimizeWithinBoundedMemory()
    {
        const int depth = 256;
        string src = Frag + "void main(){ float x = 0.0; " + string.Concat(Enumerable.Repeat("for(int i=0;i<2;i++) ", depth))
                   + "x += 1.0; o = x; }\n";
        GlslCompiler.CompileToBytes(Frag + "void main(){ o = f; }\n", ShaderStage.Fragment, optimize: true);   // warm
        long before = GC.GetTotalAllocatedBytes(true);
        GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, optimize: true);
        long mb = (GC.GetTotalAllocatedBytes(true) - before) >> 20;
        // SsaRewrite's allocation grows ~cubically with loop depth: ~340 MB at 256 deep, 2.5 GB at 512.
        Assert.True(mb < 100, $"optimizing {depth} nested loops allocated {mb} MB");
    }

    // ---- #75: layout of deep struct nesting must not be cubic ----

    [Fact]
    [Trait("Category", "Perf")]
    public void DeepStructNestingInABufferLaysOutQuickly()
    {
        const int depth = 512;
        var sb = new StringBuilder(Frag).Append("struct S0 { float x; };\n");
        for (int i = 1; i <= depth; i++) sb.Append($"struct S{i} {{ S{i - 1} x; }};\n");
        sb.Append($"layout(std430, binding=0) buffer B {{ S{depth} s; }} b;\nvoid main(){{ o = b.s")
          .Append(string.Concat(Enumerable.Repeat(".x", depth + 1))).Append("; }\n");
        string src = sb.ToString();
        var sw = Stopwatch.StartNew();
        GlslCompiler.CompileToBytes(src, ShaderStage.Fragment);
        // Align/Size/StructSize recurse without memoization: ~7.7 s at 512 levels, >40 s at 1024.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"{depth}-deep struct chain took {sw.Elapsed.TotalSeconds:F1} s");
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void NestedUnrollLoopsOptimizeQuickly()
    {
        const int depth = 128;
        string src = Frag + "void main(){ float x = 0.0; " + string.Concat(Enumerable.Repeat("[[unroll]] for(int i=0;i<2;i++) ", depth))
                   + "x += f; o = x; }\n";
        var sw = Stopwatch.StartNew();
        GlslCompiler.CompileToBytes(src, ShaderStage.Fragment, optimize: true);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"{depth} nested [[unroll]] loops took {sw.Elapsed.TotalSeconds:F1} s to optimize");
    }
}
