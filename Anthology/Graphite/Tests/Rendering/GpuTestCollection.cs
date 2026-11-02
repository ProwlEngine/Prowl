using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[CollectionDefinition("GPU Tests", DisableParallelization = true)]
public sealed class GpuTestCollection { }

/// <summary>One compiler for every GPU test, so each variant compiles once per run. Never disposed.</summary>
internal static class TestCompiler
{
    public static GlslShaderCompiler Shared { get; } = new();
}
