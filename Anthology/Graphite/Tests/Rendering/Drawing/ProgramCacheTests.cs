using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

file sealed class NoResources : IMaterialResources
{
    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => 0;

    public uint SamplerSlot(SamplerDescription? sampler) => 0;
}

/// <summary>Holds back compiles of variants with a gated keyword until released, and fails variants with a broken one.</summary>
internal sealed class GatedCompiler(GlslShaderCompiler inner) : IShaderCompiler
{
    public readonly ManualResetEventSlim Gate = new(false);
    public string GatedAxis = "";
    public string GatedValue = "";
    public int Compiles;

    public IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass) => inner.GetAxes(pass);

    public IReadOnlyList<ShaderGlobal> GetGlobals(ShaderPass pass) => inner.GetGlobals(pass);

    public ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend)
    {
        Interlocked.Increment(ref Compiles);
        foreach (Keyword keyword in combo)
        {
            if (keyword.Name == GatedAxis && keyword.Value == GatedValue)
                Gate.Wait();
            if (keyword.Name == "_BROKEN" && keyword.Value == "true")
                throw new InvalidOperationException("broken variant");
        }
        return inner.Compile(pass, combo, backend);
    }

    public ComputeDescription CompileCompute(ShaderPass pass, Keyword[] combo, GraphicsBackend backend) => inner.CompileCompute(pass, combo, backend);
}

public sealed class ProgramCacheTests : IDisposable
{
    private const string Source = """
        Shader "Test/Variants"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                GLSLPROGRAM
                Shared
                {
                    #pragma variant _TINT
                    #pragma variant _BROKEN
                    #pragma variant _QUALITY OFF LOW HIGH
                }
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) out vec4 outColor;
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0); outColor = vec4(1.0); }
                }
                ENDGLSL
            }
        }
        """;

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _glsl = new();
    private readonly GatedCompiler _compiler;
    private readonly MaterialSystem _materials;
    private readonly GlobalRegistry _globals;
    private readonly DrawPrograms _programs = new();
    private readonly ProgramCache _cache;
    private readonly Shader _shader;
    private readonly List<string> _reports = new();

    public ProgramCacheTests()
    {
        InternalShaders.Register();
        _compiler = new GatedCompiler(_glsl);
        _materials = new MaterialSystem(_device, new NoResources());
        _globals = new GlobalRegistry(_device, new NoResources());
        _cache = new ProgramCache(_device, _compiler, _globals, _programs, deterministic: false, error => _reports.Add(error.Message));
        _shader = new Shader(ShaderParser.Parse(Source), _compiler);
    }

    public void Dispose()
    {
        _compiler.Gate.Set();
        _cache.Dispose();
        _globals.Dispose();
        _materials.Dispose();
        _glsl.Dispose();
        _device.Dispose();
    }

    private ushort Program(params (string Axis, string Value)[] keywords)
    {
        Material material = _materials.Create(_shader);
        foreach ((string axis, string value) in keywords)
            material.SetKeyword(axis, value);
        _materials.Extract(new List<Material>());
        Assert.True(_programs.TryGet(material.Draw, PassType.Forward, out ushort id));
        return id;
    }

    private GraphicsProgram? Get(ushort id) => _cache.Get(id, false, _globals.Renderer.KeywordSnapshot);

