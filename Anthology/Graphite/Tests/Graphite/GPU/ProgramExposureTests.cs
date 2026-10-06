using System;
using System.Linq;

using Silk.NET.Windowing;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class ProgramExposureTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator, new()
{
    private sealed class SecondDevice : IDisposable
    {
        private readonly IWindow? _window;

        public SecondDevice()
        {
            new T().CreateGraphicsDevice(out _window, out GraphicsDevice device);
            Device = device;
        }

        public GraphicsDevice Device { get; }

        public void Dispose()
        {
            Device.WaitForIdle();
            Device.Dispose();
            _window?.Dispose();
        }
    }

    private static ResourceLayoutDescription[] StorageLayouts(string name, ShaderStages stage) =>
    [
        new ResourceLayoutDescription
        {
            Set = 0,
            Elements = [new ResourceLayoutElementDescription(name, ResourceKind.StructuredBufferReadOnly, stage, 0)]
        }
    ];

    [SkippableFact]
    public void GraphicsProgram_RebuildsOnSecondDeviceFromExposedData()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "ColoredQuadRenderer.slang");
        GraphicsProgram original = RF.CreateGraphicsProgram(new ShaderDescription(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts = StorageLayouts("InputVertices", ShaderStages.Vertex),
        });

        Assert.Equal(stages.Length, original.StageDescriptions.Count);
        for (int i = 0; i < stages.Length; i++)
        {
            Assert.Equal(stages[i].Stage, original.StageDescriptions[i].Stage);
            Assert.Equal(stages[i].EntryPoint, original.StageDescriptions[i].EntryPoint);
            Assert.True(stages[i].ShaderBytes.AsSpan().SequenceEqual(original.StageDescriptions[i].ShaderBytes));
        }

        ShaderDescription exposed = new(
            original.StageDescriptions.ToArray(),
            original.BlendState,
            original.DepthStencilState,
            original.RasterizerState,
            original.VertexLayouts.ToArray(),
            original.ResourceLayouts.ToArray());

        using SecondDevice second = new();
        using GraphicsProgram rebuilt = second.Device.ResourceFactory.CreateGraphicsProgram(exposed);

        Assert.Equal(ProgramKey.Compute(stages), original.Key);
        Assert.Equal(original.Key, rebuilt.Key);
        Assert.Equal(original.Stages, rebuilt.Stages);
        Assert.Equal(original.BlendState, rebuilt.BlendState);
        Assert.Equal(original.DepthStencilState, rebuilt.DepthStencilState);
        Assert.Equal(original.RasterizerState, rebuilt.RasterizerState);
        Assert.Equal(original.ResourceLayouts.Count, rebuilt.ResourceLayouts.Count);
    }

    [SkippableFact]
    public void ComputeProgram_RebuildsOnSecondDeviceFromExposedData()
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "ComputeColoredQuadGenerator.slang");
        ComputeProgram original = RF.CreateComputeProgram(new ComputeDescription(
            stage, StorageLayouts("OutputVertices", ShaderStages.Compute), 16, 16, 1));

        Assert.Equal(stage.Stage, original.StageDescription.Stage);
        Assert.Equal(stage.EntryPoint, original.StageDescription.EntryPoint);
        Assert.True(stage.ShaderBytes.AsSpan().SequenceEqual(original.StageDescription.ShaderBytes));

        ComputeDescription exposed = new(
            original.StageDescription,
            original.ResourceLayouts.ToArray(),
            original.ThreadGroupSizeX,
            original.ThreadGroupSizeY,
            original.ThreadGroupSizeZ);

        using SecondDevice second = new();
        using ComputeProgram rebuilt = second.Device.ResourceFactory.CreateComputeProgram(exposed);

        Assert.Equal(ProgramKey.Compute([stage]), original.Key);
        Assert.Equal(original.Key, rebuilt.Key);
        Assert.Equal(original.ThreadGroupSizeX, rebuilt.ThreadGroupSizeX);
        Assert.Equal(original.ThreadGroupSizeY, rebuilt.ThreadGroupSizeY);
        Assert.Equal(original.ThreadGroupSizeZ, rebuilt.ThreadGroupSizeZ);
        Assert.Equal(original.ResourceLayouts.Count, rebuilt.ResourceLayouts.Count);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanProgramExposureTests : ProgramExposureTests<VulkanDeviceCreator> { }
#endif
