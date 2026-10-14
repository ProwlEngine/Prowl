using System.Text.RegularExpressions;
using Prowl.Prism.Compiler;
using Prowl.Prism.Glsl;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// The corpus of intentionally-broken shaders under Error Validation/. Golden generation verified at
/// generation time that the reference compiler rejects every one of them, and captured its normalized
/// diagnostic as the error-message golden (Golden/Errors). This suite asserts Prism rejects them
/// the same way - cleanly, with a <see cref="GlslParseException"/> - and that the rejection happens
/// where the reference compiler's does: the reported source line must match the line of its first
/// diagnostic. (Prism's message phrasing is deliberately its own; the golden rides along in the
/// failure output as the reference for what the compiler should be complaining about.)
/// </summary>
public sealed class ErrorCorpusTests
{
    // glslang reports these six at a line PAST the construct - its directive-error and end-of-input
    // bookkeeping emits the next line rather than the token's own. Prism reports the true source
    // line, which reads better, so these documented detection-point divergences are exempt from the
    // location check instead of replicating the quirk.
    static readonly HashSet<string> LineExempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "160-macro-self-recursion.frag",       // expansion consumed into the following line
        "preprocessor_09_line_no_arg.frag",    // #line renumbers before glslang's own error reports
        "preprocessor_12_if_trailing_op.frag", // #if evaluation error reported on the following line
        "preprocessor_16_if_trailing_op.frag",
        "syntax_20_malformed_extension.frag",  // directive error reported on the following line
        "syntax_22_abrupt_eof.comp",           // end-of-input reported one line past the file
    };

    public static IEnumerable<object[]> Cases() => TestCorpus.Errors.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Cases))]
    public void IsRejectedWhereGlslangDiagnosesTheError(Snippet snippet)
    {
        var golden = TestCorpus.GoldenError(snippet);
        Assert.True(golden is not null, $"{snippet.Name}: no error golden was generated.");

        var ex = Assert.Throws<GlslParseException>(() =>
            GlslCompiler.Compile(snippet.Source, snippet.Stage, snippet.Target));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message), $"{snippet.Name}: rejected with an empty message.");

        // Location gate: when both sides carry a line, Prism's rejection must sit on the line of
        // glslang's first diagnostic - same defect, same place. Lineless Prism messages (entry-point
        // and late-semantic errors) and locationless goldens have nothing to compare; marker goldens
        // (glslang dies without a diagnostic) fall out the same way.
        var gls = Regex.Match(golden, "^ERROR: <shader>:(\\d+): ", RegexOptions.Multiline);
        var prism = Regex.Match(ex.Message, "^line (\\d+):");
        if (gls.Success && prism.Success && !LineExempt.Contains(snippet.Name))
            Assert.True(prism.Groups[1].Value == gls.Groups[1].Value,
                $"""
                {snippet.Name}: Prism rejects at line {prism.Groups[1].Value} but the reference diagnostic names line {gls.Groups[1].Value}.
                --- the golden (the reference diagnostic) ---
                {golden.TrimEnd()}
                --- Prism's message ---
                {ex.Message}
                """);
    }
}