    private GraphicsProgram WaitFor(ushort id)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (Get(id) is { } program && _cache.Pending == 0)
                return program;
            Thread.Sleep(1);
        }
        throw new TimeoutException("The variant never compiled.");
    }

    [Fact]
    public void Variants_CompileOnAWorker_AndDrawNothingUntilTheFirstIsReady()
    {
        _compiler.GatedAxis = "_TINT";
        _compiler.GatedValue = "false";
        ushort plain = Program();

        Assert.Null(Get(plain));
        Assert.Equal(1, _cache.Pending);

        _compiler.Gate.Set();
        GraphicsProgram program = WaitFor(plain);
        Assert.Same(program, Get(plain));
        Assert.Equal(1, _compiler.Compiles);
    }

    [Fact]
    public void VariantsNotReadyYet_DrawWithThePassDefault_ThenTheirOwn()
    {
        _compiler.GatedAxis = "_TINT";
        _compiler.GatedValue = "true";
        ushort tinted = Program(("_TINT", "true"));
        Stopwatch watch = Stopwatch.StartNew();
        GraphicsProgram? fallback;
        while ((fallback = Get(tinted)) == null && watch.Elapsed < TimeSpan.FromSeconds(20))
            Thread.Sleep(5);
        Assert.NotNull(fallback);
        Assert.False(_cache.Ready(tinted, false, _globals.Renderer.KeywordSnapshot));

        _compiler.Gate.Set();
        watch.Restart();
        while (!_cache.Ready(tinted, false, _globals.Renderer.KeywordSnapshot) && watch.Elapsed < TimeSpan.FromSeconds(20))
            Thread.Sleep(5);
        Assert.NotSame(fallback, Get(tinted));
    }

    [Fact]
    public void ADefaultVariantNotReady_DrawsTheDeclaredFallbackShader()
    {
        _shader.Fallback = new Shader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Fallback.shader")!), _compiler);
        _compiler.GatedAxis = "_TINT";
        _compiler.GatedValue = "false";
        ushort plain = Program();
        Stopwatch watch = Stopwatch.StartNew();
        GraphicsProgram? fallback;
        while ((fallback = Get(plain)) == null && watch.Elapsed < TimeSpan.FromSeconds(20))
            Thread.Sleep(5);
        Assert.NotNull(fallback);
        Assert.StartsWith("Graphite/Fallback", fallback!.Name);
        Assert.False(_cache.Ready(plain, false, _globals.Renderer.KeywordSnapshot));
    }

    [Fact]
    public void EditingThePassSource_RecompilesBuiltAndFailedVariants()
    {
        ushort plain = Program();
        ushort broken = Program(("_BROKEN", "true"));
        WaitFor(plain);
        Stopwatch first = Stopwatch.StartNew();
        while (!_cache.Failed(broken, false, _globals.Renderer.KeywordSnapshot) && first.Elapsed < TimeSpan.FromSeconds(20))
            Thread.Sleep(5);
        Assert.True(_cache.Failed(broken, false, _globals.Renderer.KeywordSnapshot));
        int compiles = _compiler.Compiles;

        _shader.Definition.Passes![0].Source += Environment.NewLine;
        Assert.False(_cache.Ready(plain, false, _globals.Renderer.KeywordSnapshot));
        WaitFor(plain);
        Assert.False(_cache.Failed(broken, false, _globals.Renderer.KeywordSnapshot));
        Stopwatch watch = Stopwatch.StartNew();
        while (!_cache.Failed(broken, false, _globals.Renderer.KeywordSnapshot) && watch.Elapsed < TimeSpan.FromSeconds(20))
            Thread.Sleep(5);
        Assert.True(_cache.Failed(broken, false, _globals.Renderer.KeywordSnapshot));
        Assert.True(_compiler.Compiles >= compiles + 2);
    }

    [Fact]
    public void AnAxisDeclaredDifferentlyInTwoPasses_IsRejected()
    {
        const string twoPasses = """
            Shader "Test/Mismatch"
            {
                Pass { Tags { "LightMode" = "Forward" } GLSLPROGRAM Shared { #pragma variant MODE A B } Vertex { void main() { } } Fragment { void main() { } } ENDGLSL }
                Pass { Tags { "LightMode" = "Prepass" } GLSLPROGRAM Shared { #pragma variant MODE X Y Z } Vertex { void main() { } } Fragment { void main() { } } ENDGLSL }
            }
            """;
        Assert.Throws<InvalidOperationException>(() => new Shader(ShaderParser.Parse(twoPasses), _compiler));
    }

    [Fact]
    public void BakedVariants_LoadWithoutACompiler()
    {
        ShaderPass compiled = ShaderParser.Parse(Source).Passes![0];
        PassVariants source = compiled.Variants(_compiler, _device.BackendType);
        ShaderDescription description = source.Require();
        BakedVariants.Add(compiled.SourceName, compiled.Source, new BakedVariants.Pass
        {
            Axes = [.. source.Axes],
            Globals = [.. source.Globals],
            Variants = [new Variant(source.Combo(0), [(_device.BackendType, description)])],
        });
        try
        {
            using System.IO.MemoryStream file = new();
            BakedVariants.Write(file);
            BakedVariants.Clear();
            file.Position = 0;
            BakedVariants.Read(file);

            ShaderPass player = ShaderParser.Parse(Source).Passes![0];
            PassVariants baked = player.Variants(null, _device.BackendType);
            Assert.Equal(VariantState.Ready, baked.Request(0, CompilePolicy.BakedOnly, out ShaderDescription loaded, out _));
            Assert.Equal(VariantState.Failed, baked.Request(1, CompilePolicy.BakedOnly, out _, out _));
            Assert.Equal(source.Globals, baked.Globals);
            loaded.BlendState = BlendStateDescription.SingleOverrideBlend;
            loaded.DepthStencilState = DepthStencilStateDescription.Disabled;
            loaded.RasterizerState = RasterizerStateDescription.CullNone;
            using GraphicsProgram program = _device.ResourceFactory.CreateGraphicsProgram(loaded);
        }
        finally
        {
            BakedVariants.Clear();
        }
    }

    [Fact]
    public void FailedVariants_DrawWithThePassDefault()
    {
        ushort broken = Program(("_BROKEN", "true"));
        GraphicsProgram fallback = WaitFor(broken);
        Assert.True(_cache.Failed(broken, false, _globals.Renderer.KeywordSnapshot));
        Assert.Same(fallback, Get(broken));
    }

    [Fact]
    public void FreedProgramIds_DropTheirCompiledVariants_OnceIdleLongEnough()
    {
        _programs.Freed += _cache.Drop;
        _programs.IdleFrames = 2;
        ushort plain = Program();
        _programs.Acquire(plain);
        WaitFor(plain);
        Assert.Equal(1, _cache.EntryCount);

        _programs.Release(plain);
        _programs.Sweep();
        _programs.Sweep();
        Assert.Equal(1, _cache.EntryCount);
        _programs.Sweep();
        Assert.Equal(0, _cache.EntryCount);
    }

    // A renderable moving between buckets releases its program and takes it again in the same frame
    [Fact]
    public void AProgramReleasedAndTakenAgain_KeepsItsIdAndCompiledVariants()
    {
        _programs.Freed += _cache.Drop;
        ushort plain = Program();
        _programs.Acquire(plain);
        WaitFor(plain);
        int compiles = _compiler.Compiles;

        for (int frame = 0; frame < 300; frame++)
        {
            _programs.Sweep();
            _programs.Release(plain);
            Assert.Equal(plain, Program());
            _programs.Acquire(plain);
            Assert.NotNull(Get(plain));
        }

        Assert.Equal(compiles, _compiler.Compiles);
        Assert.Equal(1, _cache.EntryCount);
    }

    [Fact]
    public void BrokenVariants_AreReportedOnce_AndDrawNothing()
    {
        ushort broken = Program(("_BROKEN", "true"));
        Stopwatch watch = Stopwatch.StartNew();
        while (_reports.Count == 0 && watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            Assert.Null(Get(broken));
            Thread.Sleep(1);
        }

        Assert.Null(Get(broken));
        Assert.Single(_reports);
        Assert.Contains("broken variant", _reports[0]);
    }
}
