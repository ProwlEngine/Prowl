using System;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.Editor.GUI;
using Prowl.Editor.Inspector;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;
using Prowl.Editor.Thumbnails;
using Prowl.Editor.Projects;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;

namespace Prowl.Editor.GUI.Panels;

[EditorWindow("General/Preferences")]
public class PreferencesPanel : DockPanel
{
    public override string Title => Loc.Get("panel.preferences");
    public override string Icon => EditorIcons.Sliders;

    private enum Tab { General, Theme, Shortcuts }
    private Tab _tab = Tab.General;
    private string _shortcutSearch = "";
    private string? _rebindingId;
    private string _themeCat = "presets";

    private static UnitValue ST => UnitValue.StretchOne;
    private static UnitValue STV => UnitValue.Stretch();
    // Theme-driven spacing (between elements) / padding (internal), so the whole panel scales with the theme.
    private static float SP => EditorTheme.Spacing;
    private static float PAD => EditorTheme.Padding;

    // label holds a localization key, resolved via Loc.Get where the tabs are rendered.
    private static readonly (string id, string label, string icon)[] Cats =
    {
        ("general",   "pref.general",   EditorIcons.Gear),
        ("theme",     "pref.theme",     EditorIcons.Palette),
        ("shortcuts", "pref.shortcuts", EditorIcons.Keyboard),
    };

    private static readonly (string id, string label, string icon)[] ThemeCats =
    {
        ("presets", "pref.cat_presets", EditorIcons.Swatchbook),
        ("colors",  "pref.cat_colors",  EditorIcons.Droplet),
        ("type",    "pref.cat_type",    EditorIcons.Font),
        ("spacing", "pref.cat_spacing", EditorIcons.TableCells),
        ("shape",   "pref.cat_shape",   EditorIcons.Cube),
        ("effects", "pref.cat_effects", EditorIcons.Bolt),
    };

    /// <summary>Switch this Preferences window to the Theme tab (used by the header quick-access button).</summary>
    public void ShowTheme() => _tab = Tab.Theme;

    private static string TabId(Tab t) => t switch { Tab.Theme => "theme", Tab.Shortcuts => "shortcuts", _ => "general" };
    private static Tab ParseTab(string id) => id switch { "theme" => Tab.Theme, "shortcuts" => Tab.Shortcuts, _ => Tab.General };

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var settings = EditorSettings.Instance;

