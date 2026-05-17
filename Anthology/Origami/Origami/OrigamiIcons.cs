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

    // ── File dialog icons ──────────────────────────────────────

    /// <summary>Folder glyph (closed folder).</summary>
    public string Folder = string.Empty;

    /// <summary>Generic file glyph.</summary>
    public string File = string.Empty;

    /// <summary>Hard drive / disk glyph for drive listing.</summary>
    public string Drive = string.Empty;

    /// <summary>Star / bookmark glyph for favorites.</summary>
    public string Star = string.Empty;

    /// <summary>Clock / history glyph for recent files.</summary>
    public string Clock = string.Empty;

    /// <summary>Trash / delete glyph.</summary>
    public string Trash = string.Empty;

    /// <summary>Plus / add glyph.</summary>
    public string Plus = string.Empty;

    /// <summary>Left arrow for back navigation.</summary>
    public string ArrowLeft = string.Empty;

    /// <summary>Right arrow for forward navigation.</summary>
    public string ArrowRight = string.Empty;

    /// <summary>Up arrow for parent directory.</summary>
    public string ArrowUp = string.Empty;

    /// <summary>Pencil / edit glyph for rename.</summary>
    public string Pencil = string.Empty;

    /// <summary>Folder with plus for new folder.</summary>
    public string FolderPlus = string.Empty;

    /// <summary>Desktop glyph for quick access.</summary>
    public string Desktop = string.Empty;

    /// <summary>Download glyph for quick access.</summary>
    public string Download = string.Empty;

    /// <summary>User / home glyph.</summary>
    public string User = string.Empty;

    /// <summary>Document / file-lines glyph.</summary>
    public string Document = string.Empty;

    // ── Docking icons ─────────────────────────────────────────

    /// <summary>Down arrow for dock indicator.</summary>
    public string ArrowDown = string.Empty;

    /// <summary>Clone / duplicate glyph for dock-to-center indicator.</summary>
    public string Duplicate = string.Empty;

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
        Folder = Folder,
        File = File,
        Drive = Drive,
        Star = Star,
        Clock = Clock,
        Trash = Trash,
        Plus = Plus,
        ArrowLeft = ArrowLeft,
        ArrowRight = ArrowRight,
        ArrowUp = ArrowUp,
        Pencil = Pencil,
        FolderPlus = FolderPlus,
        Desktop = Desktop,
        Download = Download,
        User = User,
        Document = Document,
        ArrowDown = ArrowDown,
        Duplicate = Duplicate,
    };
}
