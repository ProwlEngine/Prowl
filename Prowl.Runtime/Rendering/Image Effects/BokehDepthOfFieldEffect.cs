// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

public sealed class BokehDepthOfFieldEffect : ImageEffect
{
    public enum ResolutionMode
    {
        Full = 1,
        Half = 2,
        Quarter = 4,
        Eighth = 8
    }

    public bool UseAutoFocus = true;
    /// <summary>Focus distance in world units from the camera, used when <see cref="UseAutoFocus"/> is off.</summary>
    public float ManualFocusPoint = 10f;
    // Retuned for a circle of confusion measured in linear view depth. The old values were set
    // against raw depth-buffer samples, where the same difference meant something very different.
    public float FocusStrength = 1.0f;
    public float MaxBlurRadius = 2.0f;
    public ResolutionMode Resolution = ResolutionMode.Half;

    /// <summary>How quickly the focus distance follows its target, per second. Higher snaps faster;
    /// 0 or less jumps straight to it.</summary>
    public float FocusSpeed = 4f;

    private const int PrefilterPass = 3;
    private const int FocusPass = 4;
    private static readonly TextureImageFormat[] FocusFormat = [TextureImageFormat.Float];

    private Material _mat;

    // The focus distance lives on the GPU in two 1x1 targets, read from one and written to the other
    // each frame so it can ease toward its target.
    private readonly RenderTexture?[] _focus = new RenderTexture?[2];
    private int _focusIndex;
    private bool _focusValid;

    public override void OnRenderEffect(RenderContext context)
    {
        if (_mat.IsNotValid()) _mat = new Material(Shader.LoadDefault(DefaultShader.BokehDoF));

        int fullWidth = context.Width;
        int fullHeight = context.Height;
        int divisor = (int)Resolution;
        int blurWidth = System.Math.Max(1, fullWidth / divisor);
        int blurHeight = System.Math.Max(1, fullHeight / divisor);

        // Create MRT render texture for horizontal pass (3 color attachments for R, G, B)
        // Use floating point format to store complex number values (can be negative)
        RenderTexture horizontalMRT = RenderTexture.GetTemporaryRT(blurWidth, blurHeight, false, [
            TextureImageFormat.Short4,
            TextureImageFormat.Short4,
            TextureImageFormat.Short4
        ]);

        // Create vertical result texture
        RenderTexture verticalResult = RenderTexture.GetTemporaryRT(blurWidth, blurHeight, false, [context.SceneColor.MainTexture.ImageFormat]);

        // Set common shader properties
        _mat.SetFloat("_FocusStrength", FocusStrength);
        _mat.SetFloat("_MaxBlurRadius", MaxBlurRadius);

        using var cmd = Graphics.GetCommandBuffer("BokehDoF");

        // Pass 4: Focus - ease the 1x1 focus distance toward this frame's target. A camera cut starts fresh.
        if (!context.Camera.HasPreviousViewProjectionMatrix) _focusValid = false;
        for (int i = 0; i < 2; i++)
            if (_focus[i].IsNotValid()) { _focus[i] = new RenderTexture(1, 1, false, FocusFormat); _focusValid = false; }

        RenderTexture prevFocus = _focus[_focusIndex]!;
        _focusIndex ^= 1;
        RenderTexture focus = _focus[_focusIndex]!;

        float speed = FocusSpeed;
        _mat.SetFloat("_UseAutoFocus", UseAutoFocus ? 1f : 0f);
        _mat.SetFloat("_ManualFocusPoint", System.MathF.Max(ManualFocusPoint, 0.01f));
        _mat.SetFloat("_FocusBlend", speed > 0f ? 1f - System.MathF.Exp(-speed * Time.UnscaledDeltaTime) : 1f);
        _mat.SetFloat("_FocusHistoryValid", _focusValid ? 1f : 0f);
        _mat.SetTexture("_PrevFocusTex", prevFocus.MainTexture);
        cmd.Blit(focus, _mat, FocusPass);
        _focusValid = true;
        _mat.SetTexture("_FocusTex", focus.MainTexture);

        // Set resolution for blur passes
        _mat.SetVector("_Resolution", new Float2(blurWidth, blurHeight));

        // Pass 3: Prefilter - average the scene down to the blur resolution so it doesn't alias there.
        RenderTexture? prefiltered = null;
        Texture2D blurSource = context.SceneColor.MainTexture;
        if (divisor > 1)
        {
            prefiltered = RenderTexture.GetTemporaryRT(blurWidth, blurHeight, false, [context.SceneColor.MainTexture.ImageFormat]);
            _mat.SetFloat("_PrefilterOffset", divisor * 0.25f);
            cmd.Blit(context.SceneColor, prefiltered, _mat, PrefilterPass);
            blurSource = prefiltered.MainTexture;
        }

        // Pass 0: Horizontal MRT - outputs to 3 render targets (R, G, B channels)
        _mat.SetTexture("_MainTex", blurSource);
        cmd.Blit(horizontalMRT, _mat, 0);

        // Pass 1: Vertical Composite - reads from 3 horizontal textures and combines
        _mat.SetTexture("_HorizR", horizontalMRT.InternalTextures[0]);
        _mat.SetTexture("_HorizG", horizontalMRT.InternalTextures[1]);
        _mat.SetTexture("_HorizB", horizontalMRT.InternalTextures[2]);
        cmd.Blit(verticalResult, _mat, 1);

        // Pass 2: Final Combine - blend with original image based on CoC (at full resolution)
        _mat.SetTexture("_MainTex", context.SceneColor.MainTexture);
        _mat.SetTexture("_BlurredTex", verticalResult.MainTexture);
        _mat.SetVector("_Resolution", new Float2(fullWidth, fullHeight));
        var temp = RenderTexture.GetTemporaryRT(fullWidth, fullHeight, false, [context.SceneColor.MainTexture.ImageFormat]);
        cmd.Blit(context.SceneColor, temp, _mat, 2);
        cmd.Blit(temp, context.SceneColor, null, 0);
        Graphics.Submit(cmd);
        RenderTexture.ReleaseTemporaryRT(temp);

        // Clean up MRT
        RenderTexture.ReleaseTemporaryRT(horizontalMRT);
        RenderTexture.ReleaseTemporaryRT(verticalResult);
        if (prefiltered != null) RenderTexture.ReleaseTemporaryRT(prefiltered);
    }

    public override void OnDisable()
    {
        if (_mat.IsValid()) _mat.Dispose();
        _mat = null;
        for (int i = 0; i < 2; i++)
        {
            if (_focus[i].IsValid()) _focus[i]!.Dispose();
            _focus[i] = null;
        }
        _focusValid = false;
    }
}
