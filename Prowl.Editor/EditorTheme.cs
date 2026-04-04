using System.Drawing;

using Prowl.Scribe;

namespace Prowl.Editor;

public static class EditorTheme
{
    public static FontFile? DefaultFont;
    public static FontFile? DefaultBoldFont;

    // Sizing
    public const float MenuBarHeight = 26f;
    public const float StatusBarHeight = 22f;
    public const float TabBarHeight = 26f;
    public const float RowHeight = 22f;
    public const float SplitterSize = 14f;
    public const float DockPadding = 14f;
    public const float Spacing = 2f;
    public const float Padding = 4f;
    public const float FontSize = 17f;
    public const float LabelWidth = 120f;

    #region Outdated

    // 4-shade system: Darkest → Dark → Normal → Bright
    public static Color Background = Color.FromArgb(255, 10, 10, 12);// Background (near black)
    public static Color Darkest = Color.FromArgb(255, 20, 20, 22);   // Menu bar
    public static Color Dark = Color.FromArgb(255, 28, 28, 30);   // Background (near black)
    public static Color Normal = Color.FromArgb(255, 37, 37, 40);   // Windows/panels
    public static Color Bright = Color.FromArgb(255, 65, 65, 70);   // Scrollbar, outline

    // Aliases for readability
    public static Color MenuBarBackground => Darkest;
    public static Color WindowBackground => Normal;
    public static Color PanelBackground => Normal;
    public static Color HeaderBackground = Color.FromArgb(255, 30, 30, 33);
    public static Color InputBackground = Color.FromArgb(255, 28, 28, 30);
    public static Color Border => Bright;

    // Text
    public static Color Text = Color.FromArgb(255, 220, 220, 220);
    public static Color TextDim = Color.FromArgb(255, 150, 150, 150);
    public static Color TextDisabled = Color.FromArgb(255, 90, 90, 90);

    // Interactive
    public static Color ButtonNormal = Color.FromArgb(255, 55, 55, 58);
    public static Color ButtonHovered = Color.FromArgb(255, 70, 70, 74);
    public static Color ButtonActive = Color.FromArgb(255, 40, 40, 43);

    // Accent
    public static Color Accent = Color.FromArgb(255, 51, 122, 183);
    public static Color AccentDim = Color.FromArgb(255, 40, 90, 140);

    // Borders
    public static Color BorderFocused = Color.FromArgb(255, 51, 122, 183);

    // Splitter
    public static Color Splitter = Color.FromArgb(255, 25, 25, 25);
    public static Color SplitterHovered = Color.FromArgb(255, 51, 122, 183);

    // Tab
    public static Color TabActive => Normal;
    public static Color TabInactive => Darkest;
    public static Color TabHovered = Color.FromArgb(255, 55, 55, 58);

    #endregion

    // Static palette for the application theme.
    // Built from 8 base colors: #101116, #16151A, #18191D, #1D1E22,
    // #563784, #82AAC6, #271D36, #CB594F.
    //
    // Organized into five groups:
    //   - Backgrounds  : four depth levels from page base to elevated surface
    //   - Purple Accent: primary brand / interactive color ramp
    //   - Blue Accent  : secondary / informational color ramp
    //   - Danger       : error, destructive action, alert ramp
    //   - Text &amp; Borders: hierarchy from primary text down to default border

    // ─────────────────────────────────────────────────────────────
    //  BACKGROUNDS
    //  Four dark neutrals that form a natural depth stack.
    //  Use them in order from the deepest layer upward.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Void — #101116
    /// Role: Deepest background / page base.
    /// The lowest layer of the UI; sits behind everything else.
    /// </summary>
    public static readonly Color BgPage = ColorTranslator.FromHtml("#101116");

    /// <summary>
    /// Abyss — #16151A
    /// Role: App background.
    /// The main application shell background, one step above the page base.
    /// </summary>
    public static readonly Color BgApp = ColorTranslator.FromHtml("#16151A");

    /// <summary>
    /// Obsidian — #18191D
    /// Role: Sidebar / panels.
    /// Used for sidebars, drawers, and secondary panels.
    /// </summary>
    public static readonly Color BgPanel = ColorTranslator.FromHtml("#18191D");

