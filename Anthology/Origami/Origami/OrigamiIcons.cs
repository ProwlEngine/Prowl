// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.OrigamiUI;

/// <summary>
/// The icons Origami widget chrome draws (disclosure chevrons, close/search, checkboxes, sort carets,
/// dock indicators, file-dialog glyphs …). Each is an <see cref="IOrigamiIcon"/>, so a host can swap
/// any of them for its own icon. Defaults to Font Awesome glyphs from <see cref="OrigamiGlyphs"/>.
/// </summary>
public sealed class OrigamiIcons
{
    // ── Disclosure / navigation ──────────────────────────────
    public IOrigamiIcon? ChevronDown = OrigamiGlyphs.ChevronDown_I;
    public IOrigamiIcon? ChevronRight = OrigamiGlyphs.ChevronRight_I;
    public IOrigamiIcon? ChevronUp = OrigamiGlyphs.ChevronUp_I;
    public IOrigamiIcon? ChevronLeft = OrigamiGlyphs.ChevronLeft_I;
    public IOrigamiIcon? ArrowLeft = OrigamiGlyphs.ArrowLeft_I;
    public IOrigamiIcon? ArrowRight = OrigamiGlyphs.ArrowRight_I;
    public IOrigamiIcon? ArrowUp = OrigamiGlyphs.ArrowUp_I;
    public IOrigamiIcon? ArrowDown = OrigamiGlyphs.ArrowDown_I;

    // ── Status / feedback ────────────────────────────────────
    public IOrigamiIcon? Check = OrigamiGlyphs.Check_I;
    public IOrigamiIcon? Close = OrigamiGlyphs.Xmark_I;
    public IOrigamiIcon? Info = OrigamiGlyphs.CircleInfo_I;
    public IOrigamiIcon? Warning = OrigamiGlyphs.TriangleExclamation_I;
    public IOrigamiIcon? Danger = OrigamiGlyphs.CircleExclamation_I;
    public IOrigamiIcon? Success = OrigamiGlyphs.CircleCheck_I;

    // ── Toggles ──────────────────────────────────────────────
    public IOrigamiIcon? CheckboxOff = OrigamiGlyphs.Square_I;
    public IOrigamiIcon? CheckboxOn = OrigamiGlyphs.SquareCheck_I;

    // ── Actions / affordances ────────────────────────────────
    public IOrigamiIcon? Search = OrigamiGlyphs.MagnifyingGlass_I;
    public IOrigamiIcon? More = OrigamiGlyphs.EllipsisVertical_I;
    public IOrigamiIcon? Eye = OrigamiGlyphs.Eye_I;
    public IOrigamiIcon? EyeOff = OrigamiGlyphs.EyeSlash_I;
    public IOrigamiIcon? Plus = OrigamiGlyphs.Plus_I;
    public IOrigamiIcon? Pencil = OrigamiGlyphs.Pencil_I;
    public IOrigamiIcon? Trash = OrigamiGlyphs.Trash_I;
    public IOrigamiIcon? Duplicate = OrigamiGlyphs.Copy_I;

    // ── File dialog ──────────────────────────────────────────
    public IOrigamiIcon? Folder = OrigamiGlyphs.Folder_I;
    public IOrigamiIcon? FolderPlus = OrigamiGlyphs.FolderPlus_I;
    public IOrigamiIcon? File = OrigamiGlyphs.File_I;
    public IOrigamiIcon? Document = OrigamiGlyphs.FileLines_I;
    public IOrigamiIcon? Drive = OrigamiGlyphs.HardDrive_I;
    public IOrigamiIcon? Star = OrigamiGlyphs.Star_I;
    public IOrigamiIcon? Clock = OrigamiGlyphs.Clock_I;
    public IOrigamiIcon? Desktop = OrigamiGlyphs.Desktop_I;
    public IOrigamiIcon? Download = OrigamiGlyphs.Download_I;
    public IOrigamiIcon? User = OrigamiGlyphs.User_I;

    // ── Layout / resize ───────────────────────────────────────
    public IOrigamiIcon? GripVertical = OrigamiGlyphs.GripVertical_I;
    public IOrigamiIcon? GripHorizontal = OrigamiGlyphs.GripLines_I;

    /// <summary>Shallow copy (icons are immutable, so the references are shared).</summary>
    public OrigamiIcons Clone() => new()
    {
        ChevronDown = ChevronDown,
        ChevronRight = ChevronRight,
        ChevronUp = ChevronUp,
        ChevronLeft = ChevronLeft,
        ArrowLeft = ArrowLeft,
        ArrowRight = ArrowRight,
        ArrowUp = ArrowUp,
        ArrowDown = ArrowDown,
        Check = Check,
        Close = Close,
        Info = Info,
        Warning = Warning,
        Danger = Danger,
        Success = Success,
        CheckboxOff = CheckboxOff,
        CheckboxOn = CheckboxOn,
        Search = Search,
        More = More,
        Eye = Eye,
        EyeOff = EyeOff,
        Plus = Plus,
        Pencil = Pencil,
        Trash = Trash,
        Duplicate = Duplicate,
        Folder = Folder,
        FolderPlus = FolderPlus,
        File = File,
        Document = Document,
        Drive = Drive,
        Star = Star,
        Clock = Clock,
        Desktop = Desktop,
        Download = Download,
        User = User,
        GripVertical = GripVertical,
        GripHorizontal = GripHorizontal,
    };
}
