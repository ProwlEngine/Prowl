#nullable enable

using System;

using Xunit;

namespace Prowl.Graphite.Tests;

public class UsageKindTests
{
    private static readonly GraphTextureDesc s_desc = GraphTextureDesc.Sized(4, 4, true, PixelFormat.R8_G8_B8_A8_UNorm);

    [Fact]
    public void OutputWithTwoWriteKinds_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareOutputTexture(
            "usage_multi_write", s_desc, usage: TextureState.Attachment | TextureState.Storage));
    }

    [Fact]
    public void Output_WithoutWriteKind_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareOutputTexture(
            "usage_nowrite", s_desc, usage: TextureState.Sampled));
    }

    [Fact]
    public void Input_WithWriteKind_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareInputTexture(
            "usage_inwrite", TextureState.Attachment));
    }

    [Fact]
    public void DepthReadOnly_AsColorKind_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareInputTexture(
            "usage_depthcolor", TextureState.DepthReadOnly));
    }

    [Fact]
    public void StorageDepthUsage_Throws()
    {
        RenderContextBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.DeclareOutputTexture(
            "usage_depthstorage", s_desc, depthUsage: TextureState.Storage));
    }
}
