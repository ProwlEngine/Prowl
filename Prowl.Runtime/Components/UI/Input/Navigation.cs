// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.UI;

/// <summary>How a <see cref="Selectable"/> resolves the widget a directional move should focus.</summary>
public enum NavigationMode
{
    /// <summary>Directional moves do nothing.</summary>
    None,

    /// <summary>Automatic, restricted to left and right.</summary>
    Horizontal,

    /// <summary>Automatic, restricted to up and down.</summary>
    Vertical,

    /// <summary>Picks the nearest selectable in the direction moved.</summary>
    Automatic,

    /// <summary>Uses the explicitly wired <c>SelectOn*</c> targets.</summary>
    Explicit,
}

/// <summary>Keyboard/gamepad navigation settings for a <see cref="Selectable"/>.</summary>
public struct Navigation
{
    public NavigationMode Mode;

    /// <summary>When automatic navigation finds nothing in the direction moved, continue from the
    /// far side instead of stopping.</summary>
    public bool WrapAround;

    public Selectable? SelectOnUp;
    public Selectable? SelectOnDown;
    public Selectable? SelectOnLeft;
    public Selectable? SelectOnRight;

    public static Navigation Default => new() { Mode = NavigationMode.Automatic };
}
