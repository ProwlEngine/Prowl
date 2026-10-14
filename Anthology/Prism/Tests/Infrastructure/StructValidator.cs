using Prowl.Prism.Optimizer;

namespace Prowl.Prism.Tests;

/// <summary>
/// Structural well-formedness assertions for a SPIR-V word stream, via the library's public gate
/// (SpirVOptimizer.Validate): header sanity and a strict instruction word-count walk. This is NOT
/// a full validator - the deep guarantee comes from the golden byte-parity suites (the references
/// were validated when generated). It is exactly the right strength for regression checks
/// whose contract is "compiles and produces a structurally sound module" (adversarial legal cases,
/// the f16 constructs, auto-map colliding mixes) and it needs no external toolchain.
/// </summary>
public static class StructValidator
{
    /// <summary>Asserts structural well-formedness; returns the validated word stream.</summary>
    public static uint[] Valid(byte[] bytes)
    {
        var words = new uint[bytes.Length / 4];
        for (int i = 0; i < words.Length; i++)
            words[i] = bytes[i * 4] | (uint)bytes[i * 4 + 1] << 8 | (uint)bytes[i * 4 + 2] << 16 | (uint)bytes[i * 4 + 3] << 24;
        SpirVOptimizer.Validate(words);
        return words;
    }
}
