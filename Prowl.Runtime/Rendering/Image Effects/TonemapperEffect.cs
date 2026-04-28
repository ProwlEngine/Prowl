// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

public enum TonemapperType
{
    None,
    Melon,
    ACES,
    ACESSimple,
    AgX,
    ReinhardSimple,
    ReinhardLuma,
    ReinhardWhitePreserving,
    RomBinDaHouse,
    Uncharted2,
}

public sealed class TonemapperEffect : ImageEffect
{
    public override bool TransformsToLDR => true;

    public TonemapperType Type = TonemapperType.Melon;
    public float Contrast = 1.1f;
    public float Saturation = 1.1f;

    Material _mat;

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.Tonemapper));

        _mat.SetKeyword("TONEMAP_MELON", Type == TonemapperType.Melon);
        _mat.SetKeyword("TONEMAP_ACES", Type == TonemapperType.ACES);
        _mat.SetKeyword("TONEMAP_ACES_SIMPLE", Type == TonemapperType.ACESSimple);
        _mat.SetKeyword("TONEMAP_AGX", Type == TonemapperType.AgX);
        _mat.SetKeyword("TONEMAP_REINHARD_SIMPLE", Type == TonemapperType.ReinhardSimple);
        _mat.SetKeyword("TONEMAP_REINHARD_LUMA", Type == TonemapperType.ReinhardLuma);
        _mat.SetKeyword("TONEMAP_REINHARD_WHITE", Type == TonemapperType.ReinhardWhitePreserving);
        _mat.SetKeyword("TONEMAP_ROMBINDAHOUSE", Type == TonemapperType.RomBinDaHouse);
        _mat.SetKeyword("TONEMAP_UNCHARTED2", Type == TonemapperType.Uncharted2);

        _mat.SetFloat("Contrast", Contrast);
        _mat.SetFloat("Saturation", Saturation);

        // Create new LDR buffer to replace HDR
        var ldrBuffer = RenderTexture.GetTemporaryRT(
            context.Width,
            context.Height,
            true, // Keep depth for transparents
            [TextureImageFormat.Color4b] // LDR format
        );

        // Copy depth from current buffer if it has one
        if (context.SceneColor.InternalDepth != null)
        {
            Graphics.BindFramebuffer(context.SceneColor.frameBuffer, FBOTarget.Read);
            Graphics.BindFramebuffer(ldrBuffer.frameBuffer, FBOTarget.Draw);
            Graphics.BlitFramebuffer(
                0, 0, context.Width, context.Height,
                0, 0, context.Width, context.Height,
                ClearFlags.Depth, BlitFilter.Nearest
            );
        }

        // Tonemap HDR to LDR
        RenderPipeline.Blit(context.SceneColor, ldrBuffer, _mat, 0);

        // Replace the scene color buffer with LDR version
        context.ReplaceSceneColor(ldrBuffer);
    }

    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
    }
}
