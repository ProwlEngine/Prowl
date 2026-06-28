// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>Render-resolution scale shared by screen-space effects that can run at reduced
/// resolution and upsample (GTAO, SSR).</summary>
public enum EffectResolution
{
    Full,
    Half,
    Quarter
}

internal static class EffectResolutionExtensions
{
    public static float Scale(this EffectResolution res) => res switch
    {
        EffectResolution.Half => 0.5f,
        EffectResolution.Quarter => 0.25f,
        _ => 1.0f,
    };
}

/// <summary>
/// Ground-Truth Ambient Occlusion (GTAO) effect for realistic ambient occlusion.
/// Based on "Practical Realtime Strategies for Accurate Indirect Occlusion" by Activision.
/// Reference: https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion_NEW%20VERSION_COLOR.pdf
/// </summary>
public sealed class GTAOEffect : ImageEffect
{
    // Quality Settings

    /// <summary>Number of angular slices to sample around each pixel. Higher = better quality but slower.</summary>
    public int Slices = 6;

    /// <summary>Number of samples per direction. Higher = better quality but slower.</summary>
    public int DirectionSamples = 32;

    // Appearance Settings

    /// <summary>Sampling radius in world space units. Larger = more prominent occlusion from distant objects.</summary>
    public float Radius = 1.0f;

    /// <summary>Overall intensity of the ambient occlusion effect. Higher = darker occlusion.</summary>
    public float Intensity = 1.0f;

    /// <summary>Spatial bilateral blur radius. 0 = no blur. With temporal on, a small value is enough.</summary>
    public float BlurRadius = 1.0f;

    /// <summary>Accumulate AO over frames using motion vectors (with blue-noise jitter) to remove noise
    /// cheaply, letting you use fewer slices/samples.</summary>
    public bool UseTemporal = true;

    /// <summary>How much AO history to keep (higher = cleaner/more stable, more ghosting on motion).</summary>
    [Range(0f, 1f)] public float TemporalResponse = 0.9f;

    /// <summary>Resolution the AO is computed at. Lower = faster; the result is bilinearly upsampled
    /// at composite so the scene image itself stays full resolution.</summary>
    public EffectResolution Resolution = EffectResolution.Half;

    // Private fields
    private Material _mat;
    private Texture2D _noise;
    private RenderTexture _aoHistory;
    private bool _historyValid;
    private int _frameIndex;
    private Float2 _jitter;

    public override RenderStage Stage => RenderStage.AfterOpaques;

    public override void OnPreCull(Camera camera)
    {
        // Per-frame Halton offset to scroll the blue-noise dither so temporal accumulation converges.
        _jitter = new Float2(Halton(_frameIndex + 1, 2), Halton(_frameIndex + 1, 3));
        _frameIndex = (_frameIndex + 1) % 64;
    }

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.GTAO));
        _noise ??= Texture2D.LoadDefault(DefaultTexture.Noise);

        if (!context.DepthNormals.IsValid())
            return;

        // Scaled resolution for the AO + blur passes (composite stays full-res below).
        float scale = Resolution.Scale();
        int width = Maths.Max(1, (int)(context.Width * scale));
        int height = Maths.Max(1, (int)(context.Height * scale));

        // Persistent AO history (at AO resolution). Free it when temporal is off or on resize.
        if ((!UseTemporal || (_aoHistory != null && (_aoHistory.Width != width || _aoHistory.Height != height))))
            ReleaseHistory();
        if (UseTemporal)
            _aoHistory ??= new RenderTexture(width, height, false, [PixelFormat.R8_G8_B8_A8_UNorm]);

        RenderTexture aoRT = RenderTexture.GetTemporaryRT(width, height, false, [PixelFormat.R8_G8_B8_A8_UNorm]);

        // Pass 0: Calculate GTAO (blue-noise + per-frame jitter).
        _mat.SetInt("_Slices", Slices);
        _mat.SetInt("_DirectionSamples", DirectionSamples);
        _mat.SetFloat("_Radius", Radius);
        _mat.SetFloat("_Intensity", Intensity);
        _mat.SetTexture("_Noise", _noise);
        _mat.SetVector("_NoiseScale", new Float2(width / (float)_noise.Width, height / (float)_noise.Height));
        _mat.SetVector("_JitterOffset", _jitter);
        _mat.SetTexture("_CameraDepthTexture", context.DepthNormals.InternalDepth);
        _mat.SetTexture("_CameraNormalsTexture", context.DepthNormals.InternalTextures[0]);

        var cmd = Graphics.GetCommandBuffer("GTAO");
        cmd.Blit(context.SceneColor, aoRT, _mat, 0);

        // Pass 3: Temporal accumulate against history, then store the (pre-blur) result for next frame.
        RenderTexture ao = aoRT;
        RenderTexture temporalRT = null;
        if (UseTemporal)
        {
            if (_historyValid)
            {
                temporalRT = RenderTexture.GetTemporaryRT(width, height, false, [PixelFormat.R8_G8_B8_A8_UNorm]);
                _mat.SetTexture("_PreviousBuffer", _aoHistory.MainTexture);
                _mat.SetTexture("_CameraMotionVectorsTexture", context.MotionVectors);
                _mat.SetFloat("_TResponse", TemporalResponse);
                cmd.Blit(aoRT, temporalRT, _mat, 3);
                ao = temporalRT;
            }
            cmd.Blit(ao, _aoHistory, null, 0); // store before the spatial blur
        }

        // Pass 1: spatial bilateral blur (writes back into ao).
        RenderTexture blurTempRT = null;
        if (BlurRadius > 0.01f)
        {
            blurTempRT = RenderTexture.GetTemporaryRT(width, height, false, [PixelFormat.R8_G8_B8_A8_UNorm]);
            _mat.SetVector("_BlurDirection", new Float2(1.0f, 0.0f));
            _mat.SetFloat("_BlurRadius", BlurRadius);
            cmd.Blit(ao, blurTempRT, _mat, 1);
            _mat.SetVector("_BlurDirection", new Float2(0.0f, 1.0f));
            _mat.SetFloat("_BlurRadius", BlurRadius);
            cmd.Blit(blurTempRT, ao, _mat, 1);
        }

        // Pass 2: Composite at full res; the (possibly lower-res) AO is bilinearly upsampled.
        _mat.SetTexture("_AOTex", ao.MainTexture);
        var temp = RenderTexture.GetTemporaryRT(context.Width, context.Height, false, [context.SceneColor.MainTexture.ImageFormat]);
        cmd.Blit(context.SceneColor, temp, _mat, 2);
        cmd.Blit(temp, context.SceneColor, null, 0);
        Graphics.Submit(cmd);
        if (UseTemporal) _historyValid = true;

        RenderTexture.ReleaseTemporaryRT(temp);
        if (blurTempRT != null) RenderTexture.ReleaseTemporaryRT(blurTempRT);
        if (temporalRT != null) RenderTexture.ReleaseTemporaryRT(temporalRT);
        RenderTexture.ReleaseTemporaryRT(aoRT);
    }

    private void ReleaseHistory()
    {
        _aoHistory?.Dispose();
        _aoHistory = null;
        _historyValid = false;
    }

    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
        ReleaseHistory();
        _frameIndex = 0;
    }

    private static float Halton(int index, int b)
    {
        float r = 0f, f = 1f / b;
        int i = index;
        while (i > 0) { r += f * (i % b); i /= b; f /= b; }
        return r;
    }
}
