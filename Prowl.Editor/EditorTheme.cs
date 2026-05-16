using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.Scribe;

namespace Prowl.Editor;

public static class EditorTheme
{
    /// <summary>Default transition (seconds) when applying the editor theme to Origami. 0 = snap.</summary>
    public const float OrigamiTransitionSeconds = 0.25f;

    // ── Editor's Origami theme state ──────────────────────────────────
    //
    // The editor owns its own OrigamiTheme and pushes it onto Origami's stack
    // each frame (see PushOrigami). Origami.Root is left untouched so user code
    // running inside the editor (e.g. game UI in play mode) sees the unmodified
    // root unless it explicitly opts into the editor theme.
    //
    // SyncOrigami starts a transition into a freshly-built target theme; TickOrigami
    // advances the lerp once per frame.

    private static OrigamiTheme? _origamiTheme;

    /// <summary>
    /// Live theme the editor pushes onto Origami's stack. Frame-fresh when transitioning.
    /// Re-built on access until <see cref="DefaultFont"/> has loaded — early frames before
    /// font initialisation would otherwise cache a Font=null theme and never recover.
    /// <see cref="SyncOrigami"/> replaces it when the user mutates the theme.
    /// </summary>
    public static OrigamiTheme OrigamiTheme
    {
        get
        {
            // Rebuild while Font is still null so we eventually capture the loaded font.
            // Once it's set, the cached theme is stable until SyncOrigami is called.
            if (_origamiTheme == null || _origamiTheme.Font == null)
                _origamiTheme = BuildOrigamiTheme();
            return _origamiTheme;
        }
        private set => _origamiTheme = value;
    }

    private static OrigamiTheme? _origamiStart;
    private static OrigamiTheme? _origamiTarget;
    private static float _origamiDuration;
    private static float _origamiElapsed;

    /// <summary>True while a <see cref="SyncOrigami"/> transition is in progress.</summary>
    public static bool IsOrigamiTransitioning => _origamiTarget != null;

    /// <summary>
    /// Build an <see cref="OrigamiTheme"/> mirroring the current editor theme tokens and start
    /// transitioning the editor's pushed theme toward it. Called from
    /// <c>EditorSettings.ApplyTheme</c> so user theme edits propagate live.
    /// </summary>
    /// <remarks>
    /// This is the only place in the editor that bridges into Origami — Origami itself has no
    /// reference to <see cref="EditorTheme"/> or any editor type, keeping the widget library
    /// extractable as a standalone package. The transition runs against the editor's pushed
    /// theme; <see cref="Origami.Root"/> is never written.
    /// </remarks>
    public static void SyncOrigami(float transitionSeconds = OrigamiTransitionSeconds)
    {
        var target = BuildOrigamiTheme();
        if (transitionSeconds <= 0f)
        {
            OrigamiTheme = target;
            _origamiStart = null;
            _origamiTarget = null;
            _origamiDuration = 0f;
            _origamiElapsed = 0f;
            return;
        }

        _origamiStart = OrigamiTheme.Clone();
        _origamiTarget = target;
        _origamiDuration = transitionSeconds;
        _origamiElapsed = 0f;
    }

    /// <summary>Advance the editor's Origami theme transition. Called from the editor's frame loop.</summary>
    public static void TickOrigami(float deltaSeconds)
    {
        if (_origamiTarget == null || _origamiStart == null) return;

        _origamiElapsed += deltaSeconds;
        float t = _origamiDuration > 0f ? System.Math.Clamp(_origamiElapsed / _origamiDuration, 0f, 1f) : 1f;

        if (t >= 1f)
        {
            OrigamiTheme = _origamiTarget;
            _origamiStart = null;
            _origamiTarget = null;
            _origamiDuration = 0f;
            _origamiElapsed = 0f;
            return;
        }

        OrigamiTheme = Prowl.OrigamiUI.OrigamiTheme.Lerp(_origamiStart, _origamiTarget, t);
    }

