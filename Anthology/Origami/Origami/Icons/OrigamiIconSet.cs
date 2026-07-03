// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.OrigamiUI;

/// <summary>
/// Origami's built-in icons — stroked SVG glyphs in a 16x16 viewBox, drawn by <see cref="SvgIcon"/>.
/// Use them directly (e.g. <c>OrigamiIconSet.Cube</c>) when configuring a widget, or via
/// <see cref="OrigamiIcons"/> for the theme's chrome icons. Tint one with <see cref="SvgIcon.Tinted"/>.
/// </summary>
public static class OrigamiIconSet
{
    // ── Disclosure / navigation ──────────────────────────────
    public static readonly SvgIcon ChevronDown  = new("M3.5 6l4.5 4.5 4.5-4.5");
    public static readonly SvgIcon ChevronUp    = new("M3.5 10l4.5-4.5 4.5 4.5");
    public static readonly SvgIcon ChevronRight = new("M6 3.5l4.5 4.5-4.5 4.5");
    public static readonly SvgIcon ChevronLeft  = new("M10 3.5l-4.5 4.5 4.5 4.5");
    public static readonly SvgIcon ArrowRight = new("M3 8h10M9 4l4 4-4 4");
    public static readonly SvgIcon ArrowLeft  = new("M13 8H3M7 4l-4 4 4 4");
    public static readonly SvgIcon ArrowUp    = new("M8 13V3M4 7l4-4 4 4");
    public static readonly SvgIcon ArrowDown  = new("M8 3v10M4 9l4 4 4-4");
    public static readonly SvgIcon Expand     = new("M6 2.5H2.5V6M10 2.5h3.5V6M6 13.5H2.5V10M10 13.5h3.5V10");

    // ── Status / feedback ────────────────────────────────────
    public static readonly SvgIcon Check   = new("M3.5 8.5l2.8 2.8 6.2-7");
    public static readonly SvgIcon Close   = new("M4 4l8 8M12 4l-8 8");
    public static readonly SvgIcon Info    = new("M8 1.7a6.3 6.3 0 1 0 0 12.6 6.3 6.3 0 0 0 0-12.6zM8 7v4M8 4.7v.1");
    public static readonly SvgIcon Warning = new("M8 2.3L14.6 13.6H1.4zM8 6.3v3.6M8 11.6v.1");
    public static readonly SvgIcon Danger  = new("M8 1.7a6.3 6.3 0 1 0 0 12.6 6.3 6.3 0 0 0 0-12.6zM5.6 5.6l4.8 4.8M10.4 5.6l-4.8 4.8");
    public static readonly SvgIcon Success = new("M8 1.7a6.3 6.3 0 1 0 0 12.6 6.3 6.3 0 0 0 0-12.6zM5.2 8l2 2 3.6-4");

    // ── Toggles ──────────────────────────────────────────────
    public static readonly SvgIcon CheckboxOff = new("M4 2.5h8a1.5 1.5 0 0 1 1.5 1.5v8a1.5 1.5 0 0 1-1.5 1.5H4a1.5 1.5 0 0 1-1.5-1.5V4A1.5 1.5 0 0 1 4 2.5z");
    public static readonly SvgIcon CheckboxOn  = new("M4 2.5h8a1.5 1.5 0 0 1 1.5 1.5v8a1.5 1.5 0 0 1-1.5 1.5H4a1.5 1.5 0 0 1-1.5-1.5V4A1.5 1.5 0 0 1 4 2.5zM5.5 8l1.8 1.8 3.2-3.6");

    // ── Actions / affordances ────────────────────────────────
    public static readonly SvgIcon Search = new("M7.2 12.4a5.2 5.2 0 1 0 0-10.4 5.2 5.2 0 0 0 0 10.4zM11.2 11.2L14.5 14.5");
    public static readonly SvgIcon Plus    = new("M8 3v10M3 8h10");
    public static readonly SvgIcon Gear    = new("M8 5.6a2.4 2.4 0 1 0 0 4.8 2.4 2.4 0 0 0 0-4.8zM8 1.5l.5 1.6 1.6-.6 1 1.4-1 1.3 1.5.8-.4 1.6h-1.6l-.8 1.5-1.4-.7-1.4.7-.8-1.5H4.3l-.4-1.6 1.5-.8-1-1.3 1-1.4 1.6.6z");
    public static readonly SvgIcon Bolt    = new("M8.5 1.5L3.5 9h4l-1 5.5L13 6.5H8.5z");
    public static readonly SvgIcon Pencil  = new("M11.4 2.4l2.2 2.2-8 8-2.9.7.7-2.9zM10 3.8l2.2 2.2");
    public static readonly SvgIcon Trash   = new("M3 4.5h10M6.5 4.5V3h3v1.5M4.6 4.5l.6 8.5a1 1 0 0 0 1 1h3.6a1 1 0 0 0 1-1l.6-8.5M6.7 7v4M9.3 7v4");
    public static readonly SvgIcon Link    = new("M6.6 9.4a2.6 2.6 0 0 0 3.6 0l2-2a2.6 2.6 0 0 0-3.6-3.6l-1 1M9.4 6.6a2.6 2.6 0 0 0-3.6 0l-2 2a2.6 2.6 0 0 0 3.6 3.6l1-1");
    public static readonly SvgIcon Duplicate = new("M5.5 5.5h6a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1h-6a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1zM3.5 10.5H3a1 1 0 0 1-1-1V3.5a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1V4");
    public static readonly SvgIcon More    = new("M8 3.4v.1M8 8v.1M8 12.6v.1");
    public static readonly SvgIcon Eye      = new("M1.6 8C3 5.4 5.3 3.8 8 3.8s5 1.6 6.4 4.2C13 10.6 10.7 12.2 8 12.2S3 10.6 1.6 8zM8 10.1a2.1 2.1 0 1 0 0-4.2 2.1 2.1 0 0 0 0 4.2z");
    public static readonly SvgIcon EyeOff   = new("M1.6 8C3 5.4 5.3 3.8 8 3.8s5 1.6 6.4 4.2C13 10.6 10.7 12.2 8 12.2S3 10.6 1.6 8zM8 10.1a2.1 2.1 0 1 0 0-4.2 2.1 2.1 0 0 0 0 4.2zM3 3l10 10");
    public static readonly SvgIcon List    = new("M5.5 4h8M5.5 8h8M5.5 12h8M2.5 4v.1M2.5 8v.1M2.5 12v.1");
    public static readonly SvgIcon Grid    = new("M2 2h3.5v3.5H2zM6.5 2H10v3.5H6.5zM11 2h3v3.5h-3zM2 6.5h3.5V10H2zM6.5 6.5H10V10H6.5zM11 6.5h3V10h-3zM2 11h3.5v3H2zM6.5 11H10v3H6.5zM11 11h3v3h-3z");

