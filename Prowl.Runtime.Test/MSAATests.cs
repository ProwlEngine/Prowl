// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Headless tests for MSAA render-target plumbing: the temporary-RT pool discriminates on sample
/// count, multisampled targets construct without a device, and the constructors that
/// <c>Deserialize</c> resolves reflectively still exist. GPU behaviour (multisample allocation,
/// framebuffer completeness, the resolve blit) is not exercised here - the test process is
/// headless, so that is verified by running the editor.
/// </summary>
public class MSAATests
{
    private static readonly TextureImageFormat[] Color = [TextureImageFormat.Color4b];

    [Fact]
    public void MaxSamples_DefaultsToGLMinimum_PreDevice()
    {
        // GL 4.1 guarantees MAX_SAMPLES >= 4, so the pre-device default must not be lower or
        // clamping would silently disable MSAA levels the hardware actually supports.
        Assert.True(Graphics.MaxSamples >= 4);
    }

    [Fact]
    public void MSAASamples_ValuesAreLiteralSampleCounts()
    {
        // The pipeline casts the enum straight to a GL sample count.
        Assert.Equal(1, (int)MSAASamples.None);
        Assert.Equal(2, (int)MSAASamples.X2);
        Assert.Equal(4, (int)MSAASamples.X4);
        Assert.Equal(8, (int)MSAASamples.X8);
    }

    [Fact]
    public void Camera_DefaultsToNoMSAA()
    {
        // MSAA must be opt-in: existing projects should render exactly as before.
        Assert.Equal(MSAASamples.None, new Camera().MSAA);
    }

    [Fact]
    public void RenderTexture_MultisampledConstructsHeadless()
    {
        var rt = new RenderTexture(64, 64, true, Color, 4);

        Assert.Equal(4, rt.SampleCount);
        Assert.True(rt.IsMultisampled);
        // Every attachment, depth included, must carry the sample count or the framebuffer
        // would come back FRAMEBUFFER_INCOMPLETE_MULTISAMPLE on a real device.
        Assert.Equal(4, rt.MainTexture.Samples);
        Assert.Equal(4, rt.InternalDepth.Samples);
        Assert.True(rt.InternalDepth.IsMultisampled);
    }

    [Fact]
    public void RenderTexture_SingleSampledIsNotMultisampled()
    {
        var rt = new RenderTexture(64, 64, true, Color);

        Assert.Equal(1, rt.SampleCount);
        Assert.False(rt.IsMultisampled);
        Assert.False(rt.MainTexture.IsMultisampled);
        Assert.Equal(TextureType.Texture2D, rt.MainTexture.Type);
    }

