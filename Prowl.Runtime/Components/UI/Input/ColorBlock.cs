// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// The five state tints a <see cref="Selectable"/> drives, plus the shared multiplier and fade time.
/// A convenience view over the individual color fields on <see cref="Selectable"/>.
/// </summary>
public struct ColorBlock
{
    public Color NormalColor;
    public Color HighlightedColor;
    public Color PressedColor;
    public Color SelectedColor;
    public Color DisabledColor;

    /// <summary>Multiplier applied to whichever state color is active. Values above 1 let a tint
    /// brighten past the source graphic.</summary>
    public float ColorMultiplier;

    /// <summary>Seconds the tint takes to reach a new state color. 0 snaps.</summary>
    public float FadeDuration;

    public static ColorBlock Default => new()
    {
        NormalColor      = new Color(1f, 1f, 1f, 1f),
        HighlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f),
        PressedColor     = new Color(0.78f, 0.78f, 0.78f, 1f),
        SelectedColor    = new Color(0.96f, 0.96f, 0.96f, 1f),
        DisabledColor    = new Color(0.78f, 0.78f, 0.78f, 0.5f),
        ColorMultiplier  = 1f,
        FadeDuration     = 0.08f,
    };
}
