// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace NetworkingShowcase;

/// <summary>Colors a renderer's own material, glowing, and only touches it when the color changes.</summary>
public sealed class Tint
{
    private Color _current = new(-1f, -1f, -1f, -1f);

    public void Apply(GameObject go, Color color, float glow = 2f)
    {
        if (color.Equals(_current) || go.GetComponent<MeshRenderer>()?.Material is not { } material)
        {
            return;
        }

        // Glow is in display whites, as the Lit shader takes emission in nits.
        _current = color;
        material.SetColor("base_color", new Color(color.R, color.G, color.B, 1f));
        material.Emissive(color, MathF.Max(0f, glow) * DisplayLuminance);
    }
}
