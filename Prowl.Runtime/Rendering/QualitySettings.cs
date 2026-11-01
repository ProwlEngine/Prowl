// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Rendering;

namespace Prowl.Runtime;

/// <summary>The standard surfaces' shading: every lobe, or the mobile tier that lights coat and fuzz from ambient light only and drops thin film and anisotropy.</summary>
public enum PbrTier
{
    Full,
    Lite,
}

/// <summary>One named set of rendering quality, which <see cref="QualitySettings"/> applies to every camera, light and scene.</summary>
public sealed class QualityLevel
{
    public string Name = "Quality";

    /// <summary>The standard surfaces' shading tier.</summary>
    public PbrTier PbrTier = PbrTier.Full;

    /// <summary>Multiplies every camera's render scale.</summary>
    [Range(0.25f, 2f)] public float RenderScale = 1f;

    /// <summary>How far along the view directional lights cast shadows, in world units. They fade out over the last tenth.</summary>
    [Header("Shadows")] public float ShadowDistance = 80f;

    /// <summary>How many cascades directional shadows split into along the view.</summary>
    public ShadowCascades ShadowCascades = ShadowCascades.Four;

    /// <summary>The largest shadow tile any light takes, its own resolution capped to it.</summary>
    public ShadowResolution MaxShadowResolution = ShadowResolution._2048;

    /// <summary>The illuminance in lux where a light's automatic range ends. Higher cuts lights shorter and cheaper.</summary>
    [Header("Lights")] public float LightCutoff = 0.01f;

    /// <summary>The largest cube any reflection probe or the sky probe captures, its own size capped to it.</summary>
    [Header("Reflections")] public int MaxProbeResolution = 256;

    public QualityLevel Clone() => (QualityLevel)MemberwiseClone();
}

/// <summary>
/// The project's quality levels and the one in use. The editor's Quality project settings fill them, a built player
/// reads them from its settings, and a game may switch levels at any time.
/// </summary>
public static class QualitySettings
{
    private static List<QualityLevel> s_levels = CreateDefaults();
    private static int s_active = 2;

    /// <summary>Low, Medium, High and Ultra.</summary>
    public static List<QualityLevel> CreateDefaults() =>
    [
        new() { Name = "Low", PbrTier = PbrTier.Lite, RenderScale = 0.8f, ShadowDistance = 30f, ShadowCascades = ShadowCascades.One, MaxShadowResolution = ShadowResolution._512, MaxProbeResolution = 64 },
        new() { Name = "Medium", PbrTier = PbrTier.Lite, ShadowDistance = 50f, ShadowCascades = ShadowCascades.Two, MaxShadowResolution = ShadowResolution._1024, MaxProbeResolution = 128 },
        new() { Name = "High" },
        new() { Name = "Ultra", ShadowDistance = 150f, MaxShadowResolution = ShadowResolution._4096, MaxProbeResolution = 512 },
    ];

    /// <summary>Every level, in order. Change them through <see cref="Set"/>.</summary>
    public static IReadOnlyList<QualityLevel> Levels => s_levels;

    /// <summary>The index of the level in use.</summary>
    public static int ActiveIndex => s_active;

    /// <summary>The level in use.</summary>
    public static QualityLevel Active => s_levels[s_active];

    /// <summary>Moves every time the levels or the active one change, so what reads them can tell.</summary>
    public static int Version { get; private set; }

    /// <summary>Raised after the levels or the active one changed.</summary>
    public static event Action? Changed;

    /// <summary>Takes copies of the levels and the one to use, clamped to them. No levels restores the defaults.</summary>
    public static void Set(IReadOnlyList<QualityLevel> levels, int active)
    {
        s_levels = levels.Count > 0 ? [.. levels.Select(level => level.Clone())] : CreateDefaults();
        s_active = Math.Clamp(active, 0, s_levels.Count - 1);
        Apply();
    }

    /// <summary>Switches to the level at an index.</summary>
    public static void SetActive(int index)
    {
        index = Math.Clamp(index, 0, s_levels.Count - 1);
        if (index == s_active) return;
        s_active = index;
        Apply();
    }

    /// <summary>Switches to the level of a name, false when there is none.</summary>
    public static bool SetActive(string name)
    {
        int index = s_levels.FindIndex(level => string.Equals(level.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        SetActive(index);
        return true;
    }

    /// <summary>Applies the level in use again after one of its values was changed in place.</summary>
    public static void Apply()
    {
        Version++;
        ApplyGlobals();
        Changed?.Invoke();
    }

    /// <summary>Sets the renderer's global shader axes from the level in use. Runs again once the renderer exists.</summary>
    internal static void ApplyGlobals()
        => Graphics.Renderer?.Globals.Set(GlobalAxis.PbrTier, Active.PbrTier == PbrTier.Lite ? "LITE" : "FULL");
}
