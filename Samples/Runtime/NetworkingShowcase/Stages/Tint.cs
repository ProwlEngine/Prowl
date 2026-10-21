// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Vector;

namespace NetworkingShowcase;

/// <summary>Colors a renderer's own material, glowing, and only touches it when the color changes.</summary>
public sealed class Tint
{
    private Color _current = new(-1f, -1f, -1f, -1f);

    public void Apply(GameObject go, Color color, float glow = 2f)
    {
        if (color.Equals(_current) || go.GetComponent<MeshRenderer>()?.Material is not { } material)
            return;

        _current = color;
        material.SetColor("_MainColor", color);
        material.SetInt("_Emission", glow > 0f ? 1 : 0);
        material.SetColor("_EmissiveColor", color);
        material.SetFloat("_EmissionIntensity", glow);
    }
}
