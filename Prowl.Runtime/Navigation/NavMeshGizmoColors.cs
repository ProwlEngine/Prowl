// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Deterministic per-agent-type color for scene-view gizmos, so surfaces and agents of different types
/// stay visually distinguishable without a hand-maintained color table that would need updating every
/// time a project adds a type. The built-in Humanoid type keeps the blue this subsystem always used, so
/// existing single-type scenes don't visually change; every other id gets a color spread around the hue
/// wheel by the golden ratio's conjugate, which keeps ids added one after another from landing on
/// similar-looking hues the way a plain linear step through the wheel would.
/// </summary>
public static class NavMeshGizmoColors
{
    private const float GoldenRatioConjugate = 0.6180339887f;

    /// <summary>The color a surface or agent of this type should draw itself with, fully opaque - a
    /// caller that wants transparency (surface polygons, say) applies its own alpha on top.</summary>
    public static Color ForAgentType(int agentTypeId)
    {
        if (agentTypeId == NavMeshAgentTypes.HumanoidId)
            return new Color(0.1f, 0.6f, 1f, 1f);

        float hue = (agentTypeId * GoldenRatioConjugate) % 1f;
        if (hue < 0f) hue += 1f;

        return Color.HSVToRGB(new Color(hue, 0.65f, 0.95f, 1f));
    }
}
