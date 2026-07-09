using System;
using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.Scribe;

namespace Prowl.Editor.Theming;

/// <summary>
/// The editor's visual tokens. The editor uses Origami's default theme as the single
/// source of truth: the colour/semantic tokens below are seeded from <see cref="OrigamiTheme.CreateDefaults"/>
/// and the theme the editor pushes onto Origami each frame is that same default theme (with the editor's
/// fonts + metrics). Custom-drawn editor chrome reads these tokens so it matches the Origami widgets.
/// </summary>
public static class EditorTheme
{
    /// <summary>Default transition (seconds) when applying the editor theme to Origami. 0 = snap.</summary>
    public const float OrigamiTransitionSeconds = 0.25f;

    // -- Editor's Origami theme state ----------------------------------
    //
    // The editor owns its own OrigamiTheme and pushes it onto Origami's stack each frame
    // (see PushOrigami). Origami.Root is left untouched. SyncOrigami starts a transition into a
    // freshly-built target theme; TickOrigami advances the lerp once per frame.

    private static OrigamiTheme? _origamiTheme;

    /// <summary>Live theme the editor pushes onto Origami's stack. Frame-fresh when transitioning.</summary>
    public static OrigamiTheme OrigamiTheme
    {
        get
        {
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

    /// <summary>Rebuild the pushed Origami theme and transition toward it.</summary>
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
        float t = _origamiDuration > 0f ? Math.Clamp(_origamiElapsed / _origamiDuration, 0f, 1f) : 1f;

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
    /// <c>using (EditorTheme.PushOrigami()) { ... }</c>.
    /// </summary>
    public static IDisposable PushOrigami() => Origami.PushTheme(OrigamiTheme);

    /// <summary>
    /// The editor theme = Origami's default palette + the editor's fonts and metrics.
    /// Icons are left at Origami's default vector set (no override).
    /// </summary>
    public static OrigamiTheme BuildOrigamiTheme()
    {
        var t = OrigamiTheme.CreateDefaults();

        // Origami's defaults are the base; the user's customization is overlaid on top.
        Customization?.ApplyTo(t);

        t.Metrics = new OrigamiMetrics
        {
            Rounding          = Roundness,
            ContainerRounding = Roundness + 2f,
            SmallRounding     = MathF.Max(3f, Roundness * 0.5f),
            RowHeight         = RowHeight,
            HeaderHeight      = RowHeight,
            CompactHeight     = RowHeight - 4f,
            FontSize          = FontSize,
            FontSizeSmall     = FontSize - 2f,
            LabelWidth        = LabelWidth,
            // Spacing / padding scale derived from the two editor knobs (Spacing=4, Padding=6 reproduce
            // Origami's stock 2/4/6/8 and 4/6/12).
            SpacingSmall      = Spacing * 0.5f,
            Spacing           = Spacing,
            SpacingMedium     = Spacing * 1.5f,
            SpacingLarge      = Spacing * 2f,
            PaddingSmall      = Padding * (2f / 3f),
            Padding           = Padding,
            PaddingLarge      = Padding * 2f,
            TabBarHeight      = TabBarHeight,
            TabPadding        = TabPadding,
            TabGap            = TabGap,
            TabCloseSize      = TabCloseSize,
            SplitterSize      = SplitterSize,
            DockPadding       = DockPadding,
            IndicatorSize     = IndicatorSize,
            IndicatorGap      = IndicatorGap,
            // Glass Blur toggle drives every backdrop-blur surface (dock windows, file dialog, header).
            WindowBackdropBlur = EffectiveBlur,
        };

        t.Font         = Font;
        t.FontMedium   = FontMedium;
        t.FontSemiBold = FontSemiBold;
        t.FontBold     = FontBold;
        t.FontMono     = FontMono;
        t.Icons        = BuildIconSet();
        return t;
    }

    /// <summary>The editor overrides Origami's built-in SVG icon set with Font Awesome glyphs (from the
    /// editor's <see cref="EditorIcons"/>) via the theme's <c>Icons</c> hook. Origami standalone keeps its
    /// own vector set. Falls back to Origami's defaults until the icon font is loaded.</summary>
    private static OrigamiIcons BuildIconSet()
    {
        // EditorGlyphIcon resolves the font at draw time, so chrome icons never flash blank before the
        // editor font finishes loading.
        Prowl.Editor.GUI.EditorGlyphIcon I(string glyph) => new(glyph, scale: 0.82f);
        return new OrigamiIcons
        {
            ChevronDown = I(EditorIcons.ChevronDown), ChevronRight = I(EditorIcons.ChevronRight),
            ChevronUp = I(EditorIcons.ChevronUp), ChevronLeft = I(EditorIcons.ChevronLeft),
            ArrowLeft = I(EditorIcons.ArrowLeft), ArrowRight = I(EditorIcons.ArrowRight),
            ArrowUp = I(EditorIcons.ArrowUp), ArrowDown = I(EditorIcons.ArrowDown),
            Check = I(EditorIcons.Check), Close = I(EditorIcons.Xmark),
            Info = I(EditorIcons.CircleInfo), Warning = I(EditorIcons.TriangleExclamation),
            Danger = I(EditorIcons.CircleExclamation), Success = I(EditorIcons.CircleCheck),
            CheckboxOff = I(EditorIcons.Square), CheckboxOn = I(EditorIcons.SquareCheck),
            Search = I(EditorIcons.MagnifyingGlass), More = I(EditorIcons.EllipsisVertical),
            Eye = I(EditorIcons.Eye), EyeOff = I(EditorIcons.EyeSlash),
            Plus = I(EditorIcons.Plus), Pencil = I(EditorIcons.Pencil),
            Trash = I(EditorIcons.Trash), Duplicate = I(EditorIcons.Copy),
            Folder = I(EditorIcons.Folder), FolderPlus = I(EditorIcons.FolderPlus),
            File = I(EditorIcons.File), Document = I(EditorIcons.FileLines),
            Drive = I(EditorIcons.HardDrive), Star = I(EditorIcons.Star),
            Clock = I(EditorIcons.Clock), Desktop = I(EditorIcons.Desktop),
            Download = I(EditorIcons.Download), User = I(EditorIcons.User),
        };
    }

    // -- Fonts ---------------------------------------------------------

    /// <summary>Regular (400) weight the editor's primary UI face.</summary>
    public static FontFile? DefaultFont;
    /// <summary>Bold (700) weight.</summary>
    public static FontFile? DefaultBoldFont;
    public static FontFile? FontMedium;
    public static FontFile? FontSemiBold;
    public static FontFile? FontMono;
    /// <summary>Display face for wordmarks / big headings (Space Grotesk).</summary>
    public static FontFile? FontDisplay;
    /// <summary>Logo face for the PROWL wordmark (Audiowide).</summary>
    public static FontFile? FontLogo;
    /// <summary>Font Awesome solid (filled) glyphs, for drawing an icon directly in the filled style.</summary>
    public static FontFile? FontIconSolid;
    /// <summary>Font Awesome regular (outline) glyphs, for drawing an icon directly in the outline style.</summary>
    public static FontFile? FontIconOutline;

    public static FontFile? Font => DefaultFont;
    public static FontFile? FontBold => DefaultBoldFont ?? FontSemiBold ?? DefaultFont;

    public static string DefaultFontName = "Geist";
    public static string DefaultBoldFontName = "Geist";

    // DPI Scaling value
    public static float UserScale { get; set; } = 1f;

    // -- Sizing (mutable so the Preferences panel can tweak) -----------
    public static float MenuBarHeight = 40f;
    public static float StatusBarHeight = 26f;
    public static float RowHeight = 24f;
    // Base spacing/padding the full Origami metric scale (SpacingSmall..PaddingLarge) is derived from
    // these in BuildOrigamiTheme, so tweaking them retunes gaps/padding everywhere (property grid, etc.).
    public static float Spacing = 4f;
    public static float Padding = 6f;
    public static float FontSize = 17f;
    public static float FontSizeSmall = FontSize - 2f;
    public static float FontSizeLarge = FontSize + 2f;
    public static float FontSizeLogo = 72f;
    public static float LabelWidth = 150f;
    public static float Roundness = 6f;

    // -- Effects (mirrored from EditorThemeData; applied globally) -----
    public static bool GlassBlur = true;
    public static float BlurAmount = 22f;
    public static bool DropShadows = true;
    public static bool AccentGlow = true;
    public static bool AntiAliasing = true;
    public static bool AnimatedBackground = true;
    public static float BackgroundSpeed = 1f;
    public static EditorBackgroundStyle BackgroundStyle = EditorBackgroundStyle.Nebula;
    public static Color BackgroundColorA = Color.FromArgb(27, 17, 48);
    public static Color BackgroundColorB = Color.FromArgb(8, 6, 12);
    public static bool BgShowGradients = true;
    public static bool BgShowStars = true;
    public static bool BgShowComets = true;
    public static Color BackgroundVoidColor = Color.FromArgb(6, 4, 9);

    /// <summary>True when the (animated or static) nebula should be drawn rather than a gradient/solid.</summary>
    public static bool UsesNebulaBackground => AnimatedBackground || BackgroundStyle == EditorBackgroundStyle.Nebula;

    /// <summary>The blur radius actually pushed into Origami's metrics (0 when Glass Blur is off).</summary>
    public static float EffectiveBlur => GlassBlur ? BlurAmount : 0f;

    public static float IndicatorSize = 28f;
    public static float IndicatorGap = 4f;
    public static float SplitterSize = 6f;
    public static float DockPadding = 6f;

    public static float SidePixelPadding = 10f;
    public static float VerticalNavbarSpacing = 4f;

    public static float TabBarHeight = 32f;
    public static float TabPadding = 12f;
    public static float TabCloseSize = 14f;
    public static float TabGap = 0f;

    // ==================================================================
    //  Colour tokens
    //
    //  Origami's default theme is the source of truth. The live palette is
    //  EditorTheme.OrigamiTheme = Origami defaults + the user's Customization
    //  (applied in BuildOrigamiTheme). The tokens below are computed VIEWS over
    //  that live theme, so editing a ramp in Preferences retints the whole editor
    //  and every Origami widget at once. A few depth tokens are editor-specific
    //  (Origami has no ramp for the void / app-shell / separator).
    // ==================================================================

    /// <summary>User customization overlaid onto Origami's defaults when building the live theme.
    /// Null = pure Origami defaults. Set by <see cref="EditorSettings.ApplyTheme"/>.</summary>
    public static EditorThemeData? Customization;

    private static OrigamiTheme T => OrigamiTheme;

    // -- Neutral: editor depth stack. 100/200/500 are editor-specific; 300/400/600/700 map to the ramp. --
    public static Color Neutral100 => Color.FromArgb(255, 6, 4, 9);        // void deepest base
    public static Color Neutral200 => Color.FromArgb(240, 12, 10, 20);     // app shell
    public static Color Neutral300 => T.Neutral.C300;                      // panels / sidebar glass
    public static Color Neutral400 => T.Neutral.C500;                      // cards / raised surface
    public static Color Neutral500 => Color.FromArgb(46, 178, 150, 255);   // border / separator
    public static Color Neutral600 => T.Neutral.C600;
    public static Color Neutral700 => T.Neutral.C700;

    // -- Brand ramps (the editor's ★400 slot maps to the ramp's bright C500 stop). --
    public static Color Purple100 => T.Primary.C100;
    public static Color Purple200 => T.Primary.C200;
    public static Color Purple300 => T.Primary.C300;
    public static Color Purple400 => T.Primary.C500;
    public static Color Purple500 => T.Primary.C600;
    public static Color Purple600 => T.Primary.C700;
    public static Color Purple700 => T.Primary.C700;

    public static Color Blue100 => T.Blue.C100;
    public static Color Blue200 => T.Blue.C200;
    public static Color Blue300 => T.Blue.C300;
    public static Color Blue400 => T.Blue.C500;
    public static Color Blue500 => T.Blue.C600;
    public static Color Blue600 => T.Blue.C700;
    public static Color Blue700 => T.Blue.C700;

    public static Color Red100 => T.Red.C100;
    public static Color Red200 => T.Red.C200;
    public static Color Red300 => T.Red.C300;
    public static Color Red400 => T.Red.C500;
    public static Color Red500 => T.Red.C600;
    public static Color Red600 => T.Red.C700;
    public static Color Red700 => T.Red.C700;

    public static Color Green100 => T.Green.C100;
    public static Color Green200 => T.Green.C200;
    public static Color Green300 => T.Green.C300;
    public static Color Green400 => T.Green.C500;
    public static Color Green500 => T.Green.C600;
    public static Color Green600 => T.Green.C700;
    public static Color Green700 => T.Green.C700;

    public static Color Amber100 => T.Amber.C100;
    public static Color Amber200 => T.Amber.C200;
    public static Color Amber300 => T.Amber.C300;
    public static Color Amber400 => T.Amber.C500;
    public static Color Amber500 => T.Amber.C600;
    public static Color Amber600 => T.Amber.C700;
    public static Color Amber700 => T.Amber.C700;

    // -- Ink: borders (100/200 editor-specific) + text hierarchy (300 hint -> 500 primary). --
    public static Color Ink100 => Color.FromArgb(40, 178, 150, 255);
    public static Color Ink200 => Color.FromArgb(72, 190, 150, 255);
    public static Color Ink300 => T.Ink.C300;
    public static Color Ink400 => T.Ink.C400;
    public static Color Ink500 => T.Ink.C500;
    public static Color Ink600 => T.Ink.C600;
    public static Color Ink700 => T.Ink.C700;
    /// <summary>Dim text tier (below <see cref="Ink300"/>), for de-emphasised metadata.</summary>
    public static Color InkDim => T.Ink.C200;
    /// <summary>Faintest text tier, for the most-muted captions / placeholders.</summary>
    public static Color InkFaint => T.Ink.C100;

    // -- Semantic surfaces / states. --
    /// <summary>Inset glass fill for toolbars, headers, tag pills and search fields within a panel.</summary>
    public static Color Glass => T.Glass;
    /// <summary>Menu / dropdown / popover surface (more opaque so it reads over anything).</summary>
    public static Color Popover => T.Popover;
    /// <summary>Soft hairline border / divider.</summary>
    public static Color BorderSoft => T.BorderSoft;
    /// <summary>Stronger border for focused / emphasised edges.</summary>
    public static Color BorderStrong => T.BorderStrong;
    /// <summary>Drop-shadow colour for popovers, dropdowns and modals.</summary>
    public static Color Shadow => T.Shadow;
    /// <summary>The bright accent (buttons, focus, active). Same as <see cref="Purple400"/>.</summary>
    public static Color Accent => T.Primary.C500;
    /// <summary>Brighter accent for hover.</summary>
    public static Color AccentBright => T.Primary.C600;
    /// <summary>Light accent for text / small highlights.</summary>
    public static Color AccentText => T.Primary.C700;

    public static Color Hover => WithAlpha(Accent, 31);
    public static Color Selected => WithAlpha(Accent, 41);

    public static Color WithAlpha(Color c, int a) => Color.FromArgb(a, c.R, c.G, c.B);
}
