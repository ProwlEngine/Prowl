// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Temporal Anti-Aliasing image effect.
/// Jitters the camera projection matrix each frame using a Halton sequence and
/// accumulates samples over time, using motion vectors to reproject the previous
/// frame's result. Similar to Unity's ImageEffect-style TAA.
///
/// Usage:
///   1. Add TAAEffect to Camera.Effects.
///   2. The effect automatically enables DepthTextureMode.MotionVectors on the camera.
///   3. OnPreCull applies the sub-pixel jitter to the camera's ProjectionMatrix
///      while NonJitteredProjectionMatrix stays clean.
///   4. OnRenderEffect resolves the jittered frame against the reprojected history.
/// </summary>
public sealed class TAAEffect : ImageEffect
{
    public override RenderStage Stage => RenderStage.PostProcess;
    public override DepthTextureMode RequiredDepthTextureMode => DepthTextureMode.MotionVectors;

    /// <summary>How much of the history to keep (0..0.99). Higher = smoother but ghosts more.</summary>
    public float BlendFactor = 0.95f;

    /// <summary>Scale for motion-based neighborhood tightening. Higher = more aggressive ghosting rejection.</summary>
    public float MotionScale = 2.0f;

    /// <summary>Post-resolve sharpening amount (0 = none, 1 = strong).</summary>
    public float Sharpness = 0.025f;

    /// <summary>Number of Halton samples in the jitter sequence before repeating.</summary>
    public int JitterSpread = 8;

    private Material _mat;
    private RenderTexture _history;
    private bool _historyValid;
    private int _frameIndex;
    private Float2 _jitter;
    private Float2 _previousJitter;

    /// <summary>
    /// Current sub-pixel jitter offset in pixel coordinates.
    /// </summary>
    public Float2 Jitter => _jitter;

    /// <summary>
    /// Previous frame's jitter offset in pixel coordinates.
    /// </summary>
    public Float2 PreviousJitter => _previousJitter;

    public override void OnPreCull(Camera camera)
    {
        _previousJitter = _jitter;

        // Compute Halton jitter for this frame
        int index = _frameIndex % Math.Max(1, JitterSpread);
        float haltonX = HaltonSequence(index + 1, 2);
        float haltonY = HaltonSequence(index + 1, 3);

        // Map from [0,1] to [-0.5, 0.5] pixel offset
        _jitter = new Float2(haltonX - 0.5f, haltonY - 0.5f);

        // Save the unjittered projection before applying jitter.
        // NonJitteredProjectionMatrix is used by the pipeline for motion vectors.
        camera.NonJitteredProjectionMatrix = camera.ProjectionMatrix;

        // Apply jitter as sub-pixel offset to the projection matrix.
        // Column-major layout: c2 is the third column. c2.X = row0/col2, c2.Y = row1/col2.
        Float4x4 proj = camera.ProjectionMatrix;
        float pixelWidth = camera.PixelWidth;
        float pixelHeight = camera.PixelHeight;

        // Convert pixel offset to NDC offset (2/width, 2/height because NDC is [-1,1])
        proj.c2.X += _jitter.X * (2.0f / pixelWidth);
        proj.c2.Y += _jitter.Y * (2.0f / pixelHeight);
        camera.ProjectionMatrix = proj;

        // Upload jitter to global uniforms so shaders can unjitter if needed
        GlobalUniforms.SetCameraJitter(_jitter);
        GlobalUniforms.SetCameraPreviousJitter(_previousJitter);

        _frameIndex++;
    }

    public override void OnPostRender(Camera camera)
    {
        // Reset the projection matrix back to unjittered so other systems
        // (picking, gizmos, etc.) don't see the jittered matrix.
        camera.ResetProjectionMatrix();
    }

    public override void OnRenderEffect(RenderContext context)
    {
        _mat ??= new Material(Shader.LoadDefault(DefaultShader.TAA));

        int w = context.Width;
        int h = context.Height;
        var format = context.SceneColor.MainTexture.ImageFormat;

        // Invalidate history if resolution changed
        if (_history != null && (_history.Width != w || _history.Height != h))
        {
            _history.Dispose();
            _history = null;
            _historyValid = false;
        }

        _history ??= new RenderTexture(w, h, false, [format]);

        // Set uniforms
        _mat.SetVector("_Resolution", new Float2(w, h));
        _mat.SetVector("_Jitter", _jitter);
        _mat.SetFloat("_HistoryValid", _historyValid ? 1.0f : 0.0f);
        _mat.SetFloat("_BlendFactor", Maths.Clamp(BlendFactor, 0.0f, 0.99f));
        _mat.SetFloat("_MotionScale", Math.Max(0.0f, MotionScale));
        _mat.SetFloat("_Sharpness", Maths.Clamp(Sharpness, 0.0f, 1.0f));
        _mat.SetTexture("_HistoryTex", _history.MainTexture);

        // Bind motion vectors and depth — these are globals set by the pipeline,
        // but the shader uses its own uniform names so we must bind explicitly.
        if (context.MotionVectors != null)
            _mat.SetTexture("_MotionVectorsTex", context.MotionVectors.MainTexture);
        _mat.SetTexture("_CameraDepthTexture", context.DepthNormals.InternalDepth);

        using var cmd = Graphics.GetCommandBuffer("TAA");

        // Resolve: blend current jittered frame with reprojected history
        var resolved = RenderTexture.GetTemporaryRT(w, h, false, [format]);
        cmd.Blit(context.SceneColor, resolved, _mat, 0);

        // Store resolved result as history for next frame
        cmd.Blit(resolved, _history, null, 0);
        _historyValid = true;

        // Copy resolved back to scene color
        cmd.Blit(resolved, context.SceneColor, null, 0);
        Graphics.Submit(cmd);
        RenderTexture.ReleaseTemporaryRT(resolved);
    }

    public override void OnDisable()
    {
        _mat?.Dispose();
        _mat = null;
        _history?.Dispose();
        _history = null;
        _historyValid = false;
        _frameIndex = 0;
    }

    /// <summary>
    /// Generates the Halton low-discrepancy sequence value for the given index and base.
    /// Used for sub-pixel jitter patterns that converge to a uniform distribution.
    /// </summary>
    private static float HaltonSequence(int index, int b)
    {
        float result = 0.0f;
        float fraction = 1.0f / b;
        int i = index;
        while (i > 0)
        {
            result += fraction * (i % b);
            i /= b;
            fraction /= b;
        }
        return result;
    }
}