    /// <summary>
    /// Push the editor's Origami theme onto Origami's stack. Wrap the editor's render in
    /// <c>using (EditorTheme.PushOrigami()) { ... }</c> so all editor widgets see the editor
    /// theme while user code outside the scope still sees <see cref="Origami.Root"/>.
    /// </summary>
    public static System.IDisposable PushOrigami() => Origami.PushTheme(OrigamiTheme);

    /// <summary>Construct the editor-flavoured Origami theme without applying it (useful for preview/diff).</summary>
    public static OrigamiTheme BuildOrigamiTheme() => new()
    {
        // Neutral — editor's 5-stop ramp extended to 7 by reusing Ink200/Ink300 for the lighter end,
        // matching how the editor itself blends Neutral into Ink at the brighter range.
        Neutral = new OrigamiRamp
        {
            C100 = Neutral100, C200 = Neutral200, C300 = Neutral300,
            C400 = Neutral400, C500 = Neutral500, C600 = Ink200, C700 = Ink300,
        },

        // Editor's branded ramps map 1:1 (Purple → Primary is the only rename).
        Primary = RampFrom(Purple100, Purple200, Purple300, Purple400, Purple500, Purple600, Purple700),
        Blue    = RampFrom(Blue100,   Blue200,   Blue300,   Blue400,   Blue500,   Blue600,   Blue700),
        Red     = RampFrom(Red100,    Red200,    Red300,    Red400,    Red500,    Red600,    Red700),

        // Editor has no Green or Amber yet — hand-tuned. Replace once the editor adds them.
        Green = RampFromHex("#0F1F15", "#162C20", "#1F4530", "#2D5C42", "#3D7A57", "#5DC07F", "#A6E5B7"),
        Amber = RampFromHex("#1F1808", "#3A2A10", "#5C4017", "#7A5520", "#9B7332", "#E0A954", "#F4D8A8"),

        // Ink ramp: the editor's 5 stops + 2 extra-bright headroom (white) for emphasis text.
        Ink = new OrigamiRamp
        {
            C100 = Ink100, C200 = Ink200, C300 = Ink300,
            C400 = Ink400, C500 = Ink500, C600 = Color.White, C700 = Color.White,
        },

        Metrics = new OrigamiMetrics
        {
            Rounding     = Roundness,
            HeaderHeight = RowHeight,
            HeaderPadX   = 6f,
            IconWidth    = 16f,
            BadgePadLeft = 6f,
            FontSize     = FontSize,
        },

        Icons = new OrigamiIcons
        {
            ChevronDown  = EditorIcons.ChevronDown,
            ChevronRight = EditorIcons.ChevronRight,
            ChevronUp    = EditorIcons.ChevronUp,
            ChevronLeft  = EditorIcons.ChevronLeft,
            CheckboxOff  = EditorIcons.Square,
            CheckboxOn   = EditorIcons.SquareCheck,
            Check        = EditorIcons.Check,
            Close        = EditorIcons.Xmark,
            Search       = EditorIcons.MagnifyingGlass,
            More         = EditorIcons.EllipsisVertical,
            Info         = EditorIcons.CircleInfo,
            Warning      = EditorIcons.TriangleExclamation,
            Danger       = EditorIcons.CircleExclamation,
            Success      = EditorIcons.CircleCheck,
            Folder       = EditorIcons.Folder,
            File         = EditorIcons.File,
            Drive        = EditorIcons.HardDrive,
            Star         = EditorIcons.Star,
            Clock        = EditorIcons.Clock,
            Trash        = EditorIcons.Trash,
            Plus         = EditorIcons.Plus,
            ArrowLeft    = EditorIcons.ArrowLeft,
            ArrowRight   = EditorIcons.ArrowRight,
            ArrowUp      = EditorIcons.ArrowUp,
            Pencil       = EditorIcons.Pen,
            FolderPlus   = EditorIcons.FolderPlus,
            Desktop      = EditorIcons.Desktop,
            Download     = EditorIcons.Download,
            User         = EditorIcons.User,
            Document     = EditorIcons.FolderOpen,
        },
        Font = DefaultFont,
    };