    [Fact]
    public void RenderTexture_RejectsZeroSampleCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTexture(64, 64, true, Color, 0));
    }

    [Fact]
    public void Texture2D_MultisampleCtor_RejectsSingleSample()
    {
        // A sample count of 1 would produce a TEXTURE_2D_MULTISAMPLE with no sampler state
        // rather than a normal sampleable texture, which is never what a caller wants.
        Assert.Throws<ArgumentOutOfRangeException>(() => new Texture2D(64, 64, 1, TextureImageFormat.Color4b));
    }

    [Fact]
    public void Pool_DoesNotAliasAcrossSampleCounts()
    {
        // The highest-value test here. ReleaseTemporaryRT rebuilds the pool key from the
        // instance rather than remembering it, so if either the key struct or the rebuild drops
        // SampleCount, a released single-sampled RT gets handed back to a request for a
        // multisampled one and rendering breaks in a way no compiler catches.
        RenderTexture single = RenderTexture.GetTemporaryRT(32, 32, true, Color);
        RenderTexture.ReleaseTemporaryRT(single);

        RenderTexture multi = RenderTexture.GetTemporaryRT(32, 32, true, Color, 4);

        Assert.NotSame(single, multi);
        Assert.Equal(4, multi.SampleCount);

        RenderTexture.ReleaseTemporaryRT(multi);
    }

    [Fact]
    public void Pool_ReusesTargetsOfMatchingSampleCount()
    {
        // The flip side: SampleCount must survive the key rebuild in ReleaseTemporaryRT, or a
        // multisampled RT would be filed under the wrong key and never reused.
        RenderTexture first = RenderTexture.GetTemporaryRT(32, 32, true, Color, 4);
        RenderTexture.ReleaseTemporaryRT(first);

        RenderTexture second = RenderTexture.GetTemporaryRT(32, 32, true, Color, 4);

        Assert.Same(first, second);

        RenderTexture.ReleaseTemporaryRT(second);
    }

    [Fact]
    public void RenderTexture_DeserializeConstructor_IsResolvableByReflection()
    {
        // RenderTexture.Deserialize invokes this constructor reflectively by exact signature, so
        // changing its parameter list breaks deserialization at runtime with no compile error.
        Assert.NotNull(typeof(RenderTexture).GetConstructor(
            [typeof(int), typeof(int), typeof(bool), typeof(TextureImageFormat[]), typeof(int)]));
    }

    [Fact]
    public void RenderTexture_FourArgConstructor_StillExists()
    {
        // Retained for the many direct callers that predate MSAA.
        Assert.NotNull(typeof(RenderTexture).GetConstructor(
            [typeof(int), typeof(int), typeof(bool), typeof(TextureImageFormat[])]));
    }

    [Fact]
    public void Texture2D_DeserializeConstructor_IsResolvableByReflection()
    {
        // Same reflective-invoke hazard as RenderTexture: the multisample overload must not have
        // displaced the signature Texture2D.Deserialize looks up.
        Assert.NotNull(typeof(Texture2D).GetConstructor(
            [typeof(uint), typeof(uint), typeof(bool), typeof(TextureImageFormat)]));
    }

    [Fact]
    public void Texture2DMultisample_IsNotMipmappable()
    {
        Assert.True(Enum.IsDefined(typeof(TextureType), TextureType.Texture2DMultisample));
        Assert.False(Texture.IsTextureTypeMipmappable(TextureType.Texture2DMultisample));
    }

    [Fact]
    public void BlitShader_HasBlendOffCopyPass()
    {
        // The pipeline copies into multisampled targets with pass 1, which must be the Blend Off
        // pass. If pass order changed, copies would silently alpha-blend instead of replace.
        Shader blit = Shader.LoadDefault(DefaultShader.Blit);

        string[] passNames = blit.Passes.Select(p => p.Name).ToArray();
        Assert.Equal(2, passNames.Length);
        Assert.Equal("Copy", passNames[1]);
        Assert.False(blit.GetPass(1).State.DoBlend);

        // ZWrite Off matters: copying into a multisampled target must not disturb its depth.
        Assert.False(blit.GetPass(1).State.DepthWrite);
        Assert.False(blit.GetPass(1).State.DepthTest);
    }

    [Fact]
    public void Blit_FromMultisampledSource_Throws()
    {
        // MainTexture is a Texture2D either way, so without this guard a multisampled attachment
        // would bind to a sampler2D and render garbage rather than fail.
        var ms = new RenderTexture(32, 32, true, Color, 4);
        var dst = new RenderTexture(32, 32, true, Color);

        using var cmd = Graphics.GetCommandBuffer("test");
        Assert.Throws<InvalidOperationException>(() => cmd.Blit(ms, dst));
    }

    [Fact]
    public void ResolveMultisample_RejectsMismatchedSizes()
    {
        // GL forbids scaling on any blit touching a multisampled framebuffer; catching it here
        // beats an INVALID_OPERATION surfacing later on the render thread.
        var src = new RenderTexture(64, 64, true, Color, 4);
        var dst = new RenderTexture(32, 32, true, Color);

        using var cmd = Graphics.GetCommandBuffer("test");
        Assert.Throws<ArgumentException>(() => cmd.ResolveMultisample(src, dst));
    }
}
