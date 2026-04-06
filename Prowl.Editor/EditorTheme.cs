using System.Drawing;

using Prowl.Scribe;

namespace Prowl.Editor;

public static class EditorTheme
{
    public static FontFile? DefaultFont;
    public static FontFile? DefaultBoldFont;

    // Sizing — mutable so themes can override
    public static float MenuBarHeight = 26f;
    public static float StatusBarHeight = 22f;
    public static float RowHeight = 22f;
    public static float Spacing = 2f;
    public static float Padding = 4f;
    public static float FontSize = 17f;
    public static float LabelWidth = 120f;
    public static float Roundness = 8f;

    public static float IndicatorSize = 28f;
    public static float IndicatorGap = 4f;
    public static float SplitterSize = 14f;
    public static float DockPadding = 14f;

    public static float TabBarHeight = 26f;
    public static float TabPadding = 12f;
    public static float TabCloseSize = 14f;
    public static float TabGap = 0f;

    #region Outdated

    // 4-shade system: Darkest → Dark → Normal → Bright
    // public static Color Background = Color.FromArgb(255, 10, 10, 12);// Background (near black)
    // public static Color Darkest = Color.FromArgb(255, 20, 20, 22);   // Menu bar
    // public static Color Dark = Color.FromArgb(255, 28, 28, 30);   // Background (near black)
    // public static Color Normal = Color.FromArgb(255, 37, 37, 40);   // Windows/panels
    // public static Color Bright = Color.FromArgb(255, 65, 65, 70);   // Scrollbar, outline

    // Aliases for readability
    // public static Color MenuBarBackground => Darkest;
    // public static Color WindowBackground => Normal;
    // public static Color PanelBackground => Normal;
    // public static Color HeaderBackground = Color.FromArgb(255, 30, 30, 33);
    // public static Color InputBackground = Color.FromArgb(255, 28, 28, 30);
    // public static Color Border => Bright;

    // Text
    // public static Color Text = Color.FromArgb(255, 220, 220, 220);
    // public static Color TextDim = Color.FromArgb(255, 150, 150, 150);
    // public static Color TextDisabled = Color.FromArgb(255, 90, 90, 90);

    // Interactive
    // public static Color ButtonNormal = Color.FromArgb(255, 55, 55, 58);
    public static Color ButtonHovered = Color.FromArgb(255, 70, 70, 74);
    public static Color ButtonActive = Color.FromArgb(255, 40, 40, 43);

    // Accent
    // public static Color Accent = Color.FromArgb(255, 51, 122, 183);
    // public static Color AccentDim = Color.FromArgb(255, 40, 90, 140);

    // Borders
    // public static Color BorderFocused = Color.FromArgb(255, 51, 122, 183);

    // Splitter
    // public static Color Splitter = Color.FromArgb(255, 25, 25, 25);
    // public static Color SplitterHovered = Color.FromArgb(255, 51, 122, 183);

    // Tab
    // public static Color TabActive => Normal;
    // public static Color TabInactive => Darkest;
    // public static Color TabHovered = Color.FromArgb(255, 55, 55, 58);

    #endregion

    // Static color palette for the application theme.
    // Built from 8 base colors: #101116, #16151A, #18191D, #1D1E22,
    // #563784, #82AAC6, #271D36, #CB594F.
    //
    // Colors are organized into ramps using a 100-based numeric scale.
    // Lower numbers are darker (background tints), higher numbers are lighter
    // (text, highlights). The ★ marker denotes the primary stop of each ramp.
    //
    // Interactive state convention (applies to all ramps):
    //   Normal  = ★ primary stop
    //   Hovered = +100  (one step lighter — lifts)
    //   Pressed = -100  (one step darker  — sinks)
    //
    // Ramps:
    //   Neutral  100–500  Background depth stack (page → surface)
    //   Purple   100–700  Primary brand / interactive
    //   Blue     100–700  Secondary / informational
    //   Red      100–700  Danger / error / destructive
    //   Ink      100–500  Text and border hierarchy

    // ─────────────────────────────────────────────────────────────
    //  NEUTRAL — Background depth stack
    //  Use in ascending order from the deepest layer upward.
    //  100 = page base (behind everything), 400 = elevated surface.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Neutral100 — #101116  "Void"
    /// Deepest background. The lowest layer of the UI; sits behind everything else.
    /// </summary>
    public static Color Neutral100 = ColorTranslator.FromHtml("#101116");

