// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.OrigamiUI;

/// <summary>
/// Semantic style variants for Origami widgets. Each variant maps to an
/// <see cref="OrigamiPalette"/> in the active <see cref="OrigamiContext"/>.
/// </summary>
public enum OrigamiVariant
{
    /// <summary>Neutral, used when no semantic meaning is implied.</summary>
    Default,

    /// <summary>Brand / call-to-action.</summary>
    Primary,

    /// <summary>Positive outcome, completion, confirmation.</summary>
    Success,

    /// <summary>Caution, non-blocking concern.</summary>
    Warning,

    /// <summary>Destructive, blocking error, dangerous action.</summary>
    Danger,

    /// <summary>Informational note, neutral guidance.</summary>
    Info,

    /// <summary>De-emphasised, secondary content.</summary>
    Subtle,
}