    /// <summary>
    /// Slate — #1D1E22
    /// Role: Cards / surfaces / elevated elements.
    /// The topmost background layer; use for cards, modals, and raised containers.
    /// </summary>
    public static readonly Color BgSurface = ColorTranslator.FromHtml("#1D1E22");


    // ─────────────────────────────────────────────────────────────
    //  PURPLE ACCENT RAMP
    //  Anchored on #563784 (Amethyst) as the primary brand color.
    //  Ranges from a subtle tinted surface through to a pale highlight.
    //  This is the dominant interactive color family:
    //  buttons, links, focus rings, selection states.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Dusk — #271D36
    /// Role: Accent-subtle. Tinted surface / hover states.
    /// Lightest purple-tinted background; use for hover fills on rows or list items.
    /// </summary>
    public static readonly Color AccentSubtle = ColorTranslator.FromHtml("#271D36");

    /// <summary>
    /// Twilight — #3D2660
    /// Role: Accent-muted. Active states / selection background.
    /// Slightly stronger tint; use as a selected or active item background.
    /// </summary>
    public static readonly Color AccentMuted = ColorTranslator.FromHtml("#3D2660");

    /// <summary>
    /// Amethyst — #563784  ★ PRIMARY BRAND COLOR
    /// Role: Accent-primary. Primary buttons / interactive links.
    /// The core brand purple. Default fill for buttons, focus rings, and key UI actions.
    /// </summary>
    public static readonly Color AccentPrimary = ColorTranslator.FromHtml("#563784");

    /// <summary>
    /// Lavender — #7252AA
    /// Role: Accent-hover. Button hover state / focus ring.
    /// One step lighter than Amethyst; use as a hover or pressed variant of AccentPrimary.
    /// </summary>
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#7252AA");

    /// <summary>
    /// Wisteria — #A886D8
    /// Role: Accent-light. Highlighted text / badges / chips.
    /// A mid-tone purple for text labels, badge text, and icon fills on dark backgrounds.
    /// </summary>
    public static readonly Color AccentLight = ColorTranslator.FromHtml("#A886D8");

    /// <summary>
    /// Lilac — #D4B8F4
    /// Role: Accent-pale. Lightest purple highlight / disabled text on accent bg.
    /// Use for very soft highlights or placeholder text that sits on a purple surface.
    /// </summary>
    public static readonly Color AccentPale = ColorTranslator.FromHtml("#D4B8F4");


    // ─────────────────────────────────────────────────────────────
    //  BLUE ACCENT RAMP  (Secondary / Informational)
    //  Anchored on #82AAC6 (Glacier). Used for informational states,
    //  secondary actions, and data-link text.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Deep Ocean — #0D1A24
    /// Role: Info-base. Darkest informational tint.
    /// Background for deeply nested info surfaces.
    /// </summary>
    public static readonly Color InfoBase = ColorTranslator.FromHtml("#0D1A24");

    /// <summary>
    /// Midnight Blue — #1D3044
    /// Role: Info-subtle. Info callout / tooltip background.
    /// Use as the background of informational banners or tooltip panels.
    /// </summary>
    public static readonly Color InfoSubtle = ColorTranslator.FromHtml("#1D3044");

    /// <summary>
    /// Harbor — #2E5470
    /// Role: Info-muted. Secondary button border / info icon background.
    /// Mid-dark blue for outlines on secondary actions or bordered info tags.
    /// </summary>
    public static readonly Color InfoMuted = ColorTranslator.FromHtml("#2E5470");

    /// <summary>
    /// Glacier — #82AAC6  ★ SECONDARY BRAND / INFO COLOR
    /// Role: Info-primary. Links / informational text / icons.
    /// The core info blue. Use for hyperlinks, info-state icons, and secondary CTAs.
    /// </summary>
    public static readonly Color InfoPrimary = ColorTranslator.FromHtml("#82AAC6");

    /// <summary>
    /// Mist — #AECADD
    /// Role: Info-light. Info callout body text.
    /// Lighter blue text that remains readable on dark info-tinted backgrounds.
    /// </summary>
    public static readonly Color InfoLight = ColorTranslator.FromHtml("#AECADD");

    /// <summary>
    /// Powder — #CDDEED
    /// Role: Info-pale. Very soft informational highlight.
    /// Use for subtle info-state underlines or chart fill areas.
    /// </summary>
    public static readonly Color InfoPale = ColorTranslator.FromHtml("#CDDEED");

