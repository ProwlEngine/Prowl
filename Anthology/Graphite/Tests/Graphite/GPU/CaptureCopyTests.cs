#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugging;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file static class CapturePasses
{
    public static void AddWrite(FrameGraph graph, string name, RenderTexture texture, DeviceBuffer external, Color color, byte value, bool depth = false)
    {
        TextureHandle handle = graph.Import(texture);
        graph.AddPass(name, pass => pass.Write(handle, TextureState.Attachment, depth ? TextureState.Attachment : null), context =>
        {
            CommandBuffer cmd = context.Command;
            cmd.ClearColorTarget(0, color);
            if (depth)
                cmd.ClearDepthStencil(0.5f, 7);
            cmd.UpdateBuffer(external, 0, new byte[] { value, value, value, value });
        });
    }
}

internal sealed class CopyingProfiler : ICaptureProfiler, IDisposable
{
    public readonly List<(string Pass, string Name, CopyPlacement Placement, CaptureCopy Copy)> Copies = new();
    public readonly List<Exception> Errors = new();
    public string? OnlyPass;

    public void BeginExecution(ulong executionId, string graphName) { }

    public void EndExecution() { }

    public void DescribeGroup(in GroupCaptureInfo group) { }

    public void OnPassEnd(in PassInfo pass, ReadOnlySpan<PassReference> references, ICaptureContext capture)
    {
        if (OnlyPass != null && pass.Name != OnlyPass)
            return;

        foreach (PassReference reference in references.ToArray())
        {
            foreach (CopyPlacement placement in new[] { CopyPlacement.BeforePass, CopyPlacement.AfterPass })
            {
                try
                {
                    Copies.Add((pass.Name, reference.Name, placement, capture.Copy(reference, placement)));
                }
                catch (InvalidOperationException ex)
                {
                    Errors.Add(ex);
                }
            }
        }
    }

    public void Dispose()
    {
        foreach ((string _, string _, CopyPlacement _, CaptureCopy copy) in Copies)
            copy.Staging.Dispose();
    }
}

public abstract class CaptureCopyTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [SkippableFact]
    public void BeforeAndAfterPass_HoldPrePassAndPassOutputContents()
    {
        DeviceBuffer external = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        external.Name = "External";
        RenderTexture target = RF.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false));
        target.ColorTextures[0].Name = "Target";
        using CopyingProfiler profiler = new() { OnlyPass = "Second" };
        using FrameGraph graph = new(GD, "Test");
        CapturePasses.AddWrite(graph, "First", target, external, Color.FromBytes(255, 0, 0, 255), 1);
        CapturePasses.AddWrite(graph, "Second", target, external, Color.FromBytes(0, 0, 255, 255), 2);
        graph.Execute(profiler);
        GD.WaitForIdle();

        Assert.Empty(profiler.Errors);
        Assert.Equal(Enumerable.Repeat((byte)1, 4), Read(profiler, "External", CopyPlacement.BeforePass).Take(4));
        Assert.Equal(Enumerable.Repeat((byte)2, 4), Read(profiler, "External", CopyPlacement.AfterPass).Take(4));
        Assert.Equal(16, Read(profiler, "External", CopyPlacement.AfterPass).Length);
        Assert.All(Texels(Read(profiler, "Target", CopyPlacement.BeforePass)), texel => Assert.Equal(new byte[] { 255, 0, 0, 255 }, texel));
        Assert.All(Texels(Read(profiler, "Target", CopyPlacement.AfterPass)), texel => Assert.Equal(new byte[] { 0, 0, 255, 255 }, texel));
    }

    [SkippableFact]
    public void DepthStencil_CopiesBothAspects()
    {
        RenderTexture target = RF.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: true));
        target.DepthTexture!.Name = "Depth";
        using CopyingProfiler profiler = Run(target, depth: true);

        Assert.Empty(profiler.Errors);
        CaptureCopy copy = profiler.Copies.Single(c => c.Name == "Depth" && c.Placement == CopyPlacement.AfterPass).Copy;
        byte[] bytes = GD.Map(copy.Staging).ToArray();
        CopyRegion depth = copy.Regions.Span[0];
        CopyRegion stencil = copy.Regions.Span[1];
        Assert.Equal(PixelFormat.R8_UInt, stencil.Format);
        for (int i = 0; i < 8 * 8; i++)
        {
            uint raw = BitConverter.ToUInt32(bytes, (int)depth.Offset + i * 4);
            float value = depth.Format == PixelFormat.D32_Float_S8_UInt ? BitConverter.UInt32BitsToSingle(raw) : (raw & 0xFFFFFF) / 16777215f;
            Assert.InRange(value, 0.499f, 0.501f);
            Assert.Equal(7, bytes[(int)stencil.Offset + i]);
        }
    }

    [SkippableFact]
    public void MultisampledColor_IsResolved()
    {
        RenderTexture target = RF.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: false, sampleCount: TextureSampleCount.Count4));
        target.ColorTextures[0].Name = "Msaa";
        using CopyingProfiler profiler = Run(target, depth: false);

        Assert.Empty(profiler.Errors);
        Assert.All(Texels(Read(profiler, "Msaa", CopyPlacement.AfterPass)), texel => Assert.Equal(new byte[] { 255, 0, 0, 255 }, texel));
    }

    private CopyingProfiler Run(RenderTexture target, bool depth)
    {
        DeviceBuffer external = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        CopyingProfiler profiler = new();
        using FrameGraph graph = new(GD, "Test");
        CapturePasses.AddWrite(graph, "Only", target, external, Color.FromBytes(255, 0, 0, 255), 1, depth);
        graph.Execute(profiler);
        GD.WaitForIdle();

        return profiler;
    }

    [SkippableFact]
    public void TargetBacking_Throws()
    {
        RenderTexture target = RF.CreateRenderTexture(new RenderTextureDescription(8, 8, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, depth: true));
        using CopyingProfiler profiler = new();
        using FrameGraph graph = new(GD, "Test");
        TextureHandle handle = graph.ImportTarget(target.Framebuffer);
        graph.AddPass("Target", pass => pass.Write(handle), _ => { });
        graph.Execute(profiler);
        GD.WaitForIdle();

        Assert.NotEmpty(profiler.Errors);
        Assert.Empty(profiler.Copies);
    }

    private byte[] Read(CopyingProfiler profiler, string name, CopyPlacement placement)
    {
        CaptureCopy copy = profiler.Copies.Single(c => c.Name == name && c.Placement == placement).Copy;
        return GD.Map(copy.Staging).ToArray();
    }

    private static IEnumerable<byte[]> Texels(byte[] bytes)
        => Enumerable.Range(0, 8 * 8).Select(i => bytes.Skip(i * 4).Take(4).ToArray());
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanCaptureCopyTests : CaptureCopyTests<VulkanDeviceCreator> { }
#endif
