#nullable enable

using System;

using Xunit;

namespace Prowl.Graphite.RenderGraph.Tests;

public class UsageKindTests
{
    private static readonly GraphTextureDesc s_desc = GraphTextureDesc.Sized(4, 4, true, PixelFormat.R8_G8_B8_A8_UNorm);

    [Fact]
    public void MultiKindOutput_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareOutputTexture(
            "usage_multi", s_desc, usage: TextureUsageKind.Attachment | TextureUsageKind.Sampled));
    }

    [Fact]
    public void MultiKindInput_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareInputTexture(
            "usage_multi_in", TextureUsageKind.Sampled | TextureUsageKind.TransferSrc));
    }

    [Fact]
    public void SingleKindOutput_IsAccepted()
    {
        RenderContextBuilder builder = new();
        TextureHandle handle = builder.DeclareOutputTexture("usage_ok", s_desc, usage: TextureUsageKind.Storage);
        Assert.True(handle.IsValid);
    }

    [Fact]
    public void Output_WithoutWriteKind_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareOutputTexture(
            "usage_nowrite", s_desc, usage: TextureUsageKind.Sampled));
    }

    [Fact]
    public void Input_WithWriteKind_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareInputTexture(
            "usage_inwrite", TextureUsageKind.Attachment));
    }

    [Fact]
    public void DepthReadOnly_AsColorKind_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareInputTexture(
            "usage_depthcolor", TextureUsageKind.DepthReadOnly));
    }

    [Fact]
    public void StorageDepthUsage_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareOutputTexture(
            "usage_depthstorage", s_desc, depthUsage: TextureUsageKind.Storage));
    }

    [Fact]
    public void DepthReadOnly_OnOutput_IsAccepted()
    {
        RenderContextBuilder builder = new();
        TextureHandle handle = builder.DeclareOutputTexture(
            "usage_depthro", s_desc, depthUsage: TextureUsageKind.DepthReadOnly);
        Assert.True(handle.IsValid);
    }
}
