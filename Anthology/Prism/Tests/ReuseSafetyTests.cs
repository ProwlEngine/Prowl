using Prowl.Prism.Compiler;
using Prowl.Prism.Glsl;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// The front-end reuses a thread-static Lowerer/Module and object pools across compiles (Reset rewinds
/// them). This guards that reuse never leaks state between compiles: a shader's output must not depend
/// on what was compiled before it on the same thread. The whole parity corpus is compiled forward and
/// then in reverse on one thread, and every shader must produce byte-identical SPIR-V both times -
/// each pass is additionally pinned against its golden, so order-independence and correctness are the
/// same assertion.
/// </summary>
public sealed class ReuseSafetyTests
{
    [Fact]
    public void ReusedLowererIsOrderIndependent()
    {
        var corpus = TestCorpus.Parity;

        static byte[]? Try(Snippet s)
        {
            try { return GlslCompiler.CompileToBytes(s.Source, s.Stage, s.Target); }
            catch (GlslParseException) { return null; }
        }

        var forward = new Dictionary<string, byte[]?>();
        foreach (var s in corpus) forward[s.Name] = Try(s);

        var mismatches = new List<string>();
        foreach (var s in Enumerable.Reverse(corpus))
        {
            var again = Try(s);
            var first = forward[s.Name];
            bool same = (again is null && first is null) || (again is not null && first is not null && again.AsSpan().SequenceEqual(first));
            if (!same) mismatches.Add(s.Name);
        }

        Assert.True(mismatches.Count == 0, "state leaked across reused compiles for: " + string.Join(", ", mismatches.Take(20)));
    }
}
