// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.OrigamiUI;

/// <summary>
/// The icons Origami widget chrome draws (disclosure chevrons, close/search, checkboxes, sort carets,
/// dock indicators, file-dialog glyphs …). Each is an <see cref="IOrigamiIcon"/>, so a host can swap
/// any of them for its own vector / Font Awesome glyph. Defaults to Origami's built-in
/// <see cref="OrigamiIconSet"/>. Widgets skip any icon set to <c>null</c>.
/// </summary>
public sealed class OrigamiIcons
{
    // ── Disclosure / navigation ──────────────────────────────
    public IOrigamiIcon? ChevronDown  = OrigamiIconSet.ChevronDown;
    public IOrigamiIcon? ChevronRight = OrigamiIconSet.ChevronRight;
    public IOrigamiIcon? ChevronUp    = OrigamiIconSet.ChevronUp;
    public IOrigamiIcon? ChevronLeft  = OrigamiIconSet.ChevronLeft;
    public IOrigamiIcon? ArrowLeft    = OrigamiIconSet.ArrowLeft;
    public IOrigamiIcon? ArrowRight   = OrigamiIconSet.ArrowRight;
    public IOrigamiIcon? ArrowUp      = OrigamiIconSet.ArrowUp;
    public IOrigamiIcon? ArrowDown    = OrigamiIconSet.ArrowDown;

    // ── Status / feedback ────────────────────────────────────
    public IOrigamiIcon? Check   = OrigamiIconSet.Check;
    public IOrigamiIcon? Close   = OrigamiIconSet.Close;
    public IOrigamiIcon? Info    = OrigamiIconSet.Info;
    public IOrigamiIcon? Warning = OrigamiIconSet.Warning;
    public IOrigamiIcon? Danger  = OrigamiIconSet.Danger;
    public IOrigamiIcon? Success = OrigamiIconSet.Success;

    // ── Toggles ──────────────────────────────────────────────
    public IOrigamiIcon? CheckboxOff = OrigamiIconSet.CheckboxOff;
    public IOrigamiIcon? CheckboxOn  = OrigamiIconSet.CheckboxOn;

    // ── Actions / affordances ────────────────────────────────
    public IOrigamiIcon? Search    = OrigamiIconSet.Search;
    public IOrigamiIcon? More      = OrigamiIconSet.More;
    public IOrigamiIcon? Eye       = OrigamiIconSet.Eye;
    public IOrigamiIcon? EyeOff    = OrigamiIconSet.EyeOff;
    public IOrigamiIcon? Plus      = OrigamiIconSet.Plus;
    public IOrigamiIcon? Pencil    = OrigamiIconSet.Pencil;
    public IOrigamiIcon? Trash     = OrigamiIconSet.Trash;
    public IOrigamiIcon? Duplicate = OrigamiIconSet.Duplicate;

    // ── File dialog ──────────────────────────────────────────
    public IOrigamiIcon? Folder     = OrigamiIconSet.Folder;
    public IOrigamiIcon? FolderPlus = OrigamiIconSet.FolderPlus;
    public IOrigamiIcon? File       = OrigamiIconSet.File;
    public IOrigamiIcon? Document   = OrigamiIconSet.Document;
    public IOrigamiIcon? Drive      = OrigamiIconSet.Drive;
    public IOrigamiIcon? Star       = OrigamiIconSet.Star;
    public IOrigamiIcon? Clock      = OrigamiIconSet.Clock;
    public IOrigamiIcon? Desktop    = OrigamiIconSet.Desktop;
    public IOrigamiIcon? Download   = OrigamiIconSet.Download;
    public IOrigamiIcon? User       = OrigamiIconSet.User;

    /// <summary>Shallow copy (icons are immutable, so the references are shared).</summary>
    public OrigamiIcons Clone() => new()
    {
        ChevronDown = ChevronDown, ChevronRight = ChevronRight, ChevronUp = ChevronUp, ChevronLeft = ChevronLeft,
        ArrowLeft = ArrowLeft, ArrowRight = ArrowRight, ArrowUp = ArrowUp, ArrowDown = ArrowDown,
        Check = Check, Close = Close, Info = Info, Warning = Warning, Danger = Danger, Success = Success,
        CheckboxOff = CheckboxOff, CheckboxOn = CheckboxOn,
        Search = Search, More = More, Eye = Eye, EyeOff = EyeOff, Plus = Plus, Pencil = Pencil, Trash = Trash, Duplicate = Duplicate,
        Folder = Folder, FolderPlus = FolderPlus, File = File, Document = Document, Drive = Drive,
        Star = Star, Clock = Clock, Desktop = Desktop, Download = Download, User = User,
    };
}
