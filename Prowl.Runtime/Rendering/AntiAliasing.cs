// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

/// <summary>How a camera smooths the edges of what it draws.</summary>
public enum AntiAliasingMode
{
    None,
    FXAA,
    SMAA,
    TAA,
}

/// <summary>FXAA 3.11 on display colour.</summary>
[Serializable]
public sealed class FxaaSettings
{
    /// <summary>The contrast an edge needs relative to its brightest pixel, lower smoothing more.</summary>
    [Range(0.0625f, 0.333f)] public float EdgeThresholdMax = 0.0625f;

    /// <summary>The contrast below which dark edges are left alone.</summary>
    [Range(0.0312f, 0.0833f)] public float EdgeThresholdMin = 0.0312f;

    [Range(0f, 1f)] public float SubpixelQuality = 0.75f;
}

/// <summary>SMAA 1x with luma edges.</summary>
[Serializable]
public sealed class SmaaSettings
{
    /// <summary>Edge sensitivity, lower finding more edges.</summary>
    [Range(0.05f, 0.2f)] public float EdgeThreshold = 0.1f;
}

/// <summary>Temporal anti aliasing: jittered frames blended over time.</summary>
[Serializable]
public sealed class TaaSettings
{
    /// <summary>How much history is kept. Higher is smoother and ghosts more.</summary>
    [Range(0f, 0.99f)] public float BlendFactor = 0.95f;

    /// <summary>How much history is kept where the image moves fast.</summary>
    [Range(0f, 0.99f)] public float MotionBlendFactor = 0.85f;

    public float MotionScale = 2.0f;

    [Range(0f, 1f)] public float Sharpness = 0.025f;

    /// <summary>Sub pixel offsets before the jitter repeats.</summary>
    public int JitterSpread = 8;
}

/// <summary>A camera's anti aliasing features, one per mode, made when first used.</summary>
internal sealed class AntiAliasingFeatures : IDisposable
{
    private Renderer? _renderer;
    private Fxaa? _fxaa;
    private Smaa? _smaa;
    private TemporalAntiAliasing? _taa;

    public RenderFeature? Get(Renderer renderer, AntiAliasingMode mode, FxaaSettings? fxaa, SmaaSettings? smaa, TaaSettings? taa)
    {
        if (_renderer != renderer)
        {
            Dispose();
            _renderer = renderer;
        }

        switch (mode)
        {
            case AntiAliasingMode.FXAA:
                _fxaa ??= new Fxaa(renderer);
                fxaa ??= new FxaaSettings();
                _fxaa.EdgeThresholdMax = fxaa.EdgeThresholdMax;
                _fxaa.EdgeThresholdMin = fxaa.EdgeThresholdMin;
                _fxaa.SubpixelQuality = fxaa.SubpixelQuality;
                return _fxaa;
            case AntiAliasingMode.SMAA:
                _smaa ??= new Smaa(renderer);
                _smaa.EdgeThreshold = (smaa ?? new SmaaSettings()).EdgeThreshold;
                return _smaa;
            case AntiAliasingMode.TAA:
                _taa ??= new TemporalAntiAliasing(renderer);
                taa ??= new TaaSettings();
                _taa.BlendFactor = taa.BlendFactor;
                _taa.MotionBlendFactor = taa.MotionBlendFactor;
                _taa.MotionScale = taa.MotionScale;
                _taa.Sharpness = taa.Sharpness;
                _taa.JitterSpread = taa.JitterSpread;
                return _taa;
            default:
                return null;
        }
    }

    public void Dispose()
    {
        Graphics.DeferDispose(_fxaa);
        Graphics.DeferDispose(_smaa);
        Graphics.DeferDispose(_taa);
        _fxaa = null;
        _smaa = null;
        _taa = null;
        _renderer = null;
    }
}
