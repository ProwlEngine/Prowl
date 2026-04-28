// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.OrigamiUI;

/// <summary>
/// Glyph strings used by Origami widget chrome. Defaults are empty so a standalone
/// Origami install (no bundled icon font) doesn't render garbage; hosts that ship a
/// font (e.g. the editor with FontAwesome) populate these with the right code points.
/// Widgets gracefully omit chrome elements whose glyph is null/empty.
/// </summary>
/// <remarks>
/// Glyphs are <see cref="string"/>s rather than <c>char</c> so multi-codepoint icons
/// (e.g. emoji modifiers, combined glyphs) work transparently.
/// </remarks>
public sealed class OrigamiIcons
{
    /// <summary>Disclosure indicator for an expanded foldout / accordion section.</summary>
    public string ChevronDown = string.Empty;

    /// <summary>Disclosure indicator for a collapsed foldout / accordion section.</summary>
    public string ChevronRight = string.Empty;

    /// <summary>Disclosure indicator pointing up (hidden / secondary collapse).</summary>
    public string ChevronUp = string.Empty;

    /// <summary>Disclosure indicator pointing left.</summary>
    public string ChevronLeft = string.Empty;

    /// <summary>Empty checkbox glyph used by toggle controls when off.</summary>
    public string CheckboxOff = string.Empty;

    /// <summary>Filled / checked checkbox glyph used by toggle controls when on.</summary>
    public string CheckboxOn = string.Empty;

    /// <summary>Plain checkmark — used for menus, confirmations, list selection.</summary>
    public string Check = string.Empty;

    /// <summary>Close / dismiss / clear glyph.</summary>
    public string Close = string.Empty;

    /// <summary>Search / filter affordance.</summary>
    public string Search = string.Empty;

    /// <summary>Generic "more options" affordance (vertical ellipsis).</summary>
    public string More = string.Empty;

    /// <summary>Informational badge.</summary>
    public string Info = string.Empty;

    /// <summary>Warning badge.</summary>
    public string Warning = string.Empty;

    /// <summary>Danger / error badge.</summary>
    public string Danger = string.Empty;

    /// <summary>Success / OK badge.</summary>
    public string Success = string.Empty;

    /// <summary>Shallow copy.</summary>
    public OrigamiIcons Clone() => new()
    {
        ChevronDown = ChevronDown,
        ChevronRight = ChevronRight,
        ChevronUp = ChevronUp,
        ChevronLeft = ChevronLeft,
        CheckboxOff = CheckboxOff,
        CheckboxOn = CheckboxOn,
        Check = Check,
        Close = Close,
        Search = Search,
        More = More,
        Info = Info,
        Warning = Warning,
        Danger = Danger,
        Success = Success,
    };
}
