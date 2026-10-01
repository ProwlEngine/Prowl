// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Bokeh depth of field. A signed circle of confusion splits the image into a far field (behind focus)
/// and a near field (in front), each gathered with a disk kernel, so sharp objects don't halo into a
/// blurred background and a blurred foreground spreads over what is behind it.
/// </summary>
public sealed class BokehDepthOfFieldEffect : ImageEffect
{
    public enum ResolutionMode
    {
        Full = 1,
        Half = 2,
        Quarter = 4,
        Eighth = 8
    }

    /// <summary>Number of samples in the bokeh disk. More gives smoother, rounder bokeh at more cost.</summary>
    public enum KernelQuality
    {
        Low,
        Medium,
        High
    }

    public bool UseAutoFocus = true;
    /// <summary>Focus distance in world units from the camera, used when <see cref="UseAutoFocus"/> is off.</summary>
    public float ManualFocusPoint = 10f;
    /// <summary>Blur of something infinitely far away, in percent of the screen height. Blur grows toward
    /// that behind the focus point and keeps growing in front of it, both capped by <see cref="MaxBlurRadius"/>.</summary>
    public float FocusStrength = 2.0f;
    /// <summary>Largest blur, in percent of the screen height.</summary>
    public float MaxBlurRadius = 2.0f;
    public ResolutionMode Resolution = ResolutionMode.Half;
    public KernelQuality Quality = KernelQuality.Medium;

    /// <summary>How quickly the focus distance follows its target, per second. Higher snaps faster;
    /// 0 or less jumps straight to it.</summary>
    public float FocusSpeed = 4f;

    private const int CoCPass = 0;
    private const int PrefilterPass = 1;
    private const int BokehPass = 2;
    private const int PostfilterPass = 3;
    private const int CombinePass = 4;
    private const int FocusPass = 5;

    private static readonly TextureImageFormat[] FocusFormat = [TextureImageFormat.Float];
    private static readonly TextureImageFormat[] CoCFormat = [TextureImageFormat.Short];
    // Signed CoC rides in alpha, so the blur targets need a float format even when the scene is LDR.
    private static readonly TextureImageFormat[] BlurFormat = [TextureImageFormat.Short4];
    // Far field and near field (with its coverage) side by side.
    private static readonly TextureImageFormat[] FieldsFormat = [TextureImageFormat.Short4, TextureImageFormat.Short4];

    private Material _mat;
    private KernelQuality? _uploadedKernel;

    // The focus distance lives on the GPU in two 1x1 targets, read from one and written to the other
    // each frame so it can ease toward its target.
    private readonly RenderTexture?[] _focus = new RenderTexture?[2];
    private int _focusIndex;
    private bool _focusValid;