    /// <summary>
    /// Frost — #E8F2F9
    /// Role: Info-faint. Faintest informational background tint.
    /// Barely-there blue wash for alternating rows or disabled info areas.
    /// </summary>
    public static readonly Color InfoFaint = ColorTranslator.FromHtml("#E8F2F9");


    // ─────────────────────────────────────────────────────────────
    //  DANGER RAMP  (Error / Destructive / Alert)
    //  Anchored on #CB594F (Cinnabar). Used for errors, destructive
    //  buttons, validation messages, and critical alerts.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Char — #1C0E0E
    /// Role: Danger-base. Darkest error tint.
    /// Deep background for critical error surfaces or severe alert panels.
    /// </summary>
    public static readonly Color DangerBase = ColorTranslator.FromHtml("#1C0E0E");

    /// <summary>
    /// Ember — #361818
    /// Role: Danger-subtle. Error background / inline validation tint.
    /// Use as the background of error toast notifications or invalid input fields.
    /// </summary>
    public static readonly Color DangerSubtle = ColorTranslator.FromHtml("#361818");

    /// <summary>
    /// Garnet — #7A3030
    /// Role: Danger-muted. Danger border / icon fill.
    /// Use for the border of error inputs and the fill of warning/error icon circles.
    /// </summary>
    public static readonly Color DangerMuted = ColorTranslator.FromHtml("#7A3030");

    /// <summary>
    /// Cinnabar — #CB594F  ★ PRIMARY DANGER / ERROR COLOR
    /// Role: Danger-primary. Destructive buttons / error alerts / critical badges.
    /// The core danger red. Use for delete buttons, error banners, and alert icons.
    /// </summary>
    public static readonly Color DangerPrimary = ColorTranslator.FromHtml("#CB594F");

    /// <summary>
    /// Blush — #E68880
    /// Role: Danger-light. Error message body text.
    /// Lighter red text readable on dark danger-tinted backgrounds.
    /// </summary>
    public static readonly Color DangerLight = ColorTranslator.FromHtml("#E68880");

    /// <summary>
    /// Rose — #F2B0AB
    /// Role: Danger-pale. Soft danger highlight / disabled error state.
    /// Very light red for muted error indications or secondary alert text.
    /// </summary>
    public static readonly Color DangerPale = ColorTranslator.FromHtml("#F2B0AB");

    /// <summary>
    /// Petal — #FDE0DE
    /// Role: Danger-faint. Faintest danger wash.
    /// Barely perceptible red tint for error state backgrounds in light regions.
    /// </summary>
    public static readonly Color DangerFaint = ColorTranslator.FromHtml("#FDE0DE");


    // ─────────────────────────────────────────────────────────────
    //  TEXT &amp; BORDERS
    //  Four stops from near-white down to a near-invisible border.
    //  Use in order to create a clear typographic and structural hierarchy.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starlight — #F0EEF8
    /// Role: Primary text.
    /// Headings, body copy, and any high-priority label. Near-white with a purple cast.
    /// </summary>
    public static readonly Color TextPrimary = ColorTranslator.FromHtml("#F0EEF8");

    /// <summary>
    /// Ash — #B0ADBE
    /// Role: Secondary text.
    /// Subtitles, metadata, and secondary labels. Visually recedes behind primary text.
    /// </summary>
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#B0ADBE");

    /// <summary>
    /// Pewter — #6C6A7A
    /// Role: Placeholder / hint text.
    /// Input placeholders, disabled labels, and lowest-priority hints.
    /// </summary>
    public static readonly Color TextHint = ColorTranslator.FromHtml("#6C6A7A");

    /// <summary>
    /// Graphite — #2E2D35
    /// Role: Default border / separator.
    /// Dividers, input outlines, card edges, and table row lines.
    /// </summary>
    public static readonly Color BorderDefault = ColorTranslator.FromHtml("#2E2D35");

    /// <summary>
    /// Iron — #3E3D47
    /// Role: Emphasis border.
    /// Slightly lighter border used on hover states or to distinguish nested containers.
    /// </summary>
    public static readonly Color BorderEmphasis = ColorTranslator.FromHtml("#3E3D47");
}