    private static OrigamiRamp RampFrom(Color c1, Color c2, Color c3, Color c4, Color c5, Color c6, Color c7) => new()
    {
        C100 = c1, C200 = c2, C300 = c3, C400 = c4, C500 = c5, C600 = c6, C700 = c7,
    };

    private static OrigamiRamp RampFromHex(string c1, string c2, string c3, string c4, string c5, string c6, string c7) => new()
    {
        C100 = ColorTranslator.FromHtml(c1), C200 = ColorTranslator.FromHtml(c2),
        C300 = ColorTranslator.FromHtml(c3), C400 = ColorTranslator.FromHtml(c4),
        C500 = ColorTranslator.FromHtml(c5), C600 = ColorTranslator.FromHtml(c6),
        C700 = ColorTranslator.FromHtml(c7),
    };

    public static FontFile? DefaultFont;
    public static FontFile? DefaultBoldFont;

    public static string DefaultFontName = "segoe ui";
    public static string DefaultBoldFontName = "segoe ui";

    // DPI Scaling value
    public static float UserScale { get; set; } = 1f;

    // Sizing mutable so themes can override
    public static float MenuBarHeight = 26f;
    public static float RowHeight = 22f;
    public static float Spacing = 2f;
    public static float Padding = 4f;
    public static float FontSize = 17f;
    public static float LabelWidth = 200f;
    public static float Roundness = 8f;

    public static float IndicatorSize = 28f;
    public static float IndicatorGap = 4f;
    public static float SplitterSize = 14f;
    public static float DockPadding = 14f;

    public static float SidePixelPadding = 10f;
    public static float VerticalNavbarSpacing = 4f;

    public static float TabBarHeight = 26f;
    public static float TabPadding = 12f;
    public static float TabCloseSize = 14f;
    public static float TabGap = 0f;

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
    //   Hovered = +100  (one step lighter lifts)
    //   Pressed = -100  (one step darker  sinks)
    //
    // Ramps:
    //   Neutral  100–500  Background depth stack (page → surface)
    //   Purple   100–700  Primary brand / interactive
    //   Blue     100–700  Secondary / informational
    //   Red      100–700  Danger / error / destructive
    //   Ink      100–500  Text and border hierarchy

    // ─────────────────────────────────────────────────────────────
    //  NEUTRAL Background depth stack
    //  Use in ascending order from the deepest layer upward.
    //  100 = page base (behind everything), 400 = elevated surface.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Neutral100 #101116  "Void"
    /// Deepest background. The lowest layer of the UI; sits behind everything else.
    /// </summary>
    public static Color Neutral100 = ColorTranslator.FromHtml("#101116");

    /// <summary>
    /// Neutral200 #16151A  "Abyss"
    /// App background. The main application shell; one step above the page base.
    /// </summary>
    public static Color Neutral200 = ColorTranslator.FromHtml("#16151A");

    /// <summary>
    /// Neutral300 #18191D  "Obsidian"
    /// Sidebar / panels. Used for sidebars, drawers, and secondary panels.
    /// </summary>
    public static Color Neutral300 = ColorTranslator.FromHtml("#18191D");

    /// <summary>
    /// Neutral400 #1D1E22  "Slate"  ★
    /// Cards / surfaces / elevated elements.
    /// The topmost background layer; use for cards, modals, and raised containers.
    /// </summary>
    public static Color Neutral400 = ColorTranslator.FromHtml("#1D1E22");

    /// <summary>
    /// Neutral500 #2E2D35  "Graphite"
    /// Default border / separator.
    /// Dividers, input outlines, card edges, and table row lines.
    /// </summary>
    public static Color Neutral500 = ColorTranslator.FromHtml("#2E2D35");