    /// <summary>
    /// Neutral200 — #16151A  "Abyss"
    /// App background. The main application shell; one step above the page base.
    /// </summary>
    public static Color Neutral200 = ColorTranslator.FromHtml("#16151A");

    /// <summary>
    /// Neutral300 — #18191D  "Obsidian"
    /// Sidebar / panels. Used for sidebars, drawers, and secondary panels.
    /// </summary>
    public static Color Neutral300 = ColorTranslator.FromHtml("#18191D");

    /// <summary>
    /// Neutral400 — #1D1E22  "Slate"  ★
    /// Cards / surfaces / elevated elements.
    /// The topmost background layer; use for cards, modals, and raised containers.
    /// </summary>
    public static Color Neutral400 = ColorTranslator.FromHtml("#1D1E22");

    /// <summary>
    /// Neutral500 — #2E2D35  "Graphite"
    /// Default border / separator.
    /// Dividers, input outlines, card edges, and table row lines.
    /// </summary>
    public static Color Neutral500 = ColorTranslator.FromHtml("#2E2D35");


    // ─────────────────────────────────────────────────────────────
    //  PURPLE — Primary brand / interactive ramp
    //  ★ Primary = Purple400 (#563784)
    //
    //  Button states:
    //    Normal  = Purple400
    //    Hovered = Purple500
    //    Pressed = Purple300
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Purple100 — #1D1010  "Char"
    /// Darkest purple tint. Deep background for critical or heavily tinted surfaces.
    /// </summary>
    public static Color Purple100 = ColorTranslator.FromHtml("#1D1010");

    /// <summary>
    /// Purple200 — #271D36  "Dusk"
    /// Subtle tinted surface. Use for hover fills on rows or list items.
    /// </summary>
    public static Color Purple200 = ColorTranslator.FromHtml("#271D36");

    /// <summary>
    /// Purple300 — #3D2660  "Twilight"
    /// Active / pressed state background. Also used for selected item backgrounds.
    /// </summary>
    public static Color Purple300 = ColorTranslator.FromHtml("#3D2660");

    /// <summary>
    /// Purple400 — #563784  "Amethyst"  ★
    /// PRIMARY BRAND COLOR. Default fill for buttons, focus rings, and key UI actions.
    /// </summary>
    public static Color Purple400 = ColorTranslator.FromHtml("#563784");

    /// <summary>
    /// Purple500 — #7252AA  "Lavender"
    /// Hover state. One step lighter than Amethyst; use as the hovered variant of Purple400.
    /// </summary>
    public static Color Purple500 = ColorTranslator.FromHtml("#7252AA");

    /// <summary>
    /// Purple600 — #A886D8  "Wisteria"
    /// Highlighted text / badges / chips.
    /// Mid-tone purple for text labels, badge text, and icon fills on dark backgrounds.
    /// </summary>
    public static Color Purple600 = ColorTranslator.FromHtml("#A886D8");

    /// <summary>
    /// Purple700 — #D4B8F4  "Lilac"
    /// Lightest purple highlight. Use for very soft highlights or placeholder text
    /// that sits on a purple-tinted surface.
    /// </summary>
    public static Color Purple700 = ColorTranslator.FromHtml("#D4B8F4");


    // ─────────────────────────────────────────────────────────────
    //  BLUE — Secondary / informational ramp
    //  ★ Primary = Blue400 (#82AAC6)
    //
    //  Button / link states:
    //    Normal  = Blue400
    //    Hovered = Blue500
    //    Pressed = Blue300
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Blue100 — #0D1A24  "Deep Ocean"
    /// Darkest informational tint. Background for deeply nested info surfaces.
    /// </summary>
    public static Color Blue100 = ColorTranslator.FromHtml("#0D1A24");

    /// <summary>
    /// Blue200 — #1D3044  "Midnight"
    /// Info callout / tooltip background.
    /// Use as the background of informational banners or tooltip panels.
    /// </summary>
    public static Color Blue200 = ColorTranslator.FromHtml("#1D3044");

    /// <summary>
    /// Blue300 — #2E5470  "Harbor"
    /// Pressed state. Also used for borders on secondary actions or bordered info tags.
    /// </summary>
    public static Color Blue300 = ColorTranslator.FromHtml("#2E5470");

    /// <summary>
    /// Blue400 — #82AAC6  "Glacier"  ★
    /// SECONDARY / INFO COLOR. Use for hyperlinks, info-state icons, and secondary CTAs.
    /// </summary>
    public static Color Blue400 = ColorTranslator.FromHtml("#82AAC6");

