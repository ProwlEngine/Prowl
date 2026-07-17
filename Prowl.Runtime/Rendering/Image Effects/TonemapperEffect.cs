// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

public enum TonemapperType
{
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

    public TonemapperType Type = TonemapperType.AgX;
    public float Contrast = 1.1f;
    public float Saturation = 1.1f;

    Material _mat;

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.Tonemapper));

        _mat.SetKeyword(new Keyword("TonemapMode", Type.ToString()));

        _mat.SetFloat("Contrast", Contrast);
        _mat.SetFloat("Saturation", Saturation);

        // Create new LDR buffer to replace HDR
        var ldrBuffer = RenderTexture.GetTemporaryRT(
            context.Width,
            context.Height,
            true, // Keep depth for transparents
            [PixelFormat.R8_G8_B8_A8_UNorm] // LDR format
        );

        var cmd = Graphics.GetCommandBuffer("Tonemapper");

        // Preserve depth so transparents drawn into the LDR buffer still occlude correctly.
        if (context.SceneColor.InternalDepth != null)
        {
            cmd.SetRenderTargets(ldrBuffer.frameBuffer, context.SceneColor.frameBuffer);
            cmd.BlitFramebuffer(false, true);
        }

        cmd.Blit(context.SceneColor, ldrBuffer, _mat, 0);
        Graphics.Submit(cmd);

        context.ReplaceSceneColor(ldrBuffer);
    }

    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
    }
}
