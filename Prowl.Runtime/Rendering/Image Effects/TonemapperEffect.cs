// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

public sealed class TonemapperEffect : ImageEffect
{
    public override bool TransformsToLDR => true;

    public float Contrast = 1.1f;
    public float Saturation = 1.1f;

    Material _mat;

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.Tonemapper));
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
}