        using (paper.Row("pref_root").Width(width).Height(height).Clip().Enter())
        {
            var cats = Cats.Select(c => (c.id, Loc.Get(c.label), c.icon)).ToArray();
            float side = EditorGUI.Sidebar(paper, "pref_side", cats, TabId(_tab), c => _tab = ParseTab(c));
            paper.Box("pref_vdiv").Width(1).Height(ST).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            float contentW = width - side - 1;

            // The Theme tab owns a fixed 3-column layout (rail | controls | live preview) with its own
            // inner scroll, so it bypasses the shared vertical scroll view.
            if (_tab == Tab.Theme)
            {
                DrawTheme(paper, font, settings, contentW, height);
            }
            else
            {
                Origami.ScrollView(paper, "pref_content", contentW, height).Body(() =>
                {
                    using (paper.Column("pref_content_col").Width(ST).Height(UnitValue.Auto).Padding(0, 0, 8, 12).Enter())
                    {
                        switch (_tab)
                        {
                            case Tab.Shortcuts: DrawShortcuts(paper, font, contentW); break;
                            default: DrawGeneral(paper, settings); break;
                        }
                    }
                });
            }
        }
    }

    // ================================================================
    //  General
    // ================================================================

    private void DrawGeneral(Paper paper, EditorSettings s)
    {
        EditorGUI.SectionHeader(paper, "pref_gen_hdr", Loc.Get("pref.general"), first: true);

        EditorGUI.SettingsRow(paper, "pref_lang", Loc.Get("pref.language"), () =>
            Origami.Dropdown(paper, "pref_lang_dd",
                LocaleHelper.GetIndex(Loc.CurrentLocale),
                LocaleHelper.SetLocale, LocaleHelper.Names).Show());

        EditorGUI.SettingsRow(paper, "pref_proj_path", Loc.Get("pref.projects_path"), () =>
            Origami.TextField(paper, "pref_proj_path_v", s.DefaultProjectsPath,
                v => { s.DefaultProjectsPath = v; s.Save(); }).Show());

        EditorGUI.SettingsToggle(paper, "pref_auto_save", Loc.Get("pref.auto_save"), SaveManager.AutoSaveEnabled,
            v => { SaveManager.AutoSaveEnabled = v; s.AutoSaveLayout = v; s.Save(); });

        EditorGUI.SettingsToggle(paper, "pref_reimport_focus", Loc.Get("pref.reimport_focus"), s.ReimportOnFocusOnly,
            v => { s.ReimportOnFocusOnly = v; s.Save(); });

        string[] thumbOptions = ["32", "64", "128"];
        int thumbIndex = s.ThumbnailSize switch { 64 => 1, 128 => 2, _ => 0 };
        EditorGUI.SettingsRow(paper, "pref_thumb_size", Loc.Get("pref.thumbnail_size"), () =>
            Origami.Dropdown(paper, "pref_thumb_size_v", thumbIndex,
                v =>
                {
                    s.ThumbnailSize = v switch { 1 => 64, 2 => 128, _ => 32 };
                    s.Save();
                    ThumbnailGenerator.DeleteAll();
                    ProjectPanel.ClearThumbnailCache();
                }, thumbOptions).Show());

        EditorGUI.SectionHeader(paper, "pref_gen_maint", Loc.Get("pref.maintenance"));
        EditorGUI.SettingsRow(paper, "pref_clear_cache", Loc.Get("pref.clear_cache"), () =>
            Origami.Button(paper, "pref_clear_cache_b", $"{EditorIcons.ArrowsRotate}  {Loc.Get("pref.clear_cache_btn")}",
                () =>
                {
                    EditorApplication.Instance?.ClearEditorCache();
                    Toasts.Info(Loc.Get("pref.clear_cache"), Loc.Get("pref.clear_cache_done"));
                }).Width(200).Show());
    }

    // ================================================================
    //  Theme (Colors + Sizing)
    // ================================================================

    private void DrawTheme(Paper paper, Scribe.FontFile font, EditorSettings s, float w, float h)
    {
        var theme = s.Theme;

        const float railW = 152f;
        const float previewW = 250f;
        const float footerH = 52f;

        float bodyH = MathF.Max(120f, h - footerH - 1f);
        // The live preview only appears when there's room for the controls too; on a narrow panel it
        // hides so the controls aren't crushed. Widen the Preferences window to reveal it.
        bool showPreview = w - railW - previewW - 2f >= 460f;
        float ctrlW = MathF.Max(240f, w - railW - 1f - (showPreview ? previewW + 1f : 0f));

        using (paper.Column("pref_theme_root").Width(w).Height(h).Enter())
        {
            using (paper.Row("pref_theme_body").Width(ST).Height(bodyH).Enter())
            {
                DrawThemeRail(paper, font, railW, bodyH);
                paper.Box("pref_theme_rdiv").Width(1).Height(ST).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

                if (_themeCat == "presets")
                {
                    // Presets are short, so they get no scroll and are centered vertically in the area.
                    using (paper.Column("pref_theme_ctrls_pr").Width(ctrlW).Height(bodyH).Padding(22, 22, 0, 0).Enter())
                    using (paper.Column("pref_theme_pr_center").Width(ST).Height(UnitValue.Auto)
                        .Margin(0, 0, STV, STV).Enter())
                        DrawThemePresets(paper, font, s, theme);
                }
                else
                {
                    Origami.ScrollView(paper, "pref_theme_ctrls", ctrlW, bodyH).Body(() =>
                    {
                        using (paper.Column("pref_theme_ctrl_col").Width(ST).Height(UnitValue.Auto)
                            .Padding(PAD * 3, PAD * 3, PAD * 2, PAD * 3).ColBetween(SP).Enter())
                        {
                            switch (_themeCat)
                            {
                                case "colors":  DrawThemeColors(paper, s, theme); break;
                                case "type":    DrawThemeType(paper, s, theme); break;
                                case "spacing": DrawThemeSpacing(paper, s, theme); break;
                                case "shape":   DrawThemeShape(paper, s, theme); break;
                                case "effects": DrawThemeEffects(paper, s, theme); break;
                            }
                        }
                    });
                }

                if (showPreview)
                {
                    paper.Box("pref_theme_pdiv").Width(1).Height(ST).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
                    DrawThemePreview(paper, font, previewW);
                }
            }

            paper.Box("pref_theme_fdiv").Width(ST).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
            DrawThemeFooter(paper, font, s, theme, footerH);
        }
    }

    // Category rail (icon + label) with a footnote card directly under the buttons, centered vertically.
    private void DrawThemeRail(Paper paper, Scribe.FontFile font, float w, float h)
    {
        using (paper.Column("pref_theme_rail").Width(w).Height(h).BackgroundColor(Color.FromArgb(36, 0, 0, 0)).Enter())
        using (paper.Column("pref_theme_rail_c").Width(ST).Height(UnitValue.Auto).Margin(0, 0, STV, STV)
            .Padding(8, 8, 10, 10).ColBetween(2).BackgroundColor(Color.FromArgb(36, 0, 0, 0)).Enter())
        {
            foreach (var (id, label, icon) in ThemeCats)
            {
                bool on = _themeCat == id;
                using (paper.Row($"pref_thr_{id}").Width(ST).Height(34).Rounded(8).Padding(11, 11, 0, 0)
                    .BackgroundColor(on ? EditorTheme.Selected : Color.Transparent)
                    .Hovered.BackgroundColor(on ? EditorTheme.Selected : EditorTheme.Hover).End()
                    .OnClick(id, (c, _) => _themeCat = c).Enter())
                {
                    paper.Box($"pref_thr_{id}_i").Width(16).Height(ST).Margin(0, 9, ST, ST).IsNotInteractable()
                        .Text(icon, font).TextColor(on ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    paper.Box($"pref_thr_{id}_l").Width(ST).Height(ST).Margin(0, 0, ST, ST).IsNotInteractable()
                        .Text(Loc.Get(label), font).TextColor(on ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();
                }
            }

            using (paper.Row("pref_thr_note").Width(ST).Height(UnitValue.Auto).Margin(0, 0, 10, 0)
                .Rounded(9).Padding(10, 10, 9, 9)
                .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).IsNotInteractable().Enter())
                paper.Box("pref_thr_note_t").Width(ST).Height(UnitValue.Auto).IsNotInteractable()
                    .Text(Loc.Get("pref.theme_note"), font)
                    .Wrap(Scribe.TextWrapMode.Wrap)
                    .TextColor(EditorTheme.InkDim).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
        }
    }

    // Footer shown on every category: Reset (left), Import / Export / Apply (right).
    private void DrawThemeFooter(Paper paper, Scribe.FontFile font, EditorSettings s, EditorThemeData theme, float h)
    {
        var semi = EditorTheme.FontSemiBold ?? font;
        using (paper.Row("pref_theme_footer").Width(ST).Height(h).Padding(PAD * 2, PAD * 2, 0, 0).RowBetween(SP * 2).Enter())
        {
            ThChip(paper, "pref_ft_reset", $"{EditorIcons.RotateLeft}  {Loc.Get("pref.reset_default")}", () => s.ResetTheme());

            paper.Box("pref_ft_sp").Width(ST).Height(1).IsNotInteractable();

            ThChip(paper, "pref_ft_import", $"{EditorIcons.Upload}  {Loc.Get("pref.import")}", () =>
                EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
                {
                    if (path == null) return;
                    var imported = EditorThemeData.ImportFromFile(path);
                    if (imported != null) { s.Theme = imported; s.ApplyTheme(); s.Save(); Toasts.Info(Loc.Get("pref.toast_theme"), Loc.Get("pref.toast_imported", new { name = imported.Name })); }
                }, filters: new[] { "*.prowltheme" }, filterLabels: new[] { Loc.Get("pref.theme_filter") }));

            ThChip(paper, "pref_ft_export", $"{EditorIcons.Download}  {Loc.Get("pref.export")}", () =>
                EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
                {
                    if (path == null) return;
                    if (!path.EndsWith(".prowltheme")) path += ".prowltheme";
                    theme.ExportToFile(path);
                    Toasts.Info(Loc.Get("pref.toast_theme"), Loc.Get("pref.toast_exported", new { file = System.IO.Path.GetFileName(path) }));
                }, filters: new[] { "*.prowltheme" }, filterLabels: new[] { Loc.Get("pref.theme_filter") }), leftGap: 8f);

            paper.Box("pref_ft_apply").Width(UnitValue.Auto).Height(30).Margin(8, 0, ST, ST).Rounded(8).Padding(16, 16, 0, 0)
                .BackgroundLinearGradient(0, 0, 1, 1, EditorTheme.Accent, EditorTheme.AccentBright)
                .Hovered.Glow(0, 2, 12, -2, Color.FromArgb(150, EditorTheme.Accent)).End()
                .Text($"{EditorIcons.Check}  Apply", semi).TextColor(Color.White).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => { s.ApplyTheme(); s.Save(); });
        }
    }

    // ================================================================
    //  Theme editor faithful port of the designer's ThemeEditor2
    //  (th-sec / th-row / th-swrow / th-preset controls)
    // ================================================================

    private readonly record struct ThemePreset(string Name, string Accent, string Accent2, string Bg, string Panel, string Text);

    // "Indigo" is the shipped default (Origami's defaults match it), so selecting it equals a full reset.
    private static readonly ThemePreset[] _presets =
    {
        new("Indigo",   "#6366F1", "#8B5CF6", "#0C0C1A", "#181830", "#EAEAF7"),
        new("Nebula",   "#A855F7", "#60A5FA", "#0F0C18", "#262036", "#F0EEF7"),
        new("Ember",    "#F97316", "#FB7185", "#160F0C", "#2A1E16", "#F7EFE8"),
        new("Verdant",  "#4ADE80", "#22C55E", "#0B1410", "#182A20", "#E8F7EF"),
        new("Abyss",    "#60A5FA", "#06B6D4", "#0A0F1A", "#182233", "#E8F0F7"),
        new("Bloom",    "#EC4899", "#A855F7", "#170C14", "#2A1826", "#F7E8F2"),
        new("Graphite", "#94A3B8", "#64748B", "#0D0F12", "#20242C", "#ECEEF2"),
        new("Solar",    "#FBBF24", "#F59E0B", "#161009", "#221A0C", "#F7F1E4"),
        new("Cyan",     "#06B6D4", "#14B8A6", "#0A1416", "#122528", "#E4F5F7"),
    };

    // Palettes always include the ramp's real default so the current colour reads as selected.
    private static readonly string[] _accentPalette =
        ["#A855F7", "#60A5FA", "#4ADE80", "#FBBF24", "#FB7185", "#8B5CF6", "#6366F1", "#06B6D4", "#F97316", "#EC4899"];
    private static readonly string[] _bgPalette   = ["#262036", "#1A1626", "#0F0C18", "#0A0F1A", "#0B1410", "#160F0C"];
    private static readonly string[] _textPalette = ["#F0EEF7", "#FFFFFF", "#E8F0F7", "#ECEEF2", "#F7EFE8", "#D8D4E8"];

    private static Color Hx(string hex) => System.Drawing.ColorTranslator.FromHtml(hex);

    private void ApplyPreset(EditorSettings s, ThemePreset p)
    {
        var t = s.Theme;
        // Presets only theme the brand/surface/text ramps; the status ramps return to defaults.
        t.Purple.Primary = p.Accent;   t.Purple.OverrideAll = false;
        t.Blue.Primary = p.Accent2;    t.Blue.OverrideAll = false;
        t.Neutral.Primary = p.Panel;   t.Neutral.OverrideAll = false;
        t.Ink.Primary = p.Text;        t.Ink.OverrideAll = false;
        t.Red.Primary = "#FB7185";     t.Red.OverrideAll = false;
        t.Green.Primary = "#4ADE80";   t.Green.OverrideAll = false;
        t.Amber.Primary = "#FBBF24";   t.Amber.OverrideAll = false;
        t.Name = p.Name;
        s.ApplyTheme(); s.Save();
    }

    // th-sec: uppercase accent section label. Extra top gap separates it from the previous group.
    private void ThSec(Paper paper, string id, string text, bool first = false)
    {
        var semi = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
        paper.Box(id).Width(ST).Height(18).Margin(0, 0, first ? 0 : SP * 2, SP).IsNotInteractable()
            .Text(text.ToUpperInvariant(), semi).TextColor(EditorTheme.AccentText)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
    }

    // th-row: 108px bright label + control.
    private void ThRow(Paper paper, string id, string label, Action drawControl, float minH = 34f, float botMargin = 0f)
    {
        var font = EditorTheme.DefaultFont;
        using (paper.Row(id).Width(ST).Height(UnitValue.Auto).MinHeight(minH).Margin(0, 0, 0, botMargin).RowBetween(PAD * 2).Enter())
        {
            paper.Box($"{id}_l").Width(108).Height(minH).Margin(0, 0, ST, ST).IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft).TextTruncate();
            // No MinHeight here: the column sizes to the widget so its stretch top/bottom margins center
            // it in the row. With a forced MinHeight the widget would top-align while the label stays
            // centered, leaving them visually misaligned.
            using (paper.Column($"{id}_c").Width(ST).Height(UnitValue.Auto).Margin(0, 0, ST, ST).Enter())
                drawControl();
        }
    }

    private void ThSlider(Paper paper, EditorSettings s, string id, string label, float value,
        float min, float max, string fmt, Action<float> set, bool applyLive = true)
    {
        ThRow(paper, id, label, () =>
            Origami.Slider(paper, $"{id}_v", value, v =>
            {
                set(MathF.Round(v, 2));
                if (applyLive) s.ApplyTheme();
                s.Save();
            }, min, max).Format(fmt).Show());
    }

    // th-swrow: a colorbox (opens the picker) + a quick-pick palette. Small palettes (Background/Text)
    // sit inline on one row; larger ones drop the palette to a second row (like the prototype's wrap).
    // Explicit swatch margins since RowBetween doesn't gap these tight rows reliably.
    private void ThSwatchRow(Paper paper, EditorSettings s, string id, string label, ColorRamp ramp, string[] palette)
    {
        bool inline = palette.Length <= 6;

        void ColorBox() =>
            Origami.ColorField(paper, $"{id}_cf", HexToVColor(ramp.Primary), v =>
            { ramp.Primary = VColorToHex(v); ramp.OverrideAll = false; s.ApplyTheme(); s.Save(); }).Width(130f).Show();

        // Each swatch: a 20px colour fill with, when selected, a 2px accent ring drawn ~3px OUTSIDE
        // it (a visible gap), so it reads as a ring even when the swatch colour equals the accent.
        // Drawn via Quill so the ring can extend past the fill.
        void Swatch(string hex)
        {
            var col = Hx(hex);
            bool on = string.Equals(ramp.Primary, hex, StringComparison.OrdinalIgnoreCase);
            paper.Box($"{id}_p_{hex}").Width(28).Height(28).Margin(0, SP * 2, ST, ST)
                .OnClick(hex, (h, _) => { ramp.Primary = h; ramp.OverrideAll = false; s.ApplyTheme(); s.Save(); })
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    float cx = (float)(r.Min.X + r.Size.X / 2), cy = (float)(r.Min.Y + r.Size.Y / 2);
                    const float sw = 20f;
                    canvas.RoundedRectFilled(cx - sw / 2, cy - sw / 2, sw, sw, 6f,
                        Prowl.Vector.Color32.FromArgb(255, col.R, col.G, col.B));
                    canvas.RoundedRect(cx - sw / 2, cy - sw / 2, sw, sw, 6f);
                    canvas.SetStrokeColor(Prowl.Vector.Color32.FromArgb(30, 255, 255, 255));
                    canvas.SetStrokeWidth(1f);
                    canvas.Stroke();
                    if (on)
                    {
                        var acc = EditorTheme.Accent;
                        const float ring = 27f;
                        canvas.RoundedRect(cx - ring / 2, cy - ring / 2, ring, ring, 8f);
                        canvas.SetStrokeColor(Prowl.Vector.Color32.FromArgb(255, acc.R, acc.G, acc.B));
                        canvas.SetStrokeWidth(2f);
                        canvas.Stroke();
                    }
                }));
        }

        void Palette()
        {
            foreach (var hex in palette)
                Swatch(hex);
        }

        ThRow(paper, id, label, () =>
        {
            if (inline)
                using (paper.Row($"{id}_r").Width(ST).Height(UnitValue.Auto).MinHeight(30).Enter())
                {
                    using (paper.Box($"{id}_cb").Width(130).Height(28).Margin(0, PAD * 2, ST, ST).Enter())
                        ColorBox();
                    using (paper.Row($"{id}_pal").Width(ST).Height(28).Margin(0, 0, ST, ST).Enter())
                        Palette();
                }
            else
                using (paper.Column($"{id}_r").Width(ST).Height(UnitValue.Auto).Enter())
                {
                    using (paper.Box($"{id}_cb").Width(130).Height(28).Margin(0, 0, 0, SP * 2).Enter())
                        ColorBox();
                    using (paper.Row($"{id}_pal").Width(ST).Height(28).Enter())
                        Palette();
                }
        }, minH: inline ? 36f : 68f);
    }

    // .chip: glass pill button. leftGap adds explicit horizontal spacing from the previous chip.
    private void ThChip(Paper paper, string id, string label, Action onClick, float leftGap = 0f)
    {
        var font = EditorTheme.DefaultFont;
        paper.Box(id).Width(UnitValue.Auto).Height(28).Margin(leftGap, 0, ST, ST).Rounded(8).Padding(11, 11, 0, 0)
            .BackgroundColor(EditorTheme.Neutral400).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BorderColor(EditorTheme.BorderStrong).End()
            .Text(label, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => onClick());
    }

    // ---- Presets ----
    private void DrawThemePresets(Paper paper, Scribe.FontFile font, EditorSettings s, EditorThemeData theme)
    {
        ThSec(paper, "pref_pr_hdr", Loc.Get("pref.builtin_themes"), first: true);

        const int cols = 3;
        for (int r = 0; r * cols < _presets.Length; r++)
        {
            using (paper.Row($"pref_pr_row{r}").Width(ST).Height(UnitValue.Auto).Margin(0, 0, 0, SP * 2).RowBetween(SP * 2).Enter())
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (i >= _presets.Length) { paper.Box($"pref_pr_e{r}{c}").Width(ST).Height(1).IsNotInteractable(); continue; }
                    var p = _presets[i];
                    bool on = string.Equals(theme.Name, p.Name, StringComparison.OrdinalIgnoreCase);

                    var card = paper.Column($"pref_pr_c{i}").Width(ST).Height(UnitValue.Auto).Rounded(10)
                        .Padding(PAD * 1.5f, PAD * 1.5f, PAD * 1.5f, PAD * 1.5f).ColBetween(SP * 2)
                        .BackgroundColor(on ? EditorTheme.Selected : EditorTheme.Glass)
                        .BorderColor(on ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(on ? 2 : 1)
                        .Hovered.BorderColor(on ? EditorTheme.Accent : EditorTheme.BorderStrong).End()
                        .OnClick(i, (idx, _) => ApplyPreset(s, _presets[idx]));
                    if (on) card.Glow(0, 8, 22, -10, Color.FromArgb(130, EditorTheme.Accent));

                    using (card.Enter())
                    {
                        using (paper.Row($"pref_pr_c{i}_sw").Width(ST).Height(38).Rounded(7).Padding(6, 6, 6, 6).RowBetween(4)
                            .BackgroundColor(Hx(p.Bg)).IsNotInteractable().Enter())
                        {
                            paper.Box($"pref_pr_c{i}_a").Width(ST).Height(ST).Rounded(4)
                                .BackgroundLinearGradient(0, 0, 1, 1, Hx(p.Accent), Hx(p.Accent2)).IsNotInteractable();
                            paper.Box($"pref_pr_c{i}_b").Width(ST).Height(ST).Rounded(4).BackgroundColor(Hx(p.Accent2)).IsNotInteractable();
                            paper.Box($"pref_pr_c{i}_p").Width(ST).Height(ST).Rounded(4).BackgroundColor(Hx(p.Panel))
                                .BorderColor(EditorTheme.WithAlpha(Hx(p.Text), 38)).BorderWidth(1).IsNotInteractable();
                        }
                        using (paper.Row($"pref_pr_c{i}_nm").Width(ST).Height(15).Enter())
                        {
                            paper.Box($"pref_pr_c{i}_nt").Width(ST).Height(ST).IsNotInteractable()
                                .Text(p.Name, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink500)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();
                            if (on)
                                paper.Box($"pref_pr_c{i}_ck").Width(12).Height(ST).IsNotInteractable()
                                    .Text(EditorIcons.Check, font).TextColor(EditorTheme.AccentText)
                                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
                        }
                    }
                }
            }
        }
    }

    // ---- Colors ----
    private void DrawThemeColors(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        ThSec(paper, "pref_cl_accent", Loc.Get("pref.accent"), first: true);
        ThSwatchRow(paper, s, "accent_primary", Loc.Get("pref.primary"), theme.Purple, _accentPalette);
        ThDiv(paper, "pref_cl_d0");
        ThSwatchRow(paper, s, "accent_info", Loc.Get("pref.info"), theme.Blue, _accentPalette);

        ThSec(paper, "pref_cl_surf", Loc.Get("pref.surfaces"));
        ThSwatchRow(paper, s, "surf_neutral", Loc.Get("pref.neutral"), theme.Neutral, _bgPalette);
        ThDiv(paper, "pref_cl_d1");
        ThSwatchRow(paper, s, "surf_ink", Loc.Get("pref.ink"), theme.Ink, _textPalette);

        ThSec(paper, "pref_cl_sem", Loc.Get("pref.semantic"));
        ThSwatchRow(paper, s, "sem_success", Loc.Get("pref.success"), theme.Green, _accentPalette);
        ThDiv(paper, "pref_cl_d2");
        ThSwatchRow(paper, s, "sem_warning", Loc.Get("pref.warning"), theme.Amber, _accentPalette);
        ThDiv(paper, "pref_cl_d3");
        ThSwatchRow(paper, s, "sem_danger", Loc.Get("pref.danger"), theme.Red, _accentPalette);
    }

    // Thin divider between color rows.
    private void ThDiv(Paper paper, string id)
        => paper.Box(id).Width(ST).Height(1).Margin(0, 0, 0, SP).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();


    // ---- Typography ----
    private void DrawThemeType(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        ThSec(paper, "pref_ty_fonts", Loc.Get("pref.fonts"), first: true);
        ThRow(paper, "pref_ty_ui", Loc.Get("pref.ui_font"), () =>
            Origami.TextField(paper, "pref_ty_ui_v", theme.DefaultFontName, v => { theme.DefaultFontName = v; s.ApplyTheme(); s.Save(); }).Show());
        ThRow(paper, "pref_ty_bold", Loc.Get("pref.bold_font"), () =>
            Origami.TextField(paper, "pref_ty_bold_v", theme.DefaultBoldFontName, v => { theme.DefaultBoldFontName = v; s.ApplyTheme(); s.Save(); }).Show());

        ThSec(paper, "pref_ty_size", Loc.Get("pref.sizing"));
        ThSlider(paper, s, "pref_ty_base", Loc.Get("pref.base_size"), theme.FontSize, 8, 32, "F2", v => theme.FontSize = v);
    }

    // ---- Spacing & Density ----
    private void DrawThemeSpacing(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        ThSec(paper, "pref_sp_density", Loc.Get("pref.density"), first: true);

        int densityIdx =
            (theme.Spacing == 2f && theme.Padding == 4f && theme.RowHeight == 20f) ? 0 :
            (theme.Spacing == 6f && theme.Padding == 9f && theme.RowHeight == 28f) ? 2 :
            (theme.Spacing == 4f && theme.Padding == 6f && theme.RowHeight == 24f) ? 1 : -1;

        ThRow(paper, "pref_sp_seg", Loc.Get("pref.preset"), () =>
            Origami.ButtonGroup(paper, "pref_sp_bg", densityIdx, idx =>
            {
                (float sp, float pad, float rh) = idx switch { 0 => (2f, 4f, 20f), 2 => (6f, 9f, 28f), _ => (4f, 6f, 24f) };
                theme.Spacing = sp; theme.Padding = pad; theme.RowHeight = rh;
                s.ApplyTheme(); s.Save();
            }).Segmented().Item(Loc.Get("pref.compact")).Item(Loc.Get("pref.cozy")).Item(Loc.Get("pref.spacious")).Show());

        ThSec(paper, "pref_sp_metrics", Loc.Get("pref.metrics"));
        ThSlider(paper, s, "pref_sp_spacing", Loc.Get("pref.spacing"), theme.Spacing, 0, 12, "F2", v => theme.Spacing = v);
        ThSlider(paper, s, "pref_sp_padding", Loc.Get("pref.padding"), theme.Padding, 0, 16, "F2", v => theme.Padding = v);
        ThSlider(paper, s, "pref_sp_row", Loc.Get("pref.row_height"), theme.RowHeight, 16, 40, "F2", v => theme.RowHeight = v);
        ThSlider(paper, s, "pref_sp_menu", Loc.Get("pref.menu_bar_height"), theme.MenuBarHeight, 18, 48, "F2", v => theme.MenuBarHeight = v);
        ThSlider(paper, s, "pref_sp_status", Loc.Get("pref.status_bar_height"), theme.StatusBarHeight, 16, 40, "F2", v => theme.StatusBarHeight = v);
        ThSlider(paper, s, "pref_sp_label", Loc.Get("pref.label_width"), theme.LabelWidth, 60, 240, "F2", v => theme.LabelWidth = v);
        ThSlider(paper, s, "pref_sp_dock", Loc.Get("pref.dock_spacing"), theme.DockSpacing, 0, 24, "F2", v => theme.DockSpacing = v);
        ThSlider(paper, s, "pref_sp_tabh", Loc.Get("pref.tab_bar_height"), theme.TabBarHeight, 18, 40, "F2", v => theme.TabBarHeight = v);
        ThSlider(paper, s, "pref_sp_tabp", Loc.Get("pref.tab_padding"), theme.TabPadding, 4, 24, "F2", v => theme.TabPadding = v);
        ThSlider(paper, s, "pref_sp_scale", Loc.Get("pref.user_scale"), theme.UserScale, 0.5f, 2, "F2", v => theme.UserScale = v, applyLive: false);
    }

    // ---- Corners & Borders ----
    private void DrawThemeShape(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        ThSec(paper, "pref_sh_corner", Loc.Get("pref.corner_radius"), first: true);
        ThSlider(paper, s, "pref_sh_round", Loc.Get("pref.roundness"), theme.Roundness, 0, 20, "F2", v => theme.Roundness = v);
    }

    // th-row colour field editing a hex-string theme value.
    private void ThColorField(Paper paper, EditorSettings s, string id, string label, Func<string> get, Action<string> set)
    {
        ThRow(paper, id, label, () =>
            Origami.ColorField(paper, $"{id}_v", HexToVColor(get()), v =>
            { set(VColorToHex(v)); s.ApplyTheme(); s.Save(); }).Width(130f).Show());
    }

    // th-row toggle: label + a pill switch pinned right.
    private void ThToggle(Paper paper, EditorSettings s, string id, string label, bool value, Action<bool> set)
    {
        ThRow(paper, id, label, () =>
        {
            using (paper.Row($"{id}_tw").Width(UnitValue.Auto).Height(Origami.Current.Metrics.RowHeight)
                .Margin(0, 0, ST, ST).Enter())
                Origami.Switch(paper, $"{id}_sw", value, v => { set(v); s.ApplyTheme(); s.Save(); }).NoLabel().Show();
        });
    }

    // ---- Effects ----
    private void DrawThemeEffects(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        ThSec(paper, "pref_fx_depth", Loc.Get("pref.depth"), first: true);
        ThToggle(paper, s, "pref_fx_glass", Loc.Get("pref.glass_blur"), theme.GlassBlur, v => theme.GlassBlur = v);
        if (theme.GlassBlur)
            ThSlider(paper, s, "pref_fx_blur", Loc.Get("pref.blur_amount"), theme.BlurAmount, 0, 40, "F2", v => theme.BlurAmount = v);
        ThToggle(paper, s, "pref_fx_shadow", Loc.Get("pref.drop_shadows"), theme.DropShadows, v => theme.DropShadows = v);
        ThToggle(paper, s, "pref_fx_glow", Loc.Get("pref.accent_glow"), theme.AccentGlow, v => theme.AccentGlow = v);

        // Per-layer nebula controls (apply to the animated nebula and the static-Nebula style).
        void NebulaLayers()
        {
            ThToggle(paper, s, "pref_fx_grad", Loc.Get("pref.nebula_gradients"), theme.BgShowGradients, v => theme.BgShowGradients = v);
            ThToggle(paper, s, "pref_fx_stars", Loc.Get("pref.stars"), theme.BgShowStars, v => theme.BgShowStars = v);
            ThToggle(paper, s, "pref_fx_comets", Loc.Get("pref.comets"), theme.BgShowComets, v => theme.BgShowComets = v);
            ThColorField(paper, s, "pref_fx_void", Loc.Get("pref.void_color"), () => theme.BackgroundVoidColor, v => theme.BackgroundVoidColor = v);
        }

        ThSec(paper, "pref_fx_bg", Loc.Get("pref.background"));
        ThToggle(paper, s, "pref_fx_anim", Loc.Get("pref.animated_bg"), theme.AnimatedBackground, v => theme.AnimatedBackground = v);
        if (theme.AnimatedBackground)
        {
            ThSlider(paper, s, "pref_fx_speed", Loc.Get("pref.speed"), theme.BackgroundSpeed, 0, 3, "F2", v => theme.BackgroundSpeed = v);
            NebulaLayers();
        }
        else
        {
            ThRow(paper, "pref_fx_style", Loc.Get("pref.style"), () =>
                Origami.EnumDropdown(paper, "pref_fx_style_v", theme.BackgroundStyle,
                    v => { theme.BackgroundStyle = v; s.ApplyTheme(); s.Save(); }).Show());

            if (theme.BackgroundStyle == EditorBackgroundStyle.Gradient)
            {
                ThColorField(paper, s, "pref_fx_ca", Loc.Get("env.top_color"), () => theme.BackgroundColorA, v => theme.BackgroundColorA = v);
                ThColorField(paper, s, "pref_fx_cb", Loc.Get("env.bottom_color"), () => theme.BackgroundColorB, v => theme.BackgroundColorB = v);
            }
            else if (theme.BackgroundStyle == EditorBackgroundStyle.Color)
            {
                ThColorField(paper, s, "pref_fx_ca", Loc.Get("env.color"), () => theme.BackgroundColorA, v => theme.BackgroundColorA = v);
            }
        }

        ThSec(paper, "pref_fx_render", Loc.Get("pref.rendering"));
        ThToggle(paper, s, "pref_fx_aa", Loc.Get("pref.anti_aliasing"), theme.AntiAliasing, v => theme.AntiAliasing = v);
    }

    // ---- Live preview (mini editor chrome drawn with live EditorTheme tokens) ----
    private void DrawThemePreview(Paper paper, Scribe.FontFile font, float w)
    {
        float radius = EditorTheme.Roundness;
        Color surface = EditorTheme.Neutral300;
        const float cardH = 208f;

        using (paper.Column("pref_pv").Width(w).Height(ST).Padding(PAD * 2, PAD * 2, PAD * 2, PAD * 2)
            .BackgroundColor(Color.FromArgb(36, 0, 0, 0)).Enter())
        using (paper.Column("pref_pv_center").Width(ST).Height(UnitValue.Auto).Margin(0, 0, STV, STV).ColBetween(SP * 2).Enter())
        {
            using (paper.Column("pref_pv_card").Width(ST).Height(cardH).Rounded(radius + 2).Clip()
                .DropShadow(0, 10, 26, -6, Color.FromArgb(150, 0, 0, 0))
                .BackgroundColor(EditorTheme.Neutral200).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
            {
                // Titlebar
                using (paper.Row("pref_pv_title").Width(ST).Height(30).Padding(PAD, PAD, 0, 0).RowBetween(SP * 1.5f)
                    .BackgroundColor(surface).Enter())
                {
                    paper.Box("pref_pv_logo").Width(13).Height(13).Margin(0, 0, ST, ST).Rounded(4)
                        .BackgroundColor(EditorTheme.Accent).IsNotInteractable();
                    paper.Box("pref_pv_name").Width(ST).Height(ST).Margin(SP, 0, 0, 0).IsNotInteractable()
                        .Text("Prowl", font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                    for (int i = 0; i < 3; i++)
                        paper.Box($"pref_pv_wdot{i}").Width(7).Height(7).Margin(0, 0, ST, ST).Rounded(4)
                            .BackgroundColor(i == 2 ? EditorTheme.Red400 : EditorTheme.Ink200).IsNotInteractable();
                }
                paper.Box("pref_pv_td").Width(ST).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

                // Toolbar
                using (paper.Row("pref_pv_tool").Width(ST).Height(28).Padding(PAD, PAD, 0, 0).RowBetween(PAD * 2)
                    .BackgroundColor(EditorTheme.Neutral200).Enter())
                {
                    paper.Box("pref_pv_file").Width(UnitValue.Auto).Height(ST).IsNotInteractable()
                        .Text(Loc.Get("menu.file"), font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                    paper.Box("pref_pv_edit").Width(ST).Height(ST).IsNotInteractable()
                        .Text(Loc.Get("menu.edit"), font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                    paper.Box("pref_pv_play").Width(20).Height(18).Margin(0, 0, ST, ST).Rounded(EditorTheme.Roundness * 0.5f)
                        .Glow(0, 0, 12, 0, Color.FromArgb(170, EditorTheme.Accent))
                        .BackgroundColor(EditorTheme.Accent).IsNotInteractable()
                        .Text(EditorIcons.Play, font).TextColor(Color.White).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleCenter);
                }

                // Body: mini Hierarchy + Inspector
                using (paper.Row("pref_pv_body").Width(ST).Height(ST).Padding(EditorTheme.DockPadding, EditorTheme.DockPadding, EditorTheme.DockPadding, EditorTheme.DockPadding).RowBetween(EditorTheme.DockPadding).Enter())
                {
                    // Hierarchy
                    using (paper.Column("pref_pv_hier").Width(ST).Height(ST).Rounded(radius).Clip()
                        .BackgroundColor(surface).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                        .Padding(PAD, PAD, PAD, PAD).ColBetween(SP).Enter())
                    {
                        paper.Box("pref_pv_hh").Width(ST).Height(16).IsNotInteractable()
                            .Text(Loc.Get("panel.hierarchy"), font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                            .Alignment(TextAlignment.MiddleLeft);
                        string[] items = ["Planet", "Character", "Camera"];
                        for (int i = 0; i < items.Length; i++)
                        {
                            bool sel = i == 0;
                            using (paper.Row($"pref_pv_hi{i}").Width(ST).Height(18).Rounded(radius * 0.5f)
                                .Padding(SP, SP, 0, 0).RowBetween(SP)
                                .BackgroundColor(sel ? EditorTheme.Selected : Color.Transparent).Enter())
                            {
                                paper.Box($"pref_pv_hd{i}").Width(7).Height(7).Margin(0, 0, ST, ST).Rounded(2)
                                    .BackgroundColor(sel ? EditorTheme.Accent : EditorTheme.Blue400).IsNotInteractable();
                                paper.Box($"pref_pv_hn{i}").Width(ST).Height(ST).Margin(SP * 0.5f, 0, 0, 0).IsNotInteractable()
                                    .Text(items[i], font).TextColor(sel ? EditorTheme.Ink500 : EditorTheme.Ink400)
                                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                            }
                        }
                    }

                    // Inspector
                    using (paper.Column("pref_pv_insp").Width(ST).Height(ST).Rounded(radius).Clip()
                        .BackgroundColor(surface).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                        .Padding(PAD, PAD, PAD, PAD).ColBetween(SP * 1.5f).Enter())
                    {
                        paper.Box("pref_pv_ih").Width(ST).Height(16).IsNotInteractable()
                            .Text(Loc.Get("panel.inspector"), font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                            .Alignment(TextAlignment.MiddleLeft);

                        PreviewField(paper, font, "pref_pv_f0", "Mass", "72.0");
                        PreviewField(paper, font, "pref_pv_f1", "Speed", "6.5");

                        // Mini slider (60% accent fill)
                        using (paper.Row("pref_pv_sld").Width(ST).Height(6).Rounded(3)
                            .BackgroundColor(EditorTheme.Neutral400).Enter())
                            paper.Box("pref_pv_sld_f").Width(UnitValue.Percentage(60f)).Height(ST).Rounded(3)
                                .BackgroundColor(EditorTheme.Accent).IsNotInteractable();

                        using (paper.Row("pref_pv_btns").Width(ST).Height(20).RowBetween(SP).Enter())
                        {
                            paper.Box("pref_pv_apply").Width(ST).Height(ST).Rounded(radius * 0.5f)
                                .Glow(0, 0, 12, 0, Color.FromArgb(150, EditorTheme.Accent))
                                .BackgroundColor(EditorTheme.Accent).IsNotInteractable()
                                .Text(Loc.Get("inspector.apply"), font).TextColor(Color.White).FontSize(EditorTheme.FontSizeSmall)
                                .Alignment(TextAlignment.MiddleCenter);
                            paper.Box("pref_pv_rst").Width(ST).Height(ST).Rounded(radius * 0.5f)
                                .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                                .IsNotInteractable()
                                .Text(Loc.Get("inspector.reset"), font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                                .Alignment(TextAlignment.MiddleCenter);
                        }
                    }
                }
            }
        }
    }

    private static void PreviewField(Paper paper, Scribe.FontFile font, string id, string label, string value)
    {
        using (paper.Row(id).Width(ST).Height(16).Enter())
        {
            paper.Box($"{id}_l").Width(ST).Height(ST).IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);
            paper.Box($"{id}_v").Width(UnitValue.Auto).Height(ST).IsNotInteractable()
                .Text(value, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleRight);
        }
    }

    private void DrawRamp(Paper paper, EditorSettings s, string name, ColorRamp ramp, string[] stopNames)
    {
        Origami.Foldout(paper, $"pref_ramp_{name}", name).Body(() =>
        {
            var primaryColor = HexToVColor(ramp.Primary);
            EditorGUI.SettingsRow(paper, $"pref_ramp_{name}_primary", $"{name} {Loc.Get("pref.primary")}", () =>
                Origami.ColorField(paper, $"pref_ramp_{name}_primary_cf", primaryColor, v =>
                {
                    ramp.Primary = VColorToHex(v);
                    s.ApplyTheme();
                }).Show());

            EditorGUI.SettingsToggle(paper, $"pref_ramp_{name}_override", Loc.Get("pref.override_stops"), ramp.OverrideAll, v =>
            {
                ramp.OverrideAll = v;
                if (v && ramp.Overrides != null)
                {
                    for (int i = 0; i < ramp.StopCount; i++)
                        ramp.Overrides[i] = ColorRamp.ColorToHex(ramp.GetStop(i));
                }
                s.ApplyTheme();
            });

            if (ramp.OverrideAll && ramp.Overrides != null)
            {
                for (int i = 0; i < ramp.StopCount; i++)
                {
                    int idx = i;
                    string label = i < stopNames.Length ? stopNames[i] : $"Stop {i}";
                    var stopColor = HexToVColor(ramp.Overrides[i]);
                    EditorGUI.SettingsRow(paper, $"pref_ramp_{name}_{i}", label, () =>
                        Origami.ColorField(paper, $"pref_ramp_{name}_{idx}_cf", stopColor, v =>
                        {
                            ramp.Overrides![idx] = VColorToHex(v);
                            s.ApplyTheme();
                        }).Show());
                }
            }
        });
    }

    private void PrefTextField(Paper paper, EditorSettings s, string label, string value, Action<string> set)
    {
        string baseId = $"pref_ft_{label.Replace(" ", "_")}";
        EditorGUI.SettingsRow(paper, baseId, label, () =>
            Origami.TextField(paper, $"{baseId}_v", value, v =>
            {
                set(v);
                //s.ApplyTheme();
                s.Save();
            }).Show());
    }

    private void SzSlider(Paper paper, EditorSettings s, string label, float value, float min, float max, Action<float> set, bool applyOnSlide = true)
    {
        string baseId = $"pref_sz_{label.Replace(" ", "_")}";
        EditorGUI.SettingsRow(paper, baseId, label, () =>
            Origami.Slider(paper, $"{baseId}_v", value, v =>
            {
                set(MathF.Round(v, 1));
                if (applyOnSlide)
                {
                    s.ApplyTheme();
                    s.Save();
                }
            }, min, max).Format("F1").Show());
    }

    // ================================================================
    //  Shortcuts
    // ================================================================

    private void DrawShortcuts(Paper paper, Scribe.FontFile font, float w)
    {
        var m = Origami.Current.Metrics;

        // Handle rebinding input each frame
        if (_rebindingId != null)
        {
            ShortcutManager.IsRebinding = true;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _rebindingId = null;
                ShortcutManager.IsRebinding = false;
            }
            else
            {
                // Check for any non-modifier key press
                foreach (KeyCode key in Enum.GetValues<KeyCode>())
                {
                    if (key == KeyCode.Unknown || key == KeyCode.Escape) continue;
                    // Skip modifier keys themselves they're captured via flags
                    if (key is KeyCode.ShiftLeft or KeyCode.ShiftRight
                        or KeyCode.ControlLeft or KeyCode.ControlRight
                        or KeyCode.AltLeft or KeyCode.AltRight
                        or KeyCode.SuperLeft or KeyCode.SuperRight) continue;

                    if (Input.GetKeyDown(key))
                    {
                        var binding = new ShortcutBinding(key, Input.IsCtrlPressed, Input.IsShiftPressed, Input.IsAltPressed);
                        ShortcutManager.SetOverride(_rebindingId, binding);
                        _rebindingId = null;
                        ShortcutManager.IsRebinding = false;
                        break;
                    }
                }
            }
        }

        EditorGUI.SectionHeader(paper, "pref_sc_hdr", Loc.Get("pref.shortcuts"), first: true);

        // Search bar
        using (paper.Box("pref_sc_search_w").Width(ST).Height(UnitValue.Auto)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 4).Enter())
            Origami.SearchField(paper, "pref_sc_search", _shortcutSearch,
                v => _shortcutSearch = v, Loc.Get("pref.search_shortcuts")).Show();

        paper.Box("pref_sc_sp1").Height(EditorTheme.Spacing * 2);

        // Reset All button
        using (paper.Row("pref_sc_actions").Height(EditorTheme.RowHeight).ChildLeft(m.PaddingLarge).Enter())
        {
            Origami.Button(paper, "pref_sc_reset_all", $"{EditorIcons.RotateLeft}  {Loc.Get("pref.reset_all")}", () => ShortcutManager.ClearAllOverrides()).Width(200).Show();
        }

        paper.Box("pref_sc_sp2").Height(EditorTheme.Padding * 2);

        // Group by category
        string? lastCategory = null;
        foreach (var shortcut in ShortcutManager.GetAllShortcuts())
        {
            // Filter by search
            if (!string.IsNullOrEmpty(_shortcutSearch) &&
                !shortcut.DisplayName.Contains(_shortcutSearch, StringComparison.OrdinalIgnoreCase) &&
                !shortcut.Id.Contains(_shortcutSearch, StringComparison.OrdinalIgnoreCase))
                continue;

            // Category header
            if (shortcut.Category != lastCategory)
            {
                lastCategory = shortcut.Category;
                EditorGUI.SectionHeader(paper, $"pref_sc_cat_{lastCategory}", lastCategory);
            }

            bool isRebinding = _rebindingId == shortcut.Id;
            bool isOverridden = shortcut.Override != null;
            string bindDisplay = isRebinding
                ? Loc.Get("pref.press_key")
                : ShortcutManager.GetDisplayString(shortcut.Binding);

            using (paper.Row($"pref_sc_{shortcut.Id}")
                .Height(EditorTheme.RowHeight)
                .ChildLeft(m.PaddingLarge).RowBetween(EditorTheme.Spacing * 2)
                .Enter())
            {
                // Display name
                paper.Box($"pref_sc_name_{shortcut.Id}")
                    .Width(w * 0.4f).Height(EditorTheme.RowHeight)
                    .Text(shortcut.DisplayName, font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft);

                // Binding button
                paper.Box($"pref_sc_bind_{shortcut.Id}")
                    .Width(160).Height(EditorTheme.RowHeight - 4).Rounded(8)
                    .BackgroundColor(isRebinding ? EditorTheme.Accent : EditorTheme.Glass)
                    .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                    .Hovered.BackgroundColor(isRebinding ? EditorTheme.Accent : EditorTheme.Hover).BorderColor(EditorTheme.BorderStrong).End()
                    .Text(bindDisplay, font)
                    .TextColor(isRebinding ? Color.White : (isOverridden ? EditorTheme.Accent : EditorTheme.Ink400))
                    .FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleCenter)
                    .OnClick(shortcut.Id, (id, _) =>
                    {
                        _rebindingId = _rebindingId == id ? null : id;
                        ShortcutManager.IsRebinding = _rebindingId != null;
                    });

                // Reset button (only if overridden)
                if (isOverridden)
                {
                    paper.Box($"pref_sc_rst_{shortcut.Id}")
                        .Width(50).Height(EditorTheme.RowHeight - 4).Rounded(8)
                        .BackgroundColor(EditorTheme.Glass)
                        .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                        .Hovered.BackgroundColor(EditorTheme.Hover).BorderColor(EditorTheme.BorderStrong).End()
                        .Text(Loc.Get("inspector.reset"), font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleCenter)
                        .OnClick(shortcut.Id, (id, _) => ShortcutManager.ClearOverride(id));
                }
            }
        }
    }

    // ================================================================
    //  Color conversion helpers
    // ================================================================

    private static VColor HexToVColor(string hex)
    {
        var c = ColorRamp.ParseHex(hex);
        return new VColor(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }

    private static string VColorToHex(VColor c)
    {
        int r = Math.Clamp((int)(c.R * 255), 0, 255);
        int g = Math.Clamp((int)(c.G * 255), 0, 255);
        int b = Math.Clamp((int)(c.B * 255), 0, 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
