using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Inspector;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Editor.Thumbnails;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

using static Prowl.Editor.GUI.EditorGUI;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;

namespace Prowl.Editor.GUI.Panels;

public class PreferencesPanel : DockPanel
{
    [MenuItem("Window/General/Preferences", priority: 7)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(PreferencesPanel));

    public override string Title => Loc.Get("panel.preferences");
    public override string Icon => EditorIcons.Sliders;

    private string _tab = "general";
    private string _search = "";

    // Matches per result group last frame, so a group's header only shows when something in it matched.
    private readonly Dictionary<string, int> _searchHits = [];
    private string? _rebindingId;
    private string _themeCat = "presets";

    // Theme-driven spacing (between elements) / padding (internal), so the whole panel scales with the theme.
    private static float SP => EditorTheme.Spacing;
    private static float PAD => EditorTheme.Padding;

    // label holds a localization key, resolved via Loc.Get where the tabs are rendered.
    private static readonly (string id, string label, string icon)[] Cats =
    [
        ("general",   "pref.general",   EditorIcons.Gear),
        ("theme",     "pref.theme",     EditorIcons.Palette),
        ("shortcuts", "pref.shortcuts", EditorIcons.Keyboard),
    ];

    private static readonly (string id, string label, string icon)[] ThemeCats =
    [
        ("presets", "pref.cat_presets", EditorIcons.Swatchbook),
        ("colors",  "pref.cat_colors",  EditorIcons.Droplet),
        ("type",    "pref.cat_type",    EditorIcons.Font),
        ("layout",  "pref.cat_layout",  EditorIcons.TableCells),
        ("effects", "pref.cat_effects", EditorIcons.Bolt),
    ];

    /// <summary>Switch this Preferences window to the Theme tab (used by the header quick-access button).</summary>
    public void ShowTheme() => _tab = "theme";

    private static string SectionTabId(PreferencesSection section) => "section:" + section.Type.FullName;

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        EditorSettings settings = EditorSettings.Instance;

        OrigamiMetrics m = Origami.Current.Metrics;
        float searchH = EditorTheme.RowHeight + m.Padding * 2;