    // ─────────────────────────────────────────────────────────────
    //  PURPLE Primary brand / interactive ramp
    //  ★ Primary = Purple400 (#563784)
    //
    //  Button states:
    //    Normal  = Purple400
    //    Hovered = Purple500
    //    Pressed = Purple300
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Purple100 #1D1010  "Char"
    /// Darkest purple tint. Deep background for critical or heavily tinted surfaces.
    /// </summary>
    public static Color Purple100 = ColorTranslator.FromHtml("#1D1010");

    /// <summary>
    /// Purple200 #271D36  "Dusk"
    /// Subtle tinted surface. Use for hover fills on rows or list items.
    /// </summary>
    public static Color Purple200 = ColorTranslator.FromHtml("#271D36");

    /// <summary>
    /// Purple300 #3D2660  "Twilight"
    /// Active / pressed state background. Also used for selected item backgrounds.
    /// </summary>
    public static Color Purple300 = ColorTranslator.FromHtml("#3D2660");

    /// <summary>
    /// Purple400 #563784  "Amethyst"  ★
    /// PRIMARY BRAND COLOR. Default fill for buttons, focus rings, and key UI actions.
    /// </summary>
    public static Color Purple400 = ColorTranslator.FromHtml("#563784");

    /// <summary>
    /// Purple500 #7252AA  "Lavender"
    /// Hover state. One step lighter than Amethyst; use as the hovered variant of Purple400.
    /// </summary>
    public static Color Purple500 = ColorTranslator.FromHtml("#7252AA");

    /// <summary>
    /// Purple600 #A886D8  "Wisteria"
    /// Highlighted text / badges / chips.
    /// Mid-tone purple for text labels, badge text, and icon fills on dark backgrounds.
    /// </summary>
    public static Color Purple600 = ColorTranslator.FromHtml("#A886D8");

    /// <summary>
    /// Purple700 #D4B8F4  "Lilac"
    /// Lightest purple highlight. Use for very soft highlights or placeholder text
    /// that sits on a purple-tinted surface.
    /// </summary>
    public static Color Purple700 = ColorTranslator.FromHtml("#D4B8F4");


    // ─────────────────────────────────────────────────────────────
    //  BLUE Secondary / informational ramp
    //  ★ Primary = Blue400 (#82AAC6)
    //
    //  Button / link states:
    //    Normal  = Blue400
    //    Hovered = Blue500
    //    Pressed = Blue300
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Blue100 #0D1A24  "Deep Ocean"
    /// Darkest informational tint. Background for deeply nested info surfaces.
    /// </summary>
    public static Color Blue100 = ColorTranslator.FromHtml("#0D1A24");

    /// <summary>
    /// Blue200 #1D3044  "Midnight"
    /// Info callout / tooltip background.
    /// Use as the background of informational banners or tooltip panels.
    /// </summary>
    public static Color Blue200 = ColorTranslator.FromHtml("#1D3044");

    /// <summary>
    /// Blue300 #2E5470  "Harbor"
    /// Pressed state. Also used for borders on secondary actions or bordered info tags.
    /// </summary>
    public static Color Blue300 = ColorTranslator.FromHtml("#2E5470");

    /// <summary>
    /// Blue400 #82AAC6  "Glacier"  ★
    /// SECONDARY / INFO COLOR. Use for hyperlinks, info-state icons, and secondary CTAs.
    /// </summary>
    public static Color Blue400 = ColorTranslator.FromHtml("#82AAC6");

    /// <summary>
    /// Blue500 #AECADD  "Mist"
    /// Hover state. Lighter blue text; also readable on dark info-tinted backgrounds.
    /// </summary>
    public static Color Blue500 = ColorTranslator.FromHtml("#AECADD");

    /// <summary>
    /// Blue600 #CDDEED  "Powder"
    /// Soft informational highlight. Use for subtle info-state underlines or chart fill areas.
    /// </summary>
    public static Color Blue600 = ColorTranslator.FromHtml("#CDDEED");

