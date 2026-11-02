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

        public ComputeDescription CompileCompute(ShaderPass pass, Keyword[] combo, GraphicsBackend backend) => throw new NotSupportedException();
    }


    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(false));
    private readonly FailingCompiler _compiler = new();
    private readonly ShaderPass _pass;


    public FailedVariantTests()
    {
        _pass = new ShaderPass { State = new PassState(), Source = "a" };
        _pass.Bind(_device, "Test", 0, [], [], _compiler);
    }


    public void Dispose()
    {
        _device.Dispose();
    }


    [Fact]
    public void FailedCompile_ThrowsEveryTime_AndCompilesOnce()
    {
        for (int i = 0; i < 5; i++)
            Assert.Equal("broken", Assert.Throws<InvalidOperationException>(() => _pass.GetVariant()).InnerException!.Message);

        Assert.Equal(1, _compiler.Calls);
    }


    [Fact]
    public void SourceChange_Retries()
    {
        Assert.Throws<InvalidOperationException>(() => _pass.GetVariant());
        _pass.Source = "b";
        Assert.Throws<InvalidOperationException>(() => _pass.GetVariant());
        Assert.Throws<InvalidOperationException>(() => _pass.GetVariant());

        Assert.Equal(2, _compiler.Calls);
    }


    [Fact]
    public void SessionChange_RetriesAndRecovers()
    {
        Assert.Throws<InvalidOperationException>(() => _pass.GetVariant());
        _compiler.SessionVersion++;
        _compiler.Fail = false;

        Variant recovered = _pass.GetVariant();

        Assert.True(recovered.IsCompiledFor(GraphicsBackend.Vulkan));
        Assert.Equal(2, _compiler.Calls);
    }
}