    // ── Files / storage ──────────────────────────────────────
    public static readonly SvgIcon Folder     = new("M2 4.2a1 1 0 0 1 1-1h3l1.2 1.4H13a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1z");
    public static readonly SvgIcon FolderOpen = new("M2 4.2a1 1 0 0 1 1-1h3l1.2 1.4H13a1 1 0 0 1 1 1v1H4.2L2.6 12.5M2 4.2v8a1 1 0 0 0 1 1h10l1.5-6H4.2");
    public static readonly SvgIcon FolderPlus = new("M2 4.2a1 1 0 0 1 1-1h3l1.2 1.4H13a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1zM8 7.3v3.4M6.3 9h3.4");
    public static readonly SvgIcon File     = new("M4 1.7h5l3.2 3.2v8.4a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V2.7a1 1 0 0 1 1-1zM9 1.7v3.2h3.2");
    public static readonly SvgIcon Document = new("M4 1.7h5l3.2 3.2v8.4a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V2.7a1 1 0 0 1 1-1zM5.5 8h5M5.5 10.5h5M5.5 5.5h2");
    public static readonly SvgIcon Drive    = new("M2 5h12a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H2a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1zM11.6 8.5v.1");
    public static readonly SvgIcon Star     = new("M8 1.8l1.9 3.9 4.3.6-3.1 3 .7 4.3L8 11.6 4.2 13.6l.7-4.3-3.1-3 4.3-.6z");
    public static readonly SvgIcon Clock    = new("M8 1.7a6.3 6.3 0 1 0 0 12.6 6.3 6.3 0 0 0 0-12.6zM8 4.6V8l2.4 1.5");
    public static readonly SvgIcon Calendar = new("M3 3.5h10a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1v-8a1 1 0 0 1 1-1zM2 6.5h12M5 2v3M11 2v3");
    public static readonly SvgIcon Download = new("M8 2v8M4.5 6.5L8 10l3.5-3.5M3 13h10");
    public static readonly SvgIcon Desktop  = new("M2 3.5h12a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H2a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1zM6 14h4M8 11.5v2.5");
    public static readonly SvgIcon User     = new("M8 8a2.8 2.8 0 1 0 0-5.6 2.8 2.8 0 0 0 0 5.6zM2.6 14a5.4 5.4 0 0 1 10.8 0");

    // ── Content / scene ──────────────────────────────────────
    public static readonly SvgIcon Cube     = new("M8 1.7L14 5v6L8 14.3 2 11V5zM8 1.7V8M8 8l6-3M8 8l-6-3");
    public static readonly SvgIcon Sphere   = new("M8 1.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13zM2 8h12M8 1.5c-2 1.6-2 11 0 13M8 1.5c2 1.6 2 11 0 13");
    public static readonly SvgIcon Globe    = new("M8 1.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13zM2 8h12M8 1.5c-2.2 1.7-2.2 11.3 0 13M8 1.5c2.2 1.7 2.2 11.3 0 13");
    public static readonly SvgIcon Layers   = new("M8 2l6 3-6 3-6-3zM2 8l6 3 6-3M2 11l6 3 6-3");
    public static readonly SvgIcon Material = new("M8 1.7a6.3 6.3 0 1 0 0 12.6c1.2 0 1.5-.8 1.5-1.5 0-1.3-1.5-1.2-1.5-2.5 0-.8.7-1.3 1.7-1.3h1.1A3.7 3.7 0 0 0 14 5.2 6.3 6.3 0 0 0 8 1.7zM5 6.2v.1M9 4.2v.1M11 7.2v.1");
    public static readonly SvgIcon Script   = new("M4 1.7h5l3.2 3.2v8.4a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V2.7a1 1 0 0 1 1-1zM5.5 8h5M5.5 10.5h5M5.5 5.5h2");
    public static readonly SvgIcon Mark     = new("M3 13.5 L8 2.5 L9.6 6 L13 13.5 L8.3 10.8 Z");
}
