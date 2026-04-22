// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

public sealed class FXAAEffect : ImageEffect
{
    public float EdgeThresholdMax = 0.0625f;  // 0.063 - 0.333 (lower = more AA, slower)
    public float EdgeThresholdMin = 0.0312f;  // 0.0312 - 0.0833 (trims dark edges)
    public float SubpixelQuality = 0.75f;     // 0.0 - 1.0 (subpixel AA amount)

    private Material _mat;

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.FXAA));

        // Set shader parameters
        _mat.SetFloat("_EdgeThresholdMax", EdgeThresholdMax);
        _mat.SetFloat("_EdgeThresholdMin", EdgeThresholdMin);
        _mat.SetFloat("_SubpixelQuality", SubpixelQuality);
        _mat.SetVector("_Resolution", new Float2(context.Width, context.Height));

        // Blit to a temporary RT to avoid reading and writing the same texture simultaneously
        var temp = RenderTexture.GetTemporaryRT(context.Width, context.Height, false, [context.SceneColor.MainTexture.ImageFormat]);
        RenderPipeline.Blit(context.SceneColor, temp, _mat, 0);
        RenderPipeline.Blit(temp, context.SceneColor, null, 0);
        RenderTexture.ReleaseTemporaryRT(temp);
    }

    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
    }
}
