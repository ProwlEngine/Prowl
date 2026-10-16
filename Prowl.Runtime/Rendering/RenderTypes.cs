// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime;

// Prowl's names for the rendering settings everyday code touches, so it needs no Graphite namespace. Each matches its
// Graphite counterpart value for value, and converts with a cast at the boundary.

public enum SurfaceType : byte
{
    Opaque,

    /// <summary>Opaque where the alpha test passes, discarded elsewhere.</summary>
    Cutout,

    /// <summary>Blended over what is behind it, drawn back to front after the opaques, without writing depth.</summary>
    Transparent,
}

/// <summary>How a transparent surface combines with what is behind it.</summary>
public enum SurfaceBlend : byte
{
    Alpha,

    /// <summary>The colour already multiplied by its alpha, so alpha 0 can still add light.</summary>
    Premultiplied,
    Additive,
    Multiply,
}

public enum SurfaceCull : byte
{
    Back,
    Front,

    /// <summary>Both sides drawn, back faces shaded with a flipped normal.</summary>
    Off,
}

/// <summary>How a material's surface draws: opaque, cut out or transparent, its blend and its culling.</summary>
public readonly record struct SurfaceSettings(SurfaceType Type = SurfaceType.Opaque, SurfaceBlend Blend = SurfaceBlend.Alpha, SurfaceCull Cull = SurfaceCull.Back)
{
    /// <summary>The render queue the surface draws in.</summary>
    public int RenderQueue => ToGraphite().RenderQueue;

    public G.SurfaceSettings ToGraphite() => new((G.SurfaceType)Type, (G.SurfaceBlend)Blend, (G.SurfaceCull)Cull);

    public static SurfaceSettings FromGraphite(G.SurfaceSettings settings) => new((SurfaceType)settings.Type, (SurfaceBlend)settings.Blend, (SurfaceCull)settings.Cull);
}

public enum AmbientMode : byte
{
    Uniform,
    Hemisphere,

    /// <summary>The sky's light, from the sky probe.</summary>
    Sky,
}

public enum FogMode : byte
{
    Off,
    Linear,
    Exponential,
    ExponentialSquared,
}

/// <summary>When the sky probe captures the sky again.</summary>
public enum SkyCaptureMode : byte
{
    OnChange,
    EveryFrame,
    Never,
}

/// <summary>The resolution a screen space effect works at, as a part of the view's.</summary>
public enum EffectResolution
{
    Full,
    Half,
    Quarter,
}

public enum DepthOfFieldResolution
{
    Full = 1,
    Half = 2,
    Quarter = 4,
    Eighth = 8,
}

public enum DepthOfFieldQuality
{
    Low,
    Medium,
    High,
}