    /// <summary>
    /// Blue700 #E8F2F9  "Frost"
    /// Faintest informational background tint.
    /// Barely-there blue wash for alternating rows or disabled info areas.
    /// </summary>
    public static Color Blue700 = ColorTranslator.FromHtml("#E8F2F9");


    // ─────────────────────────────────────────────────────────────
    //  RED Danger / error / destructive ramp
    //  ★ Primary = Red400 (#CB594F)
    //
    //  Button states:
    //    Normal  = Red400
    //    Hovered = Red500
    //    Pressed = Red300
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Red100 #1C0E0E  "Char"
    /// Darkest error tint. Background for critical error surfaces or severe alert panels.
    /// </summary>
    public static Color Red100 = ColorTranslator.FromHtml("#1C0E0E");

    /// <summary>
    /// Red200 #361818  "Ember"
    /// Error background. Use as the background of error toasts or invalid input fields.
    /// </summary>
    public static Color Red200 = ColorTranslator.FromHtml("#361818");

    /// <summary>
    /// Red300 #7A3030  "Garnet"
    /// Pressed state. Also used for borders on error inputs and error icon backgrounds.
    /// </summary>
    public static Color Red300 = ColorTranslator.FromHtml("#7A3030");

    /// <summary>
    /// Red400 #CB594F  "Cinnabar"  ★
    /// PRIMARY DANGER COLOR. Use for delete buttons, error banners, and alert icons.
    /// </summary>
    public static Color Red400 = ColorTranslator.FromHtml("#CB594F");

    /// <summary>
    /// Red500 #E68880  "Blush"
    /// Hover state. Lighter red; also readable as error message text on dark backgrounds.
    /// </summary>
    public static Color Red500 = ColorTranslator.FromHtml("#E68880");

    /// <summary>
    /// Red600 #F2B0AB  "Rose"
    /// Soft danger highlight. Use for muted error indications or secondary alert text.
    /// </summary>
    public static Color Red600 = ColorTranslator.FromHtml("#F2B0AB");

    /// <summary>
    /// Red700 #FDE0DE  "Petal"
    /// Faintest danger wash. Barely-perceptible red tint for error state backgrounds.
    /// </summary>
    public static Color Red700 = ColorTranslator.FromHtml("#FDE0DE");


    // ─────────────────────────────────────────────────────────────
    //  INK Text and border hierarchy
    //  Use in ascending order: 100 = borders, 500 = primary readable text.
    //  Never use pure black or white these stops keep the purple-dark theme cast.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Ink100 #2E2D35  "Graphite"
    /// Default border / separator.
    /// Dividers, input outlines, card edges, and table row lines.
    /// </summary>
    public static Color Ink100 = ColorTranslator.FromHtml("#2E2D35");

    /// <summary>
    /// Ink200 #3E3D47  "Iron"
    /// Emphasis border.
    /// Slightly lighter border for hover states or to distinguish nested containers.
    /// </summary>
    public static Color Ink200 = ColorTranslator.FromHtml("#3E3D47");

    /// <summary>
    /// Ink300 #6C6A7A  "Pewter"
    /// Placeholder / hint text.
    /// Input placeholders, disabled labels, and lowest-priority hints.
    /// </summary>
    public static Color Ink300 = ColorTranslator.FromHtml("#6C6A7A");

    /// <summary>
    /// Ink400 #B0ADBE  "Ash"
    /// Secondary text.
    /// Subtitles, metadata, and secondary labels. Recedes behind primary text.
    /// </summary>
    public static Color Ink400 = ColorTranslator.FromHtml("#B0ADBE");

    /// <summary>
    /// Ink500 #F0EEF8  "Starlight"
    /// Primary text.
    /// Headings, body copy, and all high-priority labels. Near-white with a purple cast.
    /// </summary>
    public static Color Ink500 = ColorTranslator.FromHtml("#F0EEF8");
}
