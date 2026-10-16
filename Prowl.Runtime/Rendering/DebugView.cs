// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.ShaderDef;

namespace Prowl.Runtime.Rendering;

/// <summary>What a view shows in place of the lit scene: its surfaces draw through their Debug pass with the GRAPHITE_DEBUG value.</summary>
public enum DebugViewMode
{
    Lit,
    Unlit,
    Albedo,
    Normals,
    Roughness,
    Metallic,
    AmbientOcclusion,
    Emission,
    Lighting,
    ShadowCascades,
    LightmapDensity,
}

public static class DebugViews
{
    private static readonly string[] s_values = ["OFF", "UNLIT", "ALBEDO", "NORMALS", "ROUGHNESS", "METALLIC", "AO", "EMISSION", "LIGHTING", "CASCADES", "LIGHTMAP_DENSITY"];
    private static readonly Keyword[]?[] s_keywords = new Keyword[]?[s_values.Length];

    /// <summary>The view keywords that show a mode, null for the lit scene.</summary>
    public static Keyword[]? Keywords(DebugViewMode mode)
    {
        int index = (int)mode;
        if (index <= 0 || index >= s_values.Length) return null;
        return s_keywords[index] ??= [new Keyword(Prowl.Graphite.Rendering.Shader.DebugAxis, s_values[index])];
    }
}
