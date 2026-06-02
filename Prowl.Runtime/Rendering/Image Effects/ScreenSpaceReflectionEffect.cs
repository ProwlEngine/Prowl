// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Stochastic screen-space reflections: GGX importance-sampled rays, a ray-reuse resolve that
/// picks a convolved scene-mip level by a roughness cone (contact hardening + glossy blur), and
/// optional temporal reprojection with one-bounce feedback. Reads per-pixel roughness/metallic and
/// view normals from the unified prepass.
/// </summary>
public sealed class ScreenSpaceReflectionEffect : ImageEffect
{
    // ── RayCast ──
    /// <summary>Resolution the reflection rays are traced at. Half is much faster.</summary>
    public EffectResolution RayResolution = EffectResolution.Half;
    /// <summary>Max ray-march steps. Higher = longer/more accurate rays.</summary>
    [Range(1f, 100f)] public float RayDistance = 70f;
    /// <summary>Biases the stochastic samples toward the mirror direction. 0 = full GGX lobe, 1 = mirror.</summary>
    [Range(0f, 1f)] public float BRDFBias = 0.7f;

    // ── Resolve ──
    /// <summary>Reuse the 4 neighbour rays per pixel (denoises the stochastic result).</summary>
    public bool RayReuse = true;
    /// <summary>Weight reused samples by BRDF/pdf (energy-correct importance resolve).</summary>
    public bool Normalization = true;
    /// <summary>Tonemap samples during the resolve to suppress bright fireflies.</summary>
    public bool ReduceFireflies = true;
    /// <summary>Build a convolved scene-colour mip chain so rough reflections blur smoothly.</summary>
    public bool UseMipMap = true;

    // ── Temporal ──
    /// <summary>Accumulate reflections over time (needs motion); also enables one-bounce feedback.</summary>
    public bool UseTemporal = true;
    /// <summary>Neighbourhood-clamp box scale for the temporal history.</summary>
    public float TemporalScale = 2.0f;
    /// <summary>How much history to keep (higher = more stable, more ghosting).</summary>
    [Range(0f, 1f)] public float TemporalResponse = 0.85f;
    /// <summary>Derive temporal velocity from the reflection hit depth (less smearing) vs. motion vectors.</summary>
    public bool ReflectionVelocity = true;

    // ── General ──
    /// <summary>Apply a Fresnel/environment-BRDF response to the reflection.</summary>
    public bool UseFresnel = true;
    /// <summary>Fraction of the screen border over which reflections fade out.</summary>
    [Range(0f, 1f)] public float ScreenFadeSize = 0.25f;

    private const int MaxMip = 5;

    private Material _mat;
    private Texture2D _noise;

    // Persistent history (full res). _prevCombined feeds last frame's result back in (one bounce);
    // _reflHistory accumulates the resolved reflection for temporal stability.
    private RenderTexture _prevCombined;
    private RenderTexture _reflHistory;
    private bool _historyValid;
    private int _frameIndex;
    private Float2 _jitter;

    public override RenderStage Stage => RenderStage.AfterOpaques;