        using (paper.Column("pref_outer").Width(width).Height(height).Clip().Enter())
        {
            using (paper.Box("pref_search_w").Width(width).Height(searchH).Padding(m.PaddingLarge, m.PaddingLarge, m.Padding, m.Padding).Enter())
                Origami.SearchField(paper, "pref_search", _search, v => _search = v, Loc.Get("pref.search_all")).Show();
            paper.Box("pref_hdiv").Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            DrawBody(paper, font, settings, width, height - searchH - 1);
        }
    }

    private void DrawBody(Paper paper, Scribe.FontFile font, EditorSettings settings, float width, float height)
    {
        using (paper.Row("pref_root").Width(width).Height(height).Clip().Enter())
        {
            (string id, string, string icon)[] cats =
            [
                .. Cats.Select(c => (c.id, Loc.Get(c.label), c.icon)),
                .. Preferences.Sections.Select(p => (SectionTabId(p), p.Name, string.IsNullOrEmpty(p.Icon) ? EditorIcons.Gear : p.Icon)),
            ];
            float side = EditorGUI.Sidebar(paper, "pref_side", cats, _tab, c => { _tab = c; _search = ""; });
            paper.Box("pref_vdiv").Width(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            float contentW = width - side - 1;

            if (_search.Length > 0)
            {
                Origami.ScrollView(paper, "pref_results", contentW, height).Body(() =>
                {
                    using (paper.Column("pref_results_col").Height(UnitValue.Auto).Padding(0, 0, 8, 12).Enter())
                        DrawSearchResults(paper, font, settings, contentW);
                });
            }
            // The Theme tab owns a fixed 3-column layout (rail | controls | live preview) with its own
            // inner scroll, so it bypasses the shared vertical scroll view.
            else if (_tab == "theme")
            {
                DrawTheme(paper, font, settings, contentW, height);
            }
            else
            {
                Origami.ScrollView(paper, "pref_content", contentW, height).Body(() =>
                {
                    using (paper.Column("pref_content_col").Height(UnitValue.Auto).Padding(0, 0, 8, 12).Enter())
                    {
                        PreferencesSection? section = Preferences.Sections.FirstOrDefault(p => SectionTabId(p) == _tab);
                        if (section != null) DrawSection(paper, section, contentW);
                        else if (_tab == "shortcuts") DrawShortcuts(paper, font, contentW);
                        else DrawGeneral(paper, settings);
                    }
                });
            }
        }
    }

    // ================================================================
    //  Search
    // ================================================================

    // Every tab drawn one after another with only the matching rows. A group whose name matches shows in full.
    private void DrawSearchResults(Paper paper, Scribe.FontFile font, EditorSettings s, float w)
    {
        HandleRebinding();
        int shown = 0;

        Group("general", Loc.Get("pref.general"), () => DrawGeneral(paper, s));
        Group("theme", Loc.Get("pref.theme"), () =>
        {
            using (paper.Column("pref_res_theme").Height(UnitValue.Auto).PaddingLeft(Origami.Current.Metrics.PaddingLarge).PaddingRight(Origami.Current.Metrics.PaddingLarge).Enter())
            {
                EditorThemeData theme = s.Theme;
                DrawThemeColors(paper, s, theme);
                DrawThemeType(paper, s, theme);
                DrawThemeLayout(paper, s, theme);
                DrawThemeEffects(paper, s, theme);
            }
        });
        Group("shortcuts", Loc.Get("pref.shortcuts"), () => EditorGUI.RowFilterMatches += DrawShortcutRows(paper, font, w, EditorGUI.RowFilter));
        foreach (PreferencesSection section in Preferences.Sections)
        {
            PreferencesSection current = section;
            Group(SectionTabId(section), section.Name, () =>
            {
                Func<FieldInfo, bool>? filter = EditorGUI.RowFilter is { } text ? f => FieldMatches(f, text) : null;
                EditorGUI.RowFilterMatches += PropertyGridUtils.GetSerializableFields(current.Type).Count(f => filter == null || filter(f));
                DrawSectionFields(paper, $"pref_res_{current.Type.Name}", current, filter);
            });
        }

        if (shown == 0)
            EditorGUI.EmptyState(paper, "pref_res_none", Loc.Get("pref.search_none"), font);

        void Group(string key, string title, Action draw)
        {
            if (_searchHits.GetValueOrDefault(key) > 0)
            {
                EditorGUI.SectionHeader(paper, $"pref_res_hdr_{key}", title);
                shown++;
            }

            bool titleMatches = title.Contains(_search, StringComparison.OrdinalIgnoreCase);
            EditorGUI.RowFilter = titleMatches ? null : _search;
            EditorGUI.RowFilterMatches = 0;
            try { draw(); }
            finally
            {
                _searchHits[key] = titleMatches ? 1 : EditorGUI.RowFilterMatches;
                EditorGUI.RowFilter = null;
            }
        }
    }

    private static bool FieldMatches(FieldInfo field, string text)
        => PropertyGridUtils.NicifyName(field.Name).Contains(text, StringComparison.OrdinalIgnoreCase)
           || (field.GetCustomAttribute<TooltipAttribute>()?.Text.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);

    // ================================================================
    //  Registered sections
    // ================================================================

    private static void DrawSection(Paper paper, PreferencesSection section, float width)
    {
        EditorGUI.SectionHeader(paper, "pref_sec_hdr", section.Name, first: true);

        using (paper.Row("pref_sec_actions").Height(EditorTheme.RowHeight).PaddingLeft(Origami.Current.Metrics.PaddingLarge)
            .Margin(0, 0, 0, EditorTheme.Padding * 2).Enter())
            Origami.Button(paper, "pref_sec_reset", $"{EditorIcons.RotateLeft}  {Loc.Get("pref.reset_section")}", () => Preferences.Reset(section)).Show();

        if (section.ProjectOverridable && Projects.Project.Current != null)
            EditorGUI.SettingsToggle(paper, "pref_sec_project", Loc.Get("pref.project_override"), section.IsProjectOverride,
                v => Preferences.SetProjectOverride(section, v));

        section.Instance.OnGUI(paper, width);
        DrawSectionFields(paper, "pref_sec_grid", section, null);
    }

    // Preferences are not scene data, so the grid runs without the inspector's undo snapshot (which would also mark the scene dirty).
    private static void DrawSectionFields(Paper paper, string id, PreferencesSection section, Func<FieldInfo, bool>? filter)
    {
        PropertyGridConfig config = EditorApplication.PropertyGridConfig;
        Action<object>? snapshot = config.OnBeginRoot;
        config.OnBeginRoot = null;
        try
        {
            Origami.PropertyGrid(paper, id, section.Instance, config)
                .OnChanged(_ => Preferences.Changed(section))
                .Overrides(Preferences.ChangedFields(section))
                .OnOverrideClicked(name => Preferences.ResetField(section, name), Loc.Get("pref.reset_to_default"))
                .FieldFilter(filter)
                .Show();
        }
        finally
        {
            config.OnBeginRoot = snapshot;
        }
    }

    // ================================================================
    //  General
    // ================================================================

    // Only applies outside play mode; the game gets an unpaced loop and sets its own.
    private static readonly int[] FrameRates = [0, 30, 60, 120, 144, 240];
    private static readonly string[] FrameRateNames = ["Unlimited", "30", "60", "120", "144", "240"];
    private static readonly int[] UnfocusedFrameRates = [0, 5, 10, 15, 30, 60];
    private static readonly string[] UnfocusedFrameRateNames = ["Same as Focused", "5", "10", "15", "30", "60"];

    private void DrawGeneral(Paper paper, EditorSettings s)
    {
        EditorGUI.SectionHeader(paper, "pref_gen_hdr", Loc.Get("pref.general"), first: true);

        EditorGUI.SettingsRow(paper, "pref_lang", Loc.Get("pref.language"), () =>
            Origami.Dropdown(paper, "pref_lang_dd",
                LocaleHelper.GetIndex(Loc.CurrentLocale),
                LocaleHelper.SetLocale, LocaleHelper.Names).Show());

        EditorGUI.SettingsRow(paper, "pref_proj_path", Loc.Get("pref.projects_path"), () =>
            Origami.TextField(paper, "pref_proj_path_v", s.DefaultProjectsPath,
                v => { s.DefaultProjectsPath = v; s.Save(); }).CommitOnEnter().Show());

        EditorGUI.SettingsToggle(paper, "pref_auto_save", Loc.Get("pref.auto_save"), s.AutoSave,
            v => { s.AutoSave = v; s.Save(); });

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
                    EditorAssetBackend.Instance?.ClearThumbnailTextureCache();
                }, thumbOptions).Show());

        EditorGUI.SectionHeader(paper, "pref_gen_perf", Loc.Get("pref.performance"));

        EditorGUI.SettingsToggle(paper, "pref_vsync", Loc.Get("pref.vsync"), s.VSync,
            v => { s.VSync = v; s.Save(); EditorApplication.ApplyFramePacing(); });

        int fpsIndex = Array.IndexOf(FrameRates, s.TargetFrameRate);
        EditorGUI.SettingsRow(paper, "pref_fps_limit", Loc.Get("pref.frame_rate_limit"), () =>
            Origami.Dropdown(paper, "pref_fps_limit_v", fpsIndex < 0 ? 0 : fpsIndex,
                v =>
                {
                    s.TargetFrameRate = FrameRates[v];
                    s.Save();
                    EditorApplication.ApplyFramePacing();
                }, FrameRateNames).Show());

        int unfocusedIndex = Array.IndexOf(UnfocusedFrameRates, s.UnfocusedFrameRate);
        EditorGUI.SettingsRow(paper, "pref_unfocused_fps", Loc.Get("pref.unfocused_frame_rate"), () =>
            Origami.Dropdown(paper, "pref_unfocused_fps_v", unfocusedIndex < 0 ? 0 : unfocusedIndex,
                v =>
                {
                    s.UnfocusedFrameRate = UnfocusedFrameRates[v];
                    s.Save();
                    EditorApplication.ApplyFramePacing();
                }, UnfocusedFrameRateNames).Show());

        EditorGUI.SectionHeader(paper, "pref_gen_cli", Loc.Get("pref.cli"));
        EditorGUI.SettingsToggle(paper, "pref_cli_eval", Loc.Get("pref.cli_allow_eval"), s.AllowCliEval,
            v => { s.AllowCliEval = v; s.Save(); });

        EditorGUI.SettingsRow(paper, "pref_setup", Loc.Get("pref.run_setup"), () =>
            Origami.Button(paper, "pref_setup_b", $"{EditorIcons.WandMagicSparkles}  {Loc.Get("pref.run_setup_btn")}", EditorSetup.Show).Show());

        EditorGUI.SectionHeader(paper, "pref_gen_profile", Loc.Get("pref.profile"));
        EditorGUI.SettingsRow(paper, "pref_profile", Loc.Get("pref.profile_desc"), () =>
        {
            using (paper.Row("pref_profile_btns").Height(UnitValue.Auto).Gap(EditorTheme.Spacing * 2).Enter())
            {
                Origami.Button(paper, "pref_profile_export", $"{EditorIcons.FileExport}  {Loc.Get("pref.profile_export")}", ExportProfile).Show();
                Origami.Button(paper, "pref_profile_import", $"{EditorIcons.FileImport}  {Loc.Get("pref.profile_import")}", ImportProfile).Show();
            }
        });
        EditorGUI.SettingsRow(paper, "pref_reset_everything", Loc.Get("pref.reset_everything"), () =>
            Origami.Button(paper, "pref_reset_everything_b", $"{EditorIcons.RotateLeft}  {Loc.Get("pref.reset_everything_btn")}", () =>
                Origami.Confirm(Loc.Get("pref.reset_everything"), Loc.Get("pref.reset_everything_body"), Preferences.ResetAll)).Show());

        EditorGUI.SectionHeader(paper, "pref_gen_maint", Loc.Get("pref.maintenance"));
        EditorGUI.SettingsRow(paper, "pref_clear_cache", Loc.Get("pref.clear_cache"), () =>
            Origami.Button(paper, "pref_clear_cache_b", $"{EditorIcons.ArrowsRotate}  {Loc.Get("pref.clear_cache_btn")}",
                () =>
                {
                    EditorApplication.Instance?.ClearEditorCache();
                    Toasts.Info(Loc.Get("pref.clear_cache"), Loc.Get("pref.clear_cache_done"));
                }).Width(200).Show());
    }

    private static readonly string[] s_profileFilters = ["*" + Preferences.ProfileExtension];

    private static void ExportProfile() => EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
    {
        if (path == null) return;
        if (!path.EndsWith(Preferences.ProfileExtension, StringComparison.OrdinalIgnoreCase)) path += Preferences.ProfileExtension;
        try
        {
            Preferences.ExportProfile(path);
            Toasts.Success(Loc.Get("pref.profile_exported"), System.IO.Path.GetFileName(path));
        }
        catch (Exception ex) { Toasts.Error(Loc.Get("pref.profile_failed"), ex.Message); }
    }, filters: s_profileFilters, filterLabels: [Loc.Get("pref.profile_filter")]);

    private static void ImportProfile() => EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
    {
        if (path == null) return;
        try
        {
            Preferences.ImportProfile(path);
            Toasts.Success(Loc.Get("pref.profile_imported"), System.IO.Path.GetFileName(path));
        }
        catch (Exception ex) { Toasts.Error(Loc.Get("pref.profile_failed"), ex.Message); }
    }, filters: s_profileFilters, filterLabels: [Loc.Get("pref.profile_filter")]);

    // ================================================================
    //  Theme (Colors + Sizing)
    // ================================================================

    private void DrawTheme(Paper paper, Scribe.FontFile font, EditorSettings s, float w, float h)
    {
        EditorThemeData theme = s.Theme;

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
            using (paper.Row("pref_theme_body").Height(bodyH).Enter())
            {
                DrawThemeRail(paper, font, railW);
                paper.Box("pref_theme_rdiv").Width(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

                Origami.ScrollView(paper, "pref_theme_ctrls", ctrlW, bodyH).Body(view =>
                {
                    // At least as tall as the view, so the presets can center vertically when they fit.
                    using (paper.Column("pref_theme_ctrl_col").Height(UnitValue.Auto).MinHeight(view.Height)
                        .Padding(PAD * 3, PAD * 3, PAD * 2, PAD * 3).Gap(SP).Enter())
                    {
                        switch (_themeCat)
                        {
                            case "presets":
                                using (paper.Column("pref_theme_pr_center").Height(UnitValue.Auto)
                                    .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                                    DrawThemePresets(paper, font, s, theme);
                                break;
                            case "colors":  DrawThemeColors(paper, s, theme); break;
                            case "type":    DrawThemeType(paper, s, theme); break;
                            case "layout":  DrawThemeLayout(paper, s, theme); break;
                            case "effects": DrawThemeEffects(paper, s, theme); break;
                        }
                    }
                });

                if (showPreview)
                {
                    paper.Box("pref_theme_pdiv").Width(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
                    DrawThemePreview(paper, font, previewW);
                }
            }

            EditorGUI.Divider(paper, "pref_theme_fdiv");
            DrawThemeFooter(paper, s, theme, footerH);
        }
    }

    private void DrawThemeRail(Paper paper, Scribe.FontFile font, float w)
    {
        EditorGUI.Sidebar(paper, "pref_theme_rail",
            [.. ThemeCats.Select(c => (c.id, Loc.Get(c.label), c.icon))],
            _themeCat, id => _themeCat = id,
            width: w, rowHeight: 34f,
            footer: () =>
            {
                using (paper.Row("pref_thr_note").Height(UnitValue.Auto).Margin(0, 0, 10, 0)
                    .Rounded(Origami.Current.Metrics.ContainerRounding).Padding(10, 10, 9, 9)
                    .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).IsNotInteractable().Enter())
                    paper.Box("pref_thr_note_t").Height(UnitValue.Auto).IsNotInteractable()
                        .Text(Loc.Get("pref.theme_note"), font)
                        .Wrap(Scribe.TextWrapMode.Wrap)
                        .TextColor(EditorTheme.InkDim).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            });
    }

    // Footer shown on every category: Reset (left), Import / Export / Apply (right).
    private void DrawThemeFooter(Paper paper, EditorSettings s, EditorThemeData theme, float h)
    {
        using (paper.Row("pref_theme_footer").Height(h).Padding(PAD * 2, PAD * 2, 0, 0).Gap(SP * 2).Enter())
        {
            EditorGUI.Chip(paper, "pref_ft_reset", $"{EditorIcons.RotateLeft}  {Loc.Get("pref.reset_default")}", () => s.ResetTheme());

            paper.Box("pref_ft_sp").Height(1).IsNotInteractable();

            EditorGUI.Chip(paper, "pref_ft_import", $"{EditorIcons.Upload}  {Loc.Get("pref.import")}", () =>
                EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
                {
                    if (path == null) return;
                    var imported = EditorThemeData.ImportFromFile(path);
                    if (imported != null) { s.Theme = imported; s.ApplyTheme(); s.Save(); Toasts.Info(Loc.Get("pref.toast_theme"), Loc.Get("pref.toast_imported", new { name = imported.Name })); }
                }, filters: ["*.prowltheme"], filterLabels: [Loc.Get("pref.theme_filter")]));

            EditorGUI.Chip(paper, "pref_ft_export", $"{EditorIcons.Download}  {Loc.Get("pref.export")}", () =>
                EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
                {
                    if (path == null) return;
                    if (!path.EndsWith(".prowltheme")) path += ".prowltheme";
                    theme.ExportToFile(path);
                    Toasts.Info(Loc.Get("pref.toast_theme"), Loc.Get("pref.toast_exported", new { file = System.IO.Path.GetFileName(path) }));
                }, filters: ["*.prowltheme"], filterLabels: [Loc.Get("pref.theme_filter")]), leftGap: 8f);

            using (paper.Box("pref_ft_apply_w").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(8, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                Origami.Button(paper, "pref_ft_apply", $"{EditorIcons.Check}  Apply", () => { s.ApplyTheme(); s.Save(); })
                    .Primary().Height(30).Show();
        }
    }

    // ================================================================
    //  Theme editor faithful port of the designer's ThemeEditor2
    //  (th-sec / th-row / th-swrow / th-preset controls)
    // ================================================================

    private const int PresetColumns = 4;

    // Palettes always include the ramp's real default so the current colour reads as selected.
    private static readonly string[] _accentPalette =
        ["#A855F7", "#60A5FA", "#4ADE80", "#FBBF24", "#FB7185", "#8B5CF6", "#6366F1", "#06B6D4", "#F97316", "#EC4899"];
    private static readonly string[] _bgPalette   = ["#262036", "#1A1626", "#0F0C18", "#0A0F1A", "#0B1410", "#160F0C"];
    private static readonly string[] _textPalette = ["#F0EEF7", "#FFFFFF", "#E8F0F7", "#ECEEF2", "#F7EFE8", "#D8D4E8"];

    private static Color Hx(string hex) => System.Drawing.ColorTranslator.FromHtml(hex);

    private static void ApplyPreset(EditorSettings s, ThemePreset p)
    {
        p.ApplyTo(s.Theme);
        s.ApplyTheme(); s.Save();
    }

    private static void ApplyLayout(EditorSettings s, LayoutPreset layout)
    {
        layout.ApplyTo(s.Theme);
        s.ApplyTheme(); s.Save();
    }

    // ---- Presets ----
    internal static void DrawThemePresets(Paper paper, Scribe.FontFile font, EditorSettings s, EditorThemeData theme, bool saveButtons = true)
    {
        EditorGUI.SectionHeader(paper, "pref_pr_hdr", Loc.Get("pref.builtin_themes"), first: true, compact: true);
        PresetGrid(paper, "pref_pr", ThemePresets.All.Length, i =>
        {
            ThemePreset p = ThemePresets.All[i];
            bool on = string.Equals(theme.Name, p.Name, StringComparison.OrdinalIgnoreCase);
            PresetCard(paper, font, $"pref_pr_c{i}", p.Name, on, () => ApplyPreset(s, p),
                id => ThemeSwatch(paper, id, Hx(p.Bg), Hx(p.Accent), Hx(p.Accent2), Hx(p.Panel), Hx(p.Text)));
        });

        if (s.SavedThemes.Count > 0)
        {
            EditorGUI.SectionHeader(paper, "pref_ut_hdr", Loc.Get("pref.your_themes"), compact: true);
            PresetGrid(paper, "pref_ut", s.SavedThemes.Count, i =>
            {
                EditorThemeData saved = s.SavedThemes[i];
                bool on = string.Equals(theme.Name, saved.Name, StringComparison.OrdinalIgnoreCase);
                PresetCard(paper, font, $"pref_ut_c{i}", saved.Name, on, () => ApplySavedTheme(s, saved),
                    id => ThemeSwatch(paper, id, ColorRamp.ParseHex(saved.BackgroundColorA), saved.Purple.GetStop(4), saved.Blue.GetStop(4),
                        saved.Neutral.GetStop(4), saved.Ink.GetStop(4)),
                    onDelete: () => { s.SavedThemes.Remove(saved); s.Save(); });
            });
        }

        EditorGUI.SectionHeader(paper, "pref_ly_hdr", Loc.Get("pref.builtin_layouts"), compact: true);
        LayoutPreset[] layouts = [.. ThemePresets.Layouts, .. s.SavedLayouts];
        PresetGrid(paper, "pref_ly", layouts.Length, i =>
        {
            LayoutPreset l = layouts[i];
            Action? delete = i < ThemePresets.Layouts.Length ? null : () => { s.SavedLayouts.Remove(l); s.Save(); };
            PresetCard(paper, font, $"pref_ly_c{i}", l.Name, l.Matches(theme), () => ApplyLayout(s, l), id =>
            {
                float pillRound = l.Roundness * 0.6f;
                using (paper.Row(id).Height(34).Rounded(l.Roundness).Padding(l.Padding, l.Padding, l.Padding, l.Padding).Gap(l.Spacing)
                    .BackgroundColor(EditorTheme.Popover).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).IsNotInteractable().Enter())
                {
                    paper.Box($"{id}_a").Rounded(pillRound)
                        .BackgroundLinearGradient(0, 0, 1, 1, EditorTheme.Accent, EditorTheme.AccentBright).IsNotInteractable();
                    paper.Box($"{id}_b").Rounded(pillRound).BackgroundColor(EditorTheme.Selected).IsNotInteractable();
                    paper.Box($"{id}_c").Rounded(pillRound).BackgroundColor(EditorTheme.Selected).IsNotInteractable();
                }
            }, delete);
        });

        if (!saveButtons) return;
        using (paper.Row("pref_save_presets").Height(UnitValue.Auto).Gap(SP * 2).Enter())
        {
            EditorGUI.Chip(paper, "pref_save_theme", $"{EditorIcons.FloppyDisk}  {Loc.Get("pref.save_theme")}", () =>
                PromptName(Loc.Get("pref.save_theme"), theme.Name, name => SaveTheme(s, name)));
            EditorGUI.Chip(paper, "pref_save_layout", $"{EditorIcons.FloppyDisk}  {Loc.Get("pref.save_layout")}", () =>
                PromptName(Loc.Get("pref.save_layout"), "", name => SaveLayout(s, name)), leftGap: 8f);
        }
    }

    private static void ThemeSwatch(Paper paper, string id, Color bg, Color accent, Color accent2, Color panel, Color text)
    {
        using (paper.Row(id).Height(34).Rounded(EditorTheme.Roundness).Padding(6, 6, 6, 6).Gap(4)
            .BackgroundColor(bg).IsNotInteractable().Enter())
        {
            paper.Box($"{id}_a").Rounded(Origami.Current.Metrics.SmallRounding)
                .BackgroundLinearGradient(0, 0, 1, 1, accent, accent2).IsNotInteractable();
            paper.Box($"{id}_b").Rounded(Origami.Current.Metrics.SmallRounding).BackgroundColor(accent2).IsNotInteractable();
            paper.Box($"{id}_p").Rounded(Origami.Current.Metrics.SmallRounding).BackgroundColor(panel)
                .BorderColor(EditorTheme.WithAlpha(text, 38)).BorderWidth(1).IsNotInteractable();
        }
    }

    private static void ApplySavedTheme(EditorSettings s, EditorThemeData saved)
    {
        s.Theme = saved.Clone();
        s.ApplyTheme(); s.Save();
    }

    // A theme saved under a name already in the list replaces it.
    private static void SaveTheme(EditorSettings s, string name)
    {
        EditorThemeData copy = s.Theme.Clone();
        copy.Name = name;
        s.Theme.Name = name;
        s.SavedThemes.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        s.SavedThemes.Add(copy);
        s.Save();
    }

    private static void SaveLayout(EditorSettings s, string name)
    {
        s.SavedLayouts.RemoveAll(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        s.SavedLayouts.Add(LayoutPreset.From(name, s.Theme));
        s.Save();
    }

    internal static void PromptName(string title, string initial, Action<string> onSave)
    {
        string name = initial;
        DialogModal dialog = Origami.Dialog(title, paper =>
            Origami.TextField(paper, "pref_name_prompt", name, v => name = v).AutoFocus().Show(), 340);
        dialog.Button(Loc.Get("dialog.save"), () =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                Modal.Remove(dialog);
                onSave(name.Trim());
            }, OrigamiVariant.Primary)
            .Button(Loc.Get("common.cancel"), () => Modal.Remove(dialog));
    }

    private static void PresetGrid(Paper paper, string id, int count, Action<int> drawCard)
    {
        for (int r = 0; r * PresetColumns < count; r++)
        {
            using (paper.Row($"{id}_row{r}").Height(UnitValue.Auto).Margin(0, 0, 0, SP * 2).Gap(SP * 2).Enter())
            {
                for (int c = 0; c < PresetColumns; c++)
                {
                    int i = r * PresetColumns + c;
                    if (i < count) drawCard(i);
                    else paper.Box($"{id}_e{r}{c}").Height(1).IsNotInteractable();
                }
            }
        }
    }

    private static void PresetCard(Paper paper, Scribe.FontFile font, string id, string name, bool on, Action onClick, Action<string> drawSwatch,
        Action? onDelete = null)
    {
        ElementBuilder card = paper.Column(id).Height(UnitValue.Auto).Rounded(Origami.Current.Metrics.ContainerRounding)
            .Padding(PAD, PAD, PAD, PAD).Gap(SP * 1.5f)
            .BackgroundColor(on ? EditorTheme.Selected : EditorTheme.Glass)
            .BorderColor(on ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(on ? 2 : 1)
            .Hovered.BorderColor(on ? EditorTheme.Accent : EditorTheme.BorderStrong).End()
            .OnClick(0, (_, _) => onClick());
        if (on) card.Glow(0, 8, 22, -10, Color.FromArgb(130, EditorTheme.Accent));

        using (card.Enter())
        {
            if (onDelete != null)
                ContextMenu.RightClickMenu(paper, $"{id}_menu", b => b.Item(Loc.Get("pref.delete_preset"), onDelete));
            drawSwatch($"{id}_sw");
            using (paper.Row($"{id}_nm").Height(15).Enter())
            {
                paper.Box($"{id}_nt").IsNotInteractable()
                    .Text(name, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();
                if (on)
                    paper.Box($"{id}_ck").Width(12).IsNotInteractable()
                        .Text(EditorIcons.Check, font).TextColor(EditorTheme.AccentText)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
            }
        }
    }

    // ---- Colors ----
    private void DrawThemeColors(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        EditorGUI.SectionHeader(paper, "pref_cl_accent", Loc.Get("pref.accent"), first: true, compact: true);
        EditorGUI.SwatchRow(paper, s, "accent_primary", Loc.Get("pref.primary"), theme.Purple, _accentPalette);
        EditorGUI.Divider(paper, "pref_cl_d0");
        EditorGUI.SwatchRow(paper, s, "accent_info", Loc.Get("pref.info"), theme.Blue, _accentPalette);

        EditorGUI.SectionHeader(paper, "pref_cl_surf", Loc.Get("pref.surfaces"), compact: true);
        EditorGUI.SwatchRow(paper, s, "surf_neutral", Loc.Get("pref.neutral"), theme.Neutral, _bgPalette);
        EditorGUI.Divider(paper, "pref_cl_d1");
        EditorGUI.SwatchRow(paper, s, "surf_ink", Loc.Get("pref.ink"), theme.Ink, _textPalette);

        EditorGUI.SectionHeader(paper, "pref_cl_sem", Loc.Get("pref.semantic"), compact: true);
        EditorGUI.SwatchRow(paper, s, "sem_success", Loc.Get("pref.success"), theme.Green, _accentPalette);
        EditorGUI.Divider(paper, "pref_cl_d2");
        EditorGUI.SwatchRow(paper, s, "sem_warning", Loc.Get("pref.warning"), theme.Amber, _accentPalette);
        EditorGUI.Divider(paper, "pref_cl_d3");
        EditorGUI.SwatchRow(paper, s, "sem_danger", Loc.Get("pref.danger"), theme.Red, _accentPalette);

        EditorGUI.SectionHeader(paper, "pref_cl_ui", Loc.Get("pref.interface_colors"), compact: true);
        OverrideColorRow(paper, s, "pref_cl_hover", Loc.Get("pref.hover_color"), theme.HoverColor, v => theme.HoverColor = v, EditorTheme.OrigamiTheme.Hover);
        OverrideColorRow(paper, s, "pref_cl_sel", Loc.Get("pref.selection_color"), theme.SelectionColor, v => theme.SelectionColor = v, EditorTheme.OrigamiTheme.Selected);
        OverrideColorRow(paper, s, "pref_cl_focus", Loc.Get("pref.focus_color"), theme.FocusColor, v => theme.FocusColor = v, EditorTheme.OrigamiTheme.FocusBorder);
        OverrideColorRow(paper, s, "pref_cl_scroll", Loc.Get("pref.scrollbar_color"), theme.ScrollbarColor, v => theme.ScrollbarColor = v, EditorTheme.OrigamiTheme.Scrollbar);
        OverrideColorRow(paper, s, "pref_cl_split", Loc.Get("pref.splitter_color"), theme.SplitterColor, v => theme.SplitterColor = v, EditorTheme.OrigamiTheme.Splitter);
        OverrideColorRow(paper, s, "pref_cl_tip", Loc.Get("pref.tooltip_color"), theme.TooltipColor, v => theme.TooltipColor = v, EditorTheme.OrigamiTheme.TooltipBackground);
    }

    // A switch that turns a colour override on, starting from the colour in use, and the colour itself while it is on.
    private static void OverrideColorRow(Paper paper, EditorSettings s, string id, string label, string hex, Action<string> set, Color current)
    {
        void Apply(string value) { set(value); s.ApplyTheme(); s.Save(); }

        EditorGUI.Row(paper, id, label, () =>
        {
            using (paper.Row($"{id}_row").Height(UnitValue.Auto).Gap(SP * 2).Enter())
            {
                bool on = hex.Length > 0;
                Origami.Switch(paper, $"{id}_on", on, v => Apply(v ? ColorRamp.ColorToHex(current) : "")).NoLabel().Show();
                if (on)
                    Origami.ColorField(paper, $"{id}_v", EditorGUI.HexToVColor(hex), v => Apply(EditorGUI.VColorToHex(v))).Width(130f).Show();
            }
        }, compact: true);
    }

    private static string[]? _installedFamilies;
    private static readonly string[] s_fontFilters = ["*.ttf;*.otf"];

    // Every installed family once, read on first use.
    private static string[] InstalledFamilies() => _installedFamilies ??=
    [
        .. (EditorApplication.Instance?.SystemFonts.Select(f => f.FamilyName) ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase),
    ];

    private static void FontRow(Paper paper, EditorSettings s, string id, string label, string bundled, string current, Action<string> set)
        => FontRow(paper, id, label, [(bundled, bundled)], current, v => { set(v); s.ApplyTheme(); s.Save(); });

    // A searchable family dropdown led by the given choices (and the chosen file, if one is set), plus a button to pick a font file.
    internal static void FontRow(Paper paper, string id, string label, (string Value, string Label)[] leading, string current, Action<string> apply, bool compact = true)
    {
        (string Value, string Label)[] options =
        [
            .. leading,
            .. EditorApplication.IsFontFile(current) ? [(current, System.IO.Path.GetFileName(current))] : Array.Empty<(string, string)>(),
            .. InstalledFamilies().Where(f => !leading.Any(l => l.Value.Equals(f, StringComparison.OrdinalIgnoreCase))).Select(f => (f, f)),
        ];
        int index = Math.Max(0, Array.FindIndex(options, o => o.Value.Equals(current, StringComparison.OrdinalIgnoreCase)));
        string[] shown = [.. options.Select(o => o.Label)];

        EditorGUI.Row(paper, id, label, () =>
        {
            using (paper.Row($"{id}_row").Height(UnitValue.Auto).Gap(4).Enter())
            {
                Origami.Dropdown(paper, $"{id}_v", index, i => apply(options[i].Value), shown).Searchable().Show();
                Origami.IconButton(paper, $"{id}_file", EditorIcons.FolderOpen_I, () =>
                    EditorApplication.OpenFileDialog(FileDialogMode.Open, path => { if (path != null) apply(path); },
                        filters: s_fontFilters, filterLabels: [Loc.Get("pref.font_filter")]))
                    .Small().Width(Origami.Current.Metrics.RowHeight).Height(Origami.Current.Metrics.RowHeight)
                    .Tooltip(Loc.Get("pref.font_file")).Show();
            }
        }, compact: compact);
    }

    // ---- Typography ----
    private void DrawThemeType(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        EditorGUI.SectionHeader(paper, "pref_ty_fonts", Loc.Get("pref.fonts"), first: true, compact: true);
        FontRow(paper, s, "pref_ty_ui", Loc.Get("pref.ui_font"), EditorApplication.BundledFontName, theme.DefaultFontName, v => theme.DefaultFontName = v);
        FontRow(paper, s, "pref_ty_bold", Loc.Get("pref.bold_font"), EditorApplication.BundledFontName, theme.DefaultBoldFontName, v => theme.DefaultBoldFontName = v);
        FontRow(paper, s, "pref_ty_mono", Loc.Get("pref.mono_font"), EditorApplication.BundledMonoFontName, theme.MonoFontName, v => theme.MonoFontName = v);

        EditorGUI.SectionHeader(paper, "pref_ty_size", Loc.Get("pref.sizing"), compact: true);
        EditorGUI.SettingsSlider(paper, "pref_ty_base", Loc.Get("pref.base_size"), theme.FontSize, 8, 32, v => { theme.FontSize = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_ty_small", Loc.Get("pref.small_delta"), theme.SmallFontDelta, 0, 6, v => { theme.SmallFontDelta = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_ty_icon", Loc.Get("pref.icon_scale"), theme.IconScale, 0.7f, 1.3f, v => { theme.IconScale = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);

        EditorGUI.SectionHeader(paper, "pref_ty_render", Loc.Get("pref.text_rendering"), compact: true);
        EditorGUI.SettingsSlider(paper, "pref_ty_letter", Loc.Get("pref.letter_spacing"), theme.LetterSpacing, -1, 3, v => { theme.LetterSpacing = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_ty_line", Loc.Get("pref.line_height"), theme.LineHeightScale, 0.8f, 1.6f, v => { theme.LineHeightScale = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.Row(paper, "pref_ty_quality", Loc.Get("pref.text_quality"), () =>
            Origami.EnumDropdown(paper, "pref_ty_quality_v", theme.TextQuality, v => { theme.TextQuality = v; s.ApplyTheme(); s.Save(); }).Show(), compact: true);
        EditorGUI.SettingsToggle(paper, "pref_ty_liga", Loc.Get("pref.ligatures"), theme.Ligatures, v => { theme.Ligatures = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
    }

    // ---- Layout ----
    private void DrawThemeLayout(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        EditorGUI.SectionHeader(paper, "pref_sp_metrics", Loc.Get("pref.metrics"), first: true, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_round", Loc.Get("pref.roundness"), theme.Roundness, 0, 20, v => { theme.Roundness = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_spacing", Loc.Get("pref.spacing"), theme.Spacing, 0, 12, v => { theme.Spacing = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_padding", Loc.Get("pref.padding"), theme.Padding, 0, 16, v => { theme.Padding = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_row", Loc.Get("pref.row_height"), theme.RowHeight, 16, 40, v => { theme.RowHeight = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_menu", Loc.Get("pref.menu_bar_height"), theme.MenuBarHeight, 18, 48, v => { theme.MenuBarHeight = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_status", Loc.Get("pref.status_bar_height"), theme.StatusBarHeight, 16, 40, v => { theme.StatusBarHeight = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_label", Loc.Get("pref.label_width"), theme.LabelWidth, 60, 240, v => { theme.LabelWidth = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_dock", Loc.Get("pref.dock_spacing"), theme.DockSpacing, 0, 24, v => { theme.DockSpacing = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_tabh", Loc.Get("pref.tab_bar_height"), theme.TabBarHeight, 18, 40, v => { theme.TabBarHeight = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SectionHeader(paper, "pref_sp_tabs", Loc.Get("pref.tabs_scrollbars"), compact: true);
        EditorGUI.Row(paper, "pref_sp_tabstyle", Loc.Get("pref.tab_style"), () =>
            Origami.EnumDropdown(paper, "pref_sp_tabstyle_v", theme.TabStyle, v => { theme.TabStyle = v; s.ApplyTheme(); s.Save(); }).Show(), compact: true);
        EditorGUI.Row(paper, "pref_sp_tabclose", Loc.Get("pref.tab_close"), () =>
            Origami.EnumDropdown(paper, "pref_sp_tabclose_v", theme.TabCloseButtons, v => { theme.TabCloseButtons = v; s.ApplyTheme(); s.Save(); }).Show(), compact: true);
        EditorGUI.SettingsToggle(paper, "pref_sp_focus", Loc.Get("pref.highlight_focused"), theme.HighlightFocusedPanel, v => { theme.HighlightFocusedPanel = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_scroll", Loc.Get("pref.scrollbar_size"), theme.ScrollbarSize, 2, 16, v => { theme.ScrollbarSize = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsToggle(paper, "pref_sp_autohide", Loc.Get("pref.autohide_scrollbars"), theme.AutoHideScrollbars, v => { theme.AutoHideScrollbars = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_sp_tabp", Loc.Get("pref.tab_padding"), theme.TabPadding, 4, 24, v => { theme.TabPadding = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsRow(paper, "pref_sp_scale", Loc.Get("pref.user_scale"), () =>
            Origami.Slider(paper, "pref_sp_scale_v", theme.UserScale, v =>
                {
                    theme.UserScale = MathF.Round(v, 2);
                    if (!_scaleDragging) ApplyUserScale(s);
                }, 0.5f, 2f)
                .OnDragStart(() => _scaleDragging = true)
                .OnDragEnd(() => { _scaleDragging = false; ApplyUserScale(s); })
                .Format("F2").Show(), separator: false, compact: true);
    }

    // Scaling while dragging would move the slider out from under the cursor, so the scale applies on release.
    private bool _scaleDragging;

    private static void ApplyUserScale(EditorSettings s)
    {
        s.ApplyTheme();
        s.Save();
    }

    // ---- Effects ----
    private void DrawThemeEffects(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        EditorGUI.SectionHeader(paper, "pref_fx_depth", Loc.Get("pref.depth"), first: true, compact: true);
        EditorGUI.SettingsSlider(paper, "pref_fx_opacity", Loc.Get("pref.window_opacity"), theme.WindowOpacity, 0.3f, 1f, v => { theme.WindowOpacity = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsToggle(paper, "pref_fx_glass", Loc.Get("pref.glass_blur"), theme.GlassBlur, v => { theme.GlassBlur = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
        if (theme.GlassBlur)
            EditorGUI.SettingsSlider(paper, "pref_fx_blur", Loc.Get("pref.blur_amount"), theme.BlurAmount, 0, 40, v => { theme.BlurAmount = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
        EditorGUI.SettingsToggle(paper, "pref_fx_shadow", Loc.Get("pref.drop_shadows"), theme.DropShadows, v => { theme.DropShadows = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
        EditorGUI.SettingsToggle(paper, "pref_fx_glow", Loc.Get("pref.accent_glow"), theme.AccentGlow, v => { theme.AccentGlow = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);

        // Per-layer nebula controls (apply to the animated nebula and the static-Nebula style).
        void NebulaLayers()
        {
            EditorGUI.SettingsToggle(paper, "pref_fx_grad", Loc.Get("pref.nebula_gradients"), theme.BgShowGradients, v => { theme.BgShowGradients = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
            EditorGUI.SettingsColorField(paper, "pref_fx_void", Loc.Get("pref.void_color"), () => theme.BackgroundVoidColor, v => { theme.BackgroundVoidColor = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
        }

        EditorGUI.SectionHeader(paper, "pref_fx_bg", Loc.Get("pref.background"), compact: true);
        EditorGUI.SettingsToggle(paper, "pref_fx_anim", Loc.Get("pref.animated_bg"), theme.AnimatedBackground, v => { theme.AnimatedBackground = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
        if (theme.AnimatedBackground)
        {
            EditorGUI.SettingsSlider(paper, "pref_fx_speed", Loc.Get("pref.speed"), theme.BackgroundSpeed, 0, 3, v => { theme.BackgroundSpeed = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
            NebulaLayers();
        }
        else
        {
            EditorGUI.Row(paper, "pref_fx_style", Loc.Get("pref.style"), () =>
                Origami.EnumDropdown(paper, "pref_fx_style_v", theme.BackgroundStyle,
                    v => { theme.BackgroundStyle = v; s.ApplyTheme(); s.Save(); }).Show(), compact: true);

            if (theme.BackgroundStyle == EditorBackgroundStyle.Gradient)
            {
                EditorGUI.SettingsColorField(paper, "pref_fx_ca", Loc.Get("env.top_color"), () => theme.BackgroundColorA, v => { theme.BackgroundColorA = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
                EditorGUI.SettingsColorField(paper, "pref_fx_cb", Loc.Get("env.bottom_color"), () => theme.BackgroundColorB, v => { theme.BackgroundColorB = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
            }
            else if (theme.BackgroundStyle == EditorBackgroundStyle.Color)
            {
                EditorGUI.SettingsColorField(paper, "pref_fx_ca", Loc.Get("env.color"), () => theme.BackgroundColorA, v => { theme.BackgroundColorA = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
            }
            else if (theme.BackgroundStyle == EditorBackgroundStyle.Image)
            {
                DrawBackgroundImagePicker(paper, s, theme);
                EditorGUI.Row(paper, "pref_fx_fit", Loc.Get("pref.bg_fit"), () =>
                    Origami.EnumDropdown(paper, "pref_fx_fit_v", theme.BackgroundImageFit,
                        v => { theme.BackgroundImageFit = v; s.ApplyTheme(); s.Save(); }).Show(), compact: true);
                EditorGUI.SettingsSlider(paper, "pref_fx_dim", Loc.Get("pref.bg_dim"), theme.BackgroundImageDim, 0, 1, v => { theme.BackgroundImageDim = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
                EditorGUI.SettingsSlider(paper, "pref_fx_blur_img", Loc.Get("pref.bg_blur"), theme.BackgroundImageBlur, 0, 40, v => { theme.BackgroundImageBlur = v; s.ApplyTheme(); s.Save(); }, "F2", separator: false, compact: true);
                EditorGUI.SettingsColorField(paper, "pref_fx_ca", Loc.Get("pref.bg_fill"), () => theme.BackgroundColorA, v => { theme.BackgroundColorA = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
            }
        }

        EditorGUI.SectionHeader(paper, "pref_fx_motion", Loc.Get("pref.motion"), compact: true);
        EditorGUI.Row(paper, "pref_fx_anim_speed", Loc.Get("pref.ui_animations"), () =>
            Origami.EnumDropdown(paper, "pref_fx_anim_speed_v", theme.UiAnimations, v => { theme.UiAnimations = v; s.ApplyTheme(); s.Save(); }).Show(), compact: true);

        EditorGUI.SectionHeader(paper, "pref_fx_render", Loc.Get("pref.rendering"), compact: true);
        EditorGUI.SettingsToggle(paper, "pref_fx_aa", Loc.Get("pref.anti_aliasing"), theme.AntiAliasing, v => { theme.AntiAliasing = v; s.ApplyTheme(); s.Save(); }, separator: false, compact: true);
    }

    private static readonly string[] _imageFilters = ["*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.webp"];

    private static void DrawBackgroundImagePicker(Paper paper, EditorSettings s, EditorThemeData theme)
    {
        string label = string.IsNullOrEmpty(theme.BackgroundImagePath)
            ? Loc.Get("pref.bg_image_none")
            : System.IO.Path.GetFileName(theme.BackgroundImagePath);

        EditorGUI.Row(paper, "pref_fx_img", Loc.Get("pref.bg_image"), () =>
            Origami.Button(paper, "pref_fx_img_v", $"{EditorIcons.FolderOpen}  {label}", () =>
                EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
                {
                    if (path == null) return;
                    theme.BackgroundImagePath = path;
                    s.ApplyTheme(); s.Save();
                }, startPath: string.IsNullOrEmpty(theme.BackgroundImagePath) ? null : System.IO.Path.GetDirectoryName(theme.BackgroundImagePath),
                filters: _imageFilters, filterLabels: [Loc.Get("pref.image_filter")])).Show(), compact: true);
    }

    // ---- Live preview (mini editor chrome drawn with live EditorTheme tokens) ----
    private void DrawThemePreview(Paper paper, Scribe.FontFile font, float w)
    {
        float radius = EditorTheme.Roundness;
        Color surface = EditorTheme.Neutral300;
        const float cardH = 208f;

        using (paper.Column("pref_pv").Width(w).Padding(PAD * 2, PAD * 2, PAD * 2, PAD * 2)
            .BackgroundColor(Color.FromArgb(36, 0, 0, 0)).Enter())
        using (paper.Column("pref_pv_center").Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Gap(SP * 2).Enter())
        {
            using (paper.Column("pref_pv_card").Height(cardH).Rounded(Origami.Current.Metrics.ContainerRounding).Clip()
                .DropShadow(0, 10, 26, -6, EditorTheme.Shadow)
                .BackgroundColor(EditorTheme.Neutral200).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
            {
                // Titlebar
                using (paper.Row("pref_pv_title").Height(30).Padding(PAD, PAD, 0, 0).Gap(SP * 1.5f)
                    .BackgroundColor(surface).Enter())
                {
                    paper.Box("pref_pv_logo").Width(13).Height(13).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(radius * 0.5f)
                        .BackgroundColor(EditorTheme.Accent).IsNotInteractable();
                    paper.Box("pref_pv_name").Margin(SP, 0, 0, 0).IsNotInteractable()
                        .Text("Prowl", font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                    for (int i = 0; i < 3; i++)
                        paper.Box($"pref_pv_wdot{i}").Width(7).Height(7).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(4)
                            .BackgroundColor(i == 2 ? EditorTheme.Red400 : EditorTheme.Ink200).IsNotInteractable();
                }
                EditorGUI.Divider(paper, "pref_pv_td");

                // Toolbar
                using (paper.Row("pref_pv_tool").Height(28).Padding(PAD, PAD, 0, 0).Gap(PAD * 2)
                    .BackgroundColor(EditorTheme.Neutral200).Enter())
                {
                    paper.Box("pref_pv_file").Width(UnitValue.Auto).IsNotInteractable()
                        .Text(Loc.Get("menu.file"), font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                    paper.Box("pref_pv_edit").IsNotInteractable()
                        .Text(Loc.Get("menu.edit"), font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                    paper.Box("pref_pv_play").Width(20).Height(18).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(radius * 0.5f)
                        .Glow(0, 0, 12, 0, Color.FromArgb(170, EditorTheme.Accent))
                        .BackgroundColor(EditorTheme.Accent).IsNotInteractable()
                        .Text(EditorIcons.Play, font).TextColor(Color.White).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleCenter);
                }

                // Body: mini Hierarchy + Inspector
                using (paper.Row("pref_pv_body").Padding(EditorTheme.DockPadding, EditorTheme.DockPadding, EditorTheme.DockPadding, EditorTheme.DockPadding).Gap(EditorTheme.DockPadding).Enter())
                {
                    // Hierarchy
                    using (paper.Column("pref_pv_hier").Rounded(radius).Clip()
                        .BackgroundColor(surface).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                        .Padding(PAD, PAD, PAD, PAD).Gap(SP).Enter())
                    {
                        paper.Box("pref_pv_hh").Height(16).IsNotInteractable()
                            .Text(Loc.Get("panel.hierarchy"), font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                            .Alignment(TextAlignment.MiddleLeft);
                        string[] items = ["Planet", "Character", "Camera"];
                        for (int i = 0; i < items.Length; i++)
                        {
                            bool sel = i == 0;
                            using (paper.Row($"pref_pv_hi{i}").Height(18).Rounded(radius * 0.5f)
                                .Padding(SP, SP, 0, 0).Gap(SP)
                                .BackgroundColor(sel ? EditorTheme.Selected : Color.Transparent).Enter())
                            {
                                paper.Box($"pref_pv_hd{i}").Width(7).Height(7).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(radius * 0.5f)
                                    .BackgroundColor(sel ? EditorTheme.Accent : EditorTheme.Blue400).IsNotInteractable();
                                paper.Box($"pref_pv_hn{i}").Margin(SP * 0.5f, 0, 0, 0).IsNotInteractable()
                                    .Text(items[i], font).TextColor(sel ? EditorTheme.Ink500 : EditorTheme.Ink400)
                                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                            }
                        }
                    }

                    // Inspector
                    using (paper.Column("pref_pv_insp").Rounded(radius).Clip()
                        .BackgroundColor(surface).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                        .Padding(PAD, PAD, PAD, PAD).Gap(SP * 1.5f).Enter())
                    {
                        paper.Box("pref_pv_ih").Height(16).IsNotInteractable()
                            .Text(Loc.Get("panel.inspector"), font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                            .Alignment(TextAlignment.MiddleLeft);

                        PreviewField(paper, font, "pref_pv_f0", "Mass", "72.0");
                        PreviewField(paper, font, "pref_pv_f1", "Speed", "6.5");

                        // Mini slider (60% accent fill)
                        using (paper.Row("pref_pv_sld").Height(6).Rounded(3)
                            .BackgroundColor(EditorTheme.Neutral400).Enter())
                            paper.Box("pref_pv_sld_f").Width(UnitValue.Percentage(60f)).Rounded(3)
                                .BackgroundColor(EditorTheme.Accent).IsNotInteractable();

                        using (paper.Row("pref_pv_btns").Height(20).Gap(SP).Enter())
                        {
                            paper.Box("pref_pv_apply").Rounded(radius * 0.5f)
                                .Glow(0, 0, 12, 0, Color.FromArgb(150, EditorTheme.Accent))
                                .BackgroundColor(EditorTheme.Accent).IsNotInteractable()
                                .Text(Loc.Get("inspector.apply"), font).TextColor(Color.White).FontSize(EditorTheme.FontSizeSmall)
                                .Alignment(TextAlignment.MiddleCenter);
                            paper.Box("pref_pv_rst").Rounded(radius * 0.5f)
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
        using (paper.Row(id).Height(16).Enter())
        {
            paper.Box($"{id}_l").IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);
            paper.Box($"{id}_v").Width(UnitValue.Auto).IsNotInteractable()
                .Text(value, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleRight);
        }
    }

    // ================================================================
    //  Shortcuts
    // ================================================================

    private void DrawShortcuts(Paper paper, Scribe.FontFile font, float w)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        HandleRebinding();

        EditorGUI.SectionHeader(paper, "pref_sc_hdr", Loc.Get("pref.shortcuts"), first: true);

        using (paper.Row("pref_sc_actions").Height(EditorTheme.RowHeight).PaddingLeft(m.PaddingLarge).Enter())
        {
            Origami.Button(paper, "pref_sc_reset_all", $"{EditorIcons.RotateLeft}  {Loc.Get("pref.reset_all")}", () => ShortcutManager.ClearAllOverrides()).Width(200).Show();
        }

        paper.Box("pref_sc_sp2").Height(EditorTheme.Padding * 2);

        DrawShortcutRows(paper, font, w, null);
    }

    private void HandleRebinding()
    {
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
    }

    // Draws the shortcuts matching filter by name, category or key, grouped by category, and returns how many matched.
    private int DrawShortcutRows(Paper paper, Scribe.FontFile font, float w, string? filter)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        int count = 0;
        // Group by category
        string? lastCategory = null;
        foreach (ShortcutDefinition shortcut in ShortcutManager.GetAllShortcuts())
        {
            if (!EditorUtils.MatchesSearch(shortcut.DisplayName, filter) &&
                !EditorUtils.MatchesSearch(shortcut.Category, filter) &&
                !EditorUtils.MatchesSearch(ShortcutManager.GetDisplayString(shortcut.Binding), filter))
                continue;
            count++;

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
                .PaddingLeft(m.PaddingLarge).Gap(EditorTheme.Spacing * 2)
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
                    .Width(160).Height(EditorTheme.RowHeight - 4).Rounded(EditorTheme.Roundness)
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
                    string shortcutId = shortcut.Id;
                    Origami.Button(paper, $"pref_sc_rst_{shortcutId}", Loc.Get("inspector.reset"), () => ShortcutManager.ClearOverride(shortcutId))
                        .Small().Height(EditorTheme.RowHeight - 4).Show();
                }

                List<ShortcutDefinition> conflicts = ShortcutManager.FindConflicts(shortcut, shortcut.Binding);
                if (conflicts.Count > 0)
                {
                    string names = string.Join(", ", conflicts.Select(c => $"{c.Category}: {c.DisplayName}"));
                    paper.Box($"pref_sc_conflict_{shortcut.Id}")
                        .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                        .Text($"{EditorIcons.TriangleExclamation}  {Loc.Get("pref.shortcut_conflict", new { names })}", font)
                        .TextColor(EditorTheme.Amber400)
                        .FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft).TextTruncate()
                        .Tooltip(names);
                }
            }
        }
        return count;
    }
}
