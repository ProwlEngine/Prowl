using System;
using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class FailedVariantTests : IDisposable
{
    private sealed class FailingCompiler : IShaderCompiler
    {
        public int Calls;
        public bool Fail = true;
        public int SessionVersion { get; set; }

        public IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass) => [];

        public ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend)
        {
            Calls++;
            if (Fail)
                throw new InvalidOperationException("broken");

            return new ShaderDescription();
        }
    }


    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(false));
    private readonly FailingCompiler _compiler = new();
    private readonly Variant _fallback = new([], [(GraphicsBackend.Vulkan, new ShaderDescription())]);
    private readonly ShaderPass _pass;
    private int _reports;


    public FailedVariantTests()
    {
        _pass = new ShaderPass { State = new PassState(), InlineSlang = "a" };
        _pass.CompileFailed += (_, _, _) => _reports++;
        _pass.Bind(_device, "Test", 0, [], [], _compiler, CompileMode.OnDemand, _fallback);
    }


    public void Dispose()
    {
        _device.Dispose();
    }


    [Fact]
    public void FailedCompile_FallsBackAndRetriesOnce()
    {
        for (int i = 0; i < 5; i++)
            Assert.Same(_fallback, _pass.GetVariant(0));

        Assert.Equal(1, _compiler.Calls);
        Assert.Equal(1, _reports);
    }


    [Fact]
    public void SourceChange_Retries()
    {
        _pass.GetVariant(0);
        _pass.InlineSlang = "b";
        _pass.GetVariant(0);
        _pass.GetVariant(0);

        Assert.Equal(2, _compiler.Calls);
        Assert.Equal(2, _reports);
    }


    [Fact]
    public void SessionChange_RetriesAndRecovers()
    {
        _pass.GetVariant(0);
        _compiler.SessionVersion++;
        _compiler.Fail = false;

        Variant recovered = _pass.GetVariant(0);

        Assert.NotSame(_fallback, recovered);
        Assert.Equal(2, _compiler.Calls);
        Assert.Equal(1, _reports);
    }


    [Fact]
    public void NoFallback_ThrowsWithInnerException()
    {
        ShaderPass pass = new() { State = new PassState(), InlineSlang = "a" };
        pass.Bind(_device, "Test", 0, [], [], _compiler, CompileMode.OnDemand);

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(() => pass.GetVariant(0));
        InvalidOperationException second = Assert.Throws<InvalidOperationException>(() => pass.GetVariant(0));

        Assert.Equal("broken", first.InnerException!.Message);
        Assert.Equal("broken", second.InnerException!.Message);
        Assert.Equal(1, _compiler.Calls);
    }
}