    public override void OnPreCull(Camera camera)
    {
        // Per-frame Halton offset to scroll the blue-noise sampling.
        _jitter = new Float2(Halton(_frameIndex + 1, 2), Halton(_frameIndex + 1, 3));
        _frameIndex = (_frameIndex + 1) % 64;
    }

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.SSR));
        _noise ??= Texture2D.LoadDefault(DefaultTexture.Noise);

        if (!context.DepthNormals.IsValid())
            return;

        int w = context.Width, h = context.Height;
        var format = context.SceneColor.MainTexture.ImageFormat;

        float rayScale = RayResolution.Scale();
        int rayW = Maths.Max(1, (int)(w * rayScale));
        int rayH = Maths.Max(1, (int)(h * rayScale));

        // Invalidate persistent buffers on resize.
        if (_prevCombined != null && (_prevCombined.Width != w || _prevCombined.Height != h))
            ReleaseHistory();
        _prevCombined ??= new RenderTexture(w, h, false, [format]);
        _reflHistory ??= new RenderTexture(w, h, false, [format]);

        // Shared uniforms.
        _mat.SetTexture("_CameraDepthTexture", context.DepthNormals.InternalDepth);
        _mat.SetTexture("_CameraNormalsTexture", context.DepthNormals.InternalTextures[0]);
        _mat.SetTexture("_CameraMotionVectorsTexture", context.MotionVectors);
        _mat.SetTexture("_Noise", _noise);
        _mat.SetVector("_NoiseSize", new Float2(_noise.Width, _noise.Height));
        _mat.SetVector("_JitterSizeAndOffset", new Float4(rayW / (float)_noise.Width, rayH / (float)_noise.Height, _jitter.X, _jitter.Y));
        _mat.SetVector("_ResolveSize", new Float2(w, h));
        _mat.SetFloat("_NumSteps", RayDistance);
        _mat.SetFloat("_BRDFBias", BRDFBias);
        _mat.SetFloat("_EdgeFactor", ScreenFadeSize);
        _mat.SetFloat("_MaxMipMap", MaxMip);
        _mat.SetInt("_RayReuse", RayReuse ? 1 : 0);
        _mat.SetInt("_UseNormalization", Normalization ? 1 : 0);
        _mat.SetInt("_Fireflies", ReduceFireflies ? 1 : 0);
        _mat.SetInt("_UseFresnel", UseFresnel ? 1 : 0);
        _mat.SetInt("_ReflectionVelocity", ReflectionVelocity ? 1 : 0);

        bool feedback = UseTemporal && _historyValid;

        using var cmd = Graphics.GetCommandBuffer("SSR");

        // 1) Reflection source. With one-bounce feedback, reflections sample last frame's combined
        //    result reprojected by motion; otherwise the current scene.
        RenderTexture sourceRT = null;
        RenderTexture source;
        if (feedback)
        {
            sourceRT = RenderTexture.GetTemporaryRT(w, h, false, [format]);
            cmd.Blit(_prevCombined, sourceRT, _mat, 4); // Reproject (Blit binds _MainTex = _prevCombined)
            source = sourceRT;
        }
        else
        {
            source = context.SceneColor;
        }

        // 2) Convolved scene-colour pyramid (level 0 sharp ... level 4 blurriest).
        RenderTexture s1 = null, s2 = null, s3 = null, s4 = null, tF = null, tH = null, tQ = null, tE = null;
        if (UseMipMap)
        {
            int w1 = Maths.Max(1, w / 2), h1 = Maths.Max(1, h / 2);
            int w2 = Maths.Max(1, w / 4), h2 = Maths.Max(1, h / 4);
            int w3 = Maths.Max(1, w / 8), h3 = Maths.Max(1, h / 8);
            int w4 = Maths.Max(1, w / 16), h4 = Maths.Max(1, h / 16);
            s1 = RenderTexture.GetTemporaryRT(w1, h1, false, [format]);
            s2 = RenderTexture.GetTemporaryRT(w2, h2, false, [format]);
            s3 = RenderTexture.GetTemporaryRT(w3, h3, false, [format]);
            s4 = RenderTexture.GetTemporaryRT(w4, h4, false, [format]);
            tF = RenderTexture.GetTemporaryRT(w, h, false, [format]);
            tH = RenderTexture.GetTemporaryRT(w1, h1, false, [format]);
            tQ = RenderTexture.GetTemporaryRT(w2, h2, false, [format]);
            tE = RenderTexture.GetTemporaryRT(w3, h3, false, [format]);

            BlurLevel(cmd, source, tF, s1); // full -> half
            BlurLevel(cmd, s1, tH, s2);     // half -> quarter
            BlurLevel(cmd, s2, tQ, s3);     // quarter -> eighth
            BlurLevel(cmd, s3, tE, s4);     // eighth -> sixteenth

            _mat.SetTexture("_Scene0", source.MainTexture);
            _mat.SetTexture("_Scene1", s1.MainTexture);
            _mat.SetTexture("_Scene2", s2.MainTexture);
            _mat.SetTexture("_Scene3", s3.MainTexture);
            _mat.SetTexture("_Scene4", s4.MainTexture);
        }
        else
        {
            // All levels = sharp source (mip selection collapses to no blur).
            _mat.SetTexture("_Scene0", source.MainTexture);
            _mat.SetTexture("_Scene1", source.MainTexture);
            _mat.SetTexture("_Scene2", source.MainTexture);
            _mat.SetTexture("_Scene3", source.MainTexture);
            _mat.SetTexture("_Scene4", source.MainTexture);
        }

        // 3) RayCast (at ray resolution).
        RenderTexture rayData = RenderTexture.GetTemporaryRT(rayW, rayH, false, [TextureImageFormat.Short4]);
        cmd.Blit(context.SceneColor, rayData, _mat, 0);
        _mat.SetTexture("_RayCast", rayData.MainTexture);

        // 4) Resolve (full res).
        RenderTexture reflection = RenderTexture.GetTemporaryRT(w, h, false, [format]);
        cmd.Blit(context.SceneColor, reflection, _mat, 2);

        // 5) Temporal accumulation against the reflection history.
        RenderTexture reflFinal = reflection;
        RenderTexture temporal = null;
        if (UseTemporal)
        {
            temporal = RenderTexture.GetTemporaryRT(w, h, false, [format]);
            _mat.SetTexture("_PreviousBuffer", _reflHistory.MainTexture);
            _mat.SetFloat("_TScale", TemporalScale);
            _mat.SetFloat("_TResponse", TemporalResponse);
            cmd.Blit(reflection, temporal, _mat, 3); // Blit binds _MainTex = current reflection
            cmd.Blit(temporal, _reflHistory, null, 0); // store history
            reflFinal = temporal;
        }

        // 6) Combine over the scene.
        _mat.SetTexture("_ReflectionBuffer", reflFinal.MainTexture);
        RenderTexture combined = RenderTexture.GetTemporaryRT(w, h, false, [format]);
        cmd.Blit(context.SceneColor, combined, _mat, 5); // Blit binds _MainTex = scene color

        // 7) Store for next frame's feedback, then output.
        cmd.Blit(combined, _prevCombined, null, 0);
        cmd.Blit(combined, context.SceneColor, null, 0);
        Graphics.Submit(cmd);
        _historyValid = true;

        // Cleanup.
        RenderTexture.ReleaseTemporaryRT(combined);
        if (temporal != null) RenderTexture.ReleaseTemporaryRT(temporal);
        RenderTexture.ReleaseTemporaryRT(reflection);
        RenderTexture.ReleaseTemporaryRT(rayData);
        if (UseMipMap)
        {
            RenderTexture.ReleaseTemporaryRT(tE); RenderTexture.ReleaseTemporaryRT(tQ);
            RenderTexture.ReleaseTemporaryRT(tH); RenderTexture.ReleaseTemporaryRT(tF);
            RenderTexture.ReleaseTemporaryRT(s4); RenderTexture.ReleaseTemporaryRT(s3);
            RenderTexture.ReleaseTemporaryRT(s2); RenderTexture.ReleaseTemporaryRT(s1);
        }
        if (sourceRT != null) RenderTexture.ReleaseTemporaryRT(sourceRT);
    }

    // One pyramid level: separable Gaussian (H into a same-res scratch, V into the smaller dest).
    // Blit binds the source as _MainTex; both passes read the source-level texel size so the
    // kernel is isotropic and the downsample happens only on the V write.
    private void BlurLevel(CommandBuffer cmd, RenderTexture src, RenderTexture scratchSameRes, RenderTexture dst)
    {
        _mat.SetVector("_BlurDir", new Float2(1.0f, 0.0f));
        cmd.Blit(src, scratchSameRes, _mat, 1);
        _mat.SetVector("_BlurDir", new Float2(0.0f, 1.0f));
        cmd.Blit(scratchSameRes, dst, _mat, 1);
    }

    private void ReleaseHistory()
    {
        _prevCombined?.Dispose(); _prevCombined = null;
        _reflHistory?.Dispose(); _reflHistory = null;
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