    public override void OnRenderEffect(RenderContext context)
    {
        if (_mat.IsNotValid())
        {
            _mat = new Material(Shader.LoadDefault(DefaultShader.BokehDoF));
            _uploadedKernel = null;
        }
        if (_uploadedKernel != Quality) UploadKernel(Quality);

        int fullWidth = context.Width;
        int fullHeight = context.Height;
        int divisor = (int)Resolution;
        int blurWidth = Math.Max(1, fullWidth / divisor);
        int blurHeight = Math.Max(1, fullHeight / divisor);

        _mat.SetFloat("_FocusStrength", Math.Max(0f, FocusStrength));
        _mat.SetFloat("_MaxBlurRadius", Math.Max(0f, MaxBlurRadius));
        _mat.SetVector("_Resolution", new Float2(fullWidth, fullHeight));
        // The real ratio, which drifts from the divisor when the size doesn't divide evenly.
        float downscale = fullHeight / (float)blurHeight;
        _mat.SetFloat("_Downscale", downscale);
        _mat.SetFloat("_MaxCoC", Math.Max(0f, MaxBlurRadius) * 0.01f * fullHeight / downscale);

        using var cmd = Graphics.GetCommandBuffer("BokehDoF");

        // Focus: ease the 1x1 focus distance toward this frame's target. A camera cut starts fresh.
        if (!context.Camera.HasPreviousViewProjectionMatrix) _focusValid = false;
        for (int i = 0; i < 2; i++)
            if (_focus[i].IsNotValid()) { _focus[i] = new RenderTexture(1, 1, false, FocusFormat); _focusValid = false; }

        RenderTexture prevFocus = _focus[_focusIndex]!;
        _focusIndex ^= 1;
        RenderTexture focus = _focus[_focusIndex]!;

        float speed = FocusSpeed;
        _mat.SetFloat("_UseAutoFocus", UseAutoFocus ? 1f : 0f);
        _mat.SetFloat("_ManualFocusPoint", MathF.Max(ManualFocusPoint, 0.01f));
        _mat.SetFloat("_FocusBlend", speed > 0f ? 1f - MathF.Exp(-speed * Time.UnscaledDeltaTime) : 1f);
        _mat.SetFloat("_FocusHistoryValid", _focusValid ? 1f : 0f);
        _mat.SetTexture("_PrevFocusTex", prevFocus.MainTexture);
        cmd.Blit(focus, _mat, FocusPass);
        _focusValid = true;
        _mat.SetTexture("_FocusTex", focus.MainTexture);

        // CoC at full resolution.
        RenderTexture coc = RenderTexture.GetTemporaryRT(fullWidth, fullHeight, false, CoCFormat);
        cmd.Blit(coc, _mat, CoCPass);
        _mat.SetTexture("_CoCTex", coc.MainTexture);

        // Prefilter, gather and smooth at the blur resolution.
        RenderTexture prefiltered = RenderTexture.GetTemporaryRT(blurWidth, blurHeight, false, BlurFormat);
        cmd.Blit(context.SceneColor, prefiltered, _mat, PrefilterPass);

        RenderTexture bokeh = RenderTexture.GetTemporaryRT(blurWidth, blurHeight, false, FieldsFormat);
        cmd.Blit(prefiltered, bokeh, _mat, BokehPass);

        RenderTexture smoothed = RenderTexture.GetTemporaryRT(blurWidth, blurHeight, false, FieldsFormat);
        _mat.SetTexture("_FarTex", bokeh.InternalTextures[0]);
        _mat.SetTexture("_NearTex", bokeh.InternalTextures[1]);
        cmd.Blit(smoothed, _mat, PostfilterPass);

        // Combine at full resolution.
        _mat.SetTexture("_FarTex", smoothed.InternalTextures[0]);
        _mat.SetTexture("_NearTex", smoothed.InternalTextures[1]);
        var temp = RenderTexture.GetTemporaryRT(fullWidth, fullHeight, false, [context.SceneColor.MainTexture.ImageFormat]);
        cmd.Blit(context.SceneColor, temp, _mat, CombinePass);
        cmd.Blit(temp, context.SceneColor, null, 0);
        Graphics.Submit(cmd);

        RenderTexture.ReleaseTemporaryRT(temp);
        RenderTexture.ReleaseTemporaryRT(coc);
        RenderTexture.ReleaseTemporaryRT(prefiltered);
        RenderTexture.ReleaseTemporaryRT(bokeh);
        RenderTexture.ReleaseTemporaryRT(smoothed);
    }

    // Unit disk samples in concentric rings: the center, then 7, 14, 21, 28 points outward.
    private void UploadKernel(KernelQuality quality)
    {
        int rings = quality switch
        {
            KernelQuality.Low => 2,
            KernelQuality.Medium => 3,
            _ => 4,
        };

        int index = 0;
        _mat.SetVector(KernelName(index++), Float2.Zero);
        for (int ring = 1; ring <= rings; ring++)
        {
            int count = ring * 7;
            float radius = ring / (float)rings;
            for (int j = 0; j < count; j++)
            {
                float angle = MathF.PI * 2f * j / count;
                _mat.SetVector(KernelName(index++), new Float2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
            }
        }

        _mat.SetInt("_KernelCount", index);
        _uploadedKernel = quality;
    }

    private static readonly string[] s_kernelNames = BuildKernelNames();

    private static string[] BuildKernelNames()
    {
        var names = new string[71];
        for (int i = 0; i < names.Length; i++) names[i] = $"_Kernel[{i}]";
        return names;
    }

    private static string KernelName(int index) => s_kernelNames[index];

    public override void OnDisable()
    {
        if (_mat.IsValid()) _mat.Dispose();
        _mat = null;
        _uploadedKernel = null;
        for (int i = 0; i < 2; i++)
        {
            if (_focus[i].IsValid()) _focus[i]!.Dispose();
            _focus[i] = null;
        }
        _focusValid = false;
    }
}