    /// <summary>
    /// Blue500 — #AECADD  "Mist"
    /// Hover state. Lighter blue text; also readable on dark info-tinted backgrounds.
    /// </summary>
    public static Color Blue500 = ColorTranslator.FromHtml("#AECADD");

    /// <summary>
    /// Blue600 — #CDDEED  "Powder"
    /// Soft informational highlight. Use for subtle info-state underlines or chart fill areas.
    /// </summary>
    public static Color Blue600 = ColorTranslator.FromHtml("#CDDEED");

    /// <summary>
    /// Blue700 — #E8F2F9  "Frost"
    /// Faintest informational background tint.
    /// Barely-there blue wash for alternating rows or disabled info areas.
    /// </summary>
    public static Color Blue700 = ColorTranslator.FromHtml("#E8F2F9");


    // ─────────────────────────────────────────────────────────────
    //  RED — Danger / error / destructive ramp
    //  ★ Primary = Red400 (#CB594F)
    //
    //  Button states:
    //    Normal  = Red400
    //    Hovered = Red500
    //    Pressed = Red300
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Red100 — #1C0E0E  "Char"
    /// Darkest error tint. Background for critical error surfaces or severe alert panels.
    /// </summary>
    public static Color Red100 = ColorTranslator.FromHtml("#1C0E0E");

    /// <summary>
    /// Red200 — #361818  "Ember"
    /// Error background. Use as the background of error toasts or invalid input fields.
    /// </summary>
    public static Color Red200 = ColorTranslator.FromHtml("#361818");

    /// <summary>
    /// Red300 — #7A3030  "Garnet"
    /// Pressed state. Also used for borders on error inputs and error icon backgrounds.
    /// </summary>
    public static Color Red300 = ColorTranslator.FromHtml("#7A3030");

    /// <summary>
    /// Red400 — #CB594F  "Cinnabar"  ★
    /// PRIMARY DANGER COLOR. Use for delete buttons, error banners, and alert icons.
    /// </summary>
    public static Color Red400 = ColorTranslator.FromHtml("#CB594F");

    /// <summary>
    /// Red500 — #E68880  "Blush"
    /// Hover state. Lighter red; also readable as error message text on dark backgrounds.
    /// </summary>
    public static Color Red500 = ColorTranslator.FromHtml("#E68880");

    /// <summary>
    /// Red600 — #F2B0AB  "Rose"
    /// Soft danger highlight. Use for muted error indications or secondary alert text.
    /// </summary>
    public static Color Red600 = ColorTranslator.FromHtml("#F2B0AB");

    /// <summary>
    /// Red700 — #FDE0DE  "Petal"
    /// Faintest danger wash. Barely-perceptible red tint for error state backgrounds.
    /// </summary>
    public static Color Red700 = ColorTranslator.FromHtml("#FDE0DE");


    // ─────────────────────────────────────────────────────────────
    //  INK — Text and border hierarchy
    //  Use in ascending order: 100 = borders, 500 = primary readable text.
    //  Never use pure black or white — these stops keep the purple-dark theme cast.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Ink100 — #2E2D35  "Graphite"
    /// Default border / separator.
    /// Dividers, input outlines, card edges, and table row lines.
    /// </summary>
    public static Color Ink100 = ColorTranslator.FromHtml("#2E2D35");

    /// <summary>
    /// Ink200 — #3E3D47  "Iron"
    /// Emphasis border.
    /// Slightly lighter border for hover states or to distinguish nested containers.
    /// </summary>
    public static Color Ink200 = ColorTranslator.FromHtml("#3E3D47");

    /// <summary>
    /// Ink300 — #6C6A7A  "Pewter"
    /// Placeholder / hint text.
    /// Input placeholders, disabled labels, and lowest-priority hints.
    /// </summary>
    public static Color Ink300 = ColorTranslator.FromHtml("#6C6A7A");

    /// <summary>
    /// Ink400 — #B0ADBE  "Ash"
    /// Secondary text.
    /// Subtitles, metadata, and secondary labels. Recedes behind primary text.
    /// </summary>
    public static Color Ink400 = ColorTranslator.FromHtml("#B0ADBE");

    /// <summary>
    /// Ink500 — #F0EEF8  "Starlight"
    /// Primary text.
    /// Headings, body copy, and all high-priority labels. Near-white with a purple cast.
    /// </summary>
    public static Color Ink500 = ColorTranslator.FromHtml("#F0EEF8");
}